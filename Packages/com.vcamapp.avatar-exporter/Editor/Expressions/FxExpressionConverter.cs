using System;
using System.Collections.Generic;
using System.Linq;
using UniVRM10;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Turns the facial animations of the avatar's FX layer (hand gesture faces, FaceEmo menus and the like, after
    /// non-destructive tools have merged them) into VRM expressions. Each clip becomes a custom expression of the
    /// shape keys it sets on the face, and clips named like an emotion also fill the VRM emotion presets.
    /// </summary>
    internal static class FxExpressionConverter
    {
        private static readonly (ExpressionPreset Preset, string[] Words)[] PresetWords =
        {
            (ExpressionPreset.happy, new[] { "joy", "happy", "smile", "笑", "にこ", "ニコ", "喜" }),
            (ExpressionPreset.angry, new[] { "angry", "anger", "怒", "おこ" }),
            (ExpressionPreset.sad, new[] { "sad", "cry", "tear", "泣", "悲", "かなし" }),
            (ExpressionPreset.relaxed, new[] { "relax", "calm", "nagomi", "なごみ", "和" }),
            (ExpressionPreset.surprised, new[] { "surprise", "odoroki", "驚", "びっくり" }),
        };

        // The animator parameter VRChat sets to the viseme being spoken.
        private const string VisemeParameter = "Viseme";

        // A shape key below this weight (of 100) is treated as off. Clips write every face shape key, mostly zeros.
        private const float MinimumWeight = 1;

        // Clips longer than this animate over time, as idle and blinking loops do, rather than hold an expression.
        private const float MaximumLength = 1;

        /// <param name="face">The mesh the visemes or the blink are on, whose shape keys the clips are read for.</param>
        internal static void Convert(VRM10ObjectExpression expression, Transform root, VRCAvatarDescriptor descriptor, SkinnedMeshRenderer face, ExportReport report)
        {
            if (face == null || face.sharedMesh == null)
            {
                return;
            }
            var facePath = face.transform.RelativePathFrom(root);
            var usedNames = new HashSet<string>(expression.Clips.Select(clip => clip.Clip.name), StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var movingBones = 0;
            var visemeClips = VisemeClips(descriptor).Values.ToHashSet();
            foreach (var clip in FxClips(descriptor).Where(clip => !visemeClips.Contains(clip)))
            {
                var bindings = clip.length <= MaximumLength ? FaceBindings(clip, face, facePath) : null;
                if (bindings == null || bindings.Length == 0 || !usedNames.Add(clip.name))
                {
                    continue;
                }
                expression.AddClip(ExpressionPreset.custom, ExpressionConverter.Create(clip.name, bindings));
                added++;
                if (AnimationUtility.GetCurveBindings(clip).Any(MovesBones))
                {
                    movingBones++;
                }
                FillPreset(expression, clip.name, bindings);
            }
            if (added > 0)
            {
                report.Add("Report:FxExpressions", added);
            }
            if (movingBones > 0)
            {
                report.Add("Report:FxMovesBones", movingBones);
            }
        }

        private static IEnumerable<AnimationClip> FxClips(VRCAvatarDescriptor descriptor) =>
            FxControllers(descriptor)
                .SelectMany(controller => controller.animationClips)
                .Where(clip => clip != null)
                .Distinct();

        private static IEnumerable<RuntimeAnimatorController> FxControllers(VRCAvatarDescriptor descriptor)
        {
            if (!descriptor.customizeAnimationLayers)
            {
                return Enumerable.Empty<RuntimeAnimatorController>();
            }
            return descriptor.baseAnimationLayers
                .Where(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX && !layer.isDefault && layer.animatorController != null)
                .Select(layer => layer.animatorController);
        }

        /// <summary>
        /// The clips the FX layer plays for the visemes, keyed by VRChat's viseme index, for an avatar that animates its
        /// own mouth from the Viseme parameter: the destinations of transitions taken when Viseme equals the index.
        /// </summary>
        internal static Dictionary<int, AnimationClip> VisemeClips(VRCAvatarDescriptor descriptor)
        {
            var clips = new Dictionary<int, AnimationClip>();
            void Visit(AnimatorTransitionBase transition)
            {
                if (transition.destinationState == null || transition.destinationState.motion is not AnimationClip clip)
                {
                    return;
                }
                foreach (var condition in transition.conditions)
                {
                    if (condition.mode == AnimatorConditionMode.Equals && condition.parameter == VisemeParameter)
                    {
                        clips.TryAdd(Mathf.RoundToInt(condition.threshold), clip);
                    }
                }
            }
            void VisitMachine(AnimatorStateMachine machine)
            {
                foreach (var transition in machine.anyStateTransitions.Concat<AnimatorTransitionBase>(machine.entryTransitions))
                {
                    Visit(transition);
                }
                foreach (var state in machine.states)
                {
                    foreach (var transition in state.state.transitions)
                    {
                        Visit(transition);
                    }
                }
                foreach (var child in machine.stateMachines)
                {
                    VisitMachine(child.stateMachine);
                }
            }
            foreach (var controller in FxControllers(descriptor).OfType<AnimatorController>())
            {
                foreach (var layer in controller.layers)
                {
                    VisitMachine(layer.stateMachine);
                }
            }
            return clips;
        }

        /// <summary>The mesh whose shape keys the clip moves the most, which for a viseme clip is the face.</summary>
        internal static SkinnedMeshRenderer AnimatedFace(Transform root, AnimationClip clip) =>
            AnimationUtility.GetCurveBindings(clip)
                .Where(binding => binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape."))
                .GroupBy(binding => binding.path)
                .OrderByDescending(group => group.Max(binding => AnimationUtility.GetEditorCurve(clip, binding).Evaluate(clip.length)))
                .Select(group => root.Find(group.Key))
                .Select(transform => transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null)
                .FirstOrDefault(renderer => renderer != null && renderer.sharedMesh != null);

        /// <summary>
        /// Whether the binding moves a bone: a transform, or a humanoid muscle as the finger poses hand gesture clips
        /// carry along with the face.
        /// </summary>
        private static bool MovesBones(EditorCurveBinding binding) =>
            binding.type == typeof(Transform) || binding.type == typeof(Animator);

        /// <summary>
        /// The face shape keys the clip ends on, or null when the clip also changes anything other than shape keys and
        /// bones (outfit toggles, material swaps and the like), which makes it something other than an expression.
        /// Facial clips often ease in from zero, so the end is the expression the clip holds.
        /// </summary>
        internal static MorphTargetBinding[] FaceBindings(AnimationClip clip, SkinnedMeshRenderer face, string facePath)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var changesOtherThings = AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0
                || bindings.Any(binding => !MovesBones(binding)
                                           && (binding.type != typeof(SkinnedMeshRenderer) || !binding.propertyName.StartsWith("blendShape.")));
            if (changesOtherThings)
            {
                return null;
            }
            return bindings
                .Where(binding => binding.path == facePath)
                .Select(binding =>
                {
                    var weight = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(clip.length);
                    var index = face.sharedMesh.GetBlendShapeIndex(binding.propertyName.Substring("blendShape.".Length));
                    return (index, weight);
                })
                .Where(pair => pair.index >= 0 && pair.weight >= MinimumWeight)
                .Select(pair => new MorphTargetBinding(facePath, pair.index, Mathf.Min(pair.weight * MorphTargetBinding.UNITY_TO_VRM, MorphTargetBinding.MAX_WEIGHT)))
                .ToArray();
        }

        private static void FillPreset(VRM10ObjectExpression expression, string name, MorphTargetBinding[] bindings)
        {
            var lower = name.ToLowerInvariant();
            foreach (var (preset, words) in PresetWords)
            {
                if (!words.Any(lower.Contains) || Preset(expression, preset) != null)
                {
                    continue;
                }
                expression.AddClip(preset, ExpressionConverter.Create(preset.ToString(), bindings));
                return;
            }
        }

        private static VRM10Expression Preset(VRM10ObjectExpression expression, ExpressionPreset preset) =>
            expression.Clips.FirstOrDefault(clip => clip.Preset == preset).Clip;
    }
}
