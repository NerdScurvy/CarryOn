# transformGroupResolver Guide

This guide explains `transformGroupResolver` in `Carryable` behavior patches:

- what it does
- which resolver codes are available in CarryOn
- how resolver output works with `transformGroups`

For template and `transformGroups` authoring basics, see [Transform Templates and Inline Transform Groups](transform-templates-and-groups-guide.md).

---

## What Transform Group Resolvers Do

CarryOn has two separate resolver types:

- **Root resolver** — selects which transform groups to use for the root carried block (the block itself).
- **Attachment resolver** — selects transform groups for attached/cluster children (e.g. items in a display case, plants in a planter).

A `Carryable` behavior can set either or both:

```json
"rootGroupResolver": "codepath",
"attachmentGroupResolver": "displaycase"
```

The legacy `transformGroupResolver` property is a shorthand that sets both `rootGroupResolver` and `attachmentGroupResolver` to the same value when they aren't explicitly set.

At render time, CarryOn resolves the configured code to a canonical namespaced code and asks the matched resolver to choose transform group candidates dynamically based on carried block entity data (container slots, item types, block code path, etc.).

If the configured value is bare (no domain), CarryOn defaults it to the `carryon` domain.

Without a resolver, CarryOn uses the standard base group flow (`hands`, `backpack-none`, `backpack-small`, `backpack-large`) plus optional type suffix mapping from `groups`.

With resolvers, CarryOn can:

- provide better group candidates for special cases
- add extra transform groups for contained items
- apply per-slot display behavior (`onDisplayTransform`, display yaw)

---

## Built-In Resolver Codes

CarryOn registers these resolvers on the client:

| Canonical resolver code | Type | Class | Typical target |
| --- | --- | --- | --- |
| `carryon:plant-container` | Root + Attachment | `PlantContainerTransformGroupResolver` | Flowerpots and planters |
| `carryon:codepath` | Root | `GenericCodePathTransformGroupResolver` | Generic code-path-based root candidate selection |
| `carryon:displaycase` | Attachment | `DisplayCaseTransformGroupResolver` | Display cases |
| `carryon:moldrack` | Attachment | `MoldRackTransformGroupResolver` | Mold racks |
| `carryon:container-slot` | Attachment | `ContainerSlotTransformGroupResolverBase` | Generic container slot content rendering |
| `carryon:data-attributes` | Attachment | `DataAttributeTransformGroupResolver` | Block entity data attribute content rendering |

Notes:

- Resolvers are registered in client startup (`CarrySystem.StartClientSide`).
- Root resolvers are only used when `rootGroupResolver` (or legacy `transformGroupResolver`) is set.
- Attachment resolvers are only used when `attachmentGroupResolver` (or legacy `transformGroupResolver`) is set.
- The two resolver types are independent — you can set either, both, or neither.
- Resolver codes must be unique within each type. If two resolvers register the same code for the same type, CarryManager rejects the duplicate registration.

---

## How Resolver Output Is Applied

### Root Resolver

A root resolver returns `IList<string>?` — an ordered list of preferred primary group candidates.

CarryOn then:

1. Tries the returned candidates in order, keeping the first group that exists in resolved `transformGroups`.
2. Falls back to the base group, then `default` if nothing matches.

### Attachment Resolver

An attachment resolver returns `AttachmentResolveResult?` containing:

- `Candidates`: list of `CarriedGroupCandidateSet` objects, one per attached child
- `EnableVertexWarp`: rendering hint for vertex-warping additional transforms

Each `CarriedGroupCandidateSet` has:

| Property | Type | Description |
| --- | --- | --- |
| `Groups` | `string[]` | Ordered list of preferred transform group names for this child |
| `AddAllMatches` | `bool` | If true, renders all matching groups instead of the first match |
| `AssetTypeIfUnset` | `Block`/`Item`/`None` | Fallback asset type for auto-resolving the child's render shape |
| `AssetNameIfUnset` | `string?` | Fallback asset location when the child has no explicit shape |
| `SourceSlotKey` | `string?` | Source inventory slot key (for debug/label purposes) |
| `ApplyDisplaySlotYaw` | `bool` | Applies vanilla display slot yaw rotation |
| `ApplyDisplayCaseYawOffset` | `bool` | Applies display-case-specific yaw offset |
| `ApplyOnDisplayTransform` | `bool` | Applies the block's `onDisplayTransform` from attributes |

CarryOn then, for each candidate set:

1. Tries `Groups` in order, keeping the first group that exists in resolved `transformGroups`.
2. Falls back to the base group if nothing matches.
3. Uses `AssetTypeIfUnset`/`AssetNameIfUnset` to resolve a fallback render shape when the child's block model is not available.

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

### 5. data-attributes

Patch pattern:

```json
"properties": {
  "dataAttributes": ["content"],
  "dataAttributesPrefix": "carried",
  "transformTemplates": ["carryon:carry-chest"],
  "attachmentGroupResolver": "data-attributes",
  "slots": { "Hands": {} }
}
```

Resolver behavior summary:

- Reads specified data attribute names from the block entity data.
- For each attribute that contains an `Itemstack`, adds a candidate set with:
  - Group: `<prefix>-<attrName>` (e.g. `carried-content`)
  - `SourceSlotKey` set to the attribute name
  - Auto-resolved fallback asset from the item stack

---

### 6. container-slot

Patch pattern:

```json
"properties": {
  "transformTemplates": ["carryon:carry-chest"],
  "attachmentGroupResolver": "container-slot",
  "slots": { "Hands": {} }
}
```

Resolver behavior summary:

- Reads inventory container slots from the carried block's block entity data.
- For each non-empty slot, adds a candidate set with:
  - Group: `<baseGroup>-slot<key>` (e.g. `hands-slot0`, `hands-slot1`)
  - `SourceSlotKey` set to the slot key
  - Auto-resolved fallback asset from the slot's item stack

The base `ContainerSlotTransformGroupResolverBase` class can be subclassed by C# mods to customize slot candidate generation (see [Custom Resolver](#custom-resolver-c-mods)).

---

## Combining Resolver + groups + transformGroups

Use each tool for a different part of the decision chain:

- `rootGroupResolver` / `attachmentGroupResolver` (or shorthand `transformGroupResolver`): dynamic candidate generation from runtime data
- `groups`: type-to-suffix mapping from carried stack attributes
- `transformGroups`: actual transform settings keyed by final group names

Typical pattern:

1. Resolver picks group candidates.
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

C# mods can add custom resolver logic by implementing `IRootTransformGroupResolver` or `IAttachmentTransformGroupResolver` and registering through `CarryManager.RegisterRootTransformGroupResolver(modId, resolver)` or `CarryManager.RegisterAttachmentTransformGroupResolver(modId, resolver)` in client startup.

### IRootTransformGroupResolver

```csharp
public interface IRootTransformGroupResolver
{
    string ResolverCode { get; }
    bool TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup, out IList<string>? candidates);
    string? GetCacheSignature(ICoreAPI api, CarriedBlock carried, string baseGroup) => null;
}
```

- `ResolverCode`: resolver code key. Canonical registration code is `modId:ResolverCode` when `ResolverCode` is bare.
- `TryResolve(...)`: returns `true` with an ordered list of primary group candidates, or `false` to fall back to standard behavior.
- `GetCacheSignature(...)`: optional extra cache key fragment for state-dependent resolution (default `null`).

### IAttachmentTransformGroupResolver

```csharp
public interface IAttachmentTransformGroupResolver
{
    string ResolverCode { get; }
    bool TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup, out AttachmentResolveResult? result);
    string? GetCacheSignature(ICoreAPI api, CarriedBlock carried, string baseGroup) => null;
}
```

- `TryResolve(...)`: returns `true` with an `AttachmentResolveResult` describing per-child candidate sets, or `false` to skip.
- See [How Attachment Resolver Output Is Applied](#attachment-resolver) for `AttachmentResolveResult` and `CarriedGroupCandidateSet` details.

### Registration notes

- `modId` is required at registration and identifies the owning mod.
- Canonical resolver codes are matched case-insensitively.
- Bare JSON values for `rootGroupResolver`/`attachmentGroupResolver`/`transformGroupResolver` default to `carryon:<code>` during lookup.
- Duplicate canonical resolver codes are rejected per resolver type during registration.
- A single class can implement both interfaces and register as both root and attachment resolver (like `PlantContainerTransformGroupResolver`).
- You can subclass `ContainerSlotTransformGroupResolverBase` to customize container-slot candidate generation while reusing the base fallback logic.

---

## Troubleshooting

- If resolver seems ignored, verify the patch actually sets `transformGroupResolver`.
- Ensure generated group names exist in resolved template/local `transformGroups`.
- For slot-driven resolvers, verify carried block entity inventory data exists.
- Keep stable `id` values in transform entries so overrides merge correctly.
- For `codepath`, remember candidates use `-` path segments and lowercase matching behavior.

---

## References

- `src/Common/Behaviors/BlockBehaviorCarryable.cs` — `rootGroupResolver`, `attachmentGroupResolver`, `dataAttributes`, `dataAttributesPrefix` property initialization
- `src/Client/Logic/CarryRenderer/CarryTransformPlanBuilder.cs` — resolves root and attachment groups from resolver output
- `src/Client/Logic/TransformGroupResolvers/PlantContainerTransformGroupResolver.cs`
- `src/Client/Logic/TransformGroupResolvers/DisplayCaseTransformGroupResolver.cs`
- `src/Client/Logic/TransformGroupResolvers/MoldRackTransformGroupResolver.cs`
- `src/Client/Logic/TransformGroupResolvers/GenericCodePathTransformGroupResolver.cs`
- `src/Client/Logic/TransformGroupResolvers/DataAttributeTransformGroupResolver.cs`
- `src/Client/Logic/TransformGroupResolvers/ContainerSlotTransformGroupResolverBase.cs`
- `../CarryOnLib/src/API/Common/Interfaces/IRootTransformGroupResolver.cs` — root resolver interface
- `../CarryOnLib/src/API/Common/Interfaces/IAttachmentTransformGroupResolver.cs` — attachment resolver interface
- `../CarryOnLib/src/API/Common/Models/AttachmentResolveResult.cs` — attachment resolver result model
- `../CarryOnLib/src/API/Common/Models/CarriedGroupCandidateSet.cs` — `CarriedGroupCandidateSet` model
