using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Shrinks textures larger than the export limit. UniVRM writes the source file as is when the texture is imported
    /// at its full size, so a 4096px source stays 4096px in the VRM, and phones decode every texture to full size.
    /// Normal maps keep their size: Unity stores them in a packed layout that UniVRM unpacks only from the original.
    /// </summary>
    internal sealed class TextureLimiter
    {
        private readonly int _maxSize;
        private readonly ICollection<Object> _created;
        private readonly Dictionary<Texture, Texture> _limited = new();
        private readonly Dictionary<Texture, Texture> _originals = new();
        private readonly Dictionary<Texture, TextureImporter> _importers = new();

        /// <param name="maxSize">The longest side allowed, or 0 for no limit.</param>
        /// <param name="created">Collects the shrunk textures, which the caller destroys after export.</param>
        internal TextureLimiter(int maxSize, ICollection<Object> created)
        {
            _maxSize = maxSize;
            _created = created;
        }

        internal Texture Limit(Texture texture)
        {
            if (texture == null || _maxSize <= 0 || Mathf.Max(texture.width, texture.height) <= _maxSize || texture is not Texture2D)
            {
                return texture;
            }
            if (_limited.TryGetValue(texture, out var limited))
            {
                return limited;
            }
            var importer = Importer(texture);
            if (importer != null && importer.textureType == TextureImporterType.NormalMap)
            {
                return texture;
            }

            var scale = (float)_maxSize / Mathf.Max(texture.width, texture.height);
            var width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
            var linear = importer != null && !importer.sRGBTexture;
            var result = TextureBlit.Render(texture, null, width, height, linear, texture.name);
            _created.Add(result);
            _limited[texture] = result;
            _originals[result] = texture;
            return result;
        }

        /// <summary>The importer of the texture, or of the texture it was shrunk from.</summary>
        internal TextureImporter Importer(Texture texture)
        {
            var source = _originals.TryGetValue(texture, out var original) ? original : texture;
            if (!_importers.TryGetValue(source, out var importer))
            {
                importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source)) as TextureImporter;
                _importers[source] = importer;
            }
            return importer;
        }
    }
}
