using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Merges the bones a skinned mesh lists more than once. Unity allows it, glTF skins must name each joint once,
    /// and UniVRM writes the bone list as it is.
    /// </summary>
    internal static class UniqueJoints
    {
        /// <summary>Runs after the bones are normalized, when every entry of one bone has the same bind pose.</summary>
        internal static void Apply(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = renderer.bones;
                var mesh = renderer.sharedMesh;
                var unique = bones.Distinct().ToList();
                if (mesh == null || unique.Count == bones.Length)
                {
                    continue;
                }

                var remap = Array.ConvertAll(bones, bone => unique.IndexOf(bone));
                var bindposes = mesh.bindposes;
                var bonesPerVertex = mesh.GetBonesPerVertex().ToArray();
                var weights = mesh.GetAllBoneWeights().ToArray();
                var newBonesPerVertex = new byte[bonesPerVertex.Length];
                var newWeights = new List<BoneWeight1>(weights.Length);
                var start = 0;
                for (var vertex = 0; vertex < bonesPerVertex.Length; vertex++)
                {
                    // The weights stay sorted by weight, as Unity requires.
                    var merged = new ArraySegment<BoneWeight1>(weights, start, bonesPerVertex[vertex])
                        .GroupBy(weight => remap[weight.boneIndex])
                        .Select(group => new BoneWeight1 { boneIndex = group.Key, weight = group.Sum(weight => weight.weight) })
                        .OrderByDescending(weight => weight.weight)
                        .ToList();
                    newWeights.AddRange(merged);
                    newBonesPerVertex[vertex] = (byte)merged.Count;
                    start += bonesPerVertex[vertex];
                }

                using var bonesPerVertexArray = new NativeArray<byte>(newBonesPerVertex, Allocator.Temp);
                using var weightsArray = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Temp);
                mesh.SetBoneWeights(bonesPerVertexArray, weightsArray);
                mesh.bindposes = unique.Select(bone => bindposes[Array.IndexOf(bones, bone)]).ToArray();
                renderer.bones = unique.ToArray();
            }
        }
    }
}
