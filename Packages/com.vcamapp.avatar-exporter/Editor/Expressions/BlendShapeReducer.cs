using System.Collections.Generic;
using System.Linq;
using UniVRM10;
using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Drops the shape keys no expression uses. VRM apps can only drive shape keys through expressions, and VRChat
    /// avatars often carry hundreds for gestures and body adjustments, which make up most of the file.
    /// Runs after the export bakes the current shape key weights into the meshes.
    /// </summary>
    internal static class BlendShapeReducer
    {
        /// <param name="created">Collects the reduced meshes, which the caller destroys after export.</param>
        internal static void Reduce(GameObject root, VRM10ObjectExpression expression, ICollection<Object> created)
        {
            var clips = expression.Clips.Select(clip => clip.Clip).ToList();
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var source = renderer.sharedMesh;
                if (source == null || source.blendShapeCount == 0)
                {
                    continue;
                }
                var path = renderer.transform.RelativePathFrom(root.transform);
                var used = clips
                    .SelectMany(clip => clip.MorphTargetBindings)
                    .Where(binding => binding.RelativePath == path)
                    .Select(binding => binding.Index)
                    .Distinct()
                    .OrderBy(index => index)
                    .ToList();
                if (used.Count == source.blendShapeCount)
                {
                    continue;
                }

                var mesh = Object.Instantiate(source);
                mesh.name = source.name;
                mesh.ClearBlendShapes();
                var frames = new BlendShapeFrames(source);
                foreach (var index in used)
                {
                    frames.CopyTo(mesh, index, source.GetBlendShapeName(index));
                }
                created.Add(mesh);
                renderer.sharedMesh = mesh;

                foreach (var clip in clips)
                {
                    clip.MorphTargetBindings = clip.MorphTargetBindings
                        .Select(binding => binding.RelativePath == path ? new MorphTargetBinding(path, used.IndexOf(binding.Index), binding.Weight) : binding)
                        .ToArray();
                }
            }
        }
    }
}
