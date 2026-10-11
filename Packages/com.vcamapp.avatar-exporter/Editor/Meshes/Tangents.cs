using System.Collections.Generic;
using System.Linq;
using UniGLTF;
using UniGLTF.MeshUtility;
using UnityEngine;
using UnityEngine.Rendering;
using UniVRM10;
using VRM10.MToon10;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Adds the mesh's tangents as <c>TANGENT</c> to the primitives whose material has a normal map. UniVRM writes no
    /// tangents to VRM 1.0, and tangents derived from the texture coordinates bend the normal map differently from the
    /// ones Unity imported, which the map is usually baked against.
    /// </summary>
    internal static class Tangents
    {
        /// <summary>
        /// Removes the normal map from the fallbacks that only meshes without tangents draw. Unity gives such a mesh no
        /// tangents, so the map bends no normal there, while a glTF app derives tangents and would bend them.
        /// </summary>
        internal static void RemoveNormalMapsWithoutTangents(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>().Where(renderer => renderer.enabled).ToList();
            static bool HasTangents(Renderer renderer) =>
                renderer.GetMesh() is { } mesh && mesh.HasVertexAttribute(VertexAttribute.Tangent);
            var bent = new HashSet<Material>(renderers.Where(HasTangents).SelectMany(renderer => renderer.sharedMaterials));
            var unbent = renderers.Where(renderer => !HasTangents(renderer))
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null && !bent.Contains(material))
                .Distinct();
            foreach (var material in unbent)
            {
                new MToon10Context(material).NormalTexture = null;
            }
        }

        internal static void Write(ExportingGltfData storage, ModelExporter converter) =>
            PrimitiveAttributes.Add(storage, converter, mesh => mesh.tangents,
                primitive => primitive.material is { } material && storage.Gltf.materials[material].normalTexture is { index: >= 0 },
                // VRM 1.0 mirrors Unity's X axis, which also flips the handedness of the basis.
                tangent => new Vector4(-tangent.x, tangent.y, tangent.z, -tangent.w),
                (primitive, accessor) => primitive.attributes.TANGENT = accessor);
    }
}
