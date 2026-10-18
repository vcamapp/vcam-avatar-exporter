# VCAM_materials_liltoon

A glTF extension that records the lilToon material settings of an avatar exported as VRM 1.0. It records the values as they are set on the lilToon material in Unity, so a reader can reproduce the author's look.

The extension is optional. It is listed in `extensionsUsed` and never in `extensionsRequired`, and every material that has it also has a `VRMC_materials_mtoon` fallback. Readers that do not understand it show the MToon fallback.

## Root

`extensions.VCAM_materials_liltoon` on the glTF root identifies what wrote the file.

| Property | Type | Required | Description |
| --- | --- | --- | --- |
| `specVersion` | string | yes | Version of this format, such as `"1.0"` |
| `lilToonVersion` | string | yes | lilToon version the materials were set up with, such as `"2.3.4"` |
| `exporterVersion` | string | yes | Version of VCam Avatar Exporter that wrote the file |

The property names and meanings of lilToon materials change between lilToon versions. The values follow the lilToon version in `lilToonVersion`.

## Material

`materials[i].extensions.VCAM_materials_liltoon` holds the settings of one material.

| Property | Type | Required | Description |
| --- | --- | --- | --- |
| `shader` | string | yes | Name of the lilToon shader in Unity, such as `"Hidden/lilToonCutoutOutline"`. It selects the rendering mode (opaque, cutout, transparent) and whether the outline pass is drawn |
| `renderQueue` | integer | yes | Render queue of the material |
| `fallbackBaseColorAlpha` | number | no | Written for a material drawn again only where a stencil allows (see below). Its MToon fallback keeps its textures and colors but has the base color alpha at 0 so that it draws nothing, and this is the alpha the fallback would otherwise have |
| `floats` | object | no | lilToon float, range and integer properties, keyed by property name, such as `{ "_ShadowBorder": 0.5 }`. Integer properties are written as JSON integers |
| `colors` | object | no | lilToon color properties as `[r, g, b, a]`, keyed by property name. RGB is linear, as the shader receives it in a linear color space project. Alpha is unchanged |
| `vectors` | object | no | lilToon vector properties as `[x, y, z, w]`, keyed by property name |
| `textures` | object | no | lilToon texture properties, keyed by property name. See below |

Only properties defined by the lilToon shader are written. Readers ignore properties they do not know.

lilToon grows outlines and fur along the mesh's normals, so their lengths scale with the mesh, which the export does not keep. `_OutlineWidth` and the `w` of `_FurVector` are written in the avatar's scale: multiplied by how many times longer the mesh's lengths are in the world as Unity draws it, such as 0.01 for a mesh modeled in centimeters.

### Textures

Each entry of `textures` points at a glTF texture.

| Property | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | integer | yes | Index into the glTF `textures` array |
| `scale` | `[x, y]` | no | Tiling of the property (`_ST.xy` in Unity). Default `[1, 1]` |
| `offset` | `[x, y]` | no | Offset of the property (`_ST.zw` in Unity). Default `[0, 0]` |

Which texture coordinate set a property samples follows lilToon's own UV mode properties, such as `_Main2ndTex_UVMode`, in `floats`. Every primitive carries UV0 as `TEXCOORD_0`, and a primitive whose material samples UV1 in a feature it turns on also carries UV1 as `TEXCOORD_1`. UV2 and UV3 are not exported in this version. A primitive whose MToon material has a normal map carries the mesh's tangents as `TANGENT`. A material drawn only by meshes without tangents has no normal map in its MToon material, since Unity bends no normal with the map on such a mesh.

`scale` and `offset` are the Unity values, in Unity's texture coordinate space, where `v` points up, unlike glTF texture coordinates.

Textures that glTF images cannot hold, such as HDR textures and cube maps, are not exported in this version.

### Materials drawn again

Unity draws the materials of a renderer past its last submesh over that submesh again. lilToon fake shadows (`_lil/[Optional] lilToonFakeShadow`) use this to shade the face under the bangs, and materials compared with a stencil (`_StencilComp` other than Always) use it to draw the bangs see-through over the eyes. Each of these is exported as one more primitive of the same mesh that draws the last submesh's triangles with that material, and its MToon fallback draws nothing: a fake shadow's has no texture, and a stencil-compared material's has its base color alpha at 0, with the alpha it would otherwise have in `fallbackBaseColorAlpha`. Other materials past the last submesh are left out, and so is a white fake shadow without a texture, which changes nothing.

The stencil settings (`_StencilRef`, `_StencilComp`, `_StencilPass`, `_OutlineStencilRef` and the like) of every material are in `floats`. lilToon shifts a fake shadow along `_FakeShadowVector` in the axes of the mesh, which the export does not keep, so the `xyz` of a fake shadow's `_FakeShadowVector` is written in the avatar's axes as Unity has them: +Y up and +Z toward the avatar's front, with +X toward the avatar's right.

## Example

```json
{
  "extensionsUsed": ["VRMC_vrm", "VRMC_materials_mtoon", "VCAM_materials_liltoon"],
  "extensions": {
    "VCAM_materials_liltoon": {
      "specVersion": "1.0",
      "lilToonVersion": "2.3.4",
      "exporterVersion": "0.1.0"
    }
  },
  "materials": [
    {
      "name": "Hair",
      "extensions": {
        "VRMC_materials_mtoon": { "specVersion": "1.0" },
        "VCAM_materials_liltoon": {
          "shader": "Hidden/lilToonCutoutOutline",
          "renderQueue": 2450,
          "floats": { "_ShadowBorder": 0.5, "_ShadowBlur": 0.1, "_OutlineWidth": 0.08 },
          "colors": { "_Color": [1, 1, 1, 1], "_ShadowColor": [0.79, 0.65, 0.73, 1] },
          "textures": { "_MainTex": { "index": 2 } }
        }
      }
    }
  ]
}
```
