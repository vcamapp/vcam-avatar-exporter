using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.localization;
using UnityEditor;
using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// The exporter's text in the editor language NDMF keeps, which Modular Avatar and other NDMF tools share, so the
    /// language picked in any of them applies here too.
    /// </summary>
    internal static class Strings
    {
        private const string DefaultLanguage = "en-US";
        private const string Folder = "Packages/com.vcamapp.avatar-exporter/Editor/Localization";

        private static readonly IReadOnlyList<string> Languages = new[] { DefaultLanguage, "ja-JP", "ko-KR", "zh-Hans", "zh-Hant" };

        private static readonly Localizer Localizer = new(DefaultLanguage, () =>
            Languages
                .Select(language => AssetDatabase.LoadAssetAtPath<LocalizationAsset>($"{Folder}/{language}.po"))
                .Where(asset => asset != null)
                .ToList());

        internal static string Tr(string key) => Localizer.GetLocalizedString(key);

        internal static string Format(string key, params object[] args) => string.Format(Tr(key), args);
    }
}
