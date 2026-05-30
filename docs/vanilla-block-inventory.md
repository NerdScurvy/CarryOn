# Vanilla Block Inventory

Patches applied to vanilla `game:` blocks. Patch files are in `resources/assets/carryon/patches/carryable` (Carryable) and `resources/assets/carryon/patches/carryable/interact` (CarryableInteract).

---

## Carryable Blocks

Blocks that receive the `Carryable` behavior, allowing players to pick them up and carry them.

Walk speed values shown are explicit overrides. A blank entry means the slot's code-level default applies (`-0.25` for Hands, `-0.15` for Back).

| Block Type | Config Key | Hands | Back | Walk Speed H / B | Time P / Pl / Sw (s) | Back Condition | Interact | Transform Templates |
| --- | --- | :---: | :---: | :---: | :---: | --- | :---: | --- |
| game:blocktypes/metal/anvil | `carryon.Carryables.Anvil` | Yes | — | -0.50 / — | 1.5 / 1.125 / 2.25 | — | — | — |
| game:blocktypes/wood/banner | `carryon.Carryables.Clutter` ¹ | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/wood/barrel | `carryon.Carryables.Barrel` | Yes | Yes | — | 0.8 / 0.6 / 1.2 | `carryon.CarryablesOnBack.Barrel` | Yes | carryon:carry-barrel |
| game:blocktypes/wood/woodtyped/bookshelf | `carryon.Carryables.Bookshelf` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/wood/bookshelf-clutter* | `carryon.Carryables.Clutter & Bookshelf` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/legacy/bookshelves | `carryon.Carryables.Bookshelf` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/wax/bunchocandles | `carryon.Carryables.BunchOCandles` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/wood/woodtyped/cabinet | *(always enabled)* | Yes | — | -0.15 / — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/metal/chandelier | `carryon.Carryables.Chandelier` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/wood/chest | `carryon.Carryables.Chest` | Yes | Yes | — | 0.8 / 0.6 / 1.2 | `carryon.CarryablesOnBack.Chest` | Yes | carryon:carry-chest, carryon:carry-chest-compact |
| game:blocktypes/wood/chest-labeled | `carryon.Carryables.Chest` | Yes | Yes | — | 0.8 / 0.6 / 1.2 | `carryon.CarryablesOnBack.Chest` | Yes | carryon:carry-chest |
| game:blocktypes/wood/chest-trunk | `carryon.Carryables.ChestTrunk` | Yes | Yes | -0.50 / -0.30 | 0.8 / 0.6 / 1.2 | `carryon.CarryablesOnBack.ChestTrunk` | — | carryon:carry-trunk |
| game:blocktypes/clutter | `carryon.Carryables.Clutter` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/wood/woodtyped/crate | `carryon.Carryables.Crate` | Yes | Yes | -0.30 / -0.30 | 0.8 / 0.6 / 1.2 | `carryon.CarryablesOnBack.Crate` | — | carryon:carry-crate |
| game:blocktypes/legacy/crate | `carryon.Carryables.Crate` | Yes | Yes | -0.30 / -0.30 | 0.8 / 0.6 / 1.2 | `carryon.CarryablesOnBack.Crate` | — | — |
| game:blocktypes/wood/displaycase* | `carryon.Carryables.DisplayCase` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | carryon:carry-displaycase |
| game:blocktypes/clay/fired/flowerpot | `carryon.Carryables.Flowerpot` | Yes | Yes | — | 0.8 / 0.6 / 1.2 | `carryon.CarryablesOnBack.Flowerpot` | — | carryon:plants-small, carryon:carry-flowerpot |
| game:blocktypes/stone/generic/forge | `carryon.Carryables.Forge` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/wood/henbox | `carryon.Carryables.Henbox` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/clay/fired/ingotmold | `carryon.Carryables.Mold` ² | Yes | — | — | 0.5 / 0.375 / 0.75 | — | — | — |
| game:blocktypes/wood/woodtyped/log-withresin | `carryon.Carryables.LogWithResin` | Yes | Yes | -0.20 / -0.20 | 0.8 / 0.6 / 1.2 | `carryon.CarryablesOnBack.LogWithResin` | — | carryon:carry-crate |
| game:blocktypes/clay/lootvessel | `carryon.Carryables.LootVessel` | Yes | Yes | — | 0.8 / 0.6 / 1.2 | `carryon.CarryablesOnBack.LootVessel` | — | carryon:carry-storagevessel |
| game:blocktypes/wood/moldrack | `carryon.Carryables.MoldRack` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/clay/oven | `carryon.Carryables.Oven` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/clay/fired/planter | `carryon.Carryables.Planter` | Yes | Yes | — | 0.8 / 0.6 / 1.2 | `carryon.CarryablesOnBack.Planter` | — | carryon:plants-large, carryon:carry-planter |
| game:blocktypes/metal/plaque | `carryon.Carryables.Sign` ¹ | Yes | — | -0.50 / — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/stone/quern | `carryon.Carryables.Quern` | Yes | — | -0.40 / — | 1.0 / 0.75 / 1.5 | — | — | — |
| game:blocktypes/reed/reedchest | `carryon.Carryables.ReedChest` | Yes | Yes | -0.15 / — | 0.4 / 0.3 / 0.6 | `carryon.CarryablesOnBack.ReedChest` | Yes | carryon:carry-reedchest |
| game:blocktypes/machine/resonator | `carryon.Carryables.Resonator` | Yes | — | -0.15 / — | 0.4 / 0.3 / 0.6 | — | — | — |
| game:blocktypes/wood/shelf | `carryon.Carryables.Shelf` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/wood/sign | `carryon.Carryables.Sign` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/clay/fired/storagevessel | `carryon.Carryables.StorageVessel` | Yes | Yes | — | 0.8 / 0.6 / 1.2 | `carryon.CarryablesOnBack.StorageVessel` | Yes | carryon:carry-storagevessel |
| game:blocktypes/clay/fired/toolmold | `carryon.Carryables.Mold` ² | Yes | — | — | 0.5 / 0.375 / 0.75 | — | — | — |
| game:blocktypes/wood/toolrack | `carryon.Carryables.ToolRack` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |
| game:blocktypes/metal/torchholder | `carryon.Carryables.TorchHolder` | Yes | — | — | 0.8 / 0.6 / 1.2 | — | — | — |

¹ Banner and plaque share another block type's config key rather than having their own.  
² Ingot mold and tool mold both use the shared `Mold` config key.

The `cabinet` patch carries no `enabledCondition` and is always applied when CarryOn is installed.

The `log-withresin` patch uses `behaviorsByType` to target all variants plus a separate entry for the `log-resinharvested-*` state with different transform groups.

Timing defaults used for the `Time P / Pl / Sw` column:

- Pickup (`P`) = `interactDelay` from `Carryable` (default `0.8s` when not set)
- Place (`Pl`) = `Pickup * 0.75`
- Swap (`Sw`) = `Pickup * 1.5`
- Final runtime timing is divided by `CarryOptions.InteractSpeedMultiplier` (default `1.0`)

---

## CarryableInteract Blocks

Blocks that receive `CarryableInteract` behavior, allowing a player to interact with them (open, toggle, etc.) while carrying something in their hands.

| Block Type | Config Key | Also Carryable |
| --- | --- | :---: |
| game:blocktypes/wood/barrel | `carryon.Interactables.Barrel` | Yes |
| game:blocktypes/wood/chest* | `carryon.Interactables.Storage` | Yes ³ |
| game:blocktypes/legacy/door | `carryon.Interactables.Door` | — |
| game:blocktypes/wood/woodtyped/door | `carryon.Interactables.Door` | — |
| game:blocktypes/metal/door* | `carryon.Interactables.Door` | — |
| game:blocktypes/clay/door-kiln | `carryon.Interactables.Door` | — |
| game:blocktypes/wood/woodtyped/fencegate | `carryon.Interactables.Door` | — |
| game:blocktypes/wood/woodtyped/roughhewnfencegate | `carryon.Interactables.Door` | — |
| game:blocktypes/reed/reedchest | `carryon.Interactables.Storage` | Yes |
| game:blocktypes/clay/storagevessel* | `carryon.Interactables.Storage` | Yes ⁴ |
| game:blocktypes/wood/woodtyped/trapdoor | `carryon.Interactables.Door` | — |
| game:blocktypes/metal/trapdoor | `carryon.Interactables.Door` | — |

³ The `chest*` pattern covers chest, chest-labeled, and chest-trunk. All three also have individual Carryable patches.  
⁴ The `storagevessel*` pattern is broader than the Carryable patch (`clay/fired/storagevessel`), covering additional storage vessel variants.

The `metal/door*` entry carries an additional `interactDelay: 1.3` override compared to other door types.

---

## Notes

- All patches target `game:` domain blocks only. Modded block support is covered in [Mod Support Inventory](mod-support-inventory.md).
- `Config Key` refers to the dot-notation world config expression used in `enabledCondition`. Most correspond to a field in `CarryablesConfig` or `CarryablesOnBack` in the server config file.
- Blocks that are disabled by default in `CarryablesConfig` (e.g. `Bookshelf`, `Clutter`, `Chandelier`, `DisplayCase`) receive `Carryable` behavior but will not be carryable until the config key is enabled per-world.
- Auto-mapping may extend Carryable behavior to additional modded blocks based on similarity to the vanilla blocks listed here. See [Automatic Behavior Assignment](automatic-behavior-assignment.md) for details.
