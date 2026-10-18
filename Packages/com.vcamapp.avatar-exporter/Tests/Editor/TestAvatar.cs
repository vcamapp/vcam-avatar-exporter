using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// A minimal humanoid avatar built in code, so tests do not depend on third-party avatars:
    /// a T-pose skeleton, one skinned mesh with viseme, blink and ARKit shape keys, a lilToon material,
    /// and a PhysBone on a two-bone ponytail that collides with the head. Its mesh, material and texture are saved
    /// as assets like a real avatar's, because NDMF moves in-memory objects into its temporary folder and deletes them.
    /// </summary>
    internal static class TestAvatar
    {
        internal const string AssetFolder = "Assets/VCamAvatarExporterTests";
        internal const string DefaultLilToonShader = "Hidden/lilToonCutoutOutline";

        internal static readonly string[] ShapeKeys =
        {
            "vrc.v_aa", "vrc.v_e", "vrc.v_ih", "vrc.v_oh", "vrc.v_ou", "Blink", "eyeBlinkLeft", "EyeBlink_R", "JawOpen", "mouthClose", "BodyShape",
        };

        internal static GameObject Create(string lilToonShader = DefaultLilToonShader, string[] shapeKeys = null)
        {
            // A run stopped before Destroy leaves the folder behind, and CreateFolder would then make a numbered one beside it.
            AssetDatabase.DeleteAsset(AssetFolder);
            AssetDatabase.CreateFolder("Assets", "VCamAvatarExporterTests");
            var root = new GameObject("TestAvatar");
            var bones = CreateSkeleton(root.transform);
            var animator = root.AddComponent<Animator>();
            animator.avatar = AvatarBuilder.BuildHumanAvatar(root, HumanDescription(root.transform, bones));

            var body = CreateBody(root.transform, bones["Head"], lilToonShader, shapeKeys ?? ShapeKeys);
            CreateDescriptor(root, body);
            CreatePhysBone(bones["Ponytail"], bones["Head"]);
            return root;
        }

        /// <summary>
        /// Gives the avatar an FX layer playing the clips, which ease in from zero as facial clips often do.
        /// "ToggleOutfit" also turns the body off, like an outfit toggle.
        /// </summary>
        internal static void AddFxClips(params (string Name, (string Property, float Value)[] Curves)[] clips)
        {
            var avatar = Object.FindObjectOfType<VRCAvatarDescriptor>();
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(AssetFolder + "/FX.controller");
            foreach (var (name, curves) in clips)
            {
                var clip = new AnimationClip { name = name };
                foreach (var (property, value) in curves)
                {
                    clip.SetCurve("Body", typeof(SkinnedMeshRenderer), property, AnimationCurve.Linear(0, 0, 0.1f, value));
                }
                if (name == "ToggleOutfit")
                {
                    clip.SetCurve("Body", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 0, 0));
                }
                AssetDatabase.AddObjectToAsset(clip, controller);
                controller.AddMotion(clip);
            }
            UseFxController(avatar, controller);
        }

        /// <summary>
        /// Moves the mouth from FX layer clips the Viseme parameter picks, as avatars with parameter-only lip sync do:
        /// an AnyState transition for each of "aa" (10) and "oh" (13) to a clip setting that viseme's shape key.
        /// </summary>
        internal static void AddVisemeAnimations(GameObject avatar)
        {
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(AssetFolder + "/LipSync.controller");
            controller.AddParameter("Viseme", AnimatorControllerParameterType.Int);
            var machine = controller.layers[0].stateMachine;
            foreach (var (viseme, shapeKey) in new[] { (10, "vrc.v_aa"), (13, "vrc.v_oh") })
            {
                var clip = new AnimationClip { name = shapeKey };
                clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shapeKey, AnimationCurve.Constant(0, 0, 100));
                AssetDatabase.AddObjectToAsset(clip, controller);
                var state = machine.AddState(shapeKey);
                state.motion = clip;
                machine.AddAnyStateTransition(state).AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, viseme, "Viseme");
            }
            UseFxController(descriptor, controller);
        }

        private static void UseFxController(VRCAvatarDescriptor descriptor, RuntimeAnimatorController controller)
        {
            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = controller },
            };
        }

        /// <summary>
        /// Dresses the avatar in a shirt with its own spine bone and a ribbon bone the avatar lacks, merged onto the
        /// avatar's bones by Modular Avatar as outfits for VRChat avatars are.
        /// </summary>
        internal static void AddMergeArmatureOutfit(GameObject avatar)
        {
            var avatarHips = avatar.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
            var outfit = new GameObject("Outfit").transform;
            outfit.SetParent(avatar.transform, false);
            var hips = new GameObject("Hips").transform;
            hips.SetParent(outfit, false);
            hips.localPosition = avatarHips.localPosition;
            var spine = new GameObject("Spine").transform;
            spine.SetParent(hips, false);
            spine.localPosition = new Vector3(0, 0.1f, 0);
            var ribbon = new GameObject("Ribbon").transform;
            ribbon.SetParent(spine, false);
            ribbon.localPosition = new Vector3(0, 0.2f, 0.1f);

            var mesh = new Mesh { name = "Shirt" };
            mesh.vertices = new[] { new Vector3(-0.1f, 1.1f, 0.1f), new Vector3(0.1f, 1.1f, 0.1f), new Vector3(0, 1.2f, 0.1f) };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1 };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                new BoneWeight { boneIndex0 = 1, weight0 = 1 },
            };
            mesh.bindposes = new[] { spine.worldToLocalMatrix * outfit.localToWorldMatrix, ribbon.worldToLocalMatrix * outfit.localToWorldMatrix };
            AssetDatabase.CreateAsset(mesh, AssetFolder + "/Shirt.asset");
            var shirt = new GameObject("Shirt").AddComponent<SkinnedMeshRenderer>();
            shirt.transform.SetParent(outfit, false);
            shirt.sharedMesh = mesh;
            shirt.bones = new[] { spine, ribbon };
            shirt.rootBone = spine;
            shirt.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetFolder + "/Skin.mat");

            hips.gameObject.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarMergeArmature>().mergeTarget.Set(avatarHips.gameObject);
        }

        /// <summary>A mesh in front of the eyes whose lilToon material writes only depth, as ambient occlusion masks do.</summary>
        internal static void AddDepthOnlyMesh(GameObject avatar) =>
            AddQuad(avatar, "AOMask", "Hidden/lilToonTransparent").sharedMaterial.SetFloat("_ColorMask", 0);

        /// <summary>
        /// A face that writes stencil 13 and bangs over its upper half whose fake shadow falls on what wrote 13, drawn
        /// past the bangs' submesh as lilToon avatars shade the face under their bangs.
        /// </summary>
        internal static void AddBangsShadow(GameObject avatar)
        {
            var face = AddQuad(avatar, "Face", "lilToon");
            face.sharedMaterial.SetFloat("_StencilRef", 13);
            face.sharedMaterial.SetFloat("_StencilPass", (float)UnityEngine.Rendering.StencilOp.Replace);

            var bangs = AddQuad(avatar, "Bangs", "lilToon");
            var shadow = CreateMaterial("BangsShadow", "_lil/[Optional] lilToonFakeShadow");
            shadow.SetColor("_Color", Color.gray);
            shadow.SetVector("_FakeShadowVector", new Vector4(1, 0, 0, 0.05f));
            shadow.SetFloat("_StencilRef", 13);
            shadow.SetFloat("_StencilComp", (float)UnityEngine.Rendering.CompareFunction.Equal);
            bangs.sharedMaterials = new[] { bangs.sharedMaterial, shadow };

            // A quad faces -Z and an avatar +Z.
            face.transform.SetLocalPositionAndRotation(new Vector3(0, 1.5f, 0.2f), Quaternion.Euler(0, 180, 0));
            bangs.transform.SetLocalPositionAndRotation(new Vector3(0, 1.75f, 0.25f), Quaternion.Euler(0, 180, 0));
            bangs.transform.localScale = new Vector3(1, 0.5f, 1);
        }

        /// <summary>
        /// A scarf modeled in centimeters, skinned to the head under a renderer scaled to a hundredth as FBX files in
        /// centimeters are, whose lilToon outline is 2.
        /// </summary>
        internal static void AddCentimeterScarf(GameObject avatar)
        {
            var head = avatar.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            var scarf = new GameObject("Scarf").AddComponent<SkinnedMeshRenderer>();
            scarf.transform.SetParent(avatar.transform, false);
            scarf.transform.localScale = Vector3.one * 0.01f;

            var mesh = new Mesh { name = "Scarf" };
            mesh.vertices = new[] { new Vector3(-10, 120, 10), new Vector3(10, 120, 10), new Vector3(0, 130, 10) };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1 };
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 3).ToArray();
            mesh.bindposes = new[] { head.worldToLocalMatrix * scarf.transform.localToWorldMatrix };
            AssetDatabase.CreateAsset(mesh, AssetFolder + "/Scarf.asset");

            scarf.sharedMesh = mesh;
            scarf.bones = new[] { head };
            scarf.rootBone = head;
            var material = CreateMaterial("Scarf", DefaultLilToonShader);
            material.SetFloat("_OutlineWidth", 2);
            scarf.sharedMaterial = material;
        }

        /// <summary>A quad under the avatar with a lilToon material of its own, saved as an asset.</summary>
        internal static MeshRenderer AddQuad(GameObject avatar, string name, string lilToonShader)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(avatar.transform, false);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateMaterial(name, lilToonShader);
            return renderer;
        }

        /// <summary>A material of the shader, saved as an asset under the test folder.</summary>
        internal static Material CreateMaterial(string name, string shader)
        {
            var material = new Material(Shader.Find(shader)) { name = name };
            AssetDatabase.CreateAsset(material, $"{AssetFolder}/{name}.mat");
            return material;
        }

        internal static void Destroy(GameObject avatar)
        {
            Object.DestroyImmediate(avatar);
            AssetDatabase.DeleteAsset(AssetFolder);
        }

        private static Dictionary<string, Transform> CreateSkeleton(Transform root)
        {
            var bones = new Dictionary<string, Transform>();
            Transform Bone(string name, string parent, float x, float y, float z)
            {
                var bone = new GameObject(name).transform;
                bone.SetParent(parent == null ? root : bones[parent], false);
                bone.localPosition = new Vector3(x, y, z);
                bones[name] = bone;
                return bone;
            }

            Bone("Hips", null, 0, 0.9f, 0);
            Bone("Spine", "Hips", 0, 0.1f, 0);
            Bone("Chest", "Spine", 0, 0.15f, 0);
            Bone("Neck", "Chest", 0, 0.2f, 0);
            Bone("Head", "Neck", 0, 0.1f, 0);
            Bone("LeftEye", "Head", -0.03f, 0.06f, 0.06f);
            Bone("RightEye", "Head", 0.03f, 0.06f, 0.06f);
            Bone("Ponytail", "Head", 0, 0.08f, -0.08f);
            Bone("Ponytail1", "Ponytail", 0, -0.1f, 0);
            Bone("Ponytail2", "Ponytail1", 0, -0.1f, 0);
            foreach (var (side, sign) in new[] { ("Left", -1f), ("Right", 1f) })
            {
                Bone(side + "UpperLeg", "Hips", sign * 0.1f, -0.05f, 0);
                Bone(side + "LowerLeg", side + "UpperLeg", 0, -0.4f, 0);
                Bone(side + "Foot", side + "LowerLeg", 0, -0.4f, 0);
                Bone(side + "UpperArm", "Chest", sign * 0.15f, 0.15f, 0);
                Bone(side + "LowerArm", side + "UpperArm", sign * 0.25f, 0, 0);
                Bone(side + "Hand", side + "LowerArm", sign * 0.25f, 0, 0);
            }
            return bones;
        }

        private static HumanDescription HumanDescription(Transform root, Dictionary<string, Transform> bones)
        {
            var human = bones.Keys
                .Select(name => (Name: name, IsHuman: System.Enum.TryParse<HumanBodyBones>(name, out var bone), Bone: bone))
                .Where(pair => pair.IsHuman)
                .Select(pair => new HumanBone { boneName = pair.Name, humanName = HumanTrait.BoneName[(int)pair.Bone], limit = new HumanLimit { useDefaultValues = true } })
                .ToArray();
            var skeleton = root.GetComponentsInChildren<Transform>()
                .Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale })
                .ToArray();
            return new HumanDescription { human = human, skeleton = skeleton };
        }

        private static SkinnedMeshRenderer CreateBody(Transform root, Transform head, string lilToonShader, string[] shapeKeys)
        {
            var mesh = new Mesh { name = "Body" };
            mesh.vertices = new[] { new Vector3(-0.1f, 1.3f, 0.1f), new Vector3(0.1f, 1.3f, 0.1f), new Vector3(0, 1.5f, 0.1f) };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1 };
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 3).ToArray();
            mesh.bindposes = new[] { head.worldToLocalMatrix * root.localToWorldMatrix };
            foreach (var name in shapeKeys)
            {
                var deltas = new[] { Vector3.up * 0.01f, Vector3.zero, Vector3.zero };
                mesh.AddBlendShapeFrame(name, 100, deltas, null, null);
            }

            AssetDatabase.CreateAsset(mesh, AssetFolder + "/Body.asset");

            var body = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
            body.transform.SetParent(root, false);
            body.sharedMesh = mesh;
            body.bones = new[] { head };
            body.rootBone = head;

            var texture = new Texture2D(4, 4) { name = "Main" };
            AssetDatabase.CreateAsset(texture, AssetFolder + "/Main.asset");
            var material = CreateMaterial("Skin", lilToonShader);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_ShadowColor", new Color(0.8f, 0.6f, 0.6f));
            material.SetFloat("_OutlineWidth", 0.1f);
            body.sharedMaterial = material;
            return body;
        }

        private static void CreateDescriptor(GameObject root, SkinnedMeshRenderer body)
        {
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
            descriptor.VisemeSkinnedMesh = body;
            descriptor.VisemeBlendShapes = new[]
            {
                "-none-", "-none-", "-none-", "-none-", "-none-", "-none-", "-none-", "-none-", "-none-", "-none-",
                "vrc.v_aa", "vrc.v_e", "vrc.v_ih", "vrc.v_oh", "vrc.v_ou",
            };
            descriptor.enableEyeLook = true;
            var eyeLook = descriptor.customEyeLookSettings;
            eyeLook.eyelidType = VRCAvatarDescriptor.EyelidType.Blendshapes;
            eyeLook.eyelidsSkinnedMesh = body;
            eyeLook.eyelidsBlendshapes = new[] { body.sharedMesh.GetBlendShapeIndex("Blink"), -1, -1 };
            descriptor.customEyeLookSettings = eyeLook;
        }

        private static void CreatePhysBone(Transform ponytail, Transform head)
        {
            var collider = head.gameObject.AddComponent<VRCPhysBoneCollider>();
            collider.shapeType = VRC.Dynamics.VRCPhysBoneColliderBase.ShapeType.Sphere;
            collider.radius = 0.1f;

            // Colliders on a rotated bone: their directions must survive the export's bone normalization.
            var rotated = new GameObject("RotatedColliders").transform;
            rotated.SetParent(head, false);
            rotated.localRotation = Quaternion.Euler(0, 0, 90);
            var floor = rotated.gameObject.AddComponent<VRCPhysBoneCollider>();
            floor.shapeType = VRC.Dynamics.VRCPhysBoneColliderBase.ShapeType.Plane;
            var capsule = rotated.gameObject.AddComponent<VRCPhysBoneCollider>();
            capsule.shapeType = VRC.Dynamics.VRCPhysBoneColliderBase.ShapeType.Capsule;
            capsule.radius = 0.05f;
            capsule.height = 0.3f;

            var physBone = ponytail.gameObject.AddComponent<VRCPhysBone>();
            physBone.pull = 0.2f;
            physBone.spring = 0.5f;
            physBone.gravity = 0.25f;
            physBone.radius = 0.02f;
            physBone.colliders = new List<VRC.Dynamics.VRCPhysBoneColliderBase> { collider, floor, capsule };
        }
    }
}
