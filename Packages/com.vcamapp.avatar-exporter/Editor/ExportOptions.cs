using UniGLTF.Extensions.VRMC_vrm;
using UniVRM10;

namespace VCam.AvatarExporter
{
    /// <summary>What the exported VRM says about the avatar and how it may be used.</summary>
    internal sealed class ExportOptions
    {
        // The most restrictive choices by default: the exporter is often not the author of the avatar.
        internal readonly VRM10ObjectMeta Meta = new()
        {
            Name = "",
            Version = "",
            CopyrightInformation = "",
            ContactInformation = "",
            AvatarPermission = AvatarPermissionType.onlyAuthor,
            CommercialUsage = CommercialUsageType.personalNonProfit,
            CreditNotation = CreditNotationType.required,
            Modification = ModificationType.prohibited,
        };

        /// <summary>The longest side of an exported texture in pixels, or 0 to keep the sources' sizes.</summary>
        internal int MaxTextureSize = 2048;

        /// <summary>
        /// Makes a shape key for each eye from a blink that closes both, when the avatar has none, so it can wink.
        /// </summary>
        internal bool SplitsBlink = true;
    }
}
