# Contributing

Thank you for your interest in VCam Avatar Exporter. This project is maintained by a single person with limited time for review. The rules below exist so that contributions can be reviewed and merged reliably within that time. Pull requests that do not follow them may be closed regardless of their content.

## Discuss large changes in an issue first

Unless your change is a bug fix, a typo fix in the documentation or the translations, or a minor suggestion, open an issue and agree on the approach before writing a pull request. The following always require prior discussion:

- New export features, or changes to what is exported
- Changes to the glTF extensions specified in `docs/`
- Supporting other versions of Unity, lilToon, UniVRM, the VRChat SDK, Modular Avatar or NDMF
- Changes to dependencies, the package layout, the menus or the window
- Changes to the build, the GitHub Actions workflows or the release process

If a large pull request arrives without prior discussion and its design does not match the direction of the project, none of it can be accepted. Reach agreement in an issue before you start implementing.

## One pull request, one purpose

- Solve exactly one problem per pull request. Put "while I was at it" changes in separate pull requests
- Keep the change under 300 lines as a guideline, and under 500 lines at most, not counting `.meta` files and translations. If it has to be larger, propose how to split it in an issue first
- Pull requests that bundle several changes, bring over the state of a fork as is, or resolve several issues at once will not be accepted
- Keep refactoring separate from feature work

## Checklist before opening a pull request

- [ ] The description links the related issue (for example `Closes #123`)
- [ ] The change has a single, focused purpose
- [ ] The branch is rebased on the latest `main`
- [ ] The EditMode tests pass in Unity 2022.3.22f1
- [ ] `python3 .github/scripts/check.py` passes
- [ ] Tests covering the changed behavior are added or updated
- [ ] No new compiler warnings or console warnings are introduced
- [ ] No unrelated files are included (`ProjectSettings/` or `Packages/` changes Unity made on its own, formatting-only diffs, generated files, and so on)
- [ ] No third-party avatars, textures or other assets are included
- [ ] The specification in `docs/` is updated together with the exporter if the exported format changed
- [ ] The README is updated if what users see or do changed
- [ ] If the change affects the exported result or the window, before and after screenshots are attached to the pull request

## Setup

The repository root is a Unity 2022.3.22f1 project for developing the package in `Packages/com.vcamapp.avatar-exporter`.

UniVRM and lilToon come from `Packages/manifest.json`. The VRChat SDK, Modular Avatar and NDMF are pinned in `Packages/vpm-manifest.json`; restore them with [vrc-get](https://github.com/vrc-get/vrc-get):

```sh
vrc-get repo add https://vpm.nadena.dev/vpm.json
vrc-get resolve
```

## Code

- Match the style, naming, and comment density of the surrounding code
- Write comments only where they explain why something is done. Comments that describe what the code does, or what was changed, are not needed
- Do not leave commented-out code behind
- Never modify the user's avatar or its assets. Export from a copy
- Export only with the versions of lilToon, UniVRM and Unity the exporter has been verified with, and stop with the reason on any other version
- Do not put "VRChat", "lilToon" or "Unity" in the names of tools, packages or menus, which could suggest an official or affiliated product. Descriptions may use them
- Add or remove the `.meta` file together with every file and folder in the package

## Export format

`docs/format.md` defines `VCAM_materials_liltoon`, and `docs/springbone-immobile.md` defines `VCAM_springBone_immobile`. Change the specification and the exporter in the same pull request, and raise the extension's `specVersion` when readers need to know about the change.

## Third-party assets

Do not commit avatars, their textures or any other third-party assets, even for tests. Many of them do not allow redistribution under their terms of use. The same applies to files attached to issues and pull requests: share screenshots, not the avatar files.

## Tests

The EditMode tests need the Unity editor, so they are not run on GitHub. Run them before you open a pull request, in the Test Runner window or from the command line:

```sh
<Unity 2022.3.22f1>/Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults .build/test-results.xml -logFile .build/test.log
```

Leave out `-nographics`: the exporter bakes and shrinks textures on the GPU.

GitHub Actions checks the translations, the package manifests, the UniVRM versions of the listing and the `.meta` files with `.github/scripts/check.py`, and builds the package's zip with `.github/scripts/vpm.py package`. Run the check with `python3 .github/scripts/check.py`. Pull requests with failing checks will not be reviewed.

Since GitHub cannot run the EditMode tests, describe in the pull request which tests you ran and which avatars you exported to check the change.

## Translations

The window and the report are translated in `Packages/com.vcamapp.avatar-exporter/Editor/Localization/<language>.po`. Add a new key to every language.

## Changes made with AI tools

Using AI tools is not prohibited, but the following applies:

- Before submitting, read, understand, and verify every line of the change yourself. Do not submit changes you cannot explain
- Do not submit large AI-generated changes as they are. The size and scope limits above apply unchanged
- Claims such as "tested" or "verified" in the pull request description must describe what you actually did yourself
- If you used an AI tool, mention it in the pull request description or in a commit trailer. This is not required, but it helps the review

## Review

- Review can take time. Please refrain from posting reminders while waiting for a reply
- A change may be declined because it does not fit the direction of the project, or because its maintenance cost outweighs its benefit
- Pull requests that receive no reply or update for an extended period will be closed

## Releases

VCC installs the package from the VPM listing at `https://vcamapp.github.io/vcam-avatar-exporter/index.json`, which GitHub Pages serves. To release:

1. Run the EditMode tests on the default branch.
2. In the Actions tab, run the Release workflow on the default branch with the version, such as `0.1.0`.

The Release workflow writes the version into `package.json`, commits it and tags the commit `v<version>`, attaches the package's zip to a GitHub release and runs the Listing workflow, which rebuilds the listing from every release. It refuses a version that is already tagged. The listing also carries UniVRM, which has no VPM listing of its own, repackaged from the versions in `.github/scripts/vpm.py`. A push to the default branch that changes `vpm.py` or the listing page in `.github/pages/` also runs the Listing workflow.

The zip leaves out the tests: VCC puts packages in the project's Packages folder, where Unity would compile them in the user's project.

## License

By contributing, you agree that your contribution is released under the project's [license](./LICENSE).
