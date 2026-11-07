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

        /// <summary>The key for the other eye of a key for one eye, or -1 when <paramref name="name"/> isn't one.</summary>
        internal int FindOtherSide(string name)
        {
            var normalized = Normalize(name);
            return normalized.EndsWith("left") ? Find(Stem(normalized, "left") + "right")
                : normalized.EndsWith("right") ? Find(Stem(normalized, "right") + "left")
                : -1;
        }

        /// <summary>The key that a key for one eye is the side of, or -1 when the mesh has none.</summary>
        internal int FindWithoutSide(string name)
        {
            var normalized = Normalize(name);
            var stem = normalized.EndsWith("left") ? Stem(normalized, "left")
                : normalized.EndsWith("right") ? Stem(normalized, "right")
                : null;
            return stem == null ? -1 : Find(stem);
        }

        internal static bool IsLeft(string name) => Normalize(name).EndsWith("left");

        private static string Stem(string normalized, string side) => normalized.Substring(0, normalized.Length - side.Length);

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
