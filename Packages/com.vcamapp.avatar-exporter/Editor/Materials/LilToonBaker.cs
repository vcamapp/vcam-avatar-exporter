using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Bakes lilToon's per-pixel color settings into single textures for the MToon fallback, which has one base color
    /// texture and one emission texture.
    /// </summary>
    internal static class LilToonBaker
    {
        private static readonly Vector4 DefaultHsvg = new(0, 1, 1, 1);
        private static readonly Shader BakerShader = Shader.Find("Hidden/ltsother_baker");

        private static readonly (string Toggle, string UvMode)[] Layers =
        {
            ("_UseMain2ndTex", "_Main2ndTex_UVMode"),
            ("_UseMain3rdTex", "_Main3rdTex_UVMode"),
        };

        /// <summary>
        /// Bakes the main color, hue/saturation/value/gamma adjustment, gradation and the 2nd and 3rd main color
        /// layers with lilToon's own baking shader, as lilToon's "Bake" button does. Null when nothing needs baking.
        /// The shader samples every layer on the main texture's coordinates, so layers mapped otherwise are left out.
        /// </summary>
        internal static Texture2D BakeMain(Material source)
        {
            var mainTexture = source.TextureOrNull("_MainTex");
            var layers = Layers.Where(layer => LilToonFeatures.Enabled(source, layer.Toggle)).ToList();
            var needsBake = source.ColorOr("_Color", Color.white) != Color.white
                || source.VectorOr("_MainTexHSVG", DefaultHsvg) != DefaultHsvg
                || source.FloatOr("_MainGradationStrength") != 0
                || layers.Any(layer => LilToonFeatures.TexCoord(source, layer.UvMode) == 0);
            if (mainTexture == null || !needsBake || BakerShader == null)
            {
                return null;
            }

            var baker = new Material(BakerShader);
            try
            {
                baker.CopyPropertiesFromMaterial(source);
                foreach (var layer in layers.Where(layer => LilToonFeatures.TexCoord(source, layer.UvMode) != 0))
                {
                    baker.SetFloat(layer.Toggle, 0);
                }
                return TextureBlit.Render(mainTexture, baker, mainTexture.width, mainTexture.height, false, source.name + "_Baked");
            }
            finally
            {
                Object.DestroyImmediate(baker);
            }
        }

        /// <summary>
        /// The main texture with lilToon's alpha mask applied to its alpha, as lilToon replaces, multiplies, adds to or
        /// subtracts from the alpha the mask's red channel times <c>_AlphaMaskScale</c> plus <c>_AlphaMaskValue</c>.
        /// <paramref name="alpha"/> is the main color's alpha, which the mask follows, so the result stands for both.
        /// Null when the material has no alpha mask or draws opaque.
        /// </summary>
        internal static Texture2D BakeAlphaMask(Material source, LilToonShader shader, Texture mainTexture, float alpha, string name)
        {
            var mode = Mathf.RoundToInt(source.FloatOr("_AlphaMaskMode"));
            if (mode is < 1 or > 4 || shader.RenderingMode == LilToonRenderingMode.Opaque)
            {
                return null;
            }
            var mask = source.GetTexture("_AlphaMask");
            var scale = source.GetFloat("_AlphaMaskScale");
            var value = source.GetFloat("_AlphaMaskValue");
            var sized = mainTexture != null ? mainTexture : mask;
            var width = sized != null ? sized.width : 4;
            var height = sized != null ? sized.height : 4;
            var result = TextureBlit.Render(mainTexture != null ? mainTexture : Texture2D.whiteTexture, null, width, height, false, name);
            // The shader reads the mask's red channel as it samples it, so it is read back linear.
            var readableMask = mask != null ? TextureBlit.Render(mask, null, mask.width, mask.height, true, mask.name) : null;
            try
            {
                var maskScale = source.GetTextureScale("_AlphaMask");
                var maskOffset = source.GetTextureOffset("_AlphaMask");
                var pixels = result.GetPixels();
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var red = readableMask != null
                            ? readableMask.GetPixelBilinear((x + 0.5f) / width * maskScale.x + maskOffset.x, (y + 0.5f) / height * maskScale.y + maskOffset.y).r
                            : 1;
                        var masked = Mathf.Clamp01(red * scale + value);
                        var index = y * width + x;
                        var a = pixels[index].a * alpha;
                        pixels[index].a = mode switch
                        {
                            1 => masked,
                            2 => a * masked,
                            3 => Mathf.Clamp01(a + masked),
                            _ => Mathf.Clamp01(a - masked),
                        };
                    }
                }
                result.SetPixels(pixels);
                result.Apply(false);
                return result;
            }
            finally
            {
                if (readableMask != null)
                {
                    Object.DestroyImmediate(readableMask);
                }
            }
        }

        /// <summary>
        /// The shade color lilToon gives each texel: the main color turned toward the shadow color texture by its alpha,
        /// times the shadow color, applied by the shadow strength times the strength mask's red channel, as
        /// <c>lerp(main, lerp(main, tex.rgb, tex.a) * color, strength * mask.r)</c>. A missing main texture stands for
        /// white, a missing shadow color texture for a clear one and a missing mask for white.
        /// </summary>
        internal static Texture2D BakeShade(Texture main, Texture colorTexture, Texture strengthMask, Color shadowColor, float strength, string name)
        {
            var sources = new[] { main, colorTexture, strengthMask }.Where(texture => texture != null).ToList();
            var width = sources.Max(texture => texture.width);
            var height = sources.Max(texture => texture.height);
            var mainPixels = TextureBlit.ReadPixels(main, width, height, false);
            var colorPixels = TextureBlit.ReadPixels(colorTexture, width, height, false);
            // The shader reads the mask's red channel as it samples it, so it is read back linear.
            var maskPixels = TextureBlit.ReadPixels(strengthMask, width, height, true);
            var color = shadowColor.linear;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name };
            var pixels = new Color[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                var albedo = mainPixels != null ? mainPixels[i].linear : Color.white;
                var shade = colorPixels != null ? Color.Lerp(albedo, colorPixels[i].linear, colorPixels[i].a) : albedo;
                var amount = strength * (maskPixels != null ? maskPixels[i].r : 1);
                var mixed = Color.Lerp(albedo, shade * color, amount).gamma;
                pixels[i] = new Color(mixed.r, mixed.g, mixed.b, albedo.a);
            }
            result.SetPixels(pixels);
            result.Apply(false);
            return result;
        }

        /// <summary>
        /// <paramref name="a"/> times <paramref name="b"/> applied by <paramref name="bStrength"/>, as lilToon's
        /// <c>lerp(a, a * b, strength)</c>, over the same UVs. A missing texture stands for white, and when no product
        /// is needed the texture itself is returned.
        /// </summary>
        /// <param name="created">Collects the product when one is made, which the caller destroys after export.</param>
        internal static Texture Multiply(Texture a, Texture b, string name, ICollection<Object> created, float bStrength = 1)
        {
            if (b == null || bStrength <= 0)
            {
                return a;
            }
            if (a == null && bStrength >= 1)
            {
                return b;
            }
            var width = a != null ? Mathf.Max(a.width, b.width) : b.width;
            var height = a != null ? Mathf.Max(a.height, b.height) : b.height;
            var pixelsA = TextureBlit.ReadPixels(a, width, height, false);
            var result = TextureBlit.Render(b, null, width, height, false, name);
            created.Add(result);
            var pixels = result.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                var factor = Color.Lerp(Color.white, pixels[i].linear, bStrength);
                pixels[i] = ((pixelsA != null ? pixelsA[i].linear : Color.white) * factor).gamma;
            }
            result.SetPixels(pixels);
            result.Apply(false);
            return result;
        }
    }
}
