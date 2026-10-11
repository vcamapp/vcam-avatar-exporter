"""Checks the package without Unity: translations, manifests, the UniVRM versions of the listing and .meta files.

The EditMode tests need a licensed Unity editor, so they run on a developer's machine instead (see CONTRIBUTING.md).
"""
import json
import os
import re
import sys

import vpm
from vpm import PACKAGE, ROOT

EDITOR = PACKAGE / "Editor"
LOCALIZATION = EDITOR / "Localization"
DEFAULT_LANGUAGE = "en-US"

# Keys the code spells out, and labels whose tooltip key is the label key plus "Tooltip".
KEY_LITERAL = re.compile(r'"((?:Window|Error|Report|Purpose|Feature):[A-Za-z0-9]+)"')
TOOLTIP_KEY = re.compile(r'Content\("(Window:[A-Za-z0-9]+)"\)')
# The window names VRM license values by their enum, which the Unity tests check against UniVRM.
ENUM_KEY = re.compile(r"^(?:AvatarPermissionType|CommercialUsageType|CreditNotationType|ModificationType):[A-Za-z]+$")
ENTRY = re.compile(r'^msgid "(.*)"\r?\nmsgstr "(.*)"', re.MULTILINE)
LANGUAGE_HEADER = re.compile(r'^"Language: (.+)\\n"$', re.MULTILINE)
PLACEHOLDER = re.compile(r"\{\d+\}")

errors = []


def error(message):
    errors.append(message)


def read_po(path):
    text = path.read_text(encoding="utf-8")
    header = LANGUAGE_HEADER.search(text)
    if header is None or header.group(1) != path.stem:
        error(f"{path.name}: the Language header does not name {path.stem}")
    entries = {}
    for key, value in ENTRY.findall(text):
        if not key:
            continue
        if key in entries:
            error(f"{path.name}: {key} is translated twice")
        entries[key] = value
    return entries


def check_translations():
    files = sorted(LOCALIZATION.glob("*.po"))
    languages = {path.stem: read_po(path) for path in files}
    english = languages.get(DEFAULT_LANGUAGE)
    if english is None:
        error(f"{DEFAULT_LANGUAGE}.po is missing")
        return

    used = set()
    for source in EDITOR.rglob("*.cs"):
        text = source.read_text(encoding="utf-8")
        used.update(KEY_LITERAL.findall(text))
        used.update(key + "Tooltip" for key in TOOLTIP_KEY.findall(text))
    for key in sorted(used - english.keys()):
        error(f"{DEFAULT_LANGUAGE}.po: {key} is used in the code but not translated")
    for key in sorted(english.keys() - used):
        if not ENUM_KEY.match(key):
            error(f"{DEFAULT_LANGUAGE}.po: {key} is not used in the code")

    for language, entries in languages.items():
        for key in sorted(english.keys() - entries.keys()):
            error(f"{language}.po: {key} is missing")
        for key in sorted(entries.keys() - english.keys()):
            error(f"{language}.po: {key} is not in {DEFAULT_LANGUAGE}.po")
        for key in sorted(english.keys() & entries.keys()):
            if not entries[key]:
                error(f"{language}.po: {key} is empty")
            elif set(PLACEHOLDER.findall(entries[key])) != set(PLACEHOLDER.findall(english[key])):
                error(f"{language}.po: {key} has other placeholders than {DEFAULT_LANGUAGE}.po")


def check_manifests():
    manifests = {}
    for path in [PACKAGE / "package.json", *PACKAGE.rglob("*.asmdef")]:
        try:
            manifests[path] = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as e:
            error(f"{path.relative_to(ROOT)}: {e}")
    package = manifests.get(PACKAGE / "package.json")
    if package is None:
        return
    for field in ["name", "displayName", "version", "unity", "license", "vpmDependencies"]:
        if field not in package:
            error(f"package.json: {field} is missing")
    if not vpm.SEMANTIC_VERSION.fullmatch(package.get("version", "")):
        error("package.json: version is not a semantic version")


def check_univrm_versions():
    """The listing must offer only UniVRM versions the exporter accepts, or VCC installs one it refuses to export with."""
    source = (EDITOR / "DependencyVersions.cs").read_text(encoding="utf-8")
    verified = re.search(r'new VerifiedPackage\("com\.vrmc\.vrm", "UniVRM", ([^)]*)\)', source)
    if verified is None:
        error("the UniVRM versions are not found in DependencyVersions.cs")
        return
    for version in vpm.UNIVRM_VERSIONS:
        if f'"{version}"' not in verified.group(1):
            error(f"vpm.py lists UniVRM {version}, which DependencyVersions.cs does not accept")


def check_meta_files():
    """Unity gives a file without a .meta a new GUID on every import, which breaks references to it for users."""
    guids = {}
    for path in sorted(PACKAGE.rglob("*")):
        if vpm.is_hidden_from_unity(path.relative_to(PACKAGE)):
            continue
        if path.suffix == ".meta":
            if not path.with_suffix("").exists():
                error(f"{path.relative_to(ROOT)} has no file or folder")
            guid = re.search(r"^guid: ([0-9a-f]{32})$", path.read_text(encoding="utf-8"), re.MULTILINE)
            if guid is None:
                error(f"{path.relative_to(ROOT)} has no GUID")
            elif guid.group(1) in guids:
                error(f"{path.relative_to(ROOT)} has the same GUID as {guids[guid.group(1)]}")
            else:
                guids[guid.group(1)] = path.relative_to(ROOT)
        elif not path.with_name(path.name + ".meta").exists():
            error(f"{path.relative_to(ROOT)} has no .meta")


check_translations()
check_manifests()
check_univrm_versions()
check_meta_files()
for message in errors:
    print(f"::error::{message}" if "GITHUB_ACTIONS" in os.environ else message)
sys.exit(1 if errors else 0)
