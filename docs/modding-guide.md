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
| `labelRenderSettings` | object | Settings for rendering inventory-icon labels on the carried block. |
| `slots` | object | Per-slot carry configuration keyed by slot name. |

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
- `Shoulder` (Not implemented)

Per-slot properties:

| Property | Description |
|---|---|
| `animation` | Animation code played while carrying in this slot. |
| `animationSit` | Animation code played while sitting and carrying. |
| `animationCrouch` | Animation code played while crouching and carrying. |
| `walkSpeedModifier` | Walk speed penalty applied in this slot. Negative values slow the player. |
| `enabledCondition` | Condition evaluated specifically for this slot. If false, this slot is unavailable. |
| `excludedTypes` | Block type wildcards excluded from using this slot. |

**Slot defaults** when properties are not specified:

| Slot | Default walk speed modifier | Default animation |
|---|---|---|
| `Hands` | `-0.25` | `carryon:holdheavy` |
| `Back` | `-0.15` | _(none)_ |
| `Shoulder` | `-0.15` | `carryon:shoulder` |

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
