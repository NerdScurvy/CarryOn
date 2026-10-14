# Changelog

All notable changes to **Carry On** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/).

## [2.0.0-rc.1] - 2026-10-14

First release candidate of the 2.0 line. Requires Vintage Story 1.22.0 or newer
and [CarryOnLib](https://mods.vintagestory.at/carryonlib) 1.0.0-rc.1.
Back up worlds before updating.

### Added

- Namespaced behavior codes (`carryon:carryable`, `carryon:carryableinteract`,
  `carryon:attachablecarryable`, `carryon:dropondamage`) with legacy aliases for
  the old bare codes, so existing JSON patches and saved worlds keep working.
- `ICarryableBlockSlotHandler` (renamed from `ICarryableTransfer`): per-slot
  routing/validation, a separate interact delay, and a `Can*`/`Try*` split.
- Whole-entity pickup key-combination presets (`pickupMode: interact` or
  `pickup-hotkey`) so racks, boats and other entities cannot be grabbed by
  accident.
- Carried-block data preserved across the 1.14 → 2.0 upgrade; corrupt carries are
  refused with a warning instead of silently deleted (with a downgrade guard).
- Carry bags from ground storage into the hands slot with contents intact;
  place them back into an aimed slot, onto an open wall space, or stow them in a
  free worn-bag slot (config-gated).
- Open a ground-storage bag's GUI while carrying (Ctrl + right-click);
  slot targeting follows the cursor.
- Ground-storage bag help HUD hint ("pick item into hands", Pick Up + right-click).
- Carried bags now render in the hands view (first- and third-person).
- Live config reconditioning (`DebuggingOptions.ReconditionOnConfigChange`,
  default off) with a client carryability snapshot (`CarryabilityStateMessage`)
  so connected players and joiners reconcile without a relog.
- `BackpackSelectionMode: None` for servers whose players hide their worn pack,
  plus automatic per-player hidden-backpack compensation for supported hiding
  mods (Hide Gear, Vanity, Combat Overhaul and its legacy-compat stack).
- Missing-block placeholder rendering: a carried block from a removed/disabled
  mod renders as a recognizable shape instead of an invisible or white-cube carry.
- Runtime keybind conflict validation with chat/toast warnings (Carry and Swap
  Back bound to the same key, or a carry key bound as a modifier combo).
- Wattle/bamboo fence gates and Medieval Architecture gates, drawbridges,
  portcullis and trapdoors can be opened/closed while carrying.
- Multiblock gate interactions now commit on release, so toggling gates actually
  open.
- Slot-attach errors now distinguish causes (`SlotEntityNotAttachable` vs
  `SlotIncompatibleBlock`).
- Creative recovery of broken carries (unreadable blocks from removed mods) via a
  raw-tree adopt.
- `CarryEntityDemo`, a working example mod for modders.
- Pack-adjustment tool now works on carried items (writes exact-item-code pose
  overrides for `carry-bag.json`).
- Load-time warning when a carryable overlay omits `patchPriority`.
- Config-reload feedback in game: `/carryon-reload` reports warnings/rejections,
  and watcher reloads notify online admins.
- Ukrainian localization brought to full parity and reviewed.
- Headless automated regression suite (test-only; not shipped with the mod).

### Changed

- `Carryables.*` and `CarryablesOnBack.*` config toggles are reversible without a
  restart; conditioning reconciles both directions from a per-world baseline.
- Behavior conditioning converges to the config-correct state, and blocks with
  multiple carryable behaviors of a kind survive conditioning intact.
- A block whose base carryable is disabled by a condition keeps its enabled
  carryable configurations (previously it could become completely un-carryable).
- Item-class carries (e.g. a bag lifted from ground storage) now prefer the
  recoverable `carryon:carriedblock` entity over spilling contents; under
  `DropMode.Items` they land like `EntityOnFailedPlacement`.
- `DebuggingOptions.ReconditionOnConfigChange` is a per-server-start setting;
  restart after changing it.
- A config reload that omits sections logs a warning naming the sections that
  will reset; a reload with only warnings is reported as applied, not rejected.
- Config migrations now run every step for files more than one version behind.
- Toasts and notices resolve in the acting player's language, not the server's,
  and failure messages no longer show raw lang keys.
- The "capture attached wall signs" flag and the pickup wire members are now
  explicit protobuf members.
- Config-gated handlers (`TrackDroppedBlocks`, `TooHotToCarry`) respond to a live
  config reload without a restart.
- Carry-key + right-click on a placement-obstructed interactable face is consumed
  (no instant gate toggle on release); interact-delay behavior is unchanged.
- CarryOnLib (required companion): `ICarryableTransfer` → `ICarryableBlockSlotHandler`,
  entity handler split into pickup handler + spawner factory, failure codes are
  full lang keys with `{0}` placeholders removed, and
  `IEntityTransformGroupResolver.GetCacheSignature` was removed — mods built
  against `1.0.0-pre.*` must migrate.

### Fixed

- Editing `CarryOnConfig.json` while the server runs no longer clobbers user
  edits (invalid, empty and truncated writes are preserved byte-exact).
- `BackpackTypes` config edits now reach the client renderer.
- Interaction-help icons no longer go stale across world switches (a client
  crash on the second world is fixed).
- `CarryablesOnBack` toggles restore the back slot again (re-enabling works, and
  a boot-time `false` can be flipped on later).
- A recondition no longer leaves already-rendered carries in the old pose.
- Rejected place-downs clear every client-predicted cell (no phantom or
  ghost/duplicate blocks); denied pickups resync optimistically-cleared attached
  signs.
- Pickup and placement are gate-free on the whole footprint: target cell,
  multiblock cells and attached-sign cells (no claim-placement across claims, no
  capture of signs on land you lack permission for).
- Block interactions validate the player's reach server-side.
- Vehicle cargo barrels can no longer be pulled straight into inventory.
- Dropped trunks place their full multiblock footprint (controller + dummy), and
  trunk + wall-sign drops apply the correct rotations.
- The `FrontCarry` white-cube rendering artifact is gone.
- Creative detach of a stale attached stack recovers the raw carry instead of
  crashing.
- Picking up a container another player has its inventory open on is rejected
  (no duplicate/ghost items), including ground-storage bags and world containers.
- Claim and reinforcement denials show a clear message exactly once.
- `EntityCarriedBlock` registers correctly at startup, and the
  `attachablecarryable`/`dropondamage` code case mismatch is restored.
- Config migrations no longer skip intermediate steps.
- Hidden-backpack detection no longer latches "not hidden" on a transient miss.
- Mods can unregister the transform-group resolvers they registered.
- The attached-block rendering toggle applies immediately.
- `CarryablesFilters.RemoveBaseCarryableBehaviour` and
  `AutoMapSimilar: false` behave as documented.

### Removed

- `IEntityTransformGroupResolver.GetCacheSignature` (CarryOnLib) — implementers
  drop the method.
- The `__continue__` failure-code sentinel (CarryOnLib).

## [2.0.0-pre.8] - 2026-07-22

Requires CarryOnLib 1.0.0-pre.8.

### Added

- Walk speed and hunger rate multipliers: a `Multipliers` section in both
  `CarryWalkSpeed` and `CarryHungerRate` scales penalties globally or by block
  material (wood, stone, ceramic, metal) without editing per-block overrides.
- Carryable report debug tool: reports every carryable block's resolved walk
  speed and hunger rate modifiers in markdown, HTML or the server log, with block
  code filters and wildcards (`DebuggingOptions.CarryableReport.Enabled`).
- `.carryon iconmode` chat command to control carried label icon rendering
  (`standalone`, `atlas`, `standalone-fallback`, `disabled`), useful for
  diagnosing icon fading or macOS compatibility.

### Changed

- Config file changes, reloads and client sync now emit debug-level log messages.
- New `DisableConfigWatcher` option to disable the config hot-reload watcher.
- Internal cleanup: moved frequently changed classes (`CarryCodes`,
  `CarryOnConfig`, …) out of CarryOnLib and into CarryOn.

### Fixed

- Planter transform group ordering: carried flowerpots and planters now resolve
  plant-specific transforms (sapling, mushroom, flower) correctly.
- Crash on macOS: added a texture fallback for older OpenGL versions.

## [2.0.0-pre.7] - 2026-07-04

Requires CarryOnLib 1.0.0-pre.7.

### Changed

- `BlockEntityData` is now allowed to be null in carryable transfers.

### Fixed

- A QP Chisel Tools sign config patch that replaced all block behaviors.
- Stale carryon config being persisted.

## [2.0.0-pre.6] - 2026-07-02

### Fixed

- Client crash when carrying chiseled blocks: guard against disposed mesh
  references, rebuild cached render info, and prune the stale cache.

## [2.0.0-pre.5] - 2026-06-30

Requires CarryOnLib 1.0.0-pre.5.

### Added

- Hunger modifier support: a `CarryHungerRate` section with per-slot
  `HungerRateMultiplier` overrides (flat or resolved per slot) and a
  `MinSaturationThreshold` that stops hunger below a level.
- Damage drop behavior: `DropCarriedOnDamage` drops carried blocks per slot once
  damage exceeds a configurable `DamageThreshold` (healing damage excluded).
- Carried block entities: with `CarriedBlockEntity.DropMode` set to `EntityAlways`
  or `EntityWhenSupported`, drops spawn as `EntityCarriedBlock` entities with a
  pickup grace period, access policy, despawn timer, z-fighting offset and
  collision-aware spawn position.
- Hot-reloadable config via `FileSystemWatcher` (500 ms debounce), synced to
  connected clients through `ConfigSyncMessage` — no server restart required.
- `CarryPermissionService` for server-side carry/drop/pickup permission checks,
  with a client-side pre-check for responsiveness.
- Interaction support for dropped `EntityCarriedBlock` (pick up with Use /
  Sneak + Use).

### Changed

- Transform resolver config rename: `transformGroupResolver` →
  `rootGroupResolver` + `attachmentGroupResolver`, and `defaultRenderFacing` →
  `rootRenderFacing` (JSON and C#).
- `IgnoreCarrySpeedPenalty` removed; walk speed modifiers are always applied
  unless a slot is disabled via the `Enabled` flags.
- Walk speed penalty always applied: `CarryWalkSpeed.HandsAllowSprint` replaces
  `AllowSprintWhileCarrying`, so sprint permission and the speed penalty are
  independent (configs that allowed sprinting now also take the penalty).

### Fixed

- `PlantContainerTransformGroupResolver` returns `false` for empty pots (no
  unnecessary cache entries) and uses a single shared instance for root and
  attachment registration.
- Removed dead null-forgiving operators in the pack-adjustment tools.

### Internal

- New network messages (`ConfigSyncMessage`, `PickupEntityMessage`), the
  `EntityCarriedBlock` entity, services (`CarriedBlockEntityService`,
  `CarryOnConfigService`, `CarryPermissionService`), resolvers
  (`HungerRateModifierResolver`, `DataAttributeTransformGroupResolver`), the
  `EntityBehaviorDropCarriedOnDamage` behavior, `CarriedBlockAccessPolicy`, and
  `CarriedBlockTreeSerializer`.

## [2.0.0-pre.4] - 2026-06-10

Requires CarryOnLib 1.0.0-pre.4.

### Added

- `BackpackSelectionMode` (`FirstFound` / `LastFound` / `FirstOnly`) to choose
  which backpack type a block's back position is adjusted for.
- Cluster carry: wall signs attached to a carried block are picked up, carried
  and placed with it (when the option is enabled).
- `attachedTransform` in `LabelRenderSettings` to tune attached-label placement
  separately from the standalone carried case.
- `defaultRenderVariant` to render a carried block as an alternative variant
  (for example a wall-sign model instead of a ground sign).
- `defaultRenderFacing` to choose which facing variant is used when a carried
  block normalises to a default direction on pickup.
- Validation that prevents attaching blocks with carried wall signs to entities
  (boats), where the inventory conversion would silently drop the signs.

### Fixed

- Players can no longer pick up trader-owned blocks in survival.
- Various render performance improvements.

## [2.0.0-pre.3] - 2026-06-04

Requires CarryOnLib 1.0.0-pre.3.

### Added

- Walk speed modifier overrides: configure carry movement speed per block or
  block type for the hands and back slots.

### Changed

- Removed broken carried-block lighting and cleaned up chat commands.
- Interaction system refactored into a dedicated controller, state machine and
  validator for better reliability.
- Rendering improvements to the entity carry renderer and transform template
  system.
- Harmony patching extracted to a dedicated utility.
- Removed the deprecated Shoulder carry slot.
- General stability improvements and null-safety hardening.

## [2.0.0-pre.2] - 2026-06-01

Requires CarryOnLib 1.0.0-pre.2.

### Added

- Carryable support for the Muck Bin (Interesting Mining & Extraction).
- Sit/crouch-specific carry animations via `animationSit` and `animationCrouch`
  in carryable slot settings.

### Changed

- Reed chest carried on the back no longer slows the player down (restored to the
  original behaviour).
- Cabinet carry behavior is now gated by the `carryon.Carryables.Cabinet`
  condition.
- Rendering, transform group resolver and caching cleanup for performance.

### Fixed

- Stuck carry animation after a failed pickup.
- Carried signs/labels no longer trim leading whitespace and blank lines.

## [2.0.0-pre.1] - 2026-05-25

The 2.0 overhaul. Requires [CarryOnLib](https://mods.vintagestory.at/carryonlib).
Early testing pre-release — back up `CarryOnConfig.json` if you may switch back
to 1.14.x.

### Added

- Back-carried blocks are adjusted for different backpack types, with straps
  added to most back-carried blocks.
- Carried rendering for contents and details: flowerpots and planters show the
  plant, display cases show their contents, labelled chests and signs show their
  label text, labelled crates show a content icon, and chiselled chests/planters
  render the chiselled block (QP's Chisel Tools).
- CarryOn hands icons.
- `TooHotToCarry` config option: prevents picking up heat-source blocks until
  they cool down.
- A public interface allowing mods to transfer carried blocks into other block
  entities (for example RackEmUp placing carried molds into a mold rack).
