# VCam Avatar Exporter

Bring the avatar you set up for VRChat into [VCam](https://vcamapp.com/) and [VCam Camera](https://vcamapp.com/vcamcamera) as a VRM.

## What it does

- **Keeps your outfit.** The avatar is exported as it is after Modular Avatar and other non-destructive tools have run, with the outfit you dressed it in.
- **Moves its face.** Mouth shapes and blinks become VRM expressions, and ARKit shape keys become Perfect Sync, so face tracking in VCam drives your avatar.
- **Brings your expressions.** The facial expressions your avatar shows with hand gestures or expression menus become VRM expressions you can switch to in VCam.
- **Keeps hair and clothes swaying.** PhysBones are converted to VRM spring bones.
- **Works in other VRM apps too.** Every material is exported as MToon, along with the lilToon settings you tuned in Unity.
- **Tells you what changes.** After export, it lists the lilToon features that MToon cannot show and anything a VRM cannot hold.
- **Leaves your project untouched.** Export works on a copy of the avatar.

## Requirements

The exporter is made for avatars set up for VRChat. It needs:

- An avatar with a VRChat Avatar Descriptor and a Humanoid rig
- Unity 2022.3.22f1, the version VRChat uses
- VRChat Avatars SDK 3.10.0 or later, NDMF 1.14.7 or later (below 2.0) and UniVRM 0.131, which VCC installs with the exporter
- lilToon 2.3.4, installed as a package or imported into Assets

## Installation

Install the exporter with the VRChat Creator Companion (VCC).

1. Add the VCam listing to VCC: open the [listing page](https://vcamapp.github.io/vcam-avatar-exporter/) and select **Add to VCC**, or in VCC go to **Settings > Packages > Add Repository** and enter `https://vcamapp.github.io/vcam-avatar-exporter/index.json`.
2. If you have not added the Modular Avatar listing, which provides NDMF, add `https://vpm.nadena.dev/vpm.json` the same way.
3. Select **Manage Project** on your avatar's project and add **VCam Avatar Exporter**. VCC installs NDMF and UniVRM with it.
4. If the project does not have lilToon 2.3.4, install it from lilToon's own distribution. Avatars made with lilToon usually include it.

Open the exporter from **Tools > VCam Avatar Exporter** in Unity. The [documentation](https://docs.vcamapp.com/en/avatar-exporter/) walks through exporting, checking the result and loading the avatar into VCam.

## Before you export

Check your avatar's terms of use. Some avatars do not allow use outside VRChat or conversion to VRM.

## License

[MIT](LICENSE)

This project is not affiliated with or endorsed by VRChat Inc. or the author of lilToon.
