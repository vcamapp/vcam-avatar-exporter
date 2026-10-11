using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Makes a shape key for each eye from a blink that closes both, so apps can wink the avatar. The vertices are
    /// parted at the middle of the face, where a narrow band moves with both keys, so the two keys add up to the blink.
    /// </summary>
    internal static class EyeBlinkSplitter
    {
        /// <summary>The band across the middle of the face, as a part of the distance between the eyes.</summary>
        private const float BandOfEyeDistance = 0.2f;
        private const float BandWithoutEyes = 0.012f;

        /// <summary>
        /// Gives the renderer a copy of its mesh with the two keys added after the others, and returns their indices.
        /// </summary>
        /// <param name="created">Collects the copied mesh, which the caller destroys after export.</param>
        internal static (int Left, int Right) Split(SkinnedMeshRenderer renderer, int blinkIndex, Animator animator, ICollection<Object> created)
        {
            var source = renderer.sharedMesh;
            var leftWeights = LeftWeights(renderer, animator);
            var name = source.GetBlendShapeName(blinkIndex);
            var leftName = UniqueName(source, name + "_L");
            var rightName = UniqueName(source, name + "_R");

            var mesh = Object.Instantiate(source);
            mesh.name = source.name;
            created.Add(mesh);
            var frames = new BlendShapeFrames(source);
            frames.CopyTo(mesh, blinkIndex, leftName, leftWeights);
            frames.CopyTo(mesh, blinkIndex, rightName, leftWeights.Select(weight => 1 - weight).ToArray());

            // A new mesh starts with every shape key at zero, and the export bakes the current weights.
            var weights = Enumerable.Range(0, source.blendShapeCount).Select(renderer.GetBlendShapeWeight).ToArray();
            renderer.sharedMesh = mesh;
            for (var i = 0; i < weights.Length; i++)
            {
                renderer.SetBlendShapeWeight(i, weights[i]);
            }
            return (mesh.GetBlendShapeIndex(leftName), mesh.GetBlendShapeIndex(rightName));
        }

        /// <summary>
        /// How much of the blink each vertex takes into the left eye's key, from where it is now across the avatar's
        /// left and right.
        /// </summary>
        private static float[] LeftWeights(SkinnedMeshRenderer renderer, Animator animator)
        {
            var avatar = animator.transform;
            float Across(Vector3 position) => avatar.InverseTransformPoint(position).x;
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            var middle = head != null ? Across(head.position) : 0;
            // An avatar faces +Z, so its left is -X.
            var toLeft = -1f;
            var band = BandWithoutEyes;
            if (leftEye != null && rightEye != null && Mathf.Abs(Across(leftEye.position) - Across(rightEye.position)) > 1e-4f)
            {
                var between = Across(leftEye.position) - Across(rightEye.position);
                toLeft = Mathf.Sign(between);
                band = Mathf.Abs(between) * BandOfEyeDistance;
                middle = (Across(leftEye.position) + Across(rightEye.position)) / 2;
            }

            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked);
                // BakeMesh leaves out the renderer's scale.
                var toAvatar = avatar.worldToLocalMatrix * Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                return baked.vertices
                    .Select(vertex => Mathf.Clamp01(0.5f + (toAvatar.MultiplyPoint3x4(vertex).x - middle) * toLeft / band))
                    .ToArray();
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }

        private static string UniqueName(Mesh mesh, string name)
        {
            var unique = name;
            for (var number = 2; mesh.GetBlendShapeIndex(unique) >= 0; number++)
            {
                unique = $"{name} ({number})";
            }
            return unique;
        }
    }
}
