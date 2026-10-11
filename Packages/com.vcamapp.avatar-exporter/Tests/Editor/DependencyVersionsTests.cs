using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VCam.AvatarExporter
{
    public sealed class DependencyVersionsTests
    {
        private static readonly Dictionary<string, string> VerifiedPackages = new()
        {
            [DependencyVersions.LilToonPackage] = "2.3.4",
            ["com.vrmc.gltf"] = "0.131.2",
            ["com.vrmc.vrm"] = "0.131.2",
        };

        private static IReadOnlyList<Message> FindProblems(string unityVersion, Dictionary<string, string> packages) =>
            DependencyVersions.FindProblems(unityVersion, name => packages.TryGetValue(name, out var version) ? version : null);

        private static string VersionList(string displayName) =>
            DependencyVersions.Packages.Single(package => package.DisplayName == displayName).VersionList;

        [Test]
        public void VerifiedVersionsHaveNoProblems()
        {
            Assert.That(FindProblems(DependencyVersions.UnityVersion, VerifiedPackages), Is.Empty);
        }

        [Test]
        public void DevelopmentProjectUsesVerifiedVersions()
        {
            Assert.That(DependencyVersions.FindProblems(), Is.Empty);
        }

        [Test]
        public void ReadsTheVersionLilToonReportsWhereverItIs()
        {
            Assert.That(DependencyVersions.LilToonVersionInAssets(), Is.EqualTo("2.3.4"));
        }

        [Test]
        public void OtherUnityVersionIsAProblem()
        {
            Assert.That(FindProblems("2022.3.62f1", VerifiedPackages), Is.EqualTo(new[] { new Message("Error:UnsupportedUnity", "2022.3.62f1", DependencyVersions.UnityVersion) }));
        }

        [Test]
        public void UnverifiedPackageVersionIsAProblem()
        {
            var packages = new Dictionary<string, string>(VerifiedPackages) { [DependencyVersions.LilToonPackage] = "2.3.5" };
            Assert.That(FindProblems(DependencyVersions.UnityVersion, packages), Is.EqualTo(new[] { new Message("Error:UnsupportedPackage", "lilToon", "2.3.5", VersionList("lilToon")) }));
        }

        [Test]
        public void MissingPackageIsAProblem()
        {
            var packages = new Dictionary<string, string>(VerifiedPackages);
            packages.Remove("com.vrmc.vrm");
            Assert.That(FindProblems(DependencyVersions.UnityVersion, packages), Is.EqualTo(new[] { new Message("Error:PackageMissing", "UniVRM", VersionList("UniVRM")) }));
        }
    }
}
