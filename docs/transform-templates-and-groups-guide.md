# Transform Templates and Inline Transform Groups

This guide explains how to use CarryOn transform templates and inline `transformGroups` in your own patches.

Use this document when you need to control how carried blocks are positioned, rotated, rendered, and decorated in hands/back slots.

Template code canonical form is `modid:code`. If a `transformTemplates` entry is bare (no domain), CarryOn defaults it to `carryon:<code>` during lookup.

---

## What This Covers

- Referencing built-in template assets from CarryOn.
- Defining your own templates in your mod.
- Adding inline `transformGroups` in a `Carryable` patch.
- Group inheritance and merge behavior (`extends`, `base`, `overrides`, `appends`).
- Advanced control groups (`^`, `@`, `~`).
- Full property reference for transform group entries.

For renderer internals and lifecycle details, see [Transform Template System](technical-overview/transform-template-system.md).

For dynamic resolver-based group selection (`transformGroupResolver`), see [transformGroupResolver Guide](transform-group-resolvers-guide.md).

---

## Where Templates Live

CarryOn built-in templates live in:

- `resources/assets/carryon/config/transformtemplates`

Current built-in template codes include:

- `carryon:carry-barrel`
- `carryon:carry-chest`
- `carryon:carry-chest-compact`
- `carryon:carry-crate`
- `carryon:carry-displaycase`
- `carryon:carry-flowerpot`
- `carryon:carry-planter`
- `carryon:carry-reedchest`
- `carryon:carry-storagevessel`
- `carryon:carry-storagevessel-planter`
- `carryon:carry-trunk`
- `carryon:carry-vat-planter`
- `carryon:plants-small`
- `carryon:plants-large`

Mods can define their own templates under their own domain:

- `resources/assets/<yourmod>/config/transformtemplates/<code>.json`

Example:

- Template file: `resources/assets/yourmod/config/transformtemplates/carry-myblock.json`
- Referenced in patch as: `"yourmod:carry-myblock"`

Notes:

- Canonical template code form is `modid:code`.
- If you omit the domain in `transformTemplates`, CarryOn defaults it to `carryon:<code>`.
- If a template JSON has a top-level `code`, it must match the filename (without `.json`) or it is skipped.

---

## Quick Start: Use a Built-In Template

Simple `Carryable` patch using only a built-in template:

```json
[
  {
    "file": "yourmod:blocktypes/mycrate",
    "op": "add",
    "path": "/behaviors/-",
    "value": {
      "name": "Carryable",
      "properties": {
        "enabledCondition": "carryon.Carryables.Crate",
        "transformTemplates": [
          "carry-crate"
        ],
        "slots": {
          "Hands": {},
          "Back": {
            "enabledCondition": "carryon.CarryablesOnBack.Crate"
          }
        }
      }
    },
    "dependsOn": [
      { "modid": "carryon" }
    ]
  }
]
```

When this is enough:

- Your block behaves like an existing carry family.
- You only need existing hands/backpack transforms.
- You do not need custom straps, extra parts, or variant-specific overrides.

---

## Quick Start: Define Your Own Template

Example template in your mod:

```json
{
  "code": "carry-myblock",
  "transformGroups": {
    "hands": [
      {
        "id": "root",
        "rotationY": 180.0
      }
    ],
    "backpack-none": [
      {
        "id": "root",
        "translation": [0.0, -0.04, 0.0],
        "rotationY": 180.0
      }
    ],
    "backpack-small": {
      "extends": "backpack-none",
      "overrides": [
        {
          "id": "root",
          "rotationZ": 10.0
        }
      ]
    }
  },
    "backpack-large": {
      "extends": "backpack-none",
      "overrides": [
        {
          "id": "root",
          "rotationZ": 13.0
        }
      ]
    }
}
```

Patch reference:

```json
"transformTemplates": [
  "yourmod:carry-myblock"
]
```

Canonical equivalent for built-in templates:

```json
"transformTemplates": [
  "carryon:carry-crate"
]
```

---

## Inline transformGroups in a Carryable Patch

Inline `transformGroups` are local to one block patch and are merged after templates.

Simple local alias example:

```json
"transformTemplates": [
  "carryon:carry-chest"
],
"transformGroups": {
  "backpack-none-normal": { "extends": "backpack-none" },
  "backpack-small-normal": { "extends": "backpack-small" },
  "backpack-large-normal": { "extends": "backpack-large" }
}
```

This is useful when:

- You want to keep using built-in templates.
- You only need per-block naming aliases or small local adjustments.
- You do not want to publish a reusable standalone template file.

---

## Group Definition Forms

A transform group can be defined in two forms.

Array shorthand:

```json
"hands": [
  { "id": "root", "rotationY": 180.0 }
]
```

Object form:

```json
"backpack-small": {
  "extends": "backpack-none",
  "base": [ { "id": "root", "translationY": 0.03 } ],
  "overrides": [ { "id": "root", "rotationZ": 11.0 } ],
  "appends": [ { "id": "strap-extra", "item": "carryon:strap" } ]
}
```

Resolution order inside one group:

1. Inherit from `extends` parent.
2. Apply `base`.
3. Apply `overrides`.
4. Append `appends`.

If entries share the same `id`, values are merged by overlay (incoming values replace existing ones).

---

## Advanced Example: Templates + Inline Overrides + Control Groups

This pattern combines template inheritance, ID-based overrides, and pre/post adjustment groups.

```json
"transformTemplates": [
  "carryon:carry-storagevessel",
  "carryon:carry-storagevessel-planter"
],
"transformGroups": {
  "^planted": [
    {
      "id": "plant1",
      "translationY": 0.26
    }
  ],
  "hands-planted": {
    "extends": "hands",
    "overrides": [
      {
        "id": "root",
        "rotation": [20.0, 10.0, -10.0]
      }
    ]
  },
  "@backpack-small": [
    {
      "id": "root",
      "rotationZ": 6.0
    }
  ],
  "~backpack-large": [
    {
      "id": "root",
      "translationY": 0.01
    }
  ]
}
```

What these do:

- `^target` applies pre-resolve relative adjustments to target group definitions.
- `@target` applies post-resolve overlay replacement to target resolved group.
- `~target` applies post-resolve relative adjustments to target resolved group.

Rules:

- `^`, `@`, and `~` groups must use array syntax.
- Entry matching is by `id` when present.
- Entry without `id` is only valid when the target has exactly one entry.

---

## Full Transform Entry Property Reference

These properties are supported inside transform group entries.

### Identity and source

| Property | Type | Description |
| --- | --- | --- |
| `id` | string | Stable identity for merge/upsert matching. Strongly recommended. |
| `item` | string | Asset code source as item (sets asset type to item). |
| `block` | string | Asset code source as block (sets asset type to block). |
| `disableIfItemStackPath` | string | Disables this entry when the specified carried data path resolves. |
| `blockEntityDataItemStackPath` | string | Uses an item stack path from carried block-entity data as render source. |

### Transform values

Vector form and component form are both supported.

| Property | Type | Description |
| --- | --- | --- |
| `translation` | vec3 | Sets `translationX/Y/Z`. |
| `rotation` | vec3 | Sets `rotationX/Y/Z`. |
| `scale` | float or vec3 | Uniform scale (float) or per-axis scale (vec3). |
| `origin` | vec3 | Sets `originX/Y/Z`. |
| `translationX/Y/Z` | float | Per-axis translation override. |
| `rotationX/Y/Z` | float | Per-axis rotation override. |
| `scaleX/Y/Z` | float | Per-axis scale override. |
| `originX/Y/Z` | float | Per-axis origin override. |

### Render and material

| Property | Type | Description |
| --- | --- | --- |
| `cullFaces` | bool | Face culling toggle. |
| `alphaTestOpaque` | float | Opaque alpha test threshold. |
| `alphaTestBlend` | float | Blended alpha test threshold. |
| `normalShaded` | bool | Normal shading toggle. |
| `renderPass` | string | Render pass hint (`opaque`, `translucent`, `both`). |
| `enabled` | bool | Enables/disables this transform entry. |
| `attachedRoot` | bool | Marks this entry as the visual root for attached block positioning. When set, the entry's transform is used as the base matrix for attached block meshes and labels (instead of `root`). Required when `root` is disabled and a `visual` entry provides the effective position. |

### Tint and glow

| Property | Type | Description |
| --- | --- | --- |
| `tintColor` | vec4 | Explicit RGBA tint. |
| `climateTintMap` | string | Climate tint map key. |
| `seasonalTintMap` | string | Seasonal tint map key. |
| `glowIntensity` | float | Converted to RGB glow vector in runtime render settings. |

---

## Merge and Precedence Rules

Definitions are merged in this order:

1. `transformTemplates` in listed order.
2. Inline `transformGroups` in the block patch.

Implications:

- Later template codes override earlier templates for the same group name.
- Inline groups override template groups with the same group name.
- Missing template codes are skipped (warning logged).

---

## Choosing Between Templates and Inline Groups

Use templates when:

- You want reuse across multiple blocks.
- You need larger transform definitions.
- You want assets that other mods or patches can reference.

Use inline groups when:

- You only need per-block local adjustments.
- You need aliases or tiny overrides on top of templates.
- You do not want another asset file.

Common pattern:

- Put base poses in template files.
- Keep block-specific aliases and tiny tuning in inline `transformGroups`.

---

## Troubleshooting Checklist

- Confirm template path: `resources/assets/<domain>/config/transformtemplates/<code>.json`.
- Use fully-qualified template codes (`modid:code`) to avoid domain confusion.
- Ensure template `code` matches filename when `code` is present.
- Use `id` on entries you expect to override or adjust later.
- Verify `^`, `@`, `~` groups are array syntax.
- Confirm target groups exist for `extends` and control-group targets.
- If transforms seem ignored, check that the block actually has `Carryable` and the relevant slot is enabled.

---

## See Also

- [Modding Guide](modding-guide.md)
- [transformGroupResolver Guide](transform-group-resolvers-guide.md)
- [Transform Template System](technical-overview/transform-template-system.md)
- [Carried Chest-Trunk and Chest Rendering](technical-overview/carried-chest-trunk-rendering.md)
- [Carried Plant Container Rendering](technical-overview/carried-plant-container-rendering.md)
