using System;
using System.Collections.Generic;
using System.Linq;
using UniVRM10;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Builds VRM expressions from the avatar descriptor's visemes and eyelids, and Perfect Sync expressions from
    /// shape keys named after ARKit's blend shapes.
    /// </summary>
    internal static class ExpressionConverter
    {
        /// <summary>ARKit's face blend shapes. Perfect Sync expressions use these names with the first letter capitalized.</summary>
        private static readonly string[] ArKitBlendShapes =
        {
            "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight",
            "cheekPuff", "cheekSquintLeft", "cheekSquintRight",
            "eyeBlinkLeft", "eyeBlinkRight",
            "eyeLookDownLeft", "eyeLookDownRight", "eyeLookInLeft", "eyeLookInRight",
            "eyeLookOutLeft", "eyeLookOutRight", "eyeLookUpLeft", "eyeLookUpRight",
            "eyeSquintLeft", "eyeSquintRight", "eyeWideLeft", "eyeWideRight",
            "jawForward", "jawLeft", "jawOpen", "jawRight",
            "mouthClose", "mouthDimpleLeft", "mouthDimpleRight",
            "mouthFrownLeft", "mouthFrownRight", "mouthFunnel", "mouthLeft",
            "mouthLowerDownLeft", "mouthLowerDownRight",
            "mouthPressLeft", "mouthPressRight", "mouthPucker", "mouthRight",
            "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
            "mouthSmileLeft", "mouthSmileRight", "mouthStretchLeft", "mouthStretchRight",
            "mouthUpperUpLeft", "mouthUpperUpRight",
            "noseSneerLeft", "noseSneerRight", "tongueOut",
        };

        // Indices into VRC_AvatarDescriptor.VisemeBlendShapes, which follows the Oculus viseme order
        // sil, PP, FF, TH, DD, kk, CH, SS, nn, RR, aa, E, I, O, U.
        private const int VisemeAa = 10;
        private const int VisemeE = 11;
        private const int VisemeI = 12;
        private const int VisemeO = 13;
        private const int VisemeU = 14;

        /// <summary>
        /// Unified Expressions names (VRCFaceTracking's shape keys) for the ARKit shapes they name differently,
        /// following VRCFaceTracking's ARKit compatibility table. The other ARKit shapes share their names.
        /// </summary>
        private static readonly Dictionary<string, string[]> UnifiedExpressionsNames = new()
        {
            ["eyeBlinkLeft"] = new[] { "EyeClosedLeft" },
            ["eyeBlinkRight"] = new[] { "EyeClosedRight" },
            ["mouthClose"] = new[] { "MouthClosed" },
            ["mouthRollUpper"] = new[] { "LipSuckUpper" },
            ["mouthRollLower"] = new[] { "LipSuckLower" },
            ["mouthFunnel"] = new[] { "LipFunnel" },
            ["mouthPucker"] = new[] { "LipPucker" },
            ["mouthShrugUpper"] = new[] { "MouthRaiserUpper" },
            ["mouthShrugLower"] = new[] { "MouthRaiserLower" },
        };

        /// <summary>The ARKit shape keys an app needs for Perfect Sync to move the eyes and mouth at all.</summary>
        private static readonly string[] PerfectSyncCore = { "eyeBlinkLeft", "eyeBlinkRight", "jawOpen", "mouthClose" };

        // Common shape key names for blinking, used when the avatar descriptor does not name one.
        private static readonly string[] BlinkNames = { "blink", "vrc.blink", "まばたき", "瞬き", "eye_close", "eyes_close", "目閉じ", "eye_toji" };
        // The ARKit and Unified Expressions keys close one eye too, as Perfect Sync avatars have them. These read
        // UnifiedExpressionsNames, so they are declared after it: static fields initialize in order.
        private static readonly string[] BlinkLeftNames = new[] { "vrc.blink_left", "blink_L", "eye_close_L", "eyes_close_L", "eye_toji_L" }.Concat(PerfectSyncNames("eyeBlinkLeft")).ToArray();
        private static readonly string[] BlinkRightNames = new[] { "vrc.blink_right", "blink_R", "eye_close_R", "eyes_close_R", "eye_toji_R" }.Concat(PerfectSyncNames("eyeBlinkRight")).ToArray();

        /// <summary>The shape key names an ARKit shape goes by: its own and its Unified Expressions names.</summary>
        private static IEnumerable<string> PerfectSyncNames(string arKitName) =>
            UnifiedExpressionsNames.GetValueOrDefault(arKitName, Array.Empty<string>()).Prepend(arKitName);

        /// <param name="splitsBlink">Makes a key for each eye from a blink that closes both, when the avatar has none.</param>
        /// <param name="created">Collects the meshes copied to hold those keys, which the caller destroys after export.</param>
        internal static VRM10ObjectExpression Convert(GameObject root, bool splitsBlink, ICollection<UnityEngine.Object> created, ExportReport report)
        {
            var expression = new VRM10ObjectExpression();
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer.sharedMesh != null)
                .ToList();
            SkinnedMeshRenderer face = null;
            if (descriptor != null)
            {
                face = ConvertVisemes(expression, root.transform, descriptor, renderers, report);
                // The face mesh comes first so a blink found by name is the face's.
                if (face != null && renderers.Remove(face))
                {
                    renderers.Insert(0, face);
                }
            }
            var shapeKeys = renderers.Select(renderer => (renderer, new ShapeKeyNames(renderer.sharedMesh))).ToList();
            var blink = ConvertBlink(expression, root, descriptor, shapeKeys, splitsBlink, created, report);
            ConvertPerfectSync(expression, root.transform, shapeKeys, report);
            if (descriptor != null)
            {
                FxExpressionConverter.Convert(expression, root.transform, descriptor, face ?? blink, report);
            }
            return expression;
        }

        /// <returns>The mesh the visemes are on, or null when the avatar has none.</returns>
        private static SkinnedMeshRenderer ConvertVisemes(VRM10ObjectExpression expression, Transform root, VRCAvatarDescriptor descriptor, List<SkinnedMeshRenderer> renderers, ExportReport report)
        {
            var mesh = descriptor.lipSync switch
            {
                VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape => ConvertVisemeShapeKeys(expression, root, descriptor, renderers, report),
                VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly => ConvertVisemeAnimations(expression, root, descriptor, report),
                _ => null,
            };
            if (mesh == null)
            {
                report.Add("Report:NoVisemes");
            }
            return mesh;
        }

        private static SkinnedMeshRenderer ConvertVisemeShapeKeys(VRM10ObjectExpression expression, Transform root, VRCAvatarDescriptor descriptor, List<SkinnedMeshRenderer> renderers, ExportReport report)
        {
            var names = descriptor.VisemeBlendShapes;
            if (names == null || names.Length <= VisemeU)
            {
                return null;
            }
            var mesh = descriptor.VisemeSkinnedMesh;
            // Some avatars name the shape keys and leave the mesh empty.
            if (mesh == null)
            {
                mesh = renderers.FirstOrDefault(renderer => renderer.sharedMesh.GetBlendShapeIndex(names[VisemeAa]) >= 0);
                if (mesh == null)
                {
                    return null;
                }
                report.Add("Report:VisemeMeshFound", mesh.name);
            }
            SetVowels(expression, (name, viseme) => Create(name, Binding(root, mesh, names[viseme])));
            return mesh;
        }

        /// <summary>
        /// An avatar with parameter-only lip sync animates its mouth itself, from FX layer clips the Viseme parameter
        /// picks. The clips of the five vowels become the mouth expressions.
        /// </summary>
        private static SkinnedMeshRenderer ConvertVisemeAnimations(VRM10ObjectExpression expression, Transform root, VRCAvatarDescriptor descriptor, ExportReport report)
        {
            var clips = FxExpressionConverter.VisemeClips(descriptor);
            if (!clips.TryGetValue(VisemeAa, out var aa) || FxExpressionConverter.AnimatedFace(root, aa) is not { } mesh)
            {
                return null;
            }
            var path = mesh.transform.RelativePathFrom(root);
            SetVowels(expression, (name, viseme) => Create(name,
                (clips.TryGetValue(viseme, out var clip) ? FxExpressionConverter.FaceBindings(clip, mesh, path) : null) ?? Array.Empty<MorphTargetBinding>()));
            report.Add("Report:VisemeAnimations", mesh.name);
            return mesh;
        }

        private static void SetVowels(VRM10ObjectExpression expression, Func<string, int, VRM10Expression> create)
        {
            expression.Aa = create("aa", VisemeAa);
            expression.Ih = create("ih", VisemeI);
            expression.Ou = create("ou", VisemeU);
            expression.Ee = create("ee", VisemeE);
            expression.Oh = create("oh", VisemeO);
        }

        /// <returns>The mesh the blink is on, or null when the avatar has none.</returns>
        private static SkinnedMeshRenderer ConvertBlink(VRM10ObjectExpression expression, GameObject root, VRCAvatarDescriptor descriptor, List<(SkinnedMeshRenderer Renderer, ShapeKeyNames Names)> renderers, bool splitsBlink, ICollection<UnityEngine.Object> created, ExportReport report)
        {
            var blink = DescriptorBlink(descriptor) ?? FindFirst(renderers, BlinkNames, report, "Purpose:Blink");
            if (blink is not { } both)
            {
                report.Add("Report:NoBlink");
                return null;
            }
            expression.Blink = Create("blink", Binding(root.transform, both.Renderer, both.Index));

            var left = FindFirst(renderers, BlinkLeftNames, report, "Purpose:BlinkLeft");
            var right = FindFirst(renderers, BlinkRightNames, report, "Purpose:BlinkRight");
            if (left == null || right == null)
            {
                if (!splitsBlink || !root.TryGetComponent<Animator>(out var animator))
                {
                    return both.Renderer;
                }
                var name = both.Renderer.sharedMesh.GetBlendShapeName(both.Index);
                var (leftIndex, rightIndex) = EyeBlinkSplitter.Split(both.Renderer, both.Index, animator, created);
                left = (both.Renderer, leftIndex);
                right = (both.Renderer, rightIndex);
                report.Add("Report:SplitBlink", name, both.Renderer.name);
            }
            expression.BlinkLeft = Create("blinkLeft", Binding(root.transform, left.Value.Renderer, left.Value.Index));
            expression.BlinkRight = Create("blinkRight", Binding(root.transform, right.Value.Renderer, right.Value.Index));
            return both.Renderer;
        }

        private static (SkinnedMeshRenderer Renderer, int Index)? DescriptorBlink(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null || !descriptor.enableEyeLook)
            {
                return null;
            }
            var settings = descriptor.customEyeLookSettings;
            var mesh = settings.eyelidsSkinnedMesh;
            var indices = settings.eyelidsBlendshapes;
            if (settings.eyelidType != VRCAvatarDescriptor.EyelidType.Blendshapes
                || mesh == null || mesh.sharedMesh == null
                || indices == null || indices.Length == 0
                || indices[0] < 0 || indices[0] >= mesh.sharedMesh.blendShapeCount)
            {
                return null;
            }
            return (mesh, indices[0]);
        }

        private static (SkinnedMeshRenderer Renderer, int Index)? FindFirst(List<(SkinnedMeshRenderer Renderer, ShapeKeyNames Names)> renderers, string[] names, ExportReport report, string purpose)
        {
            foreach (var name in names)
            {
                foreach (var (renderer, shapeKeys) in renderers)
                {
                    var index = shapeKeys.Find(name);
                    if (index >= 0)
                    {
                        report.Add("Report:ShapeKeyFound", renderer.sharedMesh.GetBlendShapeName(index), renderer.name, new Term(purpose));
                        return (renderer, index);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Exports Perfect Sync only when the core keys exist. Perfect Sync moves the face in place of the preset
        /// expressions, so a set missing any of them would leave the eyes open or the mouth still.
        /// </summary>
        private static void ConvertPerfectSync(VRM10ObjectExpression expression, Transform root, List<(SkinnedMeshRenderer Renderer, ShapeKeyNames Names)> renderers, ExportReport report)
        {
            var found = new List<(string Name, MorphTargetBinding[] Bindings)>();
            var missing = new List<string>();
            foreach (var name in ArKitBlendShapes)
            {
                var names = PerfectSyncNames(name).ToList();
                var bindings = renderers
                    .Select(pair => Binding(root, pair.Renderer, pair.Names.FindAny(names)))
                    .Where(binding => binding.HasValue)
                    .Select(binding => binding.Value)
                    .ToArray();
                if (bindings.Length == 0)
                {
                    missing.Add(name);
                    continue;
                }
                found.Add((char.ToUpperInvariant(name[0]) + name.Substring(1), bindings));
            }

            if (found.Count == 0)
            {
                return;
            }
            var missingCore = PerfectSyncCore.Where(missing.Contains).ToList();
            if (missingCore.Count > 0)
            {
                report.Add("Report:PerfectSyncSkipped", found.Count, string.Join(", ", missingCore));
                return;
            }
            foreach (var (name, bindings) in found)
            {
                expression.AddClip(ExpressionPreset.custom, Create(name, bindings));
            }
            if (missing.Count > 0)
            {
                report.Add("Report:PerfectSyncPartial", string.Join(", ", missing));
            }
        }

        private static MorphTargetBinding? Binding(Transform root, SkinnedMeshRenderer renderer, string shapeKey) =>
            renderer.sharedMesh == null ? null : Binding(root, renderer, renderer.sharedMesh.GetBlendShapeIndex(shapeKey));

        private static MorphTargetBinding? Binding(Transform root, SkinnedMeshRenderer renderer, int index) =>
            index < 0 ? null : new MorphTargetBinding(renderer.transform.RelativePathFrom(root), index, MorphTargetBinding.MAX_WEIGHT);

        internal static VRM10Expression Create(string name, MorphTargetBinding[] bindings)
        {
            var clip = ScriptableObject.CreateInstance<VRM10Expression>();
            clip.name = name;
            clip.MorphTargetBindings = bindings;
            return clip;
        }

        private static VRM10Expression Create(string name, params MorphTargetBinding?[] bindings) =>
            Create(name, bindings.Where(binding => binding.HasValue).Select(binding => binding.Value).ToArray());
    }
}
