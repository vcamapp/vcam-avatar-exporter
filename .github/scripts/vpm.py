"""Builds what VCC installs from: the package's zip for a release, and the listing published on GitHub Pages.

    python3 .github/scripts/vpm.py set-version <X.Y.Z>
    python3 .github/scripts/vpm.py package <out-dir> [--tag vX.Y.Z]
    python3 .github/scripts/vpm.py listing <site-dir>

The listing names every release's zip, and UniVRM, which has no VPM listing of its own, repackaged from its git tags.
`listing` reads the releases with the GitHub CLI from GITHUB_REPOSITORY.
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path
from string import Template

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / "Packages" / "com.vcamapp.avatar-exporter"
PAGES_URL = "https://vcamapp.github.io/vcam-avatar-exporter"
INDEX_HTML_TEMPLATE = ROOT / ".github" / "pages" / "index.html"
LISTING = {
    "name": "VCam",
    "id": "com.vcamapp.vpm",
    "url": f"{PAGES_URL}/index.json",
    "author": "VCam",
}

UNIVRM_REPOSITORY = "https://github.com/vrm-c/UniVRM.git"
# The UniVRM versions VCC can install, each one the exporter is verified with (DependencyVersions.cs; check.py checks this).
# The folders are the packages' paths in the repository.
UNIVRM_VERSIONS = ["0.131.2"]
UNIVRM_PACKAGES = {"com.vrmc.gltf": "Packages/UniGLTF", "com.vrmc.vrm": "Packages/VRM10"}
UNIVRM_LICENSE = "LICENSE.txt"
UNIVRM_LICENSE_META = """fileFormatVersion: 2
guid: $guid
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

SEMANTIC_VERSION = re.compile(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?")

# Zip entries get a fixed time so that a rebuilt zip of the same files has the same hash, which VCC checks.
ZIP_TIME = (1980, 1, 1, 0, 0, 0)


def is_hidden_from_unity(relative):
    """Whether Unity skips the path: hidden files and folders, and those ending in a tilde."""
    return any(part.startswith(".") or part.endswith("~") for part in relative.parts)


def write_zip(source, destination, leave_out=()):
    """
    Zips the files under the folder, with package.json at the root as VCC expects, the same bytes every time.
    `leave_out` names top-level files and folders, with their .meta, to leave out.
    """
    left_out = set(leave_out) | {name + ".meta" for name in leave_out}
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(source.rglob("*")):
            relative = path.relative_to(source)
            if path.is_dir() or relative.parts[0] in left_out or is_hidden_from_unity(relative):
                continue
            entry = zipfile.ZipInfo(relative.as_posix(), ZIP_TIME)
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o644 << 16
            archive.writestr(entry, path.read_bytes())


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_manifest(zip_path):
    with zipfile.ZipFile(zip_path) as archive:
        return json.loads(archive.read("package.json"))


def set_version(version):
    """Writes the version into package.json, keeping the rest of the file as it is."""
    if not SEMANTIC_VERSION.fullmatch(version):
        sys.exit(f"{version} is not a semantic version such as 0.1.0.")
    path = PACKAGE / "package.json"
    text, count = re.subn(r'^(  "version": ")[^"]*(")', rf"\g<1>{version}\2", path.read_text(encoding="utf-8"), count=1, flags=re.MULTILINE)
    if count != 1 or json.loads(text)["version"] != version:
        sys.exit("The version in package.json was not found.")
    path.write_text(text, encoding="utf-8")


def package(out_dir, tag):
    manifest = json.loads((PACKAGE / "package.json").read_text(encoding="utf-8"))
    if tag is not None and tag != f"v{manifest['version']}":
        sys.exit(f"The tag {tag} does not match the version {manifest['version']} in package.json.")
    out_dir.mkdir(parents=True, exist_ok=True)
    destination = out_dir / f"{manifest['name']}-{manifest['version']}.zip"
    # VCC puts packages in the project's Packages folder, where Unity compiles test assemblies too. The tests need
    # Modular Avatar, which users may not have, and a compile error there would stop the user's whole project.
    write_zip(PACKAGE, destination, leave_out=["Tests"])
    print(destination)


def release_zips(work):
    """The package's zip of every published release, with its download URL."""
    repository = os.environ["GITHUB_REPOSITORY"]
    # --slurp wraps the pages in one array, without which the pages' arrays follow each other and are not one JSON value.
    pages = json.loads(subprocess.run(
        ["gh", "api", "--paginate", "--slurp", f"repos/{repository}/releases"], check=True, capture_output=True, text=True
    ).stdout)
    for release in (release for page in pages for release in page):
        if release["draft"]:
            continue
        for asset in release["assets"]:
            if asset["name"].startswith("com.vcamapp.avatar-exporter-") and asset["name"].endswith(".zip"):
                path = work / asset["name"]
                urllib.request.urlretrieve(asset["browser_download_url"], path)
                yield path, asset["browser_download_url"]


def univrm_zips(work, site):
    """UniVRM's packages at each version, repackaged for VCC and saved under the site."""
    for version in UNIVRM_VERSIONS:
        checkout = work / f"UniVRM-{version}"
        subprocess.run(
            ["git", "-c", "advice.detachedHead=false", "clone", "--quiet", "--depth", "1", "--branch", f"v{version}", "--filter=blob:none", "--sparse",
             UNIVRM_REPOSITORY, str(checkout)], check=True)
        subprocess.run(["git", "-C", str(checkout), "sparse-checkout", "set", *UNIVRM_PACKAGES.values()], check=True)
        for name, folder in UNIVRM_PACKAGES.items():
            source = checkout / folder
            manifest_path = source / "package.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            if manifest["name"] != name or manifest["version"] != version:
                sys.exit(f"{folder} at v{version} is {manifest['name']} {manifest['version']}.")
            # VCC installs the UniVRM packages a package depends on only from vpmDependencies.
            manifest["vpmDependencies"] = {
                dependency: dependency_version
                for dependency, dependency_version in manifest.get("dependencies", {}).items()
                if dependency in UNIVRM_PACKAGES
            }
            manifest["license"] = "MIT"
            manifest["licensesUrl"] = f"https://github.com/vrm-c/UniVRM/blob/v{version}/{UNIVRM_LICENSE}"
            manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
            # The packages' folders do not carry the repository's license, which MIT requires with every copy. The
            # .meta has a GUID fixed by the package and version so that the zip keeps its hash when it is rebuilt.
            (source / UNIVRM_LICENSE).write_bytes((checkout / UNIVRM_LICENSE).read_bytes())
            guid = hashlib.md5(f"{name}-{version}/{UNIVRM_LICENSE}".encode()).hexdigest()
            (source / f"{UNIVRM_LICENSE}.meta").write_text(Template(UNIVRM_LICENSE_META).substitute(guid=guid), encoding="utf-8")
            relative = Path("univrm") / f"{name}-{version}.zip"
            destination = site / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            write_zip(source, destination)
            yield destination, f"{PAGES_URL}/{relative.as_posix()}"


def listing(site):
    site.mkdir(parents=True, exist_ok=True)
    packages = {}
    with tempfile.TemporaryDirectory() as temporary:
        work = Path(temporary)
        for path, url in [*release_zips(work), *univrm_zips(work, site)]:
            manifest = read_manifest(path)
            manifest["url"] = url
            manifest["zipSHA256"] = sha256(path)
            versions = packages.setdefault(manifest["name"], {"versions": {}})["versions"]
            versions[manifest["version"]] = manifest
    (site / "index.json").write_text(
        json.dumps({**LISTING, "packages": packages}, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    index_html = Template(INDEX_HTML_TEMPLATE.read_text(encoding="utf-8")).substitute(listing_url=LISTING["url"])
    (site / "index.html").write_text(index_html, encoding="utf-8")
    print(site / "index.json")


def main():
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    set_version_command = commands.add_parser("set-version")
    set_version_command.add_argument("version")
    package_command = commands.add_parser("package")
    package_command.add_argument("out_dir", type=Path)
    package_command.add_argument("--tag")
    listing_command = commands.add_parser("listing")
    listing_command.add_argument("site", type=Path)
    arguments = parser.parse_args()
    if arguments.command == "set-version":
        set_version(arguments.version)
    elif arguments.command == "package":
        package(arguments.out_dir, arguments.tag)
    else:
        listing(arguments.site)


if __name__ == "__main__":
    main()
