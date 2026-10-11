using System.Collections.Generic;
using System.Linq;
using UniGLTF;
using UnityEngine;
using UniVRM10;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Adds the mesh's second texture coordinate set as <c>TEXCOORD_1</c> to the primitives whose lilToon material
    /// samples it. UniVRM writes only the first set to VRM 1.0.
    /// </summary>
    internal static class SecondTexCoords
    {
        internal static void Write(ExportingGltfData storage, ModelExporter converter, IReadOnlyDictionary<Material, LilToonSource> lilToonSources) =>
            PrimitiveAttributes.Add(storage, converter, mesh => mesh.uv2,
                primitive => primitive.material is { } material && SamplesSecondSet(converter.Materials[material], lilToonSources),
                // glTF's v runs down the image, Unity's up.
                texCoord => new Vector2(texCoord.x, 1 - texCoord.y),
                (primitive, accessor) => primitive.attributes.TEXCOORD_1 = accessor);

        private static bool SamplesSecondSet(Material fallback, IReadOnlyDictionary<Material, LilToonSource> lilToonSources) =>
            lilToonSources.TryGetValue(fallback, out var source)
            && LilToonFeatures.TexCoordUses(source.Material, source.Shader).Any(use => use.Set == 1);
    }
}
