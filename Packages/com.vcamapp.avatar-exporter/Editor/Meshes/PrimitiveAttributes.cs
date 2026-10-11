using System;
using System.Linq;
using UniGLTF;
using UnityEngine;
using UniVRM10;

namespace VCam.AvatarExporter
{
    /// <summary>Adds vertex attributes that UniVRM does not write to the primitives it wrote.</summary>
    internal static class PrimitiveAttributes
    {
        /// <param name="read">The attribute's values for each vertex of the mesh, or fewer when the mesh has none.</param>
        /// <param name="include">Whether the primitive gets the attribute.</param>
        /// <param name="toGltf">Converts a value from Unity's axes to glTF's.</param>
        /// <param name="assign">Sets the accessor of the written values as the primitive's attribute.</param>
        internal static void Add<T>(ExportingGltfData storage, ModelExporter converter, Func<Mesh, T[]> read, Func<glTFPrimitives, bool> include, Func<T, T> toGltf, Action<glTFPrimitives, int> assign)
            where T : struct
        {
            foreach (var (mesh, group) in converter.Meshes)
            {
                var values = read(mesh);
                if (values.Length != mesh.vertexCount)
                {
                    continue;
                }
                var primitives = storage.Gltf.meshes[converter.Model.MeshGroups.IndexOf(group)].primitives;
                for (var submesh = 0; submesh < primitives.Count; submesh++)
                {
                    var primitive = primitives[submesh];
                    if (!include(primitive))
                    {
                        continue;
                    }
                    var vertices = Vertices(mesh, submesh, storage.Gltf.accessors[primitive.attributes.POSITION].count);
                    assign(primitive, storage.ExtendBufferAndGetAccessorIndex(Array.ConvertAll(vertices, vertex => toGltf(values[vertex])), glBufferTarget.ARRAY_BUFFER));
                }
            }
        }

        /// <summary>The mesh's vertices that UniVRM wrote as the submesh's primitive, in the primitive's order.</summary>
        private static int[] Vertices(Mesh mesh, int submesh, int primitiveVertexCount)
        {
            // UniVRM writes each submesh as a primitive of the vertices it uses, in the mesh's order.
            var vertices = mesh.GetTriangles(submesh).Distinct().OrderBy(vertex => vertex).ToArray();
            if (vertices.Length != primitiveVertexCount)
            {
                throw new InvalidOperationException($"The primitives of {mesh.name} do not match its submeshes.");
            }
            return vertices;
        }
    }
}
