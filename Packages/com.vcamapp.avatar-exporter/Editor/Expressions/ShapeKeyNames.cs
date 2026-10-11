using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Finds a mesh's shape keys by names written in different styles, such as "eyeBlinkLeft", "EyeBlink_L" and
    /// "eyeblink.l": case, separators and the side suffix are ignored.
    /// </summary>
    internal sealed class ShapeKeyNames
    {
        private static readonly (string Suffix, string Side)[] SideSuffixes =
        {
            ("_l", "left"), (".l", "left"), ("-l", "left"), ("_r", "right"), (".r", "right"), ("-r", "right"),
        };

        private readonly Dictionary<string, int> _indices = new();

        internal ShapeKeyNames(Mesh mesh)
        {
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                _indices.TryAdd(Normalize(mesh.GetBlendShapeName(i)), i);
            }
        }

        internal int Find(string name) => _indices.TryGetValue(Normalize(name), out var index) ? index : -1;

        /// <summary>The first of <paramref name="names"/> the mesh has.</summary>
        internal int FindAny(IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                var index = Find(name);
                if (index >= 0)
                {
                    return index;
                }
            }
            return -1;
        }

        private static string Normalize(string name)
        {
            var lower = name.ToLowerInvariant();
            foreach (var (suffix, side) in SideSuffixes)
            {
                if (lower.EndsWith(suffix))
                {
                    lower = lower.Substring(0, lower.Length - suffix.Length) + side;
                    break;
                }
            }
            return new string(lower.Where(char.IsLetterOrDigit).ToArray());
        }
    }
}
