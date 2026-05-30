# Automatic Behavior Assignment

CarryOn can automatically extend carry support to modded blocks that are similar to known carryable blocks.

## Where It Runs

Automatic behavior assignment runs during server-side startup conditioning in `BehavioralConditioning`.

Conditioning flow:

1. Remove disabled conditional behaviors.
2. Resolve multiple `Carryable` behaviors by priority/overlay.
3. Auto-map similar `Carryable` behaviors.
4. Auto-map similar `CarryableInteract` behaviors.
5. Remove carryable behaviors excluded by config prefixes.

## Enablement and Config

The feature is controlled by `CarryablesFilters.AutoMapSimilar` and defaults to enabled.

Other relevant filters:

- `AutoMatchIgnoreMods` default: `mcrate`
- `AllowedShapeOnlyMatches` defaults:
  - `block/clay/lootvessel`
  - `block/wood/chest/normal`
  - `block/wood/trunk/normal`
  - `block/reed/basket-normal`
- `RemoveBaseCarryableBehaviour` default: `woodchests:wtrunk`
- `RemoveCarryableBehaviour` defaults:
  - `game:banner`
  - `game:clutter-devastation`

## Matching Strategy

CarryOn builds a match dictionary from game-domain blocks that already have `Carryable` behavior.

For each candidate block, potential match keys are checked in this order:

1. `Class:<class>|Shape:<shape>`
2. `EntityClass:<entityClass>|Shape:<shape>`
3. `Shape:<shape>`
4. `Class:<class>`

Important behavior:

- Shape-only matches are only allowed for shapes in `AllowedShapeOnlyMatches`.
- The generic shape `block/basic/cube` is intentionally excluded from shape matching.
- First matching key wins.
- Blocks from ignored mod domains are skipped.

## CarryableInteract Auto-Mapping

Carryable interaction behavior is also auto-mapped when:

- A block does not already have `CarryableInteract`.
- Its `EntityClass` matches a known interact-enabled block.
- Its mod domain is not excluded by `AutoMatchIgnoreMods`.

## When Auto-Assignment Is Enough

Auto-assignment is usually enough when:

- Your block shares class/entity/shape characteristics with vanilla carryable blocks.
- You are fine with inherited defaults.

## When To Add A Manual Patch

Add your own patch when you need:

- Custom animations, speed penalties, or slot restrictions.
- Back-carry conditions for your block family.
- Custom transform templates or label placement.
- Explicit behavior when matching is ambiguous.

Use [Modding Guide](modding-guide.md) for patch examples and field reference.
