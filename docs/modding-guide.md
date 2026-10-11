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
| `carryon:carryable` | Block types | `"name": "carryon:carryable"` on `/behaviors/-` | Allow players to pick up and carry the block |
| `carryon:carryableinteract` | Block types | `"name": "carryon:carryableinteract"` on `/behaviors/-` | Allow players to interact with a carried block while holding it |
| `carryon:attachablecarryable` | Entity types | `"code": "carryon:attachablecarryable"` on `/server/behaviors/-` and `/client/behaviors/-` | Allow players to attach to and carry an entity (carts, sleds, etc.) |

> **Note:** Legacy bare codes (`"Carryable"`, `"CarryableInteract"`) are still accepted via alias registrations, but modders should update to the namespaced forms. The aliases will remain in place for the foreseeable future to give modders time to update.

---

## Carryable — Block Carry Support

`carryon:carryable` is a block behavior that allows players to pick up and carry a block. It supports slot configuration, transform templates, label rendering, and world config conditions.

### Quick Start

Minimal example for a mod author adding CarryOn support to their own block. The `dependsOn` ensures the patch is only applied when CarryOn is installed:

```json
[
  {
    "file": "yourmod:blocktypes/mycontainer",
    "op": "add",
    "path": "/behaviors/-",
    "value": {
      "name": "carryon:carryable",
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
      "name": "carryon:carryable",
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
| `transferCode` | string | Transfer handler mode. Unset means the block does not participate in transfer. `"auto"` (reserved) auto-detects a `BlockBehavior` on the block that implements `ICarryableBlockSlotHandler`. Any other value is the code of a handler registered via `RegisterTransferHandler` (canonical form `modid:code`; bare values are also tried with the `carryon:` prefix). See [Transfer Handler Registration](#transfer-handler-registration). |
| `renderRootFirst` | bool | Rendering order hint for carried block compositing. |
| `transformGroupResolver` | string | Resolver code used for dynamic transform group selection. Canonical form is `modid:code`; bare values default to `carryon:<code>`. If omitted, no resolver is used. |
| `transformTemplates` | string[] | Transform template IDs loaded from asset files during finalization. Canonical form is `modid:code`; bare values default to `carryon:<code>`. |
| `transformGroups` | object | Inline transform group definitions. |
| `groups` | object | Type-to-group map for variant-based transform resolution. |
| `labelRenderSettings` | object | Settings for rendering inventory-icon labels on the carried block. See [Label Render Settings](#label-render-settings) below. |
| `rootRenderFacing` | string | Overrides the facing direction used when resolving the carried block's render shape. Used for blocks like signs where the wall/ground variant normalizes during pickup. Example: `"north"`. |
| `rootRenderVariant` | string | Overrides the variant segment used when resolving the carried block's render shape. Used alongside `rootRenderFacing` to render the correct visual variant (e.g. `"wall"` for signs). When omitted, no render-block resolution is attempted. |
| `rootGroupResolver` | string | Resolver code for root transform group selection. If omitted, falls back to `transformGroupResolver` (if set), otherwise no resolver is used. Canonical form is `modid:code`. |
| `attachmentGroupResolver` | string | Resolver code for attachment (cluster child) transform group selection. If omitted, falls back to `transformGroupResolver` (if set), otherwise no resolver is used. Canonical form is `modid:code`. |
| `dataAttributes` | string[] | Attribute names used by the `data-attributes` transform group resolver. Typically references block data attributes that encode visual state (e.g. contents, logged state). |
| `dataAttributesPrefix` | string | Prefix prepended to data attribute values when generating group candidates via the `data-attributes` resolver. |
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

Blocks with multiple visual variants (e.g. `sign-wall-north`, `sign-ground-north`) may lose variant information when picked up because `OnPickBlock` can normalize the block code. The `rootRenderFacing` and `rootRenderVariant` properties restore the correct render shape:

- **`rootRenderVariant`** specifies the variant segment (e.g. `"wall"` for signs).
- **`rootRenderFacing`** specifies the facing direction (e.g. `"north"`).

When both are set, CarryOn reconstructs the original block code from `OriginalBlockCode` by replacing the variant and facing segments, then resolves the appropriate block for rendering. If `rootRenderFacing` is omitted, `"east"` is used as the default. If `rootRenderVariant` is omitted, no resolution is attempted.

**Example from `sign.json`** — a wall sign uses `rootRenderFacing` and `rootRenderVariant` to ensure the carried render shows the wall variant facing north, plus `labelRenderSettings` for the sign text label. The `attachedTransform` adjusts the label position when the sign is rendered as an attached block using the cluster carry feature (e.g. when carried alongside a chest via `CaptureAttachedWallSigns`):

```json
{
  "name": "carryon:carryable",
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
    "rootRenderFacing": "north",
    "rootRenderVariant": "wall"
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
| `hungerModifier` | Hunger rate multiplier applied when carrying in this slot. Positive values increase hunger drain. |
| `hungerModifierByBlockType` | Optional map of `type` value to hunger rate modifier for this slot. Supports exact keys and trailing `*` prefix wildcards. |
| `hungerModifierByGroup` | Optional map of carryable `groups` name to hunger rate modifier for this slot. |
| `enabledCondition` | Condition evaluated specifically for this slot. If false, this slot is unavailable. |
| `excludedTypes` | Block type wildcards excluded from using this slot. |

If both maps are present, slot speed resolution is:

1. `walkSpeedModifierByBlockType` (most specific)
2. `walkSpeedModifierByGroup`
3. `walkSpeedModifier`

Similarly, slot hunger modifier resolution follows the same order:

1. `hungerModifierByBlockType`
2. `hungerModifierByGroup`
3. `hungerModifier`

Both speed and hunger values can be overridden from `carryon-config.md` via `CarryWalkSpeed.ModifierOverrides` and `CarryHungerRate.ModifierOverrides` respectively, which take priority over patch-level values.

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

### Transfer Handler Registration

Transfer handlers (`ICarryableBlockSlotHandler`) are resolved lazily by `BlockBehaviorCarryable` per block, based on the `transferCode` behavior property:

| `transferCode` | Behavior |
|---|---|
| *(unset)* | The block does not participate in transfer. No handler resolution is attempted, so there is no per-interaction cost for blocks without transfer support. |
| `"auto"` | CarryOn scans the block's behaviors for one that implements `ICarryableBlockSlotHandler` and uses it as the handler. `"auto"` is reserved and cannot name a registered handler. |
| `"<code>"` | CarryOn looks up a handler registered with `RegisterTransferHandler`. Bare codes are also tried with the `carryon:` prefix (e.g. `"transferCode": "chest"` matches `carryon:chest`). |

Example — attach an `ICarryableBlockSlotHandler` block behavior and opt in to auto-detection:

```json
{
  "name": "carryon:carryable",
  "properties": { "transferCode": "auto" }
}
```

When the target block already carries a CarryOn-provided `Carryable` behavior (e.g. the mold rack), add `transferCode: "auto"` to your own `Carryable` patch as well — the merged or primary behavior must carry the opt-in for detection to work on both sides.

**Register on both sides.** `RegisterTransferHandler` must be called on *both* the client and server (for example in `StartClientSide` and `StartServerSide`, or from a shared `Start`, of your `ModSystem`). Handler registration is per-side and is not synced between processes: a handler registered only on the server never resolves on the client, which silently disables client-side transfer pre-validation (`IsSlotCarryable`/`CanTake`/`CanPut`) and the associated interaction feedback for that block.

```csharp
var carrySystem = api.ModLoader.GetModSystem<CarrySystem>();
carrySystem?.CarryManager?.RegisterTransferHandler("mymod:chest", new MyChestTransferHandler());
```

### Transfer Handler Interface

A transfer handler implements `ICarryableBlockSlotHandler` (`CarryOn.API.Common.Interfaces`), typically on the same `BlockBehavior` referenced by the block's `Carryable` behavior. The member shape mirrors the entity slot handler:

| Member | Side(s) | Purpose |
|---|---|---|
| `GetInteractDelay(blockEntity, api, slotIndex)` | Client | Hold duration in seconds for the progress circle, stored in the interaction's transfer delay. |
| `GetSlotIndex(blockEntity, selection)` | Client | Maps `BlockSelection.SelectionBoxIndex` to the logical 0-based inventory slot index; `-1` when the selection is not a transfer slot. |
| `IsValidSlot(blockEntity, slotIndex)` | Client + server | Cheap existence check; the server guards forged indices before `Try*`. |
| `IsSlotEmpty(blockEntity, slotIndex)` | Client | Routing/hint support. |
| `IsSlotCarryable(blockEntity, slotIndex)` | Client | Hint support; a take is only possible when the slot holds a carryable block. |
| `CanPut` / `CanTake` | Client | Non-mutating pre-hold checks; the authoritative source of mod-specific errors. `CanTake`'s failure code also routes the empty/non-carryable-slot outcome (`null` = continue to the next interaction, `__default__` = vanilla, lang key = error). |
| `TryPut` / `TryTake` | Server | Authoritative mutation; re-runs the `Can*` checks internally. The client never invokes these. |
| `IsBlockCarryAllowed(player, selection)` | Client | Blocks whole-block pickup when a slot holds content. |
| `OnClientTransferToggled(player, slotIndex, isPut)` | Client | Fired after the put/take packet is sent; optimistic state flip (empty↔filled) applied locally before the server replies (can be a no-op). |

**Error contract.** A `false` return sets `failureCode` to the *full* lang key of a message in the implementing mod's own lang files (e.g. `rackemup:mold-rack-transfer-incompatible`), resolved verbatim via `Lang.GetL(player.LanguageCode, ...)` on the server and `Lang.Get` on the client — no `carryon:` prefix is applied and no format arguments are passed, so lang strings must not use `{0}` placeholders. Server-side resolution uses the reading player's language rather than the server's, so a `ServerLanguage = "en"` server still toasts in each player's language; a key that player's language lacks falls back to English. To display such an error from your own code, use the public helper `CarryOn.Utility.CarryErrorHelper` (`ShowError(api, failureCode)` on the client, `SendError(player, failureCode)` on the server). A `null`/empty `failureCode` with `false` means "not this handler / continue to the next interaction" (so with empty hands the whole-block pickup stage runs). The reserved sentinel codes from `CarryConstants.FailureCodes` are not lang keys: `__ignore__` silently completes the interaction, `__stop__` completes the interaction, `__default__` lets the vanilla interaction proceed. With empty hands, `CanTake` is the routing decision — the validator does not pre-gate on `IsSlotCarryable`.

### Multiple Patches and Priority

When multiple `Carryable` behaviors exist on the same block (e.g. from auto-mapping and an explicit patch), CarryOn resolves them at server startup:

- If no behavior sets `overrideExistingProperties`, the highest `patchPriority` wins and the others are discarded.
- If any behavior sets `overrideExistingProperties: true`, the highest-priority non-overlay behavior is used as a base, and overlay behaviors at equal or higher priority are merged onto it.
- When overlays merge, arrays are replaced and `null` values merge (remove the key).

Use `patchPriority: 1` or higher when you need to take precedence over CarryOn's auto-assigned behavior or another bundled patch at the default priority `0`.

---

## carryon:carryableinteract — Interact While Carrying

`carryon:carryableinteract` is a block behavior that allows a player to interact with a block (open it, toggle it, etc.) while carrying another block. It is independent of `carryon:carryable` — a block can have one, both, or neither.

`carryon:carryableinteract` patches should set `"side": "Server"`.

### Example

```json
[
  {
    "file": "yourmod:blocktypes/mycontainer",
    "side": "Server",
    "op": "add",
    "path": "/behaviors/-",
    "value": {
      "name": "carryon:carryableinteract",
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
  "name": "carryon:carryableinteract",
  "properties": {
    "enabledCondition": "carryon.Interactables.Door",
    "interactDelay": 1.3
  }
}
```

### carryon:carryableinteract Properties

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

### Attributes

| Attribute | Type | Default | Description |
|---|---|---|---|
| `carrySlots` | string[] | _(all slots)_ | Array of attachment-point codes that define which entity slots are carry-eligible. Supports `*` wildcards (e.g. `"LStore*"` matches `LStore1AP`, `LStore2AP`, etc.; `"*Store*"` matches any slot containing "Store"). Only matching slots show carry interaction help and trigger attach/detach. If omitted, all slots are eligible (backward-compatible). Non-matching slots pass through to vanilla interaction (e.g. mounting a seat). |

Example with `carrySlots`:

```json
{
  "code": "carryon:attachablecarryable",
  "carrySlots": ["LStore*", "RStore*"]
}
```

Use the attachment-point codes from the entity's `rideableaccessories` → `wearableSlots` → `attachmentPointCode` (or the `selectionBoxes` array). These are the same codes used in the entity's shape attachment points. Wildcard `*` matches any sequence of characters; entries without `*` must match exactly (case-sensitive).

### Slot Handler Resolution (`slotHandlerCode`)

`slotHandlerCode` is an attribute of the `Carryable` entity behavior (not `attachablecarryable`). It selects which slot handler governs per-slot attach/detach:

| Value | Behavior |
|---|---|
| *(unset)* | Auto-detect: CarryOn scans the entity's behaviors (then the entity itself) for an implementation of `ICarryableEntitySlotHandler`. This is how the built-in `attachablecarryable` is found. |
| `"auto"` | Explicitly requests the same auto-detection. `"auto"` is **reserved** — it is never treated as a registered handler code, so it can be used to override any `slotHandlerCode` inherited from a template or another mod's patch. |
| `"<code>"` | CarryOn looks up a handler registered with `RegisterSlotHandler` (canonical form `modid:code`; bare values are also tried with the `carryon:` prefix). If the code is not registered, CarryOn logs a warning and **slot attach/detach is disabled** — it does not silently fall back to a different handler. Use `"auto"` if you want detection instead. |

Example — opt in explicitly on a `Carryable` behavior:

```json
{
  "code": "carryon:carryable",
  "slotHandlerCode": "auto"
}
```

### Coexistence with Whole-Entity Pickup

A slot handler (auto-detected `attachablecarryable`, or a handler referenced by `slotHandlerCode`) can coexist with a whole-entity handler (referenced by `handlerCode`) on the same entity — they are not mutually exclusive:

- **Looking at a storage slot** (a carry-eligible selection box): the slot handler takes precedence. Holding the carry key attaches/detaches the carried block into/from that slot; without the carry key the vanilla interaction (e.g. opening the cart's storage) proceeds.
- **Looking at any other part of the entity** (body, seats, non-eligible slots): the entity handler governs — the pickup help is shown and right-clicking picks up the whole entity.

Entities configured only for slot attach/detach (the common case, e.g. carts) show no pickup help and cannot be picked up as a whole.

### Whole-Entity Pickup Mode (`pickupMode`)

The `carryon:carryable` behavior supports an optional `pickupMode` attribute that controls which input combination triggers whole-entity pickup:

| Value | Input | Hint |
|---|---|---|
| *(unset)* / `"interact"` | Right mouse only (default) | No key combo shown |
| `"pickup-hotkey"` | Hold **Pick Up / Put Down** hotkey + Right mouse | Shows mapped key next to mouse icon |

Example — require the pickup hotkey for a demo entity:

```json
{
  "code": "carryon:carryable",
  "handlerCode": "carrieddemo:demoentity",
  "pickupMode": "pickup-hotkey",
  "pickupHelpLangCode": "carrieddemo:entityhelp-pickup"
}
```

When `pickupMode` is `"pickup-hotkey"`, the interaction help HUD automatically renders the mapped key name (e.g. "Shift + Right mouse") following the player's keybinding. The preset governs **whole-entity pickup only**; slot attach/detach always requires the carry key regardless of this setting.

**Slot error contract.** When a `CanAttach`/`CanDetach`/`TryAttach`/`TryDetach` returns `false`, `failureCode` is resolved with a hybrid rule: a code containing a domain separator (`:`) is a *full* lang key owned by the implementing mod and resolves verbatim (e.g. `rackemup:shattered`); a code without one is a *bare* code that resolves as `carryon:<code>` (the standard CarryOn-scoped messages, e.g. `slot-not-empty`). The reserved sentinel codes from `CarryConstants.FailureCodes` are not lang keys (`__ignore__` = silent). The public helper `CarryOn.Utility.CarryErrorHelper` implements this rule: `ResolveLangKey(code)` returns the resolved key, `ShowError(api, errorCode[, langKey])` displays on the client, `SendError(player, errorCode[, langKey])` sends to a server player — the two-argument forms let the reported code differ from the message key (used by CarryOn's own handlers, e.g. `FailCarryAction`). Server-side display resolves in the receiving player's language, not the server's.

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
- `carryon:carryableinteract` and `carryon:carryable` are independent — add both if the block should be both carryable and interactable-while-carried.
- `attachablecarryable` must be patched onto both `/server/behaviors/-` and `/client/behaviors/-` or carry behavior will not work correctly.
