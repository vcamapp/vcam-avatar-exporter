using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using nadena.dev.ndmf;
using UniGLTF;
using UniGLTF.Extensions.VRMC_vrm;
using UniGLTF.MeshUtility;
using UniJSON;
using UnityEditor.PackageManager;
using UnityEngine;
using UniVRM10;
using VrmLib;
using Object = UnityEngine.Object;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Exports an avatar as VRM 1.0. Everything runs on a copy of the avatar, which non-destructive tools process
    /// first so the export has the outfit the user sees in the scene.
    /// </summary>
    internal static class AvatarExporter
    {
        internal static ExportReport Export(GameObject avatar, ExportOptions options, string path)
        {
            var problems = DependencyVersions.FindProblems();
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(string.Join("\n", problems));
            }

            var report = new ExportReport();
            var copy = Object.Instantiate(avatar);
            copy.name = avatar.name;
            var created = new List<Object>();
            try
            {
                AvatarProcessor.ProcessAvatar(copy);
                HierarchyCleanup.RestoreNames(copy);
                if (!copy.TryGetComponent<Animator>(out var animator) || !animator.isHuman)
                {
                    throw new InvalidOperationException(new Message("Error:NoHumanoid", avatar.name).ToString());
                }
                if (!InternalTPose.TryMakePoseValid(copy))
                {
                    report.Add("Report:NoTPose");
                }

                var textures = new TextureLimiter(options.MaxTextureSize, created);
                var lilToonSources = ReplaceMaterials(copy, textures, created, report);
                Tangents.RemoveNormalMapsWithoutTangents(copy);
                var vrm = ScriptableObject.CreateInstance<VRM10Object>();
                created.Add(vrm);
                options.Meta.CopyTo(vrm.Meta);
                vrm.Expression = ExpressionConverter.Convert(copy, options.SplitsBlink, created, report);
                vrm.LookAt = LookAt(animator);
                created.AddRange(vrm.Expression.Clips.Select(clip => clip.Clip));

                var instance = copy.AddComponent<Vrm10Instance>();
                instance.UpdateType = Vrm10Instance.UpdateTypes.None;
                instance.Vrm = vrm;
                var (springBone, immobileJoints) = PhysBoneConverter.Convert(copy, report);
                instance.SpringBone = springBone;
                copy.GetComponent<UniHumanoid.Humanoid>().AssignBonesFromAnimator();
                HierarchyCleanup.RemoveLeftovers(copy);

                File.WriteAllBytes(path, Write(copy, lilToonSources, immobileJoints, textures, created, report));
            }
            finally
            {
                Object.DestroyImmediate(copy);
                foreach (var obj in created)
                {
                    Object.DestroyImmediate(obj);
                }
                // NDMF saves what it generates, and any in-memory object the avatar uses, into a temporary folder.
                // An upload build deletes the folder the same way.
                AvatarProcessor.CleanTemporaryAssets();
            }
            return report;
        }

        private static void RemoveRenderer(Renderer renderer)
        {
            if (renderer.TryGetComponent<MeshFilter>(out var filter))
            {
                Object.DestroyImmediate(filter);
            }
            Object.DestroyImmediate(renderer);
        }

        /// <summary>
        /// Swaps the materials of the shown renderers for their MToon fallbacks and returns which lilToon material each
        /// fallback replaced.
        /// </summary>
        private static Dictionary<Material, LilToonSource> ReplaceMaterials(GameObject root, TextureLimiter textures, List<Object> created, ExportReport report)
        {
            var fallbacks = new Dictionary<Material, Material>();
            var lilToonSources = new Dictionary<Material, LilToonSource>();
            var stencilOverlays = new HashSet<Material>();
            var features = new LilToonFeatureReport();

            Material FallbackFor(Material source, Renderer renderer)
            {
                if (fallbacks.TryGetValue(source, out var fallback))
                {
                    return fallback;
                }
                var hidden = stencilOverlays.Contains(source);
                // Apps that show the MToon fallback show nothing of a hidden one, so how its look falls short there is not reported.
                fallback = MToonFallback.Create(source, hidden ? new ExportReport() : report, created);
                float? hiddenAlpha = null;
                if (hidden)
                {
                    hiddenAlpha = MToonFallback.Hide(fallback);
                    report.Add("Report:StencilHidden", source.name);
                }
                created.Add(fallback);
                foreach (var name in fallback.GetTexturePropertyNames())
                {
                    fallback.SetTexture(name, textures.Limit(fallback.GetTexture(name)));
                }
                fallbacks[source] = fallback;
                if (LilToonShader.TryGet(source, out var shader))
                {
                    var values = shader.IsFakeShadow ? FakeShadowInAvatarAxes(source, renderer, root, created) : source;
                    lilToonSources[fallback] = new LilToonSource(values, shader, hiddenAlpha);
                    features.Check(source, shader);
                }
                return fallback;
            }

            // UniVRM writes only the renderers that are shown, so hidden ones (outfits toggled off, props for idle
            // animations) are left as they are and kept out of the report.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>().Where(renderer => renderer.enabled))
            {
                var materials = renderer.sharedMaterials;
                // Unity draws nothing for an empty material slot, but UniVRM writes the mesh, which glTF draws in its
                // default white. Avatars keep such renderers as markers, such as the avatar's height.
                if (materials.All(material => material == null))
                {
                    RemoveRenderer(renderer);
                    continue;
                }
                // Such meshes only shape the depth or stencil for other materials, as an eye's ambient occlusion mask does.
                if (materials.All(material => material != null && LilToonShader.TryGet(material, out _) && LilToonFeatures.WritesNoColor(material)))
                {
                    report.Add("Report:NoColor", renderer.name);
                    RemoveRenderer(renderer);
                    continue;
                }
                stencilOverlays.UnionWith(SplitMaterialsPastSubmeshes(renderer, created, report));
                renderer.sharedMaterials = renderer.sharedMaterials.Select(source => source == null ? null : FallbackFor(source, renderer)).ToArray();
            }
            features.WriteTo(report);
            MToonFallback.OrderTransparent(fallbacks);
            return lilToonSources;
        }

        /// <summary>
        /// lilToon shifts a fake shadow along <c>_FakeShadowVector</c> in the axes of the mesh, which the export does
        /// not keep, so the values are written from a copy that holds the vector in the avatar's axes.
        /// </summary>
        private static Material FakeShadowInAvatarAxes(Material source, Renderer renderer, GameObject root, List<Object> created)
        {
            var vector = source.GetVector("_FakeShadowVector");
            // lilToon reads unity_ObjectToWorld, which a skinned mesh's root bone sets.
            var axes = renderer is SkinnedMeshRenderer { rootBone: { } rootBone } ? rootBone : renderer.transform;
            var direction = root.transform.InverseTransformDirection(axes.TransformDirection(vector));
            var copy = new Material(source) { name = source.name };
            copy.SetVector("_FakeShadowVector", new Vector4(direction.x, direction.y, direction.z, vector.w));
            created.Add(copy);
            return copy;
        }

        /// <summary>
        /// Unity draws the materials past the last submesh over that submesh again, which effects such as see-through
        /// bangs and fake shadows use. A glTF primitive has one material, so each fake shadow and each material drawn
        /// only where a stencil allows gets a submesh of its own over the same triangles, and the other materials are
        /// left out: UniVRM would only carry their textures.
        /// </summary>
        /// <returns>The materials kept that draw only where a stencil allows, other than fake shadows.</returns>
        private static IEnumerable<Material> SplitMaterialsPastSubmeshes(Renderer renderer, List<Object> created, ExportReport report)
        {
            var mesh = renderer.GetMesh();
            var materials = renderer.sharedMaterials;
            if (mesh == null || materials.Length <= mesh.subMeshCount)
            {
                return Enumerable.Empty<Material>();
            }
            var extra = materials.Skip(mesh.subMeshCount).Where(material => material != null).ToList();
            var fakeShadows = extra.Where(material => LilToonShader.TryGet(material, out var shader) && shader.IsFakeShadow).ToList();
            var stencilOverlays = extra.Except(fakeShadows).Where(LilToonFeatures.DrawsWithStencil).ToList();
            var shadows = fakeShadows
                .Where(material => !LilToonFeatures.FakeShadowMultipliesNothing(material))
                .Concat(stencilOverlays)
                .ToList();
            var dropped = extra.Except(fakeShadows).Except(stencilOverlays).Select(material => material.name).ToList();
            if (dropped.Count > 0)
            {
                report.Add("Report:DrawsOverAgain", renderer.name, string.Join(", ", dropped));
            }
            if (shadows.Count > 0)
            {
                var split = Object.Instantiate(mesh);
                split.name = mesh.name;
                created.Add(split);
                var triangles = mesh.GetTriangles(mesh.subMeshCount - 1);
                split.subMeshCount = mesh.subMeshCount + shadows.Count;
                for (var i = 0; i < shadows.Count; i++)
                {
                    split.SetTriangles(triangles, mesh.subMeshCount + i, false);
                }
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.sharedMesh = split;
                }
                else
                {
                    renderer.GetComponent<MeshFilter>().sharedMesh = split;
                }
            }
            renderer.sharedMaterials = materials.Take(mesh.subMeshCount).Concat(shadows).ToArray();
            return stencilOverlays;
        }

        /// <summary>
        /// Looks from between the eyes. The offset is taken in world axes, which are the head's axes once the export
        /// normalizes the bones.
        /// </summary>
        private static VRM10ObjectLookAt LookAt(Animator animator)
        {
            var lookAt = new VRM10ObjectLookAt();
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (head == null || leftEye == null || rightEye == null)
            {
                lookAt.LookAtType = LookAtType.expression;
                return lookAt;
            }
            lookAt.LookAtType = LookAtType.bone;
            lookAt.OffsetFromHead = (leftEye.position + rightEye.position) / 2 - head.position;
            return lookAt;
        }

        /// <summary>UniVRM's export with mesh freezing, plus the lilToon and Immobile extensions.</summary>
        private static byte[] Write(GameObject root, IReadOnlyDictionary<Material, LilToonSource> lilToonSources, IReadOnlyList<ImmobileJoint> immobileJoints, TextureLimiter textures, List<Object> created, ExportReport report)
        {
            var settings = new GltfExportSettings
            {
                UseSparseAccessorForMorphTarget = true,
                ExportOnlyBlendShapePosition = true,
                DivideVertexBuffer = true,
            };

            using var arrayManager = new NativeArrayManager();
            // Bakes the current shape key weights, such as body shape adjustments, and clears bone rotations and
            // scales. Spring bones and the look-at offset are already in that frame, so UniVRM's geometry backup,
            // which would convert them again, is not used.
            var meshes = BoneNormalizer.NormalizeHierarchyFreezeMesh(root, bakeCurrentBlendShape: true);
            BoneNormalizer.Replace(root, meshes, KeepRotation: false);
            created.AddRange(meshes.Values.Select(info => info.Mesh));
            BlendShapeReducer.Reduce(root, root.GetComponent<Vrm10Instance>().Vrm.Expression, created);
            UniqueJoints.Apply(root);

            var converter = new ModelExporter();
            var model = converter.Export(settings, arrayManager, root);
            model.ConvertCoordinate(Coordinates.Vrm1, ignoreVrm: false);

            var materialExporter = new LilToonMaterialExporter(Vrm10MaterialExporterUtility.GetValidVrm10MaterialExporter(), lilToonSources, textures, report);
            using var exporter = new Vrm10Exporter(settings, materialExporter, new EditorTextureSerializer());
            exporter.Export(root, model, converter, new ExportArgs { sparse = true });
            SecondTexCoords.Write(exporter.Storage, converter, lilToonSources);
            Tangents.Write(exporter.Storage, converter);

            if (lilToonSources.Count > 0)
            {
                glTFExtensionExport.GetOrCreate(ref exporter.Storage.Gltf.extensions)
                    .Add(LilToonExtension.Name, LilToonExtension.Root(DependencyVersions.InstalledVersion(DependencyVersions.LilToonPackage), ExporterVersion));
            }
            if (immobileJoints.Count > 0)
            {
                glTFExtensionExport.GetOrCreate(ref exporter.Storage.Gltf.extensions)
                    .Add(ImmobileExtension.Name, ImmobileExtension.Root(immobileJoints, transform => model.Nodes.IndexOf(converter.Nodes[transform.gameObject])));
            }
            var formatter = new JsonFormatter();
            UniGLTF.GltfSerializer.Serialize(formatter, exporter.Storage.Gltf);
            // UniVRM writes the limit without the specVersion `VRMC_springBone_limit` requires.
            var json = Regex.Replace(formatter.ToString(), "\"VRMC_springBone_limit\"\\s*:\\s*\\{(?!\\s*\"specVersion\")", "$0\"specVersion\":\"1.0\",");
            return Glb.Create(GltfJsonUtil.FindUsedExtensionsAndUpdateJson(json), exporter.Storage.BinBytes).ToBytes();
        }

        private static string ExporterVersion => PackageInfo.FindForAssembly(typeof(AvatarExporter).Assembly)?.version ?? "unknown";
    }
}
