# Modding Guide

This guide covers how to add CarryOn support to blocks or entities in your own mod, including all three supported behavior types.

## How Support Is Applied

CarryOn support can come from three places:

- Automatic behavior assignment for blocks similar to supported vanilla types.
- Compatibility patches bundled with CarryOn under `carryonmore`.
- Your own mod patch adding a CarryOn behavior.

For automatic matching rules and limitations, see [Automatic Behavior Assignment](automatic-behavior-assignment.md).

For currently supported mods and targets, see [Mod Support Inventory](mod-support-inventory.md).

For detailed transform template and inline transform group authoring, see [Transform Templates and Inline Transform Groups](transform-templates-and-groups-guide.md).

For dynamic resolver-based group selection (`transformGroupResolver`), see [transformGroupResolver Guide](transform-group-resolvers-guide.md). Resolver codes are canonicalized as `modid:code`; bare values default to `carryon:<code>` at lookup.

## Behavior Types

CarryOn exposes three behaviors that can be added via JSON patches:

| Behavior | Target | Patch syntax | Purpose |
|---|---|---|---|
| `Carryable` | Block types | `"name": "Carryable"` on `/behaviors/-` | Allow players to pick up and carry the block |
| `CarryableInteract` | Block types | `"name": "CarryableInteract"` on `/behaviors/-` | Allow players to interact with a carried block while holding it |
| `attachablecarryable` | Entity types | `"code": "carryon:attachablecarryable"` on `/server/behaviors/-` and `/client/behaviors/-` | Allow players to attach to and carry an entity (carts, sleds, etc.) |

---

## Carryable — Block Carry Support

`Carryable` is a block behavior that allows players to pick up and carry a block. It supports slot configuration, transform templates, label rendering, and world config conditions.

### Quick Start

Minimal example for a mod author adding CarryOn support to their own block. The `dependsOn` ensures the patch is only applied when CarryOn is installed:

```json
[
  {
    "file": "yourmod:blocktypes/mycontainer",
    "op": "add",
    "path": "/behaviors/-",
    "value": {
      "name": "Carryable",
      "properties": {
        "enabledCondition": "carryon.Carryables.Chest",
        "transformTemplates": [
          "carryon:carry-chest"
        ],
        "slots": {
          "Hands": {}
        }
      }
    },
    "dependsOn": [
      { "modid": "carryon" }
    ]
  }
]
```

An empty `Hands` slot object is sufficient — CarryOn fills in default animation and walk speed modifier automatically.

If the transformTemplates is omitted then the block's position in hands and on back will use the defaults.

### Advanced Example

This example adds label rendering, back carry support, and adjusts timing. This pattern is used by the bundled `bettercrates` compatibility patch:

```json
[
  {
    "file": "yourmod:blocktypes/labeledcrate",
    "op": "add",
    "path": "/behaviors/-",
    "value": {
      "name": "Carryable",
      "properties": {
        "enabledCondition": "carryon.Carryables.Crate",
        "patchPriority": 1,
        "swapBackKeyPassthrough": true,
        "transformTemplates": [
          "carryon:carry-crate"
        ],
        "labelRenderSettings": {
          "iconPixelSize": 128,
          "iconFromInventory": true,
          "transform": {
            "translation": [ -0.01, 0.29, 0.23 ],
            "rotation": [ -180.0, -90.0, 0.0 ],
            "scale": [ 0.54, 0.54, 0.8433 ],
            "origin": [ 0.0, 0.0, 0.0 ]
          }
        },
        "slots": {
          "Hands": {},
          "Back": {
            "enabledCondition": "carryon.CarryablesOnBack.Crate",
            "walkSpeedModifier": -0.2
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

### Carryable Properties

Top-level properties read by `BlockBehaviorCarryable`:

| Property | Type | Description |
|---|---|---|
| `enabledCondition` | string | Dot-notation world config condition. Behavior is removed if the condition is false at startup. |
| `patchPriority` | int | Priority used when multiple `Carryable` behaviors are present on the same block. Default `0`. |
| `overrideExistingProperties` | bool | Enables overlay/merge mode instead of winner-takes-all priority resolution. |
| `interactDelay` | float | Pickup interaction hold time in seconds. |
| `transferDelay` | float | Transfer interaction hold time in seconds. |
| `preventAttaching` | bool | Disallow attaching this block to a mount. |
| `optimisticPickup` | bool | Enables client-side prediction for the pickup action. |
| `forcePickupOnSwapBack` | bool | Forces pickup when the swap-back modifier key is held. |
| `swapBackKeyPassthrough` | bool | Yields block interaction to the underlying block when the swap-back modifier key is held. |
| `renderRootFirst` | bool | Rendering order hint for carried block compositing. |
| `transformGroupResolver` | string | Resolver code used for dynamic transform group selection. Canonical form is `modid:code`; bare values default to `carryon:<code>`. If omitted, no resolver is used. |
| `transformTemplates` | string[] | Transform template IDs loaded from asset files during finalization. Canonical form is `modid:code`; bare values default to `carryon:<code>`. |
| `transformGroups` | object | Inline transform group definitions. |
| `groups` | object | Type-to-group map for variant-based transform resolution. |
| `labelRenderSettings` | object | Settings for rendering inventory-icon labels on the carried block. See [Label Render Settings](#label-render-settings) below. |
| `defaultRenderFacing` | string | Overrides the facing direction used when resolving the carried block's render shape. Used for blocks like signs where the wall/ground variant normalizes during pickup. Example: `"north"`. |
| `defaultRenderVariant` | string | Overrides the variant segment used when resolving the carried block's render shape. Used alongside `defaultRenderFacing` to render the correct visual variant (e.g. `"wall"` for signs). When omitted, no render-block resolution is attempted. |
| `slots` | object | Per-slot carry configuration keyed by slot name. |

### Label Render Settings

The `labelRenderSettings` object controls how inventory-icon labels appear on the carried block, including text and item icon labels on containers, signs, and plaques. All sub-properties are optional.

| Property | Type | Description |
|---|---|---|
| `transform` | object | Model transform for the label quad. Position, rotation, scale, and origin relative to the carried block. |
| `attachedTransform` | object | Additional model transform applied when the block is rendered as an attached child in a carried cluster (e.g. a wall sign attached to a carried chest). Used to adjust the label position for the cluster context. |
| `additionalTransforms` | array | Array of extra model transforms for rendering multiple copies of the label. |
| `maxWidth` | int | Maximum width of the text label in pixels. Default `200`. |
| `maxHeight` | int | Maximum height of the text label in pixels. |
| `iconPixelSize` | int | Pixel size for rendering inventory item icons. Used when `iconFromInventory` is `true`. |
| `iconScale` | float | Scale factor for the inventory icon. |
| `iconFromInventory` | bool | If `true`, the label uses the first non-empty inventory slot's icon instead of sign text. |
| `fontName` | string | Font name override (e.g. `"vessel"`). |
| `verticalAlign` | string | Vertical alignment for text (`"top"`, `"middle"`, or `"bottom"`). |
| `boldFont` | bool | If `true`, renders the label text in bold. |

### Render Block Resolution for Multi-Variant Blocks

Blocks with multiple visual variants (e.g. `sign-wall-north`, `sign-ground-north`) may lose variant information when picked up because `OnPickBlock` can normalize the block code. The `defaultRenderFacing` and `defaultRenderVariant` properties restore the correct render shape:

- **`defaultRenderVariant`** specifies the variant segment (e.g. `"wall"` for signs).
- **`defaultRenderFacing`** specifies the facing direction (e.g. `"north"`).

When both are set, CarryOn reconstructs the original block code from `OriginalBlockCode` by replacing the variant and facing segments, then resolves the appropriate block for rendering. If `defaultRenderFacing` is omitted, `"north"` is used as the default. If `defaultRenderVariant` is omitted, no resolution is attempted.

**Example from `sign.json`** — a wall sign uses `defaultRenderFacing` and `defaultRenderVariant` to ensure the carried render shows the wall variant facing north, plus `labelRenderSettings` for the sign text label. The `attachedTransform` adjusts the label position when the sign is rendered as an attached block using the cluster carry feature (e.g. when carried alongside a chest via `CaptureAttachedWallSigns`):

```json
{
  "name": "Carryable",
  "properties": {
    "enabledCondition": "carryon.Carryables.Sign",
    "labelRenderSettings": {
      "transform": {
        "translation": [ 0.508, 0.863, 0.063 ],
        "scale": [ 0.916, 1.01, 1.0 ]
      },
      "attachedTransform": {
        "translation": [ 0, -0.01, 0 ]
      },
      "maxWidth": 200,
      "maxHeight": 96
    },
    "defaultRenderFacing": "north",
    "defaultRenderVariant": "wall"
  }
}
```

### Transform Templates and Inline Groups

`transformTemplates` and `transformGroups` are the core tools for controlling carried rendering pose and attached visual parts.

- Use `transformTemplates` to reference reusable transform assets (built-in or from your own mod domain).
- Use inline `transformGroups` for local aliases and per-block overrides.
- Templates are merged first, then local inline groups are applied on top.
- Canonical template code form is `modid:code`; bare `transformTemplates` values default to `carryon:<code>`.

This topic now has a dedicated guide with simple and advanced examples, merge rules, control groups (`^`, `@`, `~`), and full property reference:

- [Transform Templates and Inline Transform Groups](transform-templates-and-groups-guide.md)

For resolver-specific behavior (canonical resolver codes, runtime candidate generation, and how resolver output combines with `transformGroups`), see:

- [transformGroupResolver Guide](transform-group-resolvers-guide.md)

### Slots

Slot keys recognized in `slots`:

- `Hands`
- `Back`

Per-slot properties:

| Property | Description |
|---|---|
| `animation` | Animation code played while carrying in this slot. |
| `animationSit` | Animation code played while sitting and carrying. |
| `animationCrouch` | Animation code played while crouching and carrying. |
| `walkSpeedModifier` | Walk speed penalty applied in this slot. Negative values slow the player. |
| `walkSpeedModifierByBlockType` | Optional map of `type` value to walk speed modifier for this slot. Supports exact keys and trailing `*` prefix wildcards (for example `owl*`). |
| `walkSpeedModifierByGroup` | Optional map of carryable `groups` name to walk speed modifier for this slot. |
| `enabledCondition` | Condition evaluated specifically for this slot. If false, this slot is unavailable. |
| `excludedTypes` | Block type wildcards excluded from using this slot. |

If both maps are present, slot speed resolution is:

1. `walkSpeedModifierByBlockType` (most specific)
2. `walkSpeedModifierByGroup`
3. `walkSpeedModifier`

Example for chest compact variants:

```json
"groups": {
  "normal": ["normal-generic", "normal-aged"],
  "compact": ["golden", "owl", "golden-aged", "owl-aged"]
},
"slots": {
  "Back": {
    "walkSpeedModifier": -0.15,
    "walkSpeedModifierByGroup": {
      "normal": -0.15,
      "compact": -0.10
    },
    "walkSpeedModifierByBlockType": {
      "owl*": -0.08
    }
  }
}
```

These per-slot type and group values can be overridden from the world config using the `|type` pipe syntax in `ByBlockCode` (see `carryon-config.md`). For example, `"game:chest*|owl*": { "Back": -0.05 }` overrides the owl-type Back speed regardless of what the patch defines.

**Slot defaults** when properties are not specified:

| Slot | Default walk speed modifier | Default animation |
|---|---|---|
| `Hands` | `-0.25` | `carryon:holdheavy` |
| `Back` | `-0.15` | _(none)_ |

### Multiple Patches and Priority

When multiple `Carryable` behaviors exist on the same block (e.g. from auto-mapping and an explicit patch), CarryOn resolves them at server startup:

- If no behavior sets `overrideExistingProperties`, the highest `patchPriority` wins and the others are discarded.
- If any behavior sets `overrideExistingProperties: true`, the highest-priority non-overlay behavior is used as a base, and overlay behaviors at equal or higher priority are merged onto it.
- When overlays merge, arrays are replaced and `null` values merge (remove the key).

Use `patchPriority: 1` or higher when you need to take precedence over CarryOn's auto-assigned behavior or another bundled patch at the default priority `0`.

---

## CarryableInteract — Interact While Carrying

`CarryableInteract` is a block behavior that allows a player to interact with a block (open it, toggle it, etc.) while carrying another block. It is independent of `Carryable` — a block can have one, both, or neither.

`CarryableInteract` patches should set `"side": "Server"`.

### Example

```json
[
  {
    "file": "yourmod:blocktypes/mycontainer",
    "side": "Server",
    "op": "add",
    "path": "/behaviors/-",
    "value": {
      "name": "CarryableInteract",
      "properties": {
        "enabledCondition": "carryon.Interactables.Storage"
      }
    },
    "dependsOn": [
      { "modid": "carryon" }
    ]
  }
]
```

For blocks that require a longer hold time to interact (e.g. heavy metal doors):

```json
{
  "name": "CarryableInteract",
  "properties": {
    "enabledCondition": "carryon.Interactables.Door",
    "interactDelay": 1.3
  }
}
```

### CarryableInteract Properties

| Property | Type | Description |
|---|---|---|
| `enabledCondition` | string | Dot-notation world config condition. Same evaluation rules as `Carryable`. |
| `interactDelay` | float | Hold time required to interact. Defaults to standard interact delay. |

---

## attachablecarryable — Entity Carry Support

`attachablecarryable` is an entity behavior that allows players to attach/detach carried blocks to/from supported entity types (e.g. carts, sleds, market stalls). It requires patches to **both** the server and client behavior lists.

Entity behaviors use `code` rather than `name`.

### Example

```json
[
  {
    "file": "yourmod:entities/nonliving/mycart",
    "side": "Server",
    "op": "add",
    "path": "/server/behaviors/-",
    "value": {
      "code": "carryon:attachablecarryable"
    },
    "dependsOn": [
      { "modid": "carryon" }
    ]
  },
  {
    "file": "yourmod:entities/nonliving/mycart",
    "side": "Server",
    "op": "add",
    "path": "/client/behaviors/-",
    "value": {
      "code": "carryon:attachablecarryable"
    },
    "dependsOn": [
      { "modid": "carryon" }
    ]
  }
]
```

Both entries are required. The server-side entry controls logic; the client-side entry enables rendering.

---

## Conditions and World Config

`enabledCondition` uses dot-notation to reference the CarryOn world config. If the condition evaluates false at server startup, CarryOn removes that behavior from the block.

Common condition keys used by built-in patches:

| Condition | Default |
|---|---|
| `carryon.Carryables.Chest` | enabled |
| `carryon.Carryables.Crate` | enabled |
| `carryon.Carryables.Planter` | enabled |
| `carryon.CarryablesOnBack.Crate` | enabled |
| `carryon.Interactables.Storage` | enabled |
| `carryon.Interactables.Door` | enabled |

Conditions are evaluated against `CarryablesConfig`, `CarryablesOnBackConfig`, and `InteractablesConfig` depending on the key prefix.

---

## Practical Recommendations

- Start with a minimal `Hands` slot and only add transforms, labels, or `Back` support if needed.
- Use `patchPriority: 1` only when intentionally overriding auto-mapping or another patch.
- Use `overrideExistingProperties` only when you need a partial property overlay rather than full replacement.
- Prefer existing `transformTemplates` (e.g. `carryon:carry-chest`, `carryon:carry-crate`) over defining inline `transformGroups`.
- `CarryableInteract` and `Carryable` are independent — add both if the block should be both carryable and interactable-while-carried.
- `attachablecarryable` must be patched onto both `/server/behaviors/-` and `/client/behaviors/-` or carry behavior will not work correctly.
