using System;
using System.Collections.Generic;
using System.Linq;
using UniGLTF;
using UniJSON;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VCam.AvatarExporter
{
    /// <summary>Writes the <c>VCAM_materials_liltoon</c> extension described in docs/format.md.</summary>
    internal static class LilToonExtension
    {
        internal const string Name = "VCAM_materials_liltoon";
        internal const string SpecVersion = "1.0";

        internal static ArraySegment<byte> Root(string lilToonVersion, string exporterVersion)
        {
            var json = new JsonFormatter();
            json.BeginMap();
            json.Key("specVersion");
            json.Value(SpecVersion);
            json.Key("lilToonVersion");
            json.Value(lilToonVersion);
            json.Key("exporterVersion");
            json.Value(exporterVersion);
            json.EndMap();
            return json.GetStoreBytes();
        }

        /// <param name="hiddenAlpha">The base color alpha of a fallback hidden for drawing only where a stencil allows.</param>
        internal static ArraySegment<byte> Material(Material material, float? hiddenAlpha, ITextureExporter textureExporter, TextureLimiter textures, ExportReport report)
        {
            var shader = material.shader;
            var properties = Enumerable.Range(0, shader.GetPropertyCount())
                .Select(i => (Name: shader.GetPropertyName(i), Type: shader.GetPropertyType(i), Flags: shader.GetPropertyFlags(i)))
                .ToList();

            var json = new JsonFormatter();
            json.BeginMap();
            json.Key("shader");
            json.Value(shader.name);
            json.Key("renderQueue");
            json.Value(material.renderQueue);
            if (hiddenAlpha is { } alpha)
            {
                json.Key("fallbackBaseColorAlpha");
                Number(json, alpha);
            }

            json.Key("floats");
            json.BeginMap();
            foreach (var (name, type, _) in properties)
            {
                if (type is ShaderPropertyType.Float or ShaderPropertyType.Range)
                {
                    json.Key(name);
                    Number(json, material.GetFloat(name));
                }
                else if (type == ShaderPropertyType.Int)
                {
                    json.Key(name);
                    json.Value(material.GetInteger(name));
                }
            }
            json.EndMap();

            json.Key("colors");
            json.BeginMap();
            foreach (var (name, _, _) in properties.Where(p => p.Type == ShaderPropertyType.Color))
            {
                var color = material.GetColor(name);
                var linear = color.linear;
                json.Key(name);
                Numbers(json, linear.r, linear.g, linear.b, color.a);
            }
            json.EndMap();

            json.Key("vectors");
            json.BeginMap();
            foreach (var (name, _, _) in properties.Where(p => p.Type == ShaderPropertyType.Vector))
            {
                var vector = material.GetVector(name);
                json.Key(name);
                Numbers(json, vector.x, vector.y, vector.z, vector.w);
            }
            json.EndMap();

            json.Key("textures");
            json.BeginMap();
            foreach (var (name, _, flags) in properties.Where(p => p.Type == ShaderPropertyType.Texture))
            {
                WriteTexture(json, material, name, (flags & ShaderPropertyFlags.NoScaleOffset) == 0, textureExporter, textures, report);
            }
            json.EndMap();

            json.EndMap();
            return json.GetStoreBytes();
        }

        private static void WriteTexture(JsonFormatter json, Material material, string name, bool hasScaleOffset, ITextureExporter textureExporter, TextureLimiter textures, ExportReport report)
        {
            var texture = material.GetTexture(name);
            if (texture == null)
            {
                return;
            }
            if (texture is not Texture2D)
            {
                report.Add("Report:UnsupportedTexture", material.name, name, texture.GetType().Name);
                return;
            }

            json.Key(name);
            json.BeginMap();
            json.Key("index");
            json.Value(Register(textures.Limit(texture), textureExporter, textures));
            if (hasScaleOffset)
            {
                var scale = material.GetTextureScale(name);
                var offset = material.GetTextureOffset(name);
                if (scale != Vector2.one)
                {
                    json.Key("scale");
                    Numbers(json, scale.x, scale.y);
                }
                if (offset != Vector2.zero)
                {
                    json.Key("offset");
                    Numbers(json, offset.x, offset.y);
                }
            }
            json.EndMap();
        }

        private static void Numbers(JsonFormatter json, params float[] values)
        {
            json.BeginList();
            foreach (var value in values)
            {
                Number(json, value);
            }
            json.EndList();
        }

        // JsonFormatter writes NaN and Infinity as is, which is not JSON.
        private static void Number(JsonFormatter json, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "JSON cannot hold a non-finite number.");
            }
            json.Value(value);
        }

        /// <summary>Registers the texture the way its importer reads it, so the shader receives the same values.</summary>
        private static int Register(Texture texture, ITextureExporter textureExporter, TextureLimiter textures)
        {
            if (textures.Importer(texture) is not { } importer)
            {
                return textureExporter.RegisterExportingAsSRgb(texture, needsAlpha: true);
            }
            if (importer.textureType == TextureImporterType.NormalMap)
            {
                return textureExporter.RegisterExportingAsNormal(texture);
            }
            var needsAlpha = importer.DoesSourceTextureHaveAlpha();
            return importer.sRGBTexture
                ? textureExporter.RegisterExportingAsSRgb(texture, needsAlpha)
                : textureExporter.RegisterExportingAsLinear(texture, needsAlpha);
        }
    }

    /// <summary>
    /// The lilToon material a fallback replaced, and the base color alpha the fallback had when it is hidden for drawing
    /// only where a stencil allows.
    /// </summary>
    internal readonly struct LilToonSource
    {
        internal Material Material { get; }
        internal LilToonShader Shader { get; }
        internal float? HiddenAlpha { get; }

        internal LilToonSource(Material material, LilToonShader shader, float? hiddenAlpha)
        {
            Material = material;
            Shader = shader;
            HiddenAlpha = hiddenAlpha;
        }
    }

    /// <summary>
    /// Exports each MToon fallback as usual and attaches the settings of the lilToon material it replaced.
    /// The extension is written here because the texture exporter only accepts textures during material export.
    /// </summary>
    internal sealed class LilToonMaterialExporter : IMaterialExporter
    {
        private readonly IMaterialExporter _inner;
        private readonly IReadOnlyDictionary<Material, LilToonSource> _lilToonSources;
        private readonly TextureLimiter _textures;
        private readonly ExportReport _report;

        internal LilToonMaterialExporter(IMaterialExporter inner, IReadOnlyDictionary<Material, LilToonSource> lilToonSources, TextureLimiter textures, ExportReport report)
        {
            _inner = inner;
            _lilToonSources = lilToonSources;
            _textures = textures;
            _report = report;
        }

        public glTFMaterial ExportMaterial(Material m, ITextureExporter textureExporter, GltfExportSettings settings)
        {
            var material = _inner.ExportMaterial(m, textureExporter, settings);
            if (_lilToonSources.TryGetValue(m, out var source))
            {
                glTFExtensionExport.GetOrCreate(ref material.extensions)
                    .Add(LilToonExtension.Name, LilToonExtension.Material(source.Material, source.HiddenAlpha, textureExporter, _textures, _report));
            }
            return material;
        }
    }
}
