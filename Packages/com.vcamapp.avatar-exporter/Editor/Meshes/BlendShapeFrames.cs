using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>Copies the frames of a mesh's shape keys to another mesh, reading them through one set of buffers.</summary>
    internal sealed class BlendShapeFrames
    {
        private readonly Mesh _source;
        private readonly Vector3[] _positions;
        private readonly Vector3[] _normals;
        private readonly Vector3[] _tangents;

        internal BlendShapeFrames(Mesh source)
        {
            _source = source;
            _positions = new Vector3[source.vertexCount];
            _normals = new Vector3[source.vertexCount];
            _tangents = new Vector3[source.vertexCount];
        }

        /// <param name="scales">How much of each vertex's delta the copy takes, or null for all of it.</param>
        internal void CopyTo(Mesh target, int index, string name, float[] scales = null)
        {
            for (var frame = 0; frame < _source.GetBlendShapeFrameCount(index); frame++)
            {
                _source.GetBlendShapeFrameVertices(index, frame, _positions, _normals, _tangents);
                if (scales != null)
                {
                    for (var vertex = 0; vertex < scales.Length; vertex++)
                    {
                        _positions[vertex] *= scales[vertex];
                        _normals[vertex] *= scales[vertex];
                        _tangents[vertex] *= scales[vertex];
                    }
                }
                target.AddBlendShapeFrame(name, _source.GetBlendShapeFrameWeight(index, frame), _positions, _normals, _tangents);
            }
        }
    }
}
