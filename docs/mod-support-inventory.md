# Mod Support Inventory

Compatibility patches are located in `resources/assets/carryon/patches/carryonmore`. Patches are applied by VintageStory's JSON patch system when the matching mod is present.

There are three patch types used across CarryOnMore:

- **Carryable** — block behavior allowing players to pick up and carry the block. Supports hand and back slots.
- **CarryableInteract** — block behavior that enables interaction with the block (e.g. opening a container) while it is being carried. Added alongside `Carryable` where appropriate.
- **AttachableCarryable** — entity behavior applied to entity types (not blocks), enabling players to attach to and carry a living or non-living entity.

---

## Carryable Blocks

Blocks with `Carryable` behavior added by CarryOnMore patches.

| Mod | Target | Enabled Condition | Hands | Back | Interact Support | Priority | Transform Templates |
| --- | --- | --- | :---: | :---: | :---: | :---: | --- |
| bettercrates | bettercrates:blocktypes/bettercrates | carryon.Carryables.Crate | Yes | Yes | — | 1 | carryon:carry-crate |
| bettercrates | bettercrates:blocktypes/bettercrates2sided | carryon.Carryables.Crate | Yes | Yes | — | 1 | carryon:carry-crate |
| bricklayers | bricklayers:blocktypes/clay/planter* | carryon.Carryables.Planter | Yes | Yes | — | — | carryon:plants-large, carryon:carry-planter |
| bricklayers | bricklayers:blocktypes/clay/vat_planter_small_colored | carryon.Carryables.Planter | Yes | Yes | — | — | carryon:carry-vat-planter, carryon:plants-small |
| bricklayers | bricklayers:blocktypes/clay/storagevesselcolored_planter | carryon.Carryables.Planter | Yes | Yes | — | — | carryon:carry-storagevessel-planter, carryon:plants-large |
| bricklayers | bricklayers:blocktypes/clay/flowerpot* | carryon.Carryables.Flowerpot | Yes | Yes | — | — | carryon:carry-flowerpot, carryon:plants-small |
| chiseltools | chiseltools:blocktypes/chiseledchest* | carryon.Carryables.Chest | Yes | Yes | Yes | — | — |
| chiseltools | chiseltools:blocktypes/chiseledbarrel | carryon.Carryables.Barrel | Yes | Yes | — | — | — |
| chiseltools | chiseltools:blocktypes/chiseledanvil | carryon.Carryables.Anvil | Yes | — | — | — | — |
| chiseltools | chiseltools:blocktypes/chiseledflowerpot.json | carryon.Carryables.Flowerpot | Yes | Yes | — | — | carryon:plants-small |
| chiseltools | chiseltools:blocktypes/chiseleddisplaycase | carryon.Carryables.DisplayCase | Yes | — | — | — | carryon:carry-displaycase |
| chiseltools | chiseltools:blocktypes/chiseledforge.json | carryon.Carryables.Forge | Yes | — | — | — | — |
| chiseltools | chiseltools:blocktypes/chiseledoven | carryon.Carryables.Oven | Yes | — | — | — | — |
| chiseltools | chiseltools:blocktypes/chiseledplanter | carryon.Carryables.Planter | Yes | Yes | — | — | carryon:plants-large |
| chiseltools | chiseltools:blocktypes/chiseledshelf | carryon.Carryables.Shelf | Yes | — | — | — | — |
| chiseltools | chiseltools:blocktypes/chiseledsign | carryon.Carryables.Sign | Yes | — | — | — | — |
| chiseltools | chiseltools:blocktypes/chiseledstoragevessel | carryon.Carryables.StorageVessel | Yes | Yes | — | — | — |
| chiseltools | chiseltools:blocktypes/chiseledtoolrack | carryon.Carryables.ToolRack | Yes | — | — | — | — |
| eternalstew | eternalstew:blocktypes/eternalstewstove | — | Yes | — | — | — | — |
| eternalstew | eternalstew:blocktypes/eternalstewpot | — | Yes | — | — | — | — |
| extrachests | extrachests:blocktypes/chests | carryon.Carryables.Chest | Yes | Yes | Yes | 1 | carryon:carry-chest |
| extrachests | extrachests:blocktypes/labeledchests | carryon.Carryables.Chest | Yes | Yes | Yes | 1 | carryon:carry-chest |
| japanesearchitecture | japanesearchitecture:blocktypes/furniture/tansu.json | carryon.Carryables.Chest | Yes | Yes | — | — | carryon:carry-chest |
| japanesearchitecture | japanesearchitecture:blocktypes/furniture/tansu2.json | carryon.Carryables.ChestTrunk | Yes | Yes | — | — | carryon:carry-chest |
| japanesearchitecture | japanesearchitecture:blocktypes/furniture/tansu3.json | carryon.Carryables.ChestTrunk | Yes | — | — | — | — |
| kevinsfurniture | kevinsfurniture:blocktypes/chairs/floorcushion | — | Yes | — | — | — | — |
| kevinsfurniture | kevinsfurniture:blocktypes/chairs/primitivestool | — | Yes | — | — | — | — |
| kevinsfurniture | kevinsfurniture:blocktypes/tables/coffeetable | — | Yes | — | — | — | — |
| kevinsfurniture | kevinsfurniture:blocktypes/stoveblock | — | Yes | — | — | — | — |
| kevinsfurniture | kevinsfurniture:blocktypes/containers/anightstand | — | Yes | — | — | — | — |
| kevinsfurniture | kevinsfurniture:blocktypes/containers/nightstand-fancy | — | Yes | — | — | — | — |
| kevinsfurniture | kevinsfurniture:blocktypes/containers/cabinet | — | Yes | — | — | — | — |
| kevinsfurniture | kevinsfurniture:blocktypes/containers/doorlesscabinet | — | Yes | — | — | — | — |
| labeledcontainers (lc) | lc:blocktypes/stationarybasket | carryon.Carryables.ReedChest | Yes | Yes | — | — | carryon:carry-reedchest |
| labeledcontainers (lc) | lc:blocktypes/storagevessel* | carryon.Carryables.StorageVessel | Yes | Yes | — | — | carryon:carry-storagevessel |
| labeledtrunks | labeledtrunk:blocktypes/chest-trunk | carryon.Carryables.ChestTrunk | Yes | Yes | — | — | carryon:carry-trunk |

---

## AttachableCarryable Entities

Entities with the `attachablecarryable` entity behavior code added by CarryOnMore patches. These are entity types, not blocks. The behavior is added to both server and client behavior lists.

| Mod | Entity Target | Patch File |
| --- | --- | --- |
| cartwrightscaravan | cartwrightscaravan:entities/nonliving/cart | cartwrightscaravan.json |
| cartwrightscaravan | cartwrightscaravan:entities/nonliving/sled | cartwrightscaravan.json |
| cartwrightscaravan | cartwrightscaravan:entities/nonliving/marketstall | cartwrightscaravan.json |

---

## Accessory Item Patches

Some compatibility patches define internal CarryOn item types that are required for carry rendering. These are not player-obtainable items.

| Mod | Item Target | Purpose | Patch File |
| --- | --- | --- | --- |
| eternalstew | carryon:itemtypes/eternalstew-lid | Lid accessory item for eternalstewpot carry rendering; receives shape, variant, and texture from eternalstew assets | eternalstew.json |

---

## Notes

- Inventory source: patch files in `resources/assets/carryon/patches/carryonmore`.
- `Enabled Condition` references dot-notation world config keys. An empty entry means the behavior is always applied when the mod is present.
- `Priority` column is the `patchPriority` value. Empty means default (0).
- `Interact Support` indicates a `CarryableInteract` behavior is also patched for the same target block.
- To update this document after modifying patch files, regenerate the inventory from the patch JSON definitions.
