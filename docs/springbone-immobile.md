# VCAM_springBone_immobile

A glTF extension that records the Immobile setting of the PhysBones an avatar exported as VRM 1.0 had. In VRChat, Immobile damps how much the motion of the node a PhysBone is measured from swings its bones. The PhysBones themselves are exported as `VRMC_springBone` springs.

The extension is optional. It is listed in `extensionsUsed` and never in `extensionsRequired`. For readers that do not understand it, a chain whose first joint is at least half immobile has that node as the `center` of its spring, and any other chain has no `center`.

## Root

`extensions.VCAM_springBone_immobile` on the glTF root.

| Property | Type | Required | Description |
| --- | --- | --- | --- |
| `specVersion` | string | yes | Version of this format, such as `"1.0"` |
| `joints` | array | yes | One entry per `VRMC_springBone` joint of a PhysBone with Immobile above 0. See below |

## Joint

| Property | Type | Required | Description |
| --- | --- | --- | --- |
| `node` | integer | yes | Index into the glTF `nodes` array of the joint, as a `VRMC_springBone` joint names it |
| `reference` | integer | yes | Index into the glTF `nodes` array of the node Immobile is measured from. It is above `node`, and no spring has it or a node above it as a joint |
| `immobile` | number | yes | Immobile of the PhysBone at the joint, from 0 to 1, with the PhysBone's Immobile curve applied |

Every joint of a chain that swings is listed when any of them has Immobile above 0, so a joint can be listed with 0. The last joint of a chain, which is only its tail, is not listed.

`reference` is the parent of the PhysBone's root transform for the All Motion Immobile type, and for World the ancestor of the joint right below the avatar root. When that node is a joint of another spring, as for a PhysBone nested in another, `reference` is the node above the highest such joint. A chain whose node would be the avatar root, which is not a glTF node, is not listed.

## Example

```json
{
  "extensionsUsed": ["VCAM_springBone_immobile"],
  "extensions": {
    "VCAM_springBone_immobile": {
      "specVersion": "1.0",
      "joints": [
        { "node": 42, "reference": 17, "immobile": 0.7 },
        { "node": 43, "reference": 17, "immobile": 0.7 }
      ]
    }
  }
}
```
