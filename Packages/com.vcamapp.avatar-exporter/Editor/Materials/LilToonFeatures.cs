using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>The lilToon features a material turns on that the VRM or its MToon fallback cannot carry.</summary>
    internal static class LilToonFeatures
    {
        private const float MatCapBlendModeAdd = 1;

        /// <summary>
        /// The features that sample a texture on a texture coordinate set lilToon lets the author choose, with the
        /// localization key of their names, and whether the MToon fallback shows them, which it does on the first set only.
        /// </summary>
        private static readonly (string Feature, string Toggle, string UvMode, bool InMToon)[] TexCoordFeatures =
        {
            ("Feature:Main2nd", "_UseMain2ndTex", "_Main2ndTex_UVMode", true),
            ("Feature:Main3rd", "_UseMain3rdTex", "_Main3rdTex_UVMode", true),
            ("Feature:Bump2nd", "_UseBump2ndMap", "_Bump2ndMap_UVMode", false),
            ("Feature:Glitter", "_UseGlitter", "_GlitterUVMode", false),
            ("Feature:GlitterColor", "_UseGlitter", "_GlitterColorTex_UVMode", false),
            ("Feature:Emission", "_UseEmission", "_EmissionMap_UVMode", true),
            ("Feature:Emission2nd", "_UseEmission2nd", "_Emission2ndMap_UVMode", false),
            ("Feature:AudioLinkMask", "_UseAudioLink", "_AudioLinkMask_UVMode", false),
        };

        /// <summary>The features the MToon fallback leaves out whenever they are turned on, with the localization key of their names.</summary>
        private static readonly (string Feature, string Toggle)[] ToggleFeatures =
        {
            ("Feature:Bump2nd", "_UseBump2ndMap"),
            ("Feature:Anisotropy", "_UseAnisotropy"),
            ("Feature:Backlight", "_UseBacklight"),
            ("Feature:RimShade", "_UseRimShade"),
            ("Feature:Reflection", "_UseReflection"),
            ("Feature:MatCap2nd", "_UseMatCap2nd"),
            ("Feature:Glitter", "_UseGlitter"),
            ("Feature:Emission2nd", "_UseEmission2nd"),
            ("Feature:Parallax", "_UseParallax"),
        };

        /// <summary>The shadow masks that turn the mask into a different shadow, which MToon has no way to draw.</summary>
        private static readonly string[] ShadowShapeMasks = { "_ShadowBorderMask", "_ShadowBlurMask" };

        /// <summary>
        /// The texture coordinate set a UV mode property selects, or -1 for the modes that map by view instead
        /// (matcap and rim), which lilToon numbers after UV3.
        /// </summary>
        internal static int TexCoord(Material material, string uvMode)
        {
            var mode = Mathf.RoundToInt(material.FloatOr(uvMode));
            return mode <= 3 ? mode : -1;
        }

        /// <summary>The texture coordinate sets above the first that the turned on features sample, with the feature's localization key.</summary>
        internal static IEnumerable<(string Feature, int Set)> TexCoordUses(Material material, LilToonShader shader)
        {
            foreach (var (feature, toggle, uvMode, _) in TexCoordFeatures)
            {
                var set = TexCoord(material, uvMode);
                if (Enabled(material, toggle) && set > 0)
                {
                    yield return (feature, set);
                }
            }
            var outlineVectorSet = TexCoord(material, "_OutlineVectorUVMode");
            if (shader.HasOutline && material.TextureOrNull("_OutlineVectorTex") != null && outlineVectorSet > 0)
            {
                yield return ("Feature:OutlineVector", outlineVectorSet);
            }
        }

        /// <summary>
        /// The localization keys of the turned on features the MToon fallback leaves out, AudioLink aside, which nothing
        /// outside VRChat drives.
        /// </summary>
        internal static IEnumerable<string> NotInMToon(Material material, LilToonShader shader)
        {
            if (Enabled(material, "_UseShadow"))
            {
                if (material.ColorOr("_Shadow2ndColor", Color.clear).a > 0)
                {
                    yield return "Feature:Shadow2nd";
                }
                if (material.ColorOr("_Shadow3rdColor", Color.clear).a > 0)
                {
                    yield return "Feature:Shadow3rd";
                }
                var strengthMaskLost = material.TextureOrNull("_ShadowStrengthMask") != null && ShadowStrengthMask(material) == null;
                if (strengthMaskLost || ShadowShapeMasks.Any(mask => material.TextureOrNull(mask) != null))
                {
                    yield return "Feature:ShadowMasks";
                }
            }
            foreach (var (feature, toggle) in ToggleFeatures)
            {
                if (Enabled(material, toggle))
                {
                    yield return feature;
                }
            }
            if (Enabled(material, "_UseMatCap") && !MatCapAdds(material))
            {
                yield return "Feature:MatCapBlendMode";
            }
            foreach (var (feature, toggle, uvMode, _) in TexCoordFeatures.Where(use => use.InMToon))
            {
                if (Enabled(material, toggle) && TexCoord(material, uvMode) != 0)
                {
                    yield return feature;
                }
            }
            if (material.VectorOr("_DissolveParams", Vector4.zero).x != 0)
            {
                yield return "Feature:Dissolve";
            }
            if (material.VectorOr("_DistanceFade", Vector4.zero).z != 0)
            {
                yield return "Feature:DistanceFade";
            }
            if (material.VectorOr("_MainTex_ScrollRotate", Vector4.zero) != Vector4.zero)
            {
                yield return "Feature:MainTextureAnimation";
            }
            // A lit MToon outline takes the base color, which stands in for the main texture used as the outline's.
            var outlineTexture = material.TextureOrNull("_OutlineTex");
            var litByMainTexture = outlineTexture == material.TextureOrNull("_MainTex") && material.FloatOr("_OutlineEnableLighting") > 0;
            if (shader.HasOutline && outlineTexture != null && !litByMainTexture)
            {
                yield return "Feature:OutlineTexture";
            }
            // The Z bias pushes the outline away from the camera, which hides it in hollows such as around the eyes.
            if (shader.HasOutline && material.FloatOr("_OutlineZBias") != 0)
            {
                yield return "Feature:OutlineZBias";
            }
        }

        /// <summary>
        /// The mask of the shadow strength, which the MToon fallback bakes into its shade, or null. The other mask types
        /// turn the mask into a different shadow, which MToon has no way to draw.
        /// </summary>
        internal static Texture ShadowStrengthMask(Material material) =>
            material.FloatOr("_ShadowMaskType") == 0 ? material.TextureOrNull("_ShadowStrengthMask") : null;

        /// <summary>Whether the material writes no color, as materials that only write depth or stencil do.</summary>
        internal static bool WritesNoColor(Material material) => material.FloatOr("_ColorMask", 15) == 0;

        /// <summary>Whether a fake shadow is white without a texture, which multiplies nothing. Some avatars ship theirs for the user to tint.</summary>
        internal static bool FakeShadowMultipliesNothing(Material material) =>
            material.ColorOr("_Color", Color.white) == Color.white && material.TextureOrNull("_MainTex") == null;

        /// <summary>
        /// Whether a lilToon material draws only where its stencil test passes, comparing with a value other materials
        /// write, as bangs drawn see-through over the eyes do. Unity's CompareFunction: 0 is disabled and 8 always.
        /// </summary>
        internal static bool DrawsWithStencil(Material material) =>
            LilToonShader.TryGet(material, out _) && material.FloatOr("_StencilComp") is > 0 and < 8;

        /// <summary>Whether the outline width follows the vertex colors, which the VRM does not carry.</summary>
        internal static bool UsesVertexColors(Material material, LilToonShader shader) =>
            shader.HasOutline && material.FloatOr("_OutlineVertexR2Width") != 0;

        /// <summary>Whether the matcap adds to the color, the only blend mode MToon's matcap has.</summary>
        internal static bool MatCapAdds(Material material) => material.FloatOr("_MatCapBlendMode") == MatCapBlendModeAdd;

        internal static bool Enabled(Material material, string toggle) => material.FloatOr(toggle) > 0.5f;
    }

    /// <summary>Gathers what lilToon materials lose, so the report has one line for each loss with the materials it affects.</summary>
    internal sealed class LilToonFeatureReport
    {
        private readonly List<(Message Loss, List<string> Materials)> _losses = new();

        internal void Check(Material material, LilToonShader shader)
        {
            foreach (var feature in LilToonFeatures.NotInMToon(material, shader))
            {
                Add(material, "Report:NotInMToon", new Term(feature));
            }
            foreach (var (feature, set) in LilToonFeatures.TexCoordUses(material, shader).Where(use => use.Set > 1))
            {
                Add(material, "Report:TexCoordNotExported", set, new Term(feature));
            }
            if (LilToonFeatures.UsesVertexColors(material, shader))
            {
                Add(material, "Report:VertexColorsNotExported");
            }
            if (LilToonFeatures.Enabled(material, "_UseAudioLink"))
            {
                Add(material, "Report:AudioLink");
            }
        }

        /// <summary>Adds a line for each loss, with the names of the materials it affects as the last argument.</summary>
        internal void WriteTo(ExportReport report)
        {
            foreach (var (loss, materials) in _losses)
            {
                report.Add(loss.Key, loss.Args.Append(string.Join(", ", materials)).ToArray());
            }
        }

        private void Add(Material material, string key, params object[] args)
        {
            var loss = new Message(key, args);
            var index = _losses.FindIndex(existing => existing.Loss.Equals(loss));
            if (index < 0)
            {
                _losses.Add((loss, new List<string> { material.name }));
            }
            else if (!_losses[index].Materials.Contains(material.name))
            {
                _losses[index].Materials.Add(material.name);
            }
        }
    }
}
