using System.IO;
using System.Linq;
using NUnit.Framework;
using UniGLTF;
using SpringBoneDeserializer = UniGLTF.Extensions.VRMC_springBone.GltfDeserializer;
using UniJSON;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Utils;
using UniVRM10;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace VCam.AvatarExporter
{
    public sealed class AvatarExporterTests
    {
        private GameObject _avatar;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _avatar = TestAvatar.Create();
            _path = Path.Combine(Path.GetFullPath(".build"), "Tests", "TestAvatar.vrm");
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
        }

        [TearDown]
        public void TearDown() => TestAvatar.Destroy(_avatar);

        private SkinnedMeshRenderer Body => _avatar.GetComponentInChildren<SkinnedMeshRenderer>();
        private Material Skin => Body.sharedMaterial;
        private VRCPhysBone PhysBone => _avatar.GetComponentInChildren<VRCPhysBone>();
        private VRCAvatarDescriptor Descriptor => _avatar.GetComponent<VRCAvatarDescriptor>();

        private void Recreate(string lilToonShader = TestAvatar.DefaultLilToonShader, string[] shapeKeys = null)
        {
            TestAvatar.Destroy(_avatar);
            _avatar = TestAvatar.Create(lilToonShader, shapeKeys);
        }

        /// <summary>Whether the note names the object, alone or in a list of names.</summary>
        private static bool Mentions(Message note, string name) =>
            note.Args.OfType<string>().Any(text => text.Split(new[] { ", " }, System.StringSplitOptions.None).Contains(name));

        private (GltfData Data, Vrm10Data Vrm, ExportReport Report) Export(int maxTextureSize = 2048, bool splitsBlink = true)
        {
            var options = new ExportOptions { Meta = { Name = "Test", Authors = { "Tester" } }, MaxTextureSize = maxTextureSize, SplitsBlink = splitsBlink };
            var report = AvatarExporter.Export(_avatar, options, _path);
            var data = new GlbBinaryParser(File.ReadAllBytes(_path), _path).Parse();
            return (data, Vrm10Data.Parse(data), report);
        }

        private static UniGLTF.Extensions.VRMC_springBone.VRMC_springBone SpringBone(GltfData data)
        {
            Assert.That(SpringBoneDeserializer.TryGet(data.GLTF.extensions, out var springBone), Is.True);
            return springBone;
        }

        /// <summary>The name of the shape key of the first mesh that the bind moves.</summary>
        private static string TargetName(GltfData data, UniGLTF.Extensions.VRMC_vrm.MorphTargetBind bind) =>
            data.Json.ParseAsJson()["meshes"][0]["extras"]["targetNames"][bind.Index.Value].GetString();

        /// <summary>The MToon extension of the first material.</summary>
        private static JsonNode MToon(GltfData data) => data.Json.ParseAsJson()["materials"][0]["extensions"]["VRMC_materials_mtoon"];

        [Test]
        public void ExportsVisemesBlinkAndPerfectSync()
        {
            var (_, vrm, _) = Export();
            var expressions = vrm.VrmExtension.Expressions;

            Assert.That(expressions.Preset.Aa.MorphTargetBinds, Has.Count.EqualTo(1));
            Assert.That(expressions.Preset.Oh.MorphTargetBinds, Has.Count.EqualTo(1));
            Assert.That(expressions.Preset.Blink.MorphTargetBinds, Has.Count.EqualTo(1));
            Assert.That(expressions.Custom.Keys, Is.SupersetOf(new[] { "EyeBlinkLeft", "EyeBlinkRight", "JawOpen", "MouthClose" }));
        }

        /// <summary>Perfect Sync avatars close each eye with their ARKit keys, which wink without splitting the blink.</summary>
        [Test]
        public void WinksWithTheArKitBlinks()
        {
            var (data, vrm, _) = Export();

            var expressions = vrm.VrmExtension.Expressions.Preset;
            Assert.That(TargetName(data, expressions.BlinkLeft.MorphTargetBinds.Single()), Is.EqualTo("eyeBlinkLeft"));
            Assert.That(TargetName(data, expressions.BlinkRight.MorphTargetBinds.Single()), Is.EqualTo("EyeBlink_R"));
        }

        /// <summary>Many avatars close both eyes with one shape key, which leaves VRM apps nothing to wink with.</summary>
        [Test]
        public void SplitsABlinkOfBothEyesForWinks()
        {
            Recreate(shapeKeys: new[] { "Blink" });

            var (data, vrm, report) = Export();

            var expressions = vrm.VrmExtension.Expressions.Preset;
            Assert.That(TargetName(data, expressions.BlinkLeft.MorphTargetBinds.Single()), Is.EqualTo("Blink_L"));
            Assert.That(TargetName(data, expressions.BlinkRight.MorphTargetBinds.Single()), Is.EqualTo("Blink_R"));
            Assert.That(report.Notes, Has.Some.EqualTo(new Message("Report:SplitBlink", "Blink", "Body")));
        }

        [Test]
        public void KeepsTheBlinkOfBothEyesWhenAsked()
        {
            Recreate(shapeKeys: new[] { "Blink" });

            var (_, vrm, _) = Export(splitsBlink: false);

            Assert.That(vrm.VrmExtension.Expressions.Preset.Blink.MorphTargetBinds, Has.Count.EqualTo(1));
            Assert.That(vrm.VrmExtension.Expressions.Preset.BlinkLeft, Is.Null);
        }

        /// <summary>The vertices on the avatar's left move with the left key alone, and the two keys add up to the blink.</summary>
        [Test]
        public void SplitsTheBlinkAtTheMiddleOfTheFace()
        {
            var body = Body;
            var blink = body.sharedMesh.GetBlendShapeIndex("Blink");
            var created = new System.Collections.Generic.List<Object>();
            try
            {
                var (left, right) = EyeBlinkSplitter.Split(body, blink, _avatar.GetComponent<Animator>(), created);

                Vector3[] Deltas(int index)
                {
                    var deltas = new Vector3[body.sharedMesh.vertexCount];
                    body.sharedMesh.GetBlendShapeFrameVertices(index, 0, deltas, null, null);
                    return deltas;
                }
                // Vertex 0 is at -X, the avatar's left, and the only vertex the blink moves.
                Assert.That(Deltas(left)[0], Is.EqualTo(Deltas(blink)[0]));
                Assert.That(Deltas(right)[0], Is.EqualTo(Vector3.zero));
            }
            finally
            {
                foreach (var obj in created)
                {
                    Object.DestroyImmediate(obj);
                }
            }
        }

        /// <summary>
        /// Bangs drawn again see-through over the eyes, where a stencil allows, keep their triangles. MToon has no stencil, so
        /// apps that show the fallback would draw them over the whole mesh, and the fallback draws nothing.
        /// </summary>
        [Test]
        public void ExportsTheMaterialsDrawnAgainWhereTheStencilAllows()
        {
            var bangs = TestAvatar.AddQuad(_avatar, "Bangs", "lilToon");
            var overEyes = TestAvatar.CreateMaterial("BangsOverEyes", "Hidden/lilToonTransparent");
            overEyes.SetFloat("_StencilRef", 102);
            overEyes.SetFloat("_StencilComp", (float)UnityEngine.Rendering.CompareFunction.Equal);
            bangs.sharedMaterials = new[] { bangs.sharedMaterial, overEyes };

            var (data, _, report) = Export();

            var materials = data.GLTF.materials.Select(material => material.name).ToList();
            var mesh = data.GLTF.meshes.Single(m => m.primitives.Any(primitive => materials[primitive.material.Value] == "Bangs"));
            Assert.That(mesh.primitives.Select(primitive => materials[primitive.material.Value]), Is.EqualTo(new[] { "Bangs", "BangsOverEyes" }));
            Assert.That(data.GLTF.materials[materials.IndexOf("BangsOverEyes")].pbrMetallicRoughness.baseColorFactor[3], Is.EqualTo(0));
            var overEyesExtension = data.Json.ParseAsJson()["materials"][materials.IndexOf("BangsOverEyes")]["extensions"][LilToonExtension.Name];
            Assert.That(overEyesExtension["fallbackBaseColorAlpha"].GetSingle(), Is.EqualTo(1));
            Assert.That(report.Notes, Has.Some.EqualTo(new Message("Report:StencilHidden", "BangsOverEyes")));
        }

        /// <summary>Some avatars name the viseme shape keys but leave the mesh empty.</summary>
        [Test]
        public void FindsTheVisemesWhenTheDescriptorNamesNoMesh()
        {
            Descriptor.VisemeSkinnedMesh = null;

            var (_, vrm, report) = Export();

            Assert.That(vrm.VrmExtension.Expressions.Preset.Aa.MorphTargetBinds, Has.Count.EqualTo(1));
            Assert.That(report.Notes, Has.Some.EqualTo(new Message("Report:VisemeMeshFound", "Body")));
        }

        /// <summary>Avatars with parameter-only lip sync animate the mouth from FX clips, which are visemes, not expressions.</summary>
        [Test]
        public void TakesTheVisemesFromTheAnimationsTheVisemeParameterPlays()
        {
            TestAvatar.AddVisemeAnimations(_avatar);

            var (data, vrm, _) = Export();

            var expressions = vrm.VrmExtension.Expressions;
            Assert.That(TargetName(data, expressions.Preset.Aa.MorphTargetBinds.Single()), Is.EqualTo("vrc.v_aa"));
            Assert.That(TargetName(data, expressions.Preset.Oh.MorphTargetBinds.Single()), Is.EqualTo("vrc.v_oh"));
            Assert.That(expressions.Preset.Ih?.MorphTargetBinds, Is.Null.Or.Empty);
            Assert.That(expressions.Custom?.Keys ?? Enumerable.Empty<string>(), Has.None.StartsWith("vrc.v_"));
        }

        [Test]
        public void DropsShapeKeysNoExpressionUses()
        {
            var (data, vrm, _) = Export();

            var targets = data.Json.ParseAsJson()["meshes"][0]["primitives"][0]["targets"];
            Assert.That(targets.GetArrayCount(), Is.EqualTo(TestAvatar.ShapeKeys.Length - 1), "all but BodyShape");
            Assert.That(TargetName(data, vrm.VrmExtension.Expressions.Custom["JawOpen"].MorphTargetBinds.Single()), Is.EqualTo("JawOpen"));
        }

        [Test]
        public void ShrinksTexturesOverTheLimit()
        {
            var (data, _, _) = Export(maxTextureSize: 2);

            var mainTexture = data.Json.ParseAsJson()["materials"][0]["extensions"][LilToonExtension.Name]["textures"]["_MainTex"]["index"].GetInt32();
            var texture = DecodeImage(data, mainTexture);
            try
            {
                Assert.That((texture.width, texture.height), Is.EqualTo((2, 2)), "the 4px source fits in 2px");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ExportsFxFacialAnimationsAsExpressions()
        {
            TestAvatar.AddFxClips(
                ("Smile", new[] { ("blendShape.JawOpen", 100f), ("blendShape.Blink", 50f), ("blendShape.BodyShape", 0f) }),
                ("ToggleOutfit", new[] { ("blendShape.JawOpen", 100f) }));

            var (_, vrm, _) = Export();

            var smile = vrm.VrmExtension.Expressions.Custom["Smile"].MorphTargetBinds;
            Assert.That(smile.Select(bind => bind.Weight), Is.EquivalentTo(new[] { 1f, 0.5f }), "zero weights are dropped");
            Assert.That(vrm.VrmExtension.Expressions.Preset.Happy.MorphTargetBinds, Has.Count.EqualTo(2), "a smile fills happy");
            Assert.That(vrm.VrmExtension.Expressions.Custom.Keys, Does.Not.Contain("ToggleOutfit"), "it also switches an object");
        }

        [Test]
        public void LeavesOutMeshesThatDrawNoColor()
        {
            TestAvatar.AddDepthOnlyMesh(_avatar);

            var (data, _, report) = Export();

            Assert.That(data.GLTF.nodes.Select(node => node.name), Has.None.EqualTo("AOMask"));
            Assert.That(report.Notes, Has.Some.EqualTo(new Message("Report:NoColor", "AOMask")));
        }

        [Test]
        public void LeavesOutRenderersWithoutMaterials()
        {
            TestAvatar.AddQuad(_avatar, "AvatarHeight", "lilToon").sharedMaterials = new Material[] { null };

            var (data, _, report) = Export();

            Assert.That(data.GLTF.nodes.Select(node => node.name), Has.None.EqualTo("AvatarHeight"));
            Assert.That(report.Notes, Has.None.Matches<Message>(note => Mentions(note, "AvatarHeight")));
        }

        [Test]
        public void LeavesHiddenRenderersOutOfTheReport()
        {
            TestAvatar.AddDepthOnlyMesh(_avatar);
            _avatar.transform.Find("AOMask").gameObject.SetActive(false);

            var (_, _, report) = Export();

            Assert.That(report.Notes, Has.None.Matches<Message>(note => Mentions(note, "AOMask")));
        }

        /// <summary>Authors order transparent materials by render queue, such as hair blending over the face.</summary>
        [Test]
        public void KeepsTheOrderOfTransparentMaterials()
        {
            TestAvatar.AddQuad(_avatar, "Front", "Hidden/lilToonTransparent").sharedMaterial.renderQueue = 2600;
            TestAvatar.AddQuad(_avatar, "Back", "Hidden/lilToonTransparent").sharedMaterial.renderQueue = 2500;

            var (data, _, _) = Export();

            var materials = data.Json.ParseAsJson()["materials"];
            int Offset(string name) => Enumerable.Range(0, materials.GetArrayCount())
                .Select(i => materials[i]).Single(material => material["name"].GetString() == name)
                ["extensions"]["VRMC_materials_mtoon"]["renderQueueOffsetNumber"].GetInt32();
            Assert.That(Offset("Back"), Is.LessThan(Offset("Front")));
        }

        [Test]
        public void LeavesOutMaterialsPastTheSubmeshes()
        {
            var body = Body;
            body.sharedMaterials = new[] { body.sharedMaterial, TestAvatar.CreateMaterial("Overlay", "_lil/[Optional] lilToonOverlay") };

            var (data, _, report) = Export();

            Assert.That(data.GLTF.materials.Select(material => material.name), Has.None.EqualTo("Overlay"));
            Assert.That(report.Notes, Has.Some.EqualTo(new Message("Report:DrawsOverAgain", "Body", "Overlay")));
        }

        [Test]
        public void HidesTheFakeShadowInTheMToonFallback()
        {
            Recreate(lilToonShader: "_lil/[Optional] lilToonFakeShadow");

            var (data, _, _) = Export();

            var material = data.GLTF.materials[0];
            Assert.That(material.alphaMode, Is.EqualTo("BLEND"));
            Assert.That(material.pbrMetallicRoughness.baseColorFactor[3], Is.EqualTo(0));
        }

        /// <summary>
        /// A fake shadow drawn past the bangs' submesh draws the bangs' triangles again with its own material, shifted
        /// along a vector in the avatar's axes rather than the mesh's.
        /// </summary>
        [Test]
        public void ExportsTheFakeShadowOverTheTrianglesItDrawsAgain()
        {
            TestAvatar.AddBangsShadow(_avatar);

            var (data, _, report) = Export();

            var json = data.Json.ParseAsJson();
            var materials = data.GLTF.materials.Select(material => material.name).ToList();
            var bangs = data.GLTF.meshes.Single(mesh => mesh.primitives.Any(primitive => materials[primitive.material.Value] == "Bangs"));
            Assert.That(bangs.primitives.Select(primitive => materials[primitive.material.Value]), Is.EqualTo(new[] { "Bangs", "BangsShadow" }));
            Assert.That(data.GLTF.accessors[bangs.primitives[1].indices].count, Is.EqualTo(data.GLTF.accessors[bangs.primitives[0].indices].count));
            var shadow = json["materials"][materials.IndexOf("BangsShadow")]["extensions"][LilToonExtension.Name];
            Assert.That(shadow["shader"].GetString(), Does.Contain("FakeShadow"));
            Assert.That(shadow["floats"]["_StencilRef"].GetSingle(), Is.EqualTo(13));
            var vector = shadow["vectors"]["_FakeShadowVector"];
            Assert.That(new Vector4(vector[0].GetSingle(), vector[1].GetSingle(), vector[2].GetSingle(), vector[3].GetSingle()),
                Is.EqualTo(new Vector4(-1, 0, 0, 0.05f)).Using(Vector4EqualityComparer.Instance), "in the avatar's axes: the bangs are turned around");
            Assert.That(report.Notes, Has.None.Matches<Message>(note => note.Key == "Report:DrawsOverAgain"));
        }

        /// <summary>
        /// Facial clips often move bones too, such as ears, or carry the finger pose of the hand gesture that shows them,
        /// which an expression drops. Clips that run for seconds animate over time, as idle loops do, and are not
        /// expressions.
        /// </summary>
        [Test]
        public void ExportsFacialAnimationsThatMoveBonesButNotLongOnes()
        {
            TestAvatar.AddFxClips(
                ("Smile", new[] { ("blendShape.JawOpen", 100f) }),
                ("Wink", new[] { ("blendShape.Blink", 100f) }),
                ("Idle", new[] { ("blendShape.Blink", 100f) }));
            var clips = Descriptor.baseAnimationLayers[0].animatorController.animationClips;
            clips.Single(clip => clip.name == "Smile")
                .SetCurve("Hips/Spine/Chest/Neck/Head/Ponytail", typeof(Transform), "localEulerAnglesRaw.x", AnimationCurve.Constant(0, 0, 10));
            clips.Single(clip => clip.name == "Wink")
                .SetCurve("", typeof(Animator), "LeftHand.Index.1 Stretched", AnimationCurve.Constant(0, 0, 0.4f));
            clips.Single(clip => clip.name == "Idle")
                .SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blink", AnimationCurve.Linear(0, 0, 3, 100));

            var (_, vrm, report) = Export();

            Assert.That(vrm.VrmExtension.Expressions.Custom.Keys, Contains.Item("Smile").And.Contains("Wink").And.No.Contains("Idle"));
            Assert.That(report.Notes, Has.Some.EqualTo(new Message("Report:FxMovesBones", 2)));
        }

        [Test]
        public void ReadsUnifiedExpressionsNamesAsPerfectSync()
        {
            Recreate(shapeKeys: new[] { "EyeClosedLeft", "EyeClosedRight", "JawOpen", "MouthClosed", "LipSuckUpper" });

            var (_, vrm, _) = Export();

            Assert.That(vrm.VrmExtension.Expressions.Custom.Keys, Is.SupersetOf(new[] { "EyeBlinkLeft", "EyeBlinkRight", "MouthClose", "MouthRollUpper" }));
        }

        [Test]
        public void ExportsOutfitsMergedByModularAvatar()
        {
            TestAvatar.AddMergeArmatureOutfit(_avatar);

            var (data, _, _) = Export();

            var nodes = data.GLTF.nodes;
            Assert.That(nodes.Count(node => node.name == "Spine"), Is.EqualTo(1), "the shirt's spine merges into the avatar's");
            var shirt = nodes.Single(node => node.name == "Shirt");
            var joints = data.GLTF.skins[shirt.skin].joints.Select(joint => nodes[joint].name);
            Assert.That(joints, Contains.Item("Spine"));
            Assert.That(joints, Contains.Item("Ribbon"), "Modular Avatar's suffix on merged bones is dropped");
            Assert.That(data.GLTF.meshes.Select(mesh => mesh.name), Has.None.Contains("RETARGETED__"));
        }

        [Test]
        public void RemovesObjectsOnlyVRChatUses()
        {
            var (data, _, _) = Export();

            var names = data.GLTF.nodes.Select(node => node.name).ToList();
            Assert.That(names, Has.None.EqualTo("NaNimation HeadChop Holder").And.None.EqualTo("RootBoneSubstitute"));
            Assert.That(names, Is.SupersetOf(new[] { "LeftEye", "Ponytail2", "RotatedColliders" }), "bones, joints and colliders stay");
        }

        [Test]
        public void ExportsTheSecondUvSetALilToonFeatureSamples()
        {
            var body = Body;
            body.sharedMesh.uv2 = new[] { new Vector2(0.1f, 0.2f), new Vector2(0.3f, 0.4f), new Vector2(0.5f, 0.6f) };
            body.sharedMaterial.SetFloat("_UseEmission", 1);
            body.sharedMaterial.SetFloat("_EmissionMap_UVMode", 1);

            var (data, _, report) = Export();

            var accessor = data.GLTF.meshes[0].primitives[0].attributes.TEXCOORD_1;
            var texCoords = data.GetArrayFromAccessor<Vector2>(accessor).ToArray();
            var expected = new[] { new Vector2(0.1f, 0.8f), new Vector2(0.3f, 0.6f), new Vector2(0.5f, 0.4f) };
            Assert.That(texCoords, Has.Length.EqualTo(3));
            Assert.That(texCoords.Zip(expected, Vector2.Distance), Is.All.LessThan(1e-5f), "v is flipped to glTF's");
            Assert.That(report.Notes, Has.Some.EqualTo(new Message("Report:NotInMToon", new Term("Feature:Emission"), "Skin")));
        }

        /// <summary>
        /// lilToon sets the alpha from a mask plus a value, as bangs drawn see-through over the eyes do with no mask and
        /// −0.4, which MToon has no way to say but in the texture.
        /// </summary>
        [Test]
        public void BakesTheAlphaMaskIntoTheFallbackTexture()
        {
            var material = Skin;
            material.SetFloat("_AlphaMaskMode", 1);
            material.SetFloat("_AlphaMaskScale", 1);
            material.SetFloat("_AlphaMaskValue", -0.4f);

            var (data, _, _) = Export();

            var pixel = Pixel(data, data.GLTF.materials[0].pbrMetallicRoughness.baseColorTexture.index);
            Assert.That(pixel.a, Is.EqualTo(0.6f).Within(0.01f));
        }

        /// <summary>Eyes often glow in their own color: a gray emission map tinted by the main texture.</summary>
        [Test]
        public void TintsTheFallbackEmissionWithTheMainColor()
        {
            var material = Skin;
            var main = (Texture2D)material.GetTexture("_MainTex");
            main.SetPixels(Enumerable.Repeat(Color.red, 16).ToArray());
            main.Apply();
            material.SetFloat("_UseEmission", 1);
            material.SetColor("_EmissionColor", Color.white);
            material.SetTexture("_EmissionMap", SolidTexture("Emission", Color.white));
            material.SetFloat("_EmissionMainStrength", 1);

            var (data, _, _) = Export();

            var pixel = Pixel(data, data.GLTF.materials[0].emissiveTexture.index);
            Assert.That(Vector3.Distance(new Vector3(pixel.r, pixel.g, pixel.b), new Vector3(1, 0, 0)), Is.LessThan(0.02f));
        }

        /// <summary>MToon has one mask for both; a matcap meant for highlights must not spread over the surface.</summary>
        [Test]
        public void MasksTheFallbackMatcapAndRimWithBothMasks()
        {
            var material = Skin;
            material.SetFloat("_UseMatCap", 1);
            material.SetTexture("_MatCapBlendMask", SolidTexture("MatCapMask", Color.black));
            material.SetFloat("_UseRim", 1);
            material.SetTexture("_RimColorTex", SolidTexture("RimColor", Color.white));

            var (data, _, _) = Export();

            var mtoon = MToon(data);
            var pixel = Pixel(data, mtoon["rimMultiplyTexture"]["index"].GetInt32());
            Assert.That(pixel.r, Is.LessThan(0.02f));
        }

        /// <summary>A shadow color texture painted only where the shadow differs keeps the main color elsewhere.</summary>
        [Test]
        public void TurnsTheMainTextureTowardTheShadowColorTextureByItsAlpha()
        {
            var material = Skin;
            material.SetTexture("_MainTex", SolidTexture("Red", Color.red));
            material.SetFloat("_UseShadow", 1);
            material.SetColor("_ShadowColor", Color.white);
            material.SetTexture("_ShadowColorTex", SolidTexture("ShadowColor", new Color(0, 0, 1, 0)));

            var (data, _, _) = Export();

            var mtoon = MToon(data);
            var pixel = Pixel(data, mtoon["shadeMultiplyTexture"]["index"].GetInt32());
            Assert.That(pixel.r, Is.EqualTo(1).Within(0.02f));
            Assert.That(pixel.b, Is.EqualTo(0).Within(0.02f));
        }

        /// <summary>The strength mask keeps a part out of the shadow, as ribbons that stay bright.</summary>
        [Test]
        public void KeepsTheMaskedOutPartsOutOfTheShadow()
        {
            var material = Skin;
            material.SetTexture("_MainTex", SolidTexture("Red", Color.red));
            material.SetFloat("_UseShadow", 1);
            material.SetColor("_ShadowColor", Color.blue);
            material.SetTexture("_ShadowStrengthMask", SolidTexture("StrengthMask", Color.black));

            var (data, _, report) = Export();

            var mtoon = MToon(data);
            var pixel = Pixel(data, mtoon["shadeMultiplyTexture"]["index"].GetInt32());
            Assert.That(pixel.r, Is.EqualTo(1).Within(0.02f));
            Assert.That(pixel.b, Is.EqualTo(0).Within(0.02f));
            Assert.That(report.Notes, Has.None.EqualTo(new Message("Report:NotInMToon", new Term("Feature:ShadowMasks"), "Skin")));
        }

        /// <summary>A flat or SDF mask turns the mask into a different shadow, which the fallback leaves out.</summary>
        [Test]
        public void ReportsTheStrengthMaskOfOtherMaskTypesAsLost()
        {
            var material = Skin;
            material.SetFloat("_UseShadow", 1);
            material.SetFloat("_ShadowMaskType", 1);
            material.SetTexture("_ShadowStrengthMask", SolidTexture("StrengthMask", Color.black));

            var (_, _, report) = Export();

            Assert.That(report.Notes, Has.Some.EqualTo(new Message("Report:NotInMToon", new Term("Feature:ShadowMasks"), "Skin")));
        }

        [Test]
        public void KeepsTheMainColorAlphaOnceWhenBakingTheMainColor()
        {
            var material = Skin;
            material.shader = Shader.Find("Hidden/lilToonTransparent");
            material.SetTexture("_MainTex", SolidTexture("Lens", Color.white));
            material.SetColor("_Color", new Color(0.5f, 0.5f, 0.5f, 0.5f));

            var (data, _, _) = Export();

            var baseColor = data.GLTF.materials[0].pbrMetallicRoughness;
            var alpha = baseColor.baseColorFactor[3] * Pixel(data, baseColor.baseColorTexture.index).a;
            Assert.That(alpha, Is.EqualTo(0.5f).Within(0.01f));
        }

        [Test]
        public void ExportsTheTangentsOfNormalMappedMeshes()
        {
            var renderer = Body;
            renderer.sharedMesh.RecalculateTangents();
            UseNormalMap(renderer.sharedMaterial);

            var (data, _, _) = Export();

            var primitive = SkinPrimitive(data);
            Assert.That(data.GLTF.materials[primitive.material.Value].normalTexture?.index, Is.GreaterThanOrEqualTo(0));
            Assert.That(data.GLTF.accessors[primitive.attributes.TANGENT].count, Is.EqualTo(data.GLTF.accessors[primitive.attributes.POSITION].count));
        }

        /// <summary>Unity gives a mesh imported without tangents none, so its normal map bends nothing.</summary>
        [Test]
        public void LeavesOutTheNormalMapsOfMeshesWithoutTangents()
        {
            var renderer = Body;
            renderer.sharedMesh.tangents = null;
            UseNormalMap(renderer.sharedMaterial);

            var (data, _, _) = Export();

            var primitive = SkinPrimitive(data);
            Assert.That(data.GLTF.materials[primitive.material.Value].normalTexture?.index ?? -1, Is.LessThan(0));
            Assert.That(primitive.attributes.TANGENT, Is.EqualTo(-1));
        }

        private static void UseNormalMap(Material material)
        {
            material.SetFloat("_UseBumpMap", 1);
            material.SetTexture("_BumpMap", SolidTexture("Normal", new Color(0.5f, 0.5f, 1)));
        }

        private static glTFPrimitives SkinPrimitive(GltfData data) =>
            data.GLTF.meshes.SelectMany(mesh => mesh.primitives).Single(primitive => data.GLTF.materials[primitive.material.Value].name == "Skin");

        private static Texture2D SolidTexture(string name, Color color)
        {
            var texture = new Texture2D(4, 4) { name = name };
            texture.SetPixels(Enumerable.Repeat(color, 16).ToArray());
            texture.Apply();
            AssetDatabase.CreateAsset(texture, $"{TestAvatar.AssetFolder}/{name}.asset");
            return texture;
        }

        private static Texture2D DecodeImage(GltfData data, int textureIndex)
        {
            var texture = new Texture2D(1, 1);
            texture.LoadImage(data.GetBytesFromImage(data.GLTF.textures[textureIndex].source.Value).Value.binary.ToArray());
            return texture;
        }

        private static Color Pixel(GltfData data, int textureIndex)
        {
            var texture = DecodeImage(data, textureIndex);
            try
            {
                return texture.GetPixel(0, 0);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void LeavesOutTheSecondUvSetNoFeatureSamples()
        {
            Body.sharedMesh.uv2 = new[] { Vector2.zero, Vector2.zero, Vector2.zero };

            var (data, _, _) = Export();

            Assert.That(data.GLTF.meshes[0].primitives[0].attributes.TEXCOORD_1, Is.EqualTo(-1));
        }

        [Test]
        public void ReportsLilToonFeaturesTheVrmCannotCarry()
        {
            var material = Skin;
            material.SetFloat("_UseBacklight", 1);
            material.SetFloat("_UseMain2ndTex", 1);
            material.SetFloat("_Main2ndTex_UVMode", 2);
            material.SetFloat("_OutlineVertexR2Width", 1);
            material.SetFloat("_UseAudioLink", 1);

            var (_, _, report) = Export();

            Assert.That(report.Notes, Is.SupersetOf(new[]
            {
                new Message("Report:NotInMToon", new Term("Feature:Backlight"), "Skin"),
                new Message("Report:NotInMToon", new Term("Feature:Main2nd"), "Skin"),
                new Message("Report:TexCoordNotExported", 2, new Term("Feature:Main2nd"), "Skin"),
                new Message("Report:VertexColorsNotExported", "Skin"),
                new Message("Report:AudioLink", "Skin"),
            }));
        }

        [Test]
        public void SkipsPerfectSyncWithoutTheCoreShapeKeys()
        {
            Recreate(shapeKeys: new[] { "Blink", "JawOpen", "mouthClose", "cheekPuff" });

            var (_, vrm, report) = Export();

            Assert.That(vrm.VrmExtension.Expressions.Custom ?? new System.Collections.Generic.Dictionary<string, UniGLTF.Extensions.VRMC_vrm.Expression>(), Is.Empty);
            Assert.That(report.Notes, Has.Some.Matches<Message>(note => note.Key == "Report:PerfectSyncSkipped" && (string)note.Args[1] == "eyeBlinkLeft, eyeBlinkRight"));
        }

        [Test]
        public void FindsBlinkByNameWithoutEyeLookSettings()
        {
            Descriptor.enableEyeLook = false;

            var (_, vrm, report) = Export();

            Assert.That(vrm.VrmExtension.Expressions.Preset.Blink.MorphTargetBinds, Has.Count.EqualTo(1));
            Assert.That(report.Notes, Has.Some.EqualTo(new Message("Report:ShapeKeyFound", "Blink", "Body", new Term("Purpose:Blink"))));
        }

        [Test]
        public void ExportsPhysBoneAsSpringWithCollider()
        {
            var (data, _, _) = Export();
            var springBone = SpringBone(data);

            var spring = springBone.Springs.Single();
            Assert.That(spring.Joints, Has.Count.EqualTo(3), "Ponytail, Ponytail1 and Ponytail2");
            Assert.That(spring.Joints[0].Stiffness, Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(spring.Joints[0].DragForce, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(springBone.Colliders.First().Shape.Sphere.Radius, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(spring.Center, Is.Null, "a mobile PhysBone swings in the world");
            Assert.That(data.GLTF.extensionsUsed, Does.Not.Contain(ImmobileExtension.Name));
        }

        /// <summary>The Immobile extension's entries as joint name, reference name and value.</summary>
        private static (string Joint, string Reference, double Immobile)[] ImmobileEntries(GltfData data)
        {
            var root = data.Json.ParseAsJson()["extensions"][ImmobileExtension.Name];
            Assert.That(root["specVersion"].GetString(), Is.EqualTo(ImmobileExtension.SpecVersion));
            return root["joints"].ArrayItems()
                .Select(joint => (data.GLTF.nodes[joint["node"].GetInt32()].name, data.GLTF.nodes[joint["reference"].GetInt32()].name, joint["immobile"].GetDouble()))
                .ToArray();
        }

        [TestCase(VRCPhysBoneBase.ImmobileType.AllMotion, "Head")]
        [TestCase(VRCPhysBoneBase.ImmobileType.World, "Hips")]
        public void MeasuresAnImmobilePhysBoneInWhatItIsImmobileTo(VRCPhysBoneBase.ImmobileType type, string center)
        {
            var physBone = PhysBone;
            physBone.immobileType = type;
            physBone.immobile = 0.7f;

            var (data, _, _) = Export();

            var springBone = SpringBone(data);
            Assert.That(data.GLTF.nodes[springBone.Springs.Single().Center.Value].name, Is.EqualTo(center));
            Assert.That(data.GLTF.extensionsUsed, Contains.Item(ImmobileExtension.Name));
            var entries = ImmobileEntries(data);
            Assert.That(entries.Select(entry => entry.Joint), Is.EqualTo(new[] { "Ponytail", "Ponytail1" }), "Ponytail2 is only a tail");
            Assert.That(entries.Select(entry => entry.Reference), Is.All.EqualTo(center));
            Assert.That(entries.Select(entry => entry.Immobile), Is.All.EqualTo(0.7).Within(1e-5));
        }

        /// <summary>A PhysBone less than half immobile swings in the world for readers without the extension.</summary>
        [Test]
        public void KeepsALittleImmobileOnlyInTheExtension()
        {
            var physBone = PhysBone;
            physBone.immobile = 0.3f;

            var (data, _, _) = Export();

            var springBone = SpringBone(data);
            Assert.That(springBone.Springs.Single().Center, Is.Null);
            var entries = ImmobileEntries(data);
            Assert.That(entries.Select(entry => entry.Reference), Is.All.EqualTo("Head"));
            Assert.That(entries.Select(entry => entry.Immobile), Is.All.EqualTo(0.3).Within(1e-5));
        }

        [Test]
        public void MeasuresANestedPhysBoneAboveTheSpringItHangsIn()
        {
            var ponytail1 = _avatar.transform.Find("Hips/Spine/Chest/Neck/Head/Ponytail/Ponytail1");
            var ribbon = new GameObject("Ribbon").transform;
            ribbon.SetParent(ponytail1, false);
            new GameObject("Ribbon1").transform.SetParent(ribbon, false);
            ribbon.GetChild(0).localPosition = new Vector3(0, -0.05f, 0);
            var ponytail = PhysBone;
            ponytail.ignoreTransforms.Add(ribbon);
            var ribbonPhysBone = ribbon.gameObject.AddComponent<VRCPhysBone>();
            ribbonPhysBone.immobile = 1;

            var (data, _, _) = Export();

            var springBone = SpringBone(data);
            var spring = springBone.Springs.Single(spring => data.GLTF.nodes[spring.Joints[0].Node.Value].name == "Ribbon");
            Assert.That(data.GLTF.nodes[spring.Center.Value].name, Is.EqualTo("Head"), "Ponytail1 and Ponytail swing in the ponytail's spring");
            Assert.That(ImmobileEntries(data).Single().Reference, Is.EqualTo("Head"));
        }

        /// <summary>
        /// The VRM limit points where PhysBone's does in the world, taking PhysBone's own frame from the SDK, on a tilted
        /// bone whose frame the export normalizes away.
        /// </summary>
        [TestCase(VRCPhysBoneBase.LimitType.Angle, "cone")]
        [TestCase(VRCPhysBoneBase.LimitType.Hinge, "hinge")]
        [TestCase(VRCPhysBoneBase.LimitType.Polar, "spherical")]
        public void ExportsPhysBoneLimitsPointingTheSameWay(VRCPhysBoneBase.LimitType type, string shape)
        {
            var ponytail = _avatar.transform.Find("Hips/Spine/Chest/Neck/Head/Ponytail");
            ponytail.localRotation = Quaternion.Euler(20, 30, 10);
            var physBone = ponytail.GetComponent<VRCPhysBone>();
            physBone.limitType = type;
            physBone.maxAngleX = 40;
            physBone.maxAngleZ = 20;
            physBone.limitRotation = new Vector3(15, 50, -25);
            var boneVector = ponytail.GetChild(0).position - ponytail.position;
            PhysBoneManager.CalcLimitAxis(ponytail.InverseTransformDirection(boneVector), physBone.limitRotation, out var axisX, out var axisY);
            var expectedX = Gltf(ponytail.TransformDirection(axisX).normalized);
            var expectedY = Gltf(ponytail.TransformDirection(axisY).normalized);

            var (data, _, _) = Export();

            var extension = data.Json.ParseAsJson()["extensions"]["VRMC_springBone"]["springs"][0]["joints"][0]["extensions"]["VRMC_springBone_limit"];
            Assert.That(extension["specVersion"].GetString(), Is.EqualTo("1.0"));
            var limit = extension["limit"][shape];
            var r = limit["rotation"];
            var offset = new Quaternion((float)r[0].GetDouble(), (float)r[1].GetDouble(), (float)r[2].GetDouble(), (float)r[3].GetDouble());
            var space = PhysBoneConverter.DefaultLimitOrientation(Gltf(boneVector.normalized)) * offset;
            // Mirroring Unity's space into glTF's turns the limit's X (and Z) around, which no limit tells apart:
            // cones are round, hinges turn about X and spherical limits are symmetric in pitch and yaw.
            Assert.That(Mathf.Abs(Vector3.Dot(space * Vector3.right, expectedX)), Is.EqualTo(1).Within(1e-3f));
            Assert.That(Vector3.Distance(space * Vector3.up, expectedY), Is.LessThan(1e-3f));
            var angle = limit[shape == "spherical" ? "pitch" : "angle"].GetDouble();
            Assert.That(angle, Is.EqualTo(40 * Mathf.Deg2Rad).Within(1e-5));
        }

        /// <summary>Unity's space to glTF's, which mirrors X.</summary>
        private static Vector3 Gltf(Vector3 v) => new(-v.x, v.y, v.z);

        [Test]
        public void NamesEachJointOnce()
        {
            var body = Body;
            var head = body.bones[0];
            body.sharedMesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1 },
                new BoneWeight { boneIndex0 = 0, weight0 = 1 },
            };
            body.sharedMesh.bindposes = new[] { body.sharedMesh.bindposes[0], body.sharedMesh.bindposes[0] };
            body.bones = new[] { head, head };

            var (data, _, _) = Export();

            var joints = data.GLTF.skins.Single().joints;
            Assert.That(joints, Is.Unique);
        }

        /// <summary>A chain of high spring is still damped enough to settle: spring 0.95 alone would give a drag of 0.03.</summary>
        [Test]
        public void DampsAChainOfHighSpring()
        {
            var physBone = PhysBone;
            physBone.spring = 0.95f;

            var (data, _, _) = Export();

            var springBone = SpringBone(data);
            Assert.That(springBone.Springs.Single().Joints[0].DragForce, Is.EqualTo(0.2f).Within(1e-5f));
        }

        [Test]
        public void ClampsPhysBoneValuesToTheirRanges()
        {
            var physBone = PhysBone;
            physBone.radius = -0.5f;
            physBone.pull = 1.5f;

            var (data, _, _) = Export();

            var springBone = SpringBone(data);
            var joint = springBone.Springs.Single().Joints[0];
            Assert.That(joint.HitRadius, Is.EqualTo(0));
            Assert.That(joint.Stiffness, Is.EqualTo(4).Within(1e-5f));
        }

        [Test]
        public void ExportsCollidersOnTheAvatarRoot()
        {
            var floor = _avatar.AddComponent<VRCPhysBoneCollider>();
            floor.shapeType = VRCPhysBoneColliderBase.ShapeType.Plane;
            PhysBone.colliders.Add(floor);

            var (data, _, _) = Export();

            var springBone = SpringBone(data);
            Assert.That(springBone.Colliders, Has.Count.EqualTo(4));
            Assert.That(springBone.ColliderGroups.Single().Colliders, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public void KeepsColliderDirectionsOfRotatedBones()
        {
            var (data, _, _) = Export();
            var colliders = data.Json.ParseAsJson()["extensions"]["VRMC_springBone"]["colliders"];

            // The rotated bone turns local up to world -X; glTF mirrors X.
            var plane = colliders[1]["extensions"]["VRMC_springBone_extended_collider"]["shape"]["plane"]["normal"];
            Assert.That(plane[0].GetDouble(), Is.EqualTo(1).Within(1e-5));
            Assert.That(plane[1].GetDouble(), Is.EqualTo(0).Within(1e-5));

            var capsule = colliders[2]["shape"]["capsule"];
            Assert.That(capsule["offset"][0].GetDouble(), Is.EqualTo(-0.1).Within(1e-5), "the head cap lies 0.1 along world +X");
            Assert.That(capsule["tail"][0].GetDouble(), Is.EqualTo(0.1).Within(1e-5), "the tail cap lies 0.1 along world -X");
            Assert.That(capsule["tail"][1].GetDouble(), Is.EqualTo(0).Within(1e-5));
        }

        [Test]
        public void ExportsLilToonValuesBesideTheMToonFallback()
        {
            var (data, _, _) = Export();
            var json = data.Json.ParseAsJson();

            Assert.That(data.GLTF.extensionsUsed, Contains.Item(LilToonExtension.Name));
            Assert.That(data.GLTF.extensionsRequired ?? new System.Collections.Generic.List<string>(), Does.Not.Contain(LilToonExtension.Name));
            var root = json["extensions"][LilToonExtension.Name];
            Assert.That(root["specVersion"].GetString(), Is.EqualTo(LilToonExtension.SpecVersion));
            Assert.That(root["lilToonVersion"].GetString(), Is.EqualTo("2.3.4"));

            var material = json["materials"][0]["extensions"];
            Assert.That(material.ContainsKey("VRMC_materials_mtoon"), Is.True);
            var lilToon = material[LilToonExtension.Name];
            Assert.That(lilToon["shader"].GetString(), Is.EqualTo("Hidden/lilToonCutoutOutline"));
            Assert.That(lilToon["floats"]["_OutlineWidth"].GetDouble(), Is.EqualTo(0.1).Within(1e-6));
            var shadowColor = lilToon["colors"]["_ShadowColor"];
            Assert.That(shadowColor[0].GetDouble(), Is.EqualTo(Mathf.GammaToLinearSpace(0.8f)).Within(1e-4), "colors are linear");
            var mainTexture = lilToon["textures"]["_MainTex"]["index"].GetInt32();
            Assert.That(mainTexture, Is.InRange(0, data.GLTF.textures.Count - 1));
        }

        /// <summary>
        /// lilToon grows an outline along normals that skinning leaves at the length of the mesh's scale, so a mesh
        /// modeled in centimeters draws its outline a hundredth as wide as <c>_OutlineWidth</c> says.
        /// </summary>
        [Test]
        public void ExportsTheOutlineWidthInTheAvatarsScale()
        {
            TestAvatar.AddCentimeterScarf(_avatar);

            var (data, _, _) = Export();

            var json = data.Json.ParseAsJson();
            var material = json["materials"][data.GLTF.materials.FindIndex(material => material.name == "Scarf")]["extensions"];
            Assert.That(material["VRMC_materials_mtoon"]["outlineWidthFactor"].GetDouble(), Is.EqualTo(0.0002).Within(1e-7));
            Assert.That(material[LilToonExtension.Name]["floats"]["_OutlineWidth"].GetDouble(), Is.EqualTo(0.02).Within(1e-6));
        }

        [Test]
        public void LeavesTheSourceAvatarUnchanged()
        {
            var ponytail = _avatar.transform.Find("Hips/Spine/Chest/Neck/Head/Ponytail");

            Export();

            var material = Skin;
            Assert.That(AssetDatabase.GetAssetPath(material), Is.EqualTo(TestAvatar.AssetFolder + "/Skin.mat"));
            Assert.That(material.shader.name, Is.EqualTo("Hidden/lilToonCutoutOutline"));
            Assert.That(ponytail.GetComponent<VRM10SpringBoneJoint>(), Is.Null);
            Assert.That(_avatar.GetComponent<Vrm10Instance>(), Is.Null);
        }

        [Test]
        public void ReportsWhatCouldNotBeCarriedOver()
        {
            var (_, _, report) = Export();
            Assert.That(report.Notes, Has.Some.Matches<Message>(note => note.Key == "Report:PerfectSyncPartial"));
        }
    }
}
