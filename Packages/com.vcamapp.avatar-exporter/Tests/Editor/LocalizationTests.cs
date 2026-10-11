using System;
using nadena.dev.ndmf.localization;
using NUnit.Framework;
using UniGLTF.Extensions.VRMC_vrm;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// What needs the editor: the translations as Unity imports them, and the names of UniVRM's license values.
    /// The keys and placeholders of the .po files are checked without Unity by .github/scripts/check.py.
    /// </summary>
    public sealed class LocalizationTests
    {
        [Test]
        public void NamesEveryVrmLicenseValue([Values(typeof(AvatarPermissionType), typeof(CommercialUsageType), typeof(CreditNotationType), typeof(ModificationType))] Type type)
        {
            foreach (var value in Enum.GetNames(type))
            {
                var key = $"{type.Name}:{value}";
                Assert.That(Strings.Tr(key), Is.Not.EqualTo($"<{key}>"), "NDMF shows a missing key in angle brackets");
            }
        }

        [Test]
        public void ShowsMessagesInTheEditorLanguage()
        {
            var language = LanguagePrefs.Language;
            try
            {
                LanguagePrefs.Language = "ja-JP";
                Assert.That(new Message("Report:NotInMToon", new Term("Feature:Emission"), "Skin").ToString(),
                    Is.EqualTo("MToon のマテリアルで表示する VRM アプリでは発光が表示されません: Skin"));
                LanguagePrefs.Language = "en-US";
                Assert.That(new Message("Report:NotInMToon", new Term("Feature:Emission"), "Skin").ToString(),
                    Is.EqualTo("VRM apps that show the MToon material do not show emission: Skin."));
            }
            finally
            {
                LanguagePrefs.Language = language;
            }
        }
    }
}
