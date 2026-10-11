using UnityEngine;

namespace VCam.AvatarExporter
{
    internal static class TextureBlit
    {
        /// <summary>
        /// Draws <paramref name="source"/> through <paramref name="material"/>, or as is when it is null, into a new
        /// readable texture. Only the base level is exported, so the texture has no mipmaps.
        /// </summary>
        internal static Texture2D Render(Texture source, Material material, int width, int height, bool linear, string name)
        {
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            try
            {
                if (material != null)
                {
                    Graphics.Blit(source, target, material);
                }
                else
                {
                    Graphics.Blit(source, target);
                }
                RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, linear) { name = name };
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(false);
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        /// <summary>The pixels of <paramref name="source"/> drawn at the size, or null when there is no texture.</summary>
        internal static Color[] ReadPixels(Texture source, int width, int height, bool linear)
        {
            if (source == null)
            {
                return null;
            }
            var texture = Render(source, null, width, height, linear, source.name);
            try
            {
                return texture.GetPixels();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
