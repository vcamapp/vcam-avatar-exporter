using UnityEngine;

namespace VCam.AvatarExporter
{
    internal enum LilToonRenderingMode
    {
        Opaque,
        Cutout,
        Transparent,
    }

    /// <summary>
    /// The variant of lilToon a material uses. lilToon selects the rendering mode and the outline pass by switching
    /// between shaders named like "Hidden/lilToonCutoutOutline", so the shader name carries both.
    /// </summary>
    internal readonly struct LilToonShader
    {
        internal string Name { get; }
        internal LilToonRenderingMode RenderingMode { get; }
        internal bool HasOutline { get; }

        /// <summary>
        /// Variants whose look the MToon fallback cannot approximate at all (fur, gems, refraction, overlays, the
        /// multi shader driven by keywords, and outlines without a body). Their values are still exported.
        /// </summary>
        internal bool IsSpecial { get; }

        /// <summary>
        /// The fake shadow, which multiplies a shadow onto the surfaces behind it. An opaque surface would cover them.
        /// </summary>
        internal bool IsFakeShadow { get; }

        private LilToonShader(string name)
        {
            Name = name;
            RenderingMode = name.Contains("Cutout") ? LilToonRenderingMode.Cutout
                : name.Contains("Transparent") ? LilToonRenderingMode.Transparent
                : LilToonRenderingMode.Opaque;
            HasOutline = name.Contains("Outline") && !name.Contains("OutlineOnly");
            IsFakeShadow = name.Contains("FakeShadow");
            IsSpecial = name.Contains("Fur") || name.Contains("Gem") || name.Contains("Refraction")
                || name.Contains("Overlay") || name.Contains("OutlineOnly") || name.Contains("Multi");
        }

        internal static bool TryGet(Material material, out LilToonShader shader)
        {
            var name = material != null && material.shader != null ? material.shader.name : null;
            if (name == null || !name.Contains("lilToon"))
            {
                shader = default;
                return false;
            }
            shader = new LilToonShader(name);
            return true;
        }
    }
}
