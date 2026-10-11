using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.PackageManager;
using UnityEngine;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// The Unity and package versions the exporter has been verified with. Export is refused on any other version:
    /// lilToon renames and reinterprets material properties between versions, and UniVRM changes what it writes.
    /// </summary>
    internal static class DependencyVersions
    {
        internal const string UnityVersion = "2022.3.22f1";

        internal const string LilToonPackage = "jp.lilxyzw.liltoon";

        internal static readonly IReadOnlyList<VerifiedPackage> Packages = new[]
        {
            new VerifiedPackage(LilToonPackage, "lilToon", "2.3.4"),
            new VerifiedPackage("com.vrmc.gltf", "UniGLTF", "0.131.0", "0.131.1", "0.131.2"),
            new VerifiedPackage("com.vrmc.vrm", "UniVRM", "0.131.0", "0.131.1", "0.131.2"),
        };

        /// <summary>Problems with the editor and packages of the open project, or an empty list if export can run.</summary>
        internal static IReadOnlyList<Message> FindProblems() => FindProblems(Application.unityVersion, InstalledVersion);

        /// <summary>The version of the package in the open project, or null when it is not installed.</summary>
        internal static string InstalledVersion(string name) =>
            PackageInfo.GetAllRegisteredPackages().FirstOrDefault(package => package.name == name)?.version
            ?? (name == LilToonPackage ? LilToonVersionInAssets() : null);

        /// <summary>
        /// The version lilToon reports when imported into Assets from a unitypackage, as avatars often have it, which
        /// the Package Manager does not register. It is looked up by name so that the exporter still compiles, and
        /// tells the user what is missing, without lilToon.
        /// </summary>
        internal static string LilToonVersionInAssets() =>
            Type.GetType("lilToon.lilConstants, lilToon.Editor")?.GetField("currentVersionName")?.GetValue(null) as string;

        internal static IReadOnlyList<Message> FindProblems(string unityVersion, Func<string, string> installedVersion)
        {
            var problems = new List<Message>();
            if (unityVersion != UnityVersion)
            {
                problems.Add(new Message("Error:UnsupportedUnity", unityVersion, UnityVersion));
            }
            foreach (var package in Packages)
            {
                var version = installedVersion(package.Name);
                if (version == null)
                {
                    problems.Add(new Message("Error:PackageMissing", package.DisplayName, package.VersionList));
                }
                else if (!package.Versions.Contains(version))
                {
                    problems.Add(new Message("Error:UnsupportedPackage", package.DisplayName, version, package.VersionList));
                }
            }
            return problems;
        }
    }

    internal sealed class VerifiedPackage
    {
        internal string Name { get; }
        internal string DisplayName { get; }
        internal IReadOnlyList<string> Versions { get; }

        internal string VersionList => string.Join(", ", Versions);

        internal VerifiedPackage(string name, string displayName, params string[] versions)
        {
            Name = name;
            DisplayName = displayName;
            Versions = versions;
        }
    }
}
