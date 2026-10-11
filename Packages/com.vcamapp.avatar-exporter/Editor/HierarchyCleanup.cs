using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Tidies what non-destructive tools leave in the processed copy for VRChat alone: the names Modular Avatar
    /// mangles, and objects that hold only VRChat or build tool components, such as its head chop holder and the
    /// stand-in root bones it adds for VRChat's first person view.
    /// </summary>
    internal static class HierarchyCleanup
    {
        // Modular Avatar appends "$" and a GUID to the bones it merges, which keeps them apart from the avatar's.
        private static readonly Regex MangledSuffix = new(@"\$(ConstraintRef )?[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$");
        private const string RetargetedPrefix = "RETARGETED__";

        /// <summary>
        /// Gives merged bones and retargeted meshes back their names. Runs before anything records paths, since it
        /// renames objects along them. A bone whose name is taken gets a number, as Unity numbers duplicates.
        /// </summary>
        internal static void RestoreNames(GameObject root)
        {
            var mangled = root.GetComponentsInChildren<Transform>(true).ToLookup(t => MangledSuffix.IsMatch(t.name));
            var names = new HashSet<string>(mangled[false].Select(t => t.name));
            foreach (var transform in mangled[true])
            {
                var name = MangledSuffix.Replace(transform.name, "");
                var unique = name;
                for (var i = 1; !names.Add(unique); i++)
                {
                    unique = $"{name} ({i})";
                }
                transform.name = unique;
            }

            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh != null && mesh.name.StartsWith(RetargetedPrefix))
                {
                    mesh.name = mesh.name.Substring(RetargetedPrefix.Length);
                }
            }
        }

        /// <summary>
        /// Removes the objects left with nothing to export. Runs after the spring bones are converted, since PhysBones
        /// often sit on objects of their own.
        /// </summary>
        internal static void RemoveLeftovers(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bones = new HashSet<Transform>(renderers.SelectMany(renderer => renderer.bones).Where(bone => bone != null));
            if (root.TryGetComponent<Animator>(out var animator) && animator.isHuman)
            {
                bones.UnionWith(Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                    .Select(bone => animator.GetBoneTransform((HumanBodyBones)bone))
                    .Where(bone => bone != null));
            }

            foreach (var renderer in renderers)
            {
                var rootBone = renderer.rootBone;
                if (rootBone != null && rootBone.parent != null && IsLeftover(rootBone, bones) && IsIdentity(rootBone))
                {
                    renderer.rootBone = rootBone.parent;
                }
            }
            bones.UnionWith(renderers.Select(renderer => renderer.rootBone).Where(bone => bone != null));

            // Children come before their parents, so a parent left empty by removing them goes too.
            foreach (var transform in root.GetComponentsInChildren<Transform>(true).Reverse())
            {
                if (transform != root.transform && IsLeftover(transform, bones))
                {
                    Object.DestroyImmediate(transform.gameObject);
                }
            }
        }

        private static bool IsLeftover(Transform transform, HashSet<Transform> bones) =>
            transform.childCount == 0 && !bones.Contains(transform) && transform.GetComponents<Component>().All(IsForVRChatOnly);

        /// <summary>Whether the component means nothing outside VRChat: VRChat's own, the build tools', or a missing script.</summary>
        private static bool IsForVRChatOnly(Component component)
        {
            if (component == null || component is Transform)
            {
                return true;
            }
            var ns = component.GetType().Namespace ?? "";
            return ns.StartsWith("VRC.") || ns.StartsWith("nadena.dev.");
        }

        private static bool IsIdentity(Transform transform) =>
            transform.localPosition == Vector3.zero && transform.localRotation == Quaternion.identity && transform.localScale == Vector3.one;
    }
}
