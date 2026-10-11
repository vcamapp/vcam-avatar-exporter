using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>Reads material properties that the material's shader variant may not declare.</summary>
    internal static class MaterialProperties
    {
        internal static float FloatOr(this Material material, string name, float fallback = 0) =>
            material.HasProperty(name) ? material.GetFloat(name) : fallback;

        internal static Color ColorOr(this Material material, string name, Color fallback) =>
            material.HasProperty(name) ? material.GetColor(name) : fallback;

        internal static Vector4 VectorOr(this Material material, string name, Vector4 fallback) =>
            material.HasProperty(name) ? material.GetVector(name) : fallback;

        internal static Texture TextureOrNull(this Material material, string name) =>
            material.HasProperty(name) ? material.GetTexture(name) : null;
    }
}
