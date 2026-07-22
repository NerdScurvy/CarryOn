# Behavioral Conditioning in CarryOn

This document explains how `BehavioralConditioning` processes block behaviors at world load time to establish the final set of carryable behaviors on every block.

It reflects the current implementation:
- `BehavioralConditioning.Init` runs a fixed pipeline of five passes over all world blocks.
- Conditional behaviors are evaluated and disabled or activated based on world config.
- Multiple `BlockBehaviorCarryable` instances are resolved by `patchPriority`, with optional property overlays when `overrideExistingProperties` is enabled.
- Auto-mapping extends carryable coverage to mod blocks that share game-block signatures.
- Config-driven exclusions strip carryable behaviors from explicitly blocked codes.

## 0. Scope

- `src/Server/Logic/BehavioralConditioning.cs`
- `../CarryOnLib/src/API/Common/Interfaces/IConditionalBlockBehavior.cs`
- `src/Common/Models/CarryOnConfig.cs` (`CarryablesFiltersConfig`)
- `src/Common/Behaviors/BlockBehaviorCarryable.cs` (`PatchPriority`, `OverrideExistingProperties`, `ForcePickupOnSwapBack`)

---

## 1. Pipeline Order

`BehavioralConditioning.Init` is called once during server start. It executes five passes in this fixed order:

| # | Method | Purpose |
|---|--------|---------|
| 1 | `RemoveDisabledConditionalBehaviors` | Strip behaviors whose `EnabledCondition` evaluates false |
| 2 | `ResolveMultipleCarryableBehaviors` | Deduplicate `BlockBehaviorCarryable` per block by patch priority |
| 3 | `AutoMapSimilarCarryables` | Extend carryable to non-game blocks by shape/class/entityClass signature |
| 4 | `AutoMapSimilarCarryableInteract` | Extend carryable-interact to matching-entityClass blocks |
| 5 | `RemoveExcludedCarryableBehaviors` | Remove carryable from config-blocklisted block code prefixes |

Order matters: auto-map runs after priority resolution so newly added behaviors are clean, and exclusions run last so they can remove auto-mapped results too.

---

## 2. Pass 1 — Conditional Behavior Removal

**Applies to:** any block carrying a behavior that implements `IConditionalBlockBehavior`.

`IConditionalBlockBehavior` exposes two members:
- `EnabledCondition` — a dot-notation world-config expression (e.g. `"allowSomeFeature"`)
- `ProcessConditions(api, block)` — called only when the behavior is confirmed enabled

For each block:
1. Collect all `IConditionalBlockBehavior` instances from `BlockBehaviors`.
2. For each, evaluate `EnabledCondition` against world config via `EvaluateDotNotationLogic`.
3. If the condition is present and evaluates **false**: remove every behavior of that concrete type from both `BlockBehaviors` and `CollectibleBehaviors`.
4. If the condition is absent or evaluates **true**: call `behavior.ProcessConditions(api, block)` so the behavior can perform any setup it requires.

---

## 3. Pass 2 — Multiple Carryable Behavior Resolution

**Applies to:** all blocks.

When multiple mods patch the same block and each adds a `BlockBehaviorCarryable`, the block can end up with more than one instance. Resolution now supports two modes:

- Default mode: keep a single winner by highest `patchPriority`.
- Overlay mode: keep one base behavior and merge one or more override patches into it.

**`RemoveOverriddenCarryableBehaviors<T>` logic:**

The method is generic over the array element type (used for both `BlockBehavior[]` and `CollectibleBehavior[]`). It isolates only the `BlockBehaviorCarryable` instances and applies one of three outcomes:

| Condition | Outcome |
|-----------|---------|
| Multiple carryables and none have `OverrideExistingProperties` | Keep the one with the highest `PatchPriority`; remove all others |
| Multiple carryables and one or more have `OverrideExistingProperties` | Select base behavior, apply ordered overlays, keep merged behavior; remove all others |
| Exactly one `BlockBehaviorCarryable` with `PatchPriority == 0`, and block is in `RemoveBaseCarryableBehaviour` list | Remove it |
| One instance with `PatchPriority > 0` | Kept unconditionally |

Overlay mode selection rules:

- Behaviors are evaluated in ascending `PatchPriority` order.
- Base behavior is the highest-priority behavior where `OverrideExistingProperties == false`; if none exist, fallback is the highest-priority behavior overall.
- Overlay set includes behaviors where `OverrideExistingProperties == true` and `PatchPriority >= base.PatchPriority`.
- Overlay patches are merged in ascending priority order, so higher priorities win later.

**`patchPriority`** is an integer declared in the carryable behavior JSON patch:
```json
{ "name": "Carryable", "properties": { "patchPriority": 10, ... } }
```
Default is `0` (base CarryOn patches). Third-party overrides use a higher value to win the conflict.

**`overrideExistingProperties`** is an opt-in boolean declared in the carryable behavior JSON patch:
```json
{ "name": "Carryable", "properties": { "patchPriority": 10, "overrideExistingProperties": true } }
```
Default is `false`. When set to `true`, this patch participates as an overlay instead of being selected as a standalone winner.

`MergeCarryableProperties` applies property overlays using JSON merge with these settings:

- `MergeArrayHandling = Replace`
- `MergeNullValueHandling = Merge`

After merge, `keepBehavior.Initialize(mergedProperties)` is called so all runtime fields are recalculated from the merged JSON.

**`RemoveBaseCarryableBehaviour` config list** (`CarryablesFiltersConfig.RemoveBaseCarryableBehaviour`, default `["woodchests:wtrunk"]`) names block code prefixes where even a lone priority-0 behavior should be stripped, because a different mod fully takes over that block type.

**`ShouldKeepBehavior` guard:** the highest-priority candidate is kept only if it is not simultaneously flagged for base-removal (priority 0 + `removeBaseBehavior` flag). This prevents an edge case where the highest-priority behavior is still only priority 0 on a base-removal-listed block.

Behavior property note (`forcePickupOnSwapBack`):

- `forcePickupOnSwapBack` is parsed by `BlockBehaviorCarryable.Initialize`.
- `BehavioralConditioning` does not interpret it directly, but overlay mode can set or override it.
- The client interaction components read `BlockBehaviorCarryable.ForcePickupOnSwapBack` to force pickup behavior when swap-back modifier is used.

Example:
```json
{
   "name": "Carryable",
   "properties": {
      "patchPriority": 10,
      "overrideExistingProperties": true,
      "forcePickupOnSwapBack": true
   }
}
```

---

## 4. Pass 3 — Auto-Map Similar Carryables

**Applies to:** non-carryable blocks when `CarryablesFiltersConfig.AutoMapSimilar == true`.

This pass infers that mod blocks structurally similar to vanilla carryable blocks should also be carryable, without requiring each mod to add an explicit patch.

### 4A. Building the Match Table

Iterates all blocks in the `game` domain that already carry `BlockBehaviorCarryable`. For each, one or more keys are registered in a `Dictionary<string, BlockBehaviorCarryable>`:

| Key pattern | Condition |
|-------------|-----------|
| `EntityClass:<ec>` | `EntityClass` is not null, `"Generic"`, or `"Transient"` |
| `Class:<c>` | `Class` is not `"Block"` |
| `EntityClass:<ec>\|Shape:<path>` | both entity class and shape available |
| `Class:<c>\|Shape:<path>` | both class and shape available |
| `Shape:<path>` | shape is in `AllowedShapeOnlyMatches` config list |

Shape `"block/basic/cube"` is excluded from shape keying because it is too generic to be meaningful.

First registration wins — the first game block encountered for a given key donates its behavior properties.

### 4B. Candidate Lookup and Injection

For each non-carryable block (not in `AutoMatchIgnoreMods` domain list):
1. `GetPotentialMatchKeys` generates candidate keys in priority order:
   - `Class:<c>|Shape:<path>`
   - `EntityClass:<ec>|Shape:<path>`
   - `Shape:<path>` (if available)
   - `Class:<c>`
2. The first key found in the match table wins.
3. A new `BlockBehaviorCarryable` is constructed and initialized from the matched behavior's `Properties`, then appended to both `BlockBehaviors` and `CollectibleBehaviors`.

The inherited behavior carries the same transform groups, carry slots, and other properties as the matched vanilla block.

**`AllowedShapeOnlyMatches`** (default: `["block/clay/lootvessel", "block/wood/chest/normal", "block/wood/trunk/normal", "block/reed/basket-normal"]`) restricts shape-only matches to a specific allowlist to prevent false positives on common shapes.

**`AutoMatchIgnoreMods`** (default: `["mcrate"]`) prevents auto-mapping into specific mod domains entirely.

---

## 5. Pass 4 — Auto-Map Similar Carryable Interact

**Applies to:** non-interact-carryable blocks when `AutoMapSimilar == true`.

Simpler than Pass 3. Iterates blocks that already have `BlockBehaviorCarryableInteract` and collects their `EntityClass` values (excluding `null` and `"Generic"`). Any block sharing one of those entity classes that does not already have `BlockBehaviorCarryableInteract` and is not in `AutoMatchIgnoreMods` receives a new instance appended to both behavior arrays.

No priority resolution is needed here because `BlockBehaviorCarryableInteract` instances are not patched competitively.

---

## 6. Pass 5 — Excluded Carryable Removal

**Applies to:** blocks in the `CarryablesFiltersConfig.RemoveCarryableBehaviour` prefix list (default: `["game:banner"]`).

After all earlier passes (including auto-mapping) have run, this pass does a final sweep. For each block whose code starts with any listed prefix, all `BlockBehaviorCarryable` instances are stripped from both `BlockBehaviors` and `CollectibleBehaviors`. Debug logging records each removal when `DebuggingOptions.LoggingEnabled` is true.

This is the correct place for unconditional exclusions because it runs last and cannot be undermined by auto-mapping.

---

## 7. Config Reference

All behavior-conditioning parameters live in `CarryablesFiltersConfig` (nested under `CarryOnConfig.CarryablesFilters`):

| Property | Type | Default | Effect |
|----------|------|---------|--------|
| `AutoMapSimilar` | `bool` | `true` | Enables Passes 3 and 4 |
| `AutoMatchIgnoreMods` | `string[]` | `["mcrate"]` | Domain names excluded from auto-mapping |
| `AllowedShapeOnlyMatches` | `string[]` | see §4A | Shape paths eligible for shape-only key matching |
| `RemoveBaseCarryableBehaviour` | `string[]` | `["woodchests:wtrunk"]` | Block code prefixes where a lone priority-0 carryable is removed in Pass 2 |
| `RemoveCarryableBehaviour` | `string[]` | `["game:banner"]` | Block code prefixes unconditionally stripped of carryable in Pass 5 |

`PatchPriority` is declared per-behavior in JSON patches, not in config. Default is `0`.

Behavior patch flags (per `Carryable` behavior, not config):

| Property | Type | Default | Effect |
|----------|------|---------|--------|
| `patchPriority` | `int` | `0` | Priority used for winner/overlay ordering in Pass 2 |
| `overrideExistingProperties` | `bool` | `false` | Marks this carryable patch as a property overlay patch |
| `forcePickupOnSwapBack` | `bool` | `false` | Causes swap-back modifier interaction to force pickup in the client interaction components |

---

## 8. Utility Methods

### `RemoveBehaviorsOfType<T>(T[] behaviours, Type typeToRemove)`

Public static helper. Removes all instances of a given concrete type (using `IsInstanceOfType`) from a behavior array. Used by Passes 1, 2, and 5.

### `RemoveOverriddenCarryableBehaviors<T>(T[] behaviours, bool removeBaseBehavior)`

Private generic. Operates only on `BlockBehaviorCarryable` elements; resolves either single-winner priority behavior or base+overlay merge behavior when `OverrideExistingProperties` patches are present.

### `MergeCarryableProperties(BlockBehaviorCarryable baseBehavior, List<BlockBehaviorCarryable> overlays)`

Private helper. Deep clones base behavior JSON properties and applies overlay property objects in order before reinitializing the kept behavior.

### `GetPotentialMatchKeys(Block block)`

Produces the ordered candidate key list for a block. Returns only `["Class:<c>"]` when the block has no shape path; otherwise returns all four key variants in decreasing specificity.

---

## 9. References

- `src/Server/Logic/BehavioralConditioning.cs`


- `src/Common/Behaviors/BlockBehaviorCarryable.cs`
- `src/Common/Behaviors/BlockBehaviorCarryableInteract.cs`
- `src/CarrySystem.cs`
- `../CarryOnLib/src/API/Common/Interfaces/IConditionalBlockBehavior.cs`
- `src/Common/Models/CarryOnConfig.cs`

---

## See Also

- [CarryManager and Handler Pipeline](carry-manager-and-handlers.md) — How `CarrySystem` initializes `BehavioralConditioning` during server start and how carry operations consume the resolved behaviors.
- [Transform Template System](transform-template-system.md) — How `BlockBehaviorCarryable` properties (including transform groups) declared in JSON patches are loaded and resolved on the client after behaviors have been conditioned.
- [Carried Chest-Trunk and Chest Rendering](carried-chest-trunk-rendering.md) — Example of a carryable block whose behavior is subject to auto-mapping and priority resolution.

---

This document is intended as a technical reference for understanding and debugging how CarryOn conditions block behaviors at world load time.
