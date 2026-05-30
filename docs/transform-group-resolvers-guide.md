# transformGroupResolver Guide

This guide explains `transformGroupResolver` in `Carryable` behavior patches:

- what it does
- which resolver codes are available in CarryOn
- how resolver output works with `transformGroups`

For template and `transformGroups` authoring basics, see [Transform Templates and Inline Transform Groups](transform-templates-and-groups-guide.md).

---

## What transformGroupResolver Does

`transformGroupResolver` is an optional `Carryable` property:

```json
"transformGroupResolver": "plant-container"
```

At render time, CarryOn resolves the configured code to a canonical namespaced code and asks the matched resolver to choose transform group candidates dynamically based on carried block entity data (container slots, item types, block code path, etc.).

If the configured value is bare (no domain), CarryOn defaults it to the `carryon` domain.

Without a resolver, CarryOn uses the standard base group flow (`hands`, `backpack-none`, `backpack-small`, `backpack-large`) plus optional type suffix mapping from `groups`.

With a resolver, CarryOn can:

- provide better primary group candidates for special cases
- add extra transform groups for contained items
- apply per-slot display behavior (`onDisplayTransform`, display yaw)

---

## Built-In Resolver Codes

CarryOn registers these resolvers on the client:

| Canonical resolver code | Class | Typical target |
| --- | --- | --- |
| `carryon:plant-container` (`plant-container` also works in JSON) | `PlantContainerTransformGroupResolver` | Flowerpots and planters |
| `carryon:displaycase` (`displaycase` also works in JSON) | `DisplayCaseTransformGroupResolver` | Display cases |
| `carryon:moldrack` (`moldrack` also works in JSON) | `MoldRackTransformGroupResolver` | Mold racks |
| `carryon:codepath` (`codepath` also works in JSON) | `GenericCodePathTransformGroupResolver` | Generic code-path-based fallback |

Notes:

- Resolvers are registered in client startup (`CarrySystem.StartClientSide`).
- If `transformGroupResolver` is set, only that resolver code is considered.
- If it is not set, CarryOn does not invoke a resolver and uses standard base group flow.
- Resolver codes must be unique. If two resolvers register the same code, CarryManager rejects the duplicate registration.

---

## How Resolver Output Is Applied

A resolver returns a `CarriedGroupResolution` with:

- `PrimaryGroupCandidates`: ordered list of preferred primary groups
- `AdditionalGroupCandidates`: optional extra groups to render in addition to primary
- `EnableVertexWarpForAdditionalTransforms`: optional rendering hint for added groups

CarryOn then:

1. Tries `PrimaryGroupCandidates` in order, keeping the first group that exists in resolved `transformGroups`.
2. Resolves additional groups from `AdditionalGroupCandidates`.
3. Falls back to the base group, then `default` if nothing matches.

Important implication: your template/inline `transformGroups` must contain names the resolver can produce.

---

## Using Resolvers with transformGroups

### 1. plant-container

Patch pattern:

```json
"transformTemplates": [
  "carryon:plants-small",
  "carryon:carry-flowerpot"
],
"transformGroupResolver": "plant-container"
```

Resolver behavior summary:

- Starts from `<base>-planted` (example: `hands-planted`, `backpack-small-planted`).
- For planted content, adds more specific primary candidates (examples: `hands-planted-sapling`, `hands-planted-sapling-oak`, `hands-planted-flower`, `hands-planted-mushroom`).
- Adds additional plant groups (examples: `planted-sapling-oak`, `planted-flower`, `planted-mushroom`, `planted-fern`).

What your `transformGroups`/templates should include:

- Base container groups: `hands`, `backpack-none`, `backpack-small`, `backpack-large`
- Planted variants: `<base>-planted` and optionally `<base>-planted-*`
- Plant content groups: `planted-*`

---

### 2. displaycase

Patch pattern:

```json
"transformTemplates": [
  "carryon:carry-displaycase"
],
"transformGroupResolver": "displaycase"
```

Resolver behavior summary:

- Reads display case inventory slots.
- Adds per-slot additional groups:
  - `displaycase-slot0`, `displaycase-slot1`, etc.
  - when centered placement is active: `displaycase-slot-center`
- Prefers `-crystal` variants for crystal items (example: `displaycase-slot0-crystal`).
- Can apply slot yaw and `onDisplayTransform` to displayed items.

What your `transformGroups`/templates should include:

- `displaycase-slot0..N` (or `displaycase-slot-center`)
- Optional crystal variants like `displaycase-slot0-crystal`

---

### 3. moldrack

Patch pattern:

```json
"transformGroupResolver": "moldrack",
"transformGroups": {
  "moldrack-slot0": [ ... ],
  "moldrack-slot1": [ ... ],
  "moldrack-slot0-shield": { "extends": "moldrack-slot0", ... },
  "moldrack-slot0-shield-crude": { "extends": "moldrack-slot0-shield", ... }
}
```

Resolver behavior summary:

- Reads mold rack slots and adds additional groups `moldrack-slot<key>`.
- For shield items, prefers more specific groups first:
  - `moldrack-slotN-shield-<construction>`
  - then `moldrack-slotN-shield`
  - then `moldrack-slotN`

What your `transformGroups` should include:

- `moldrack-slot0..N` base slot groups
- optional `-shield` and `-shield-<type>` specializations

---

### 4. codepath

Patch pattern:

```json
"transformGroupResolver": "codepath"
```

Resolver behavior summary:

- Builds primary group candidates from block code path segments.
- Example block path: `fancybox-iron-west` and base group `hands`:
  - `hands-fancybox-iron-west`
  - `hands-fancybox-iron`
  - `hands-fancybox`

What your `transformGroups` should include:

- any of the expected `base-suffix` groups you want to support
- fallback base group (`hands`, `backpack-*`) if no specific codepath group exists

---

## Combining Resolver + groups + transformGroups

Use each tool for a different part of the decision chain:

- `transformGroupResolver`: dynamic candidate generation from runtime data
- `groups`: type-to-suffix mapping from carried stack attributes
- `transformGroups`: actual transform settings keyed by final group names

Typical pattern:

1. Resolver picks primary/additional candidates.
2. Carryable type-suffix mapping (`groups`) may further refine primary name.
3. Renderer uses matching entries from resolved `transformGroups`.

---

## Minimal JSON Examples

Plant container:

```json
"properties": {
  "transformTemplates": ["carryon:plants-large", "carryon:carry-planter"],
  "transformGroupResolver": "plant-container",
  "slots": { "Hands": {} }
}
```

Display case:

```json
"properties": {
  "transformTemplates": ["carryon:carry-displaycase"],
  "transformGroupResolver": "displaycase",
  "slots": { "Hands": {} }
}
```

Mold rack:

```json
"properties": {
  "transformGroupResolver": "moldrack",
  "transformGroups": {
    "moldrack-slot0": [ { "id": "slot0", "translation": [0, 0.5, -0.312] } ]
  },
  "slots": { "Hands": {} }
}
```

Code path:

```json
"properties": {
  "transformGroupResolver": "codepath",
  "transformGroups": {
    "hands-myblock": [ { "id": "root", "rotationY": 180.0 } ],
    "hands": [ { "id": "root", "rotationY": 90.0 } ]
  },
  "slots": { "Hands": {} }
}
```

---

## Custom Resolver (C# Mods)

C# mods can add custom resolver logic by implementing `ICarriedTransformGroupResolver` and registering it through `CarryManager.RegisterTransformGroupResolver(modId, resolver)` in client startup.

Interface contract:

- `ResolverCode`: resolver code key. Canonical registration code is `modId:ResolverCode` when `ResolverCode` is bare.
- `TryResolve(...)`: returns `CarriedGroupResolution`
- `GetCacheSignature(...)`: optional extra cache key fragment for state-dependent resolution

Registration notes:

- `modId` is required at registration and identifies the owning mod.
- Canonical resolver codes are matched case-insensitively.
- Bare `transformGroupResolver` JSON values default to `carryon:<code>` during lookup.
- Duplicate canonical resolver codes are rejected during registration.

---

## Troubleshooting

- If resolver seems ignored, verify the patch actually sets `transformGroupResolver`.
- Ensure generated group names exist in resolved template/local `transformGroups`.
- For slot-driven resolvers, verify carried block entity inventory data exists.
- Keep stable `id` values in transform entries so overrides merge correctly.
- For `codepath`, remember candidates use `-` path segments and lowercase matching behavior.

---

## References

- `src/Common/Behaviors/BlockBehaviorCarryable.cs`
- `src/Client/Logic/CarryRenderer/CarryTransformPlanBuilder.cs`
- `src/Client/Logic/TransformGroupResolvers/PlantContainerTransformGroupResolver.cs`
- `src/Client/Logic/TransformGroupResolvers/DisplayCaseTransformGroupResolver.cs`
- `src/Client/Logic/TransformGroupResolvers/MoldRackTransformGroupResolver.cs`
- `src/Client/Logic/TransformGroupResolvers/GenericCodePathTransformGroupResolver.cs`
- `../CarryOnLib/src/API/Common/Interfaces/ICarriedTransformGroupResolver.cs`
- `../CarryOnLib/src/API/Common/Models/CarriedGroupResolution.cs`
- `../CarryOnLib/src/Utility/TransformGroupResolverHelper.cs`
