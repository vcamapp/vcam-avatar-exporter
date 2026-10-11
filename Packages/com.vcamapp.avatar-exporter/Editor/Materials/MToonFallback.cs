using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRM10.MToon10;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Builds the MToon 1.0 material that VRM apps without lilToon support show instead. It approximates the main
    /// look only: base and shade colors, the shade boundary, normal map, emission, additive matcap, rim and outline.
    /// </summary>
    internal static class MToonFallback
    {
        private const string MissingShader = "Hidden/InternalErrorShader";
        private static readonly Shader MToonShader = Shader.Find(MToon10Meta.UnityShaderName);

        /// <param name="created">Collects the textures baked for the fallback, which the caller destroys after export.</param>
        internal static Material Create(Material source, ExportReport report, ICollection<Object> created)
        {
            if (source.shader.name == MToon10Meta.UnityShaderName)
            {
                return new Material(source);
            }
            var material = new Material(MToonShader) { name = source.name };
            var mtoon = new MToon10Context(material);
            if (LilToonShader.TryGet(source, out var lilToon))
            {
                if (lilToon.IsFakeShadow || LilToonFeatures.WritesNoColor(source))
                {
                    ApplyInvisible(mtoon);
                    if (lilToon.IsFakeShadow)
                    {
                        report.Add("Report:FakeShadowHidden", source.name, lilToon.Name);
                    }
                }
                else
                {
                    ApplyLilToon(mtoon, source, lilToon, created);
                }
                if (lilToon.IsSpecial)
                {
                    report.Add("Report:SpecialShader", source.name, lilToon.Name);
                }
            }
            else
            {
                ApplyGeneric(mtoon, source);
                if (source.shader.name == MissingShader)
                {
                    report.Add("Report:MissingShader", source.name);
                }
                else
                {
                    report.Add("Report:NotLilToon", source.name, source.shader.name);
                }
            }
            mtoon.Validate();
            return material;
        }

        /// <summary>
        /// Keeps the order the sources' render queues draw transparent materials in, which authors tune so that hair
        /// blends over the face and the like. MToon orders transparent materials by an offset of 0 to 9 when they
        /// write depth and -9 to 0 when they do not, so each group's queues are ranked into its range.
        /// </summary>
        internal static void OrderTransparent(IReadOnlyDictionary<Material, Material> fallbacks)
        {
            const int maxStep = 9;
            // MToon sources keep the offsets their authors set.
            var transparent = fallbacks
                .Where(pair => pair.Key.shader.name != MToon10Meta.UnityShaderName)
                .Select(pair => (Source: pair.Key, Mtoon: new MToon10Context(pair.Value)))
                .Where(pair => pair.Mtoon.AlphaMode == MToon10AlphaMode.Transparent);
            foreach (var group in transparent.GroupBy(pair => pair.Mtoon.TransparentWithZWriteMode))
            {
                var queues = group.Select(pair => pair.Source.renderQueue).Distinct().OrderBy(queue => queue).ToList();
                var lastStep = Mathf.Min(maxStep, queues.Count - 1);
                foreach (var (source, mtoon) in group)
                {
                    var rank = queues.IndexOf(source.renderQueue);
                    var step = queues.Count - 1 > maxStep ? rank * maxStep / (queues.Count - 1) : rank;
                    mtoon.RenderQueueOffsetNumber = group.Key == MToon10TransparentWithZWriteMode.On ? step : step - lastStep;
                    mtoon.Validate();
                }
            }
        }

        private static void ApplyLilToon(MToon10Context mtoon, Material source, LilToonShader shader, ICollection<Object> created)
        {
            mtoon.AlphaMode = shader.RenderingMode switch
            {
                LilToonRenderingMode.Cutout => MToon10AlphaMode.Cutout,
                LilToonRenderingMode.Transparent => MToon10AlphaMode.Transparent,
                _ => MToon10AlphaMode.Opaque,
            };
            mtoon.AlphaCutoff = source.FloatOr("_Cutoff", 0.5f);
            mtoon.TransparentWithZWriteMode = source.FloatOr("_ZWrite") > 0.5f
                ? MToon10TransparentWithZWriteMode.On
                : MToon10TransparentWithZWriteMode.Off;
            mtoon.DoubleSidedMode = source.FloatOr("_Cull", 2) == 0 ? MToon10DoubleSidedMode.On : MToon10DoubleSidedMode.Off;

            var mainTexture = source.TextureOrNull("_MainTex");
            var baseColor = source.ColorOr("_Color", Color.white);
            var baked = LilToonBaker.BakeMain(source);
            if (baked != null)
            {
                created.Add(baked);
                mainTexture = baked;
                // The bake multiplies the main color, alpha included, into the texture.
                baseColor = Color.white;
            }
            var alphaMasked = LilToonBaker.BakeAlphaMask(source, shader, mainTexture, baseColor.a, source.name + "_AlphaMasked");
            if (alphaMasked != null)
            {
                created.Add(alphaMasked);
                mainTexture = alphaMasked;
                baseColor.a = 1;
            }
            mtoon.BaseColorTexture = mainTexture;
            mtoon.BaseColorFactorSrgb = baseColor;
            mtoon.TextureScale = source.HasProperty("_MainTex") ? source.GetTextureScale("_MainTex") : Vector2.one;
            mtoon.TextureOffset = source.HasProperty("_MainTex") ? source.GetTextureOffset("_MainTex") : Vector2.zero;

            ApplyShadow(mtoon, source, mainTexture, baseColor, created);

            if (LilToonFeatures.Enabled(source, "_UseBumpMap"))
            {
                mtoon.NormalTexture = source.TextureOrNull("_BumpMap");
                mtoon.NormalTextureScale = source.FloatOr("_BumpScale", 1);
            }

            // lilToon colors carry their strength in alpha, masks multiply the colored texture, and the main color
            // tints the emission by _EmissionMainStrength.
            if (LilToonFeatures.Enabled(source, "_UseEmission") && LilToonFeatures.TexCoord(source, "_EmissionMap_UVMode") == 0)
            {
                var emission = source.ColorOr("_EmissionColor", Color.black);
                var map = source.TextureOrNull("_EmissionMap");
                var mask = source.TextureOrNull("_EmissionBlendMask");
                var masked = LilToonBaker.Multiply(map, mask, source.name + "_Emission", created);
                mtoon.EmissiveTexture = LilToonBaker.Multiply(masked, mainTexture, source.name + "_Emission", created, source.FloatOr("_EmissionMainStrength"));
                mtoon.EmissiveFactorLinear = emission.linear * (emission.a * source.FloatOr("_EmissionBlend", 1));
            }

            Texture matcapMask = null;
            Texture rimTexture = null;
            if (LilToonFeatures.Enabled(source, "_UseMatCap") && LilToonFeatures.MatCapAdds(source))
            {
                var matcap = source.ColorOr("_MatCapColor", Color.white);
                mtoon.MatcapTexture = source.TextureOrNull("_MatCapTex");
                mtoon.MatcapColorFactorSrgb = matcap * (matcap.a * source.FloatOr("_MatCapBlend", 1));
                matcapMask = source.TextureOrNull("_MatCapBlendMask");
            }

            if (LilToonFeatures.Enabled(source, "_UseRim"))
            {
                var rim = source.ColorOr("_RimColor", Color.black);
                mtoon.ParametricRimColorFactorSrgb = rim * rim.a;
                mtoon.ParametricRimFresnelPowerFactor = source.FloatOr("_RimFresnelPower", 3);
                mtoon.ParametricRimLiftFactor = 0;
                mtoon.RimLightingMixFactor = source.FloatOr("_RimEnableLighting", 1);
                rimTexture = source.TextureOrNull("_RimColorTex");
            }
            // MToon masks its rim and matcap with one texture. Their product keeps each within its own mask at the cost
            // of dimming the other, where either alone would spread a matcap meant for highlights over the surface.
            mtoon.RimMultiplyTexture = LilToonBaker.Multiply(matcapMask, rimTexture, source.name + "_RimMask", created);

            if (shader.HasOutline)
            {
                // lilToon's outline width is in centimeters.
                mtoon.OutlineWidthMode = MToon10OutlineMode.World;
                mtoon.OutlineWidthFactor = source.FloatOr("_OutlineWidth", 0.08f) * 0.01f;
                mtoon.OutlineWidthMultiplyTexture = source.TextureOrNull("_OutlineWidthMask");
                mtoon.OutlineColorFactorSrgb = source.ColorOr("_OutlineColor", Color.black);
                mtoon.OutlineLightingMixFactor = source.FloatOr("_OutlineEnableLighting", 1);
            }
        }

        /// <summary>
        /// lilToon thresholds the half Lambert term at <c>_ShadowBorder</c> with a soft band <c>_ShadowBlur</c> wide.
        /// MToon thresholds <c>N·L + shift</c> at 0 with a band <c>2 (1 - toony)</c> wide, so in N·L terms
        /// the border maps to <c>1 - 2 border</c> and the blur to <c>1 - blur</c>.
        /// </summary>
        private static void ApplyShadow(MToon10Context mtoon, Material source, Texture mainTexture, Color baseColor, ICollection<Object> created)
        {
            if (!LilToonFeatures.Enabled(source, "_UseShadow"))
            {
                mtoon.ShadeColorTexture = mainTexture;
                mtoon.ShadeColorFactorSrgb = baseColor;
                return;
            }

            var strength = source.FloatOr("_ShadowStrength", 1);
            var colorTexture = source.TextureOrNull("_ShadowColorTex");
            var strengthMask = LilToonFeatures.ShadowStrengthMask(source);
            if (colorTexture != null || strengthMask != null)
            {
                // The color texture is painted only where the shadow differs, and the mask keeps parts out of the
                // shadow, so each texel's shade is baked from the main texture.
                var shade = LilToonBaker.BakeShade(mainTexture, colorTexture, strengthMask, source.ColorOr("_ShadowColor", Color.white), strength, source.name + "_Shade");
                created.Add(shade);
                mtoon.ShadeColorTexture = shade;
                mtoon.ShadeColorFactorSrgb = baseColor;
            }
            else
            {
                mtoon.ShadeColorTexture = mainTexture;
                mtoon.ShadeColorFactorSrgb = baseColor * Color.Lerp(Color.white, source.ColorOr("_ShadowColor", Color.white), strength);
            }
            mtoon.ShadingShiftFactor = Mathf.Clamp(1 - 2 * source.FloatOr("_ShadowBorder", 0.5f), -1, 1);
            mtoon.ShadingToonyFactor = Mathf.Clamp01(1 - source.FloatOr("_ShadowBlur", 0.1f));
        }

        /// <summary>
        /// Makes a fallback draw nothing while it keeps its textures and colors, for a material that draws a mesh again only
        /// where other materials mark the screen with a stencil, as see-through bangs over the eyes do. Without a stencil it
        /// would cover the whole mesh. Returns the base color's alpha it had.
        /// </summary>
        internal static float Hide(Material fallback)
        {
            var mtoon = new MToon10Context(fallback);
            var color = mtoon.BaseColorFactorSrgb;
            mtoon.BaseColorFactorSrgb = new Color(color.r, color.g, color.b, 0);
            if (mtoon.AlphaMode != MToon10AlphaMode.Transparent)
            {
                mtoon.AlphaMode = MToon10AlphaMode.Transparent;
                mtoon.TransparentWithZWriteMode = MToon10TransparentWithZWriteMode.Off;
            }
            mtoon.Validate();
            return color.a;
        }

        /// <summary>A transparent material that draws nothing, for lilToon materials MToon has no way to draw.</summary>
        private static void ApplyInvisible(MToon10Context mtoon)
        {
            mtoon.AlphaMode = MToon10AlphaMode.Transparent;
            mtoon.TransparentWithZWriteMode = MToon10TransparentWithZWriteMode.Off;
            mtoon.BaseColorFactorSrgb = new Color(1, 1, 1, 0);
            mtoon.ShadeColorFactorSrgb = new Color(1, 1, 1, 0);
        }

        /// <summary>
        /// Uses the main texture and color the material has saved, which are still there when its shader is missing.
        /// </summary>
        private static void ApplyGeneric(MToon10Context mtoon, Material source)
        {
            var mainTexture = source.GetTexturePropertyNames().Contains("_MainTex") ? source.GetTexture("_MainTex") : null;
            var baseColor = source.HasColor("_Color") ? source.GetColor("_Color") : Color.white;
            mtoon.BaseColorTexture = mainTexture;
            mtoon.BaseColorFactorSrgb = baseColor;
            mtoon.ShadeColorTexture = mainTexture;
            mtoon.ShadeColorFactorSrgb = baseColor;
        }
    }
}
