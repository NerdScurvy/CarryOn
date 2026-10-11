# CarryOn Atlas Scenario Suite

Automated integration scenarios for the CarryOn mod: each scenario class
boots a real Atlas headless Vintage Story server with both mods staged and
drives carry flows end-to-end through the public manager API and the internal
server message handlers.

- **Coverage index:** [INVENTORY.md](INVENTORY.md) — generated from the test
  sources (class list, row names, fixture wiring, totals). Do not edit by
  hand; regenerate from the repo root with
  `pwsh -File scripts\generate-test-inventory.ps1`.
  Its `-Verify` switch fails when the committed file has drifted.
- **Row narrative:** what each row asserts and *pins*, red-first evidence, and
  premise/control caveats are hand-written prose maintained in the
  maintainer's private working notes, not in this repo. This README keeps the
  durable parts: how to read the rows, how to run the suite, how the harness
  works, the behavior decisions the rows encode, and the suite's known gaps.

**Reading the rows** (applies to the narrative inventory and to the row names
in the generated index). Each row names the test method; in the narrative
each bullet also states what it asserts, and then says what it *pins* - the
behavior the row exists to hold in place. Rows marked **red-first** were
written to fail against the code as it stood before a fix, and flipped green
by the production fix alone, with no change to the behavioral assertion. A
row marked **premise** or **control** asserts the input a defect needs, or
the surrounding behavior that must stay green; premise and control rows are
legitimately green both before and after a fix, and a green premise row is
not evidence that the defect itself is covered. Red-first evidence (the
specific mutation used to prove a row can fail) is given in-band where the
row carries it.

**Where the rows live.** The example-mod rows live in a sibling project,
`tests/CarryOn.Scenarios.ExampleMods/` (own README and generated index). They
are kept out of this project because staging those mods into this suite would
perturb its content-pinning rows (see Decision 17).

**Trait annotations.** Every test class and row carries xUnit `[Trait]`
annotations, rendered by the generated index:

| Trait | Scope | Meaning |
|---|---|---|
| `area` | class | functional grouping (drives the index's headings) |
| `summary` | class + row | one-line setup + what is asserted |
| `pins` | row | the behavior the row exists to hold in place |
| `role` | row | `smoke` / `regression` / `premise` / `control` / `parity` / `contract` / `coverage` |
| `redfirst` | row | `true` when the row was written to fail against the pre-fix code |
| `evidence` | row | the red-first proof (mutation or failure shape) |
| `decision` | row | the public decision number(s) this row encodes (see the checklist below) |
| `caveat` | row | known-gap caveat — read it with the row, not after it |
| `gamemode` | row | `creative` opt-ins only; everything else is the survival default |

Authoring rules: each `[Trait(...)]` must fit on **one physical line** — the
lexical generator does not match attribute arguments that wrap across lines
(`+`-concatenation is silently dropped, not wrapped). Every class needs
`area` + `summary` and every row `summary`; `generate-test-inventory.ps1
-Verify` fails when either is missing. Duplicate keys are last-wins, so cite
multiple decisions inside one value (`"21,24"`). The traits also double as a
run filter: `run-tests.ps1 -Filter "role=regression"`.

**What is not covered here.** The "What the suite does not cover (known
gaps)" list at the end of this document is the authoritative statement of the
suite's limits. Several rows deliberately reach a code path that no
production caller uses, or depend on a vanilla name that this mod cannot fix;
each such row carries an inline caveat. A caveat is part of the row's claim -
read it with the row, not after it.

## Running the suite

```powershell
pwsh -File run-tests.ps1
pwsh -File run-tests.ps1 -Filter "FullyQualifiedName~TwoPlayerScenarios"
```

Preconditions (enforced by the script, not just documented): `VINTAGE_STORY`
points at a Vintage Story install (e.g. `C:\VintageStory\1.22.6`), and the
CarryOnLib checkout is a sibling of this repo. The script Release-builds both
mods first (the test host stages them as the Release-generated zips; warnings
are errors, enforcing the clean-build checklist item) and then
runs `dotnet test`. Per-class server boots dominate runtime; use `-Filter` (or
the Atlas CLI `atlas run --list/--filter`) for single-class iteration. The
example-mod rows (RackEmUp/BarrelRackDemo put/take) run from the sibling
project's own `run-tests.ps1` — see
`..\CarryOn.Scenarios.ExampleMods\README.md`.

Multiple game versions in one command, without re-pointing the machine's
`VINTAGE_STORY` (`run-versions.ps1` sets it per run inside its own process —
VS Code and other terminals are unaffected):

```powershell
pwsh -File run-versions.ps1
pwsh -File run-versions.ps1 -Filter "FullyQualifiedName~TwoPlayerScenarios"
pwsh -File run-versions.ps1 -Installs "C:\VintageStory\1.22.6,C:\VintageStory\1.22.7"
pwsh -File run-versions.ps1 -SummaryOnly
```

`run-versions.ps1` builds once against the newest install in its matrix
(Atlas's build-newest/run-older rule) and then runs `dotnet test --no-build`
per install, writing one TRX per version into `TestResults/` (diffable across
versions with the Atlas CLI's `atlas diff`). The default matrix is every
stable install at or above the modinfo game floor (`1.22.0`) found beside the
current `VINTAGE_STORY` — pre-1.22 installs need that dependency relaxed
first, and rows decompile-pinned to 1.22.6 vanilla behavior (see "Decisions")
may legitimately fail on other versions: the sweep surfaces drift, it does not
promise an all-green matrix. After a sweep, `run-versions.ps1 -SummaryOnly`
reprints the per-version summary (pass/fail counts plus each failed test's
name) from the persisted TRX files without rebuilding or re-running, and the
Atlas CLI's `atlas diff <a.trx> <b.trx>` compares two versions' runs
test-by-test (`dotnet tool install --global Pixnop.Atlas.Cli`).

## How the harness works

**Server and worlds.** Atlas boots one embedded headless Vintage Story server
per test class with both mods staged, on a superflat world. Every
scenario is `[AtlasScenario(RollbackWorld = true)]`: block/entity world data is
rolled back between scenarios. **Claims are NOT rolled back** (they are server
save state), so every claim row first drains leftover claims
(`ClearStaleClaims`). Atlas also rejects joining the same player name twice in
one class-world, so each row joins each name exactly once.

**Simulated clients.** `World.JoinPlayer(name)` joins real headless players
(own connection, own inventory, reach Playing state) — multiple players per
world. Scenario classes join through the base-class wrapper
`JoinPlayer(name, gameMode = Survival)` instead: Atlas joins players in
creative by default, and the suite forces survival so rows exercise the game
mode players actually play (see Decision 19 for the divergences creative
hides). `player.Client.Packets<T>(channel)` decodes the server→client mod
packets a real client would have received; `LockSlotsMessage` on the `carryon`
channel is the assertable one (emitted on both success and failure paths).

**The two drivers.** Atlas's client side is capture-only — arbitrary
client→server mod-channel packets cannot be sent in-test — so actions are
driven server-side against a joined player through two seams:

- `CarryActions` (manager path): the public `ICarryManager` API from
  CarryOnLib — `TryPickUp`, `TryPlaceDownAt`, `TryPickUpGroundStorageBag`,
  `TryStoreBagInBackpack`, `DropCarried`, `DropBlockAsEntityOrItem`,
  `SwapCarried`, `SetCarried`, `RemoveCarried`, `GetCarriedFromWorld`, plus the
  read side.
- `HandlerInvoke` (handler path): invokes the internal server mod-channel
  handlers `CarryServerHandler.OnPickUpMessage` / `OnPlaceDownMessage` with the
  public message records — the exact entry points a real client's packets take
  (including the `WrittenCells` prediction claim). Test-side reflection only,
  zero production source changes; resolution fails fast naming the drifted
  symbol if production internals change.

Denial rows run through **both** drivers where available (the handler path
adds client-visible observables).

**What is observable — and what is not.** Assertables: world truth
(`World.BlockAt`), carry state (`GetCarried`, `IsCarrySlotOccupied`,
`GetCarriedRevision` — failed actions bump the revision by design), captured
mod-channel client packets, CarryOnLib events (`BlockRemoved`, `BlockDropped`),
and — via `VanillaClientWire`, a reflection tap of the dummy client's raw
connection — the **vanilla server→client wire itself**: `IngameError` toasts
and the 100 ms `ServerSystemBlockSimulation` dirty-flush packets
(`ExchangeBlock` with server-truth block ids; `BlockEntities` entries carrying
BE data or the null-classname removal marker). Atlas's observation surface
drains and discards vanilla packets, so vanilla-wire rows call
`player.Client.Clear()` before the action, make no Atlas observation calls
after it, and tap once at the end. Still not observable headless: client
HUD/audio effects; outside the vanilla-wire rows, dirty marks
(`MarkBlockDirty`/`MarkBlockEntityDirty`) are asserted indirectly.

**Config seeding.** `[AtlasDataFiles("fixtures/…", TargetPath = "ModConfig")]`
seeds `<world-data>/ModConfig/CarryOnConfig.json` before server boot. Every
fixture sets `"ConfigVersion": 6`; most also set
`CarryOptions.InteractSpeedMultiplier = 2.0` so the hold-to-carry pass completes
without a long click, and the recondition fixtures set
`DebuggingOptions.ReconditionOnConfigChange = true` because that is the mode
under test. Those three are omitted below to keep each row to what the fixture
uniquely contributes.

| Fixture | Distinctive seed | Used by |
|---|---|---|
| `fixtures/ModConfig` | `Carryables.Chest = false` | ConfigSeeding |
| `fixtures/ConvergenceConfig` | `Carryables.Chest = true`, `CarryablesFilters.AutoMapSimilar = true` | ConvergenceAutomap, ConvergenceChurn, ConvergenceContract, ConvergencePostBoot, ConvergenceRestore, InteractTwinPayload |
| `fixtures/CrateOnBackConfig` | `CarryablesOnBack.Crate = true` | CarryModifierStat |
| `fixtures/DisabledCloneParityConfig` | `AutoMapSimilar = true`, **no** `Carryables` block at all | DisabledCloneParity |
| `fixtures/DoorOffConfig` | `Interactables.Door = false` | HandlerPathInteractDoorOff |
| `fixtures/OnBackBarrelOffConfig` | `Carryables.Barrel = true`, `CarryablesOnBack.Barrel = false` | CarryablesOnBackBootOff |
| `fixtures/OnBackBarrelOnConfig` | `Carryables.Barrel = true`, `CarryablesOnBack.Barrel = true` | CarryablesOnBack |
| `fixtures/ReconditionOnChangeConfig` | `Carryables.Chest = false`, `ReconditionOnConfigChange = true` | ReconditionOnChange |
| `fixtures/ReconditionRenderCacheConfig` | `Carryables.Barrel = true`, `CarryablesOnBack.Barrel = true` | ReconditionRenderCache |
| `fixtures/ReconditionSelectiveEnableConfig` | `Carryables.Chest = false`, `Carryables.Anvil = false` | ReconditionSelectiveEnable |
| `fixtures/RemoveBaseCarryableConfig` | `RemoveBaseCarryableBehaviour = ["game:chest-"]` (empty default) | RemoveBaseCarryable |
| `fixtures/RemoveBaseReconditionConfig` | `RemoveBaseCarryableBehaviour = ["woodchests:wtrunk"]`, `Carryables.Chest = true` | RemoveBaseRecondition |
| `fixtures/RemoveExcludedCarryableConfig` | `RemoveCarryableBehaviour = ["game:flowerpot-amber"]` (empty default) | RemoveExcludedCarryable |
| `fixtures/SignConfig` | `CarryOptions.CarryAttachedWallSigns = true` | HandlerPathSign |
| `fixtures/StorageOffConfig` | `Interactables.Storage = false`, `AutoMapSimilar = true` | InteractTwinStrip |
| `fixtures/TrunkConfig` | `Carryables.ChestTrunk = true` | HandlerPathTrunk, ClaimEdgeStraddle, DroppedTrunkHole, TraderLand |
| `fixtures/TrunkSignConfig` | `ChestTrunk` + `CarryAttachedWallSigns`, combined | ClaimFootprintSign, HandlerPathVanillaWire, PickupPhantomReconcile (combined because Atlas's seeder collides on file name) |
| `fixtures/WatchdogConfig` | `Carryables.Chest = false`, `InteractSpeedMultiplier = 2.0` | ConfigWatchdog, BootLatchedMode |
| `fixtures/WatchdogDisabledConfig` | as `WatchdogConfig` + `DisableConfigWatcher = true` | ConfigWatchdogDisabled |

*Table corrected 2026-10-01.* It previously listed 7 of the 17 fixture
directories and described two of those incorrectly (`ReconditionSelectiveEnable`
was credited with the `ReconditionOnConfigChange` seed, which is a standing
convention rather than that fixture's distinguishing content, and
`TrunkConfig`'s user list was missing `DroppedTrunkHole`). All 17 were then
listed with their distinctive seed and every consumer verified against the
`[AtlasDataFiles]` attributes in the row files; an 18th, `StorageOffConfig`,
was added 2026-10-07 for the interact-twin strip row, and a 19th,
`CrateOnBackConfig`, was added 2026-10-11 for the entity-stat back-slot rows.

**Claim construction and the survival default.** Claims are built in-scenario
through the vanilla `ILandClaimAPI` (`LandClaim.CreateClaim` + `Claims.Add` —
the same registration worldgen uses); membership via
`PermittedPlayerUids[uid] = BuildOrBreak`; trader land is the exact worldgen
recipe (ownerless claim, `ProtectionLevel 10`, `LastKnownOwnerName "Trader"`).
Historically only the claim rows and the death row forced survival (vanilla's
claim privilege bypasses — `OkPrivilege`, everywhere-privileges — are
creative-gated, and creative nullifies lethal damage); since 2026-09-20 the
survival forcing is the suite-wide default via the base `JoinPlayer` wrapper
(Decision 19), so the claim classes' inline forcing is gone and every row,
claim-dependent or not, runs survival. Atlas's
superflat worlds put the surface soil at `Spawn.Y` (y=2, air from y=3), so
players teleported to `Spawn.Offset(x, 0, z)` sit embedded at y=2 with their
body-level cell at y=3 — the death-drop row's entity landing and placement
geometry follow from this.

**Vanilla comparator methodology.** Parity rows compare against vanilla's own
answers for the identical geometry: `Block.CanPlaceBlock` (placement; fails
with `"claimed"`) and `Claims.TestAccess` (access). `World.SetBlock` bypasses
claims entirely and is never a comparator. The comparators are decompile-pinned
(VS 1.22.6): vanilla claim-checks placement and break at the aimed/target cell
only — no multiblock footprint sweep.

**Determinism pins.** Trunk orientation is forced by teleporting the actor
until `Block.SuggestedHVOrientation` reports the desired facing (the
`HorizontalOrientable` placement variant follows it); trunk place-down
selections use block-relative `HitPosition` (vanilla convention — the shared
`CarryActions.AimDownAt` helper fills an absolute vector, an orientation-only
quirk left unfixed to avoid fixture churn). Cluster-sign rotation is pinned by
`Entity.Pos.Yaw` (preflight estimate) plus standing due west of the target
(vanilla position-geometry `MeshAngle`), asserted to agree. Trunk footprint
table (from `chest-trunk.json`): north → dummy `+(1,0,0)`, east → `+(0,0,1)`,
south → `-(1,0,0)`, west → `-(0,0,-1)`; controller always at the target cell.
Dropped-trunk hole rows: the drop search sorts candidates Y-first and shuffles
BFS order/facings, but the dug hole cells are the only passable cells below
the dropping player's body-level center and exactly one facing fits per hole
cell, so the trunk always spans the hole — **which end becomes the controller
is not deterministic**, so those rows assert the footprint as a set
(controller in one hole cell + the exact dummy variant in the partner cell),
never the controller end; the standing player's own feet cell may become the
controller cell (the drop path places via `SetBlock` directly, no vanilla
entity-intersection check) — pinned as expected.
Vanilla-wire standing geometry: a dummy player sends no client input, so
server physics never ejects it — it stays embedded in the ground-surface cell
its teleport targeted, its 0.6×1.85 selection box reaching 0.85 blocks into
the body-level cell above, where the chest/trunk's 0.8125-tall collision box
overlaps it (deterministic `entityintersecting` for the rejection rows;
aiming at the surface cell itself would fail `notreplaceable` and the
placement path would offset up into the body-level cell anyway).
Chunk-registration flake (stress-pinned): the engine's spawn-time chunk-section
entity registration is silently skipped when the section is not in the world
map at the join instant, and the server's 1000 ms fix-up pass repairs
`InChunkIndex3d` mismatches only — so a join-time miss leaves the entity
invisible to `GetEntitiesAround` (and vanilla's entity gate) indefinitely.
Vanilla-wire rows verify visibility first and, when missing, bounce the
entity across the section boundary Spawn straddles and back so both fix-up
passes re-register it (`EnsurePlayerVisibleToChunkQueries`).

## Behavior decisions encoded by the tests — review checklist

These are the judgment calls the suite pins. If any of them is not the
intended behavior, the corresponding rows need re-targeting (rows encode
current decided behavior to stay green; changes go through a plan/issue).

1. **Deliberately stricter than vanilla on claim straddles.** Placement and
   pickup gate the whole footprint (target + multiblock dummies + attached
   signs at rotated offsets). Vanilla claim-checks the aimed/target cell only
   and *allows* part-cell intrusion; CarryOn denies it. Decided in the
   2026-09-20 claim-overlap issue (safety over parity). Rows:
   `ClaimEdgeStraddleScenarios` (both straddle rows),
    `TraderLandScenarios.TrunkStraddleAgainstTraderClaim`,
    `ClaimFootprintSignScenarios`.
2. **No grandfathering.** A claim added after a structure was placed denies
   its pickup under current claim state. Row:
   `ClaimFootprintSignScenarios.PickUp_TrunkFootprintClaimAddedAfter_Denied_NoGrandfathering`.
3. **Attached signs are left behind, never block.** Signs on unpermitted
   cells are excluded from capture (stay in the world); only the
   main/multiblock footprint can deny the parent pickup. Rows: the
   per-cell sign-capture-filter rows in `ClaimFootprintSignScenarios`.
4. **`NoPermission` before `NotCarryable` for claim-blocked dummy targets.**
   A dummy-targeted pickup whose structure controller is claim-blocked
   answers `no-permission` (permission check precedes carryable). Unclaimed
   dummy targets still answer `not-carryable` (nothing happens) — an
   accepted, safer-than-vanilla divergence from vanilla break, which removes
the whole structure (the
vanilla-straddle divergence).
5. **Failure observables by design.** Rejected handler actions still bump the
   carried revision and emit `LockSlotsMessage` so the client resyncs. The
   pickup handler only accepts `Slot = Hands`.
6. **Shipped defaults pinned by the suite:** `CarryablesOnBack.Crate = false`
   (crate carries in Hands only), `CarryablesOnBack.Chest = true`,
   `Carryables.ChestTrunk = false`, `CarryOptions.CarryAttachedWallSigns =
   false` (both overridden only by seeds), and the automapper on by default.
   A default change requires re-targeting `IsCarryable_SlotSupportMatrix`,
   the seeded classes, and the chest rows.
7. **Emergent interact-while-carrying.** Chests are interactable-while-carrying
   because the automapper grows `carryableInteract` onto entity-class siblings
   of an existing source — the baseline is not a hand-curated list. Row:
   `PermissionBaselineScenarios.CanInteractWhileCarrying…`.
8. **Drop fallback ordering.** A claim-blocked drop falls back to items
   (`BlockPlaced = false`) rather than placing into the claim or dropping the
   carry silently. Row:
   `ClaimFootprintSignScenarios.DroppedTrunk_StraddleOnlyCandidates_FallsBackToItems_NoClaimWrite`.
9. **Carried code canonicalization.** `CarriedBlock.Block.Code` canonicalizes
   to the default facing variant (a `chest-east` cell picks up as
   `game:chest-east` but a `chain-north` cell canonicalizes); the source
   cell's exact code is preserved in `OriginalBlockCode`, which the tests use
   for identity. Asserting against `Block.Code` instead would be wrong.
10. **`HandlerPathTrunkScenarios` claims the full two-cell footprint** — the
    row resolves the placed trunk variant through the engine's public static
    `Block.SuggestedHVOrientation` and derives the multiblock dummy cell from
    the same footprint table (pinned from `chest-trunk.json`) the other trunk
    rows use, so the controller **and** dummy cells are claimed and asserted.
    (Pre-2026-09-21 the row filtered its footprint by first code part
    `trunk`, missed the `multiblock-*` dummy, and was weaker than its name.)
11. **Test-harness facts encoded intentionally:** `RollbackWorld` does not
    revert claims (rows drain stale claims); Atlas rejects duplicate player
    names per class-world; vanilla claim checks are creative-gated (the reason
    the suite's survival default exists — see Decision 19); `World.SetBlock` runs placement behaviors synchronously (trunk
    dummies materialize without tick waits) but bypasses claims (never a
    comparator); `BlockPos.South()/North()/…` mutate in place (fixtures use
    `AddCopy`/`Copy().Offset(…)`); a dummy player sends no client input, so
    server physics never ejects it from its teleport target (the
    vanilla-wire rows exploit this for a deterministic `entityintersecting`
    rejection); the engine can silently skip the spawn-time chunk-section
    entity registration, leaving it invisible to entity queries forever
    (vanilla-wire rows verify and repair via a section-boundary bounce).
12. **Rejected place-downs evict phantom cells through the vanilla
    dirty-flush channel, and only when claimed.** The server does not send a
    custom eviction packet: claimed cells are `MarkBlockDirty`/
    `MarkBlockEntityDirty`-marked, and the engine's 100 ms flush delivers
    `ExchangeBlock` server-truth ids plus the BE removal marker exactly
     where the client predicted writes. Without a claim, a rejection sends no
     BE resync (nothing predicted → no phantom to evict). Rows:
     `HandlerPathVanillaWireScenarios` (the denial rows and the success
     control that validates the same channel against the success path's own
     packets), whose place-down spill row (Decision 24d) is the one
     non-rejection case: a successful place-down whose claim spilled an
     attached child, which must evict the predicted child cell explicitly.
13. **Interact-while-carrying rows pin vanilla gates only.** The
    `IMultiBlockInteract` `OnBlockInteractStop` dispatch (the Medieval
    Architecture commit-on-release mechanic) has no vanilla implementor, so
    it stays unpinned rather than staged against a third-party mod or a
    fixture-mod block (deferred candidate in the release-notes test plan).
    The legacy bare-code **alias** half of the namespaced-codes note is
    likewise unpinned — it needs a bare-code patch source (fixture-mod
    candidate); the namespaced half is pinned by
    `ChestCarryableBehavior_UsesNamespacedCode`. Rows:
    `HandlerPathInteractScenarios`, `HandlerPathInteractDoorOffScenarios`.
14. **A dead player's drop never writes into the world.** Vanilla claims
    answer `EnumWorldAccessResponse.PlayerDead` for a dead player, so the
    death/damage drop path's world placement is refused (`no-permission`)
    and the DropMode chain lands carried blocks as `carryon:carriedblock`
    entities instead — no world write by a dead player, no content loss,
    one `BlockDropped` event per occupied slot regardless of whether the
    damage drop or the death drop fires. Row:
    `DropFlowsScenarios.PlayerDeath_DropsCarried_OncePerOccupiedSlot_ContentLands`.
15. **The missing-block placeholder shadows the stored-block-code recovery.**
    In `DeserializeSafe`, a block stack whose id resolves only to VS's
    missing-block placeholder reads `Failed` (`missing-block`) **before**
    the stored block-code fallback can engage — vanilla's `GetBlock(int)`
    and the `ItemStack.Block` getter substitute the placeholder rather than
    returning null. The reachable stored-code recovery branch is item-class
    (`OriginalItemCode`). The client-side render placeholder substitution is
    likewise invisible server-side. Rows: `LibContractScenarios`
    (DeserializeSafe matrix, SetCarriedRaw). Authoring review candidate: if
    the removed-mod block recovery is meant to work, it needs the stored
    code consulted before/independently of the id resolution.
16. **The permission pipeline is priority-ordered, first-non-null-wins.**
    `RegisterCheckPermissionAt` handlers run in ascending priority around
    CarryOn's internal priority-0 handler; a null opinion falls through to
    the default world rules; once a handler decides, later handlers are not
    consulted. Row: `LibContractScenarios.CheckPermissionAt_…`.
17. **Example-mod rows live in a separate Atlas project.** `[AtlasMods]` is
    assembly-level, so staging RackEmUp/BarrelRackDemo (and foodshelves) into
    this suite would add carryable content to every class-world and perturb
    the content-pinning rows (`PermissionBaselineScenarios`,
    config-seeding baselines). `tests/CarryOn.Scenarios.ExampleMods/` boots
    its own class-worlds with the five-zip staging instead, sharing the four
    harness source files via `Compile` links. Rows: `RackEmUpScenarios`,
    `BarrelRackScenarios` (that project's README inventories them).
18. **An ungated override patch cannot escape a gated base patch.** When a
    block ends up with two `carryon:carryable` patch entries (CarryOn's own
    gated patch + a third-party override with `overrideExistingProperties`
    and no `enabledCondition`), `MultipleBehaviorResolver` merges them with
    the base as primary and the override as overlay — and the overlay's
    missing keys do not erase the base's, so the merged survivor keeps the
    base's `enabledCondition`. With shipped defaults
    (`Carryables.MoldRack = false`) the mold rack therefore ends up with no
    carryable behavior at all: RackEmUp's put/take silently no-op with **no
    client error** (resolution fails before a failure code is produced). The
    dev world config enables `MoldRack`, which is why production works; the
    example-mod rows seed both `Mold` and `MoldRack` to mirror it. Review
    candidates: RackEmUp's patch should state its own `enabledCondition` (or
    none-of-the-above semantics need deciding), and a silent handler failure
    for an unresolvable transfer target is an observability gap. Rows:
    `RackEmUpScenarios` (its class doc records the seed rationale).
19. **Survival is the suite default; creative is an explicit opt-in.** The
    base class joins every player through
    `JoinPlayer(name, gameMode = EnumGameMode.Survival)` — Atlas joins
    players in creative, and creative hides real divergences: vanilla land
    claims do not restrict creative players (`OkPrivilege` /
    everywhere-privileges bypasses), creative nullifies lethal damage, and
    CarryOn itself lets creative bypass five server-side gates. Creative
    opt-ins are rare and deliberate — a row that joins in creative must be
    pinning one of those bypasses itself (current opt-ins: the reinforcement
    bypass, the three reach bypasses, the two entity-pickup bypasses, and the
    attachable-recovery reproducer). The five known CarryOn creative bypasses,
    and the survival halves they hide: dropped-carried-block
    pickup ignores `PickupAccess`/ownership in creative
    (`CarriedBlockAccessPolicy.CanPickup`); reinforced blocks are
    pickup/place-down-permitted in creative (`CarryPermissionService`); a
    creative player may adopt an unreadable carried-block entity's raw tree
    (`CarryServerHandler` entity pickup); a creative player may detach an
    attached slot whose block no longer resolves
    (`EntityBehaviorAttachableCarryable`); and a creative player is exempt
    from reach validation on all six gated paths, because the exemption lives
    inside `CarryReach.IsWithinReach` rather than at each call site
    (checklist item 29). Coverage state: all five bypasses
    are pinned — the reinforcement pair both halves
    (`ReinforcementScenarios`); the entity-pickup pair both halves
    (`DroppedCarriedBlockPickupScenarios`: the pickup-access bypass and the
    unreadable-adoption bypass, each with its survival twin); the reach bypass
    has a creative-allowed row beside each of three survival-denied rows
    (`HandlerPathReachScenarios` interact, `DroppedCarriedBlockPickupScenarios`
    entity pickup, `VehicleAttachScenarios` attach,
    `GroundStorageBagFlows` ground-storage lift); the attachable
    pair is pinned in survival with the creative half as the known-red NRE
    reproducer (Decision 20). A future creative branch
    added to any currently-covered path would keep passing silently in a
    creative-only row — the survival default is the guard against exactly
    that.
20. **Reinforcement denies every survival player, and the placeholder breaks
    the creative recovery.** Two findings pinned by the coverage rows:
    (a) `CarryPermissionService.HasPermissionAt` denies pickup at a
    reinforced cell for EVERY survival player — including the reinforcing
    owner — with no owner carve-out, deliberately stricter than vanilla
    (where the owner retains break/remove privileges via
    `GetAccessFlags`); a survival player must remove the reinforcement
    before carrying the block. Review candidate if owner-parity is desired.
    (b) Vanilla's missing-block placeholder (`IsMissing = true`, `Code =
    null` — the same shape `CarriedBlockTreeSerializer` documents) crashes
    the creative detach recovery: `AttachableCarryConversion` dereferences
    the null `Code`, and the intended `SetCarriedRaw` recovery branch
    requires a null `Block` that the placeholder substitution never
produces server-side. Recorded as the
creative-detach recovery NRE with the proposed two-diff
fix; the red row pins the intended post-fix behavior. Rows:
`ReinforcementScenarios` (both),
     `VehicleAttachScenarios.UnresolvableCargoStack_Detach…` (both).
21. **Pickup-open guards (no config flag, self-skip UID carve-out).** A container
    rejects being picked while **another** player has its inventory open
    (`slot-inventory-open`), and the acting player's own open GUI never blocks
    the action — the server guard skips the acting player's UID, so the
    scenario "open its own GUI then pick it" still works. Applied to three
    surfaces via one shared helper `CarryInventoryGuard.HasOpenInventory`: the
    existing mounted-bag detach guard (`mountedbaginv-{slot}-{entityId}` prefix),
    the ground-storage bag pickup (`blockcontainedbaginv-blockcontainedbag-slot{slotId}-{pos}`
    wrapper prefix **or** the storage's own `beg.Inventory` ID), and world-block
    pickup (any `BlockEntityContainer`'s inventory ID). No config flag (author
    call — revisit only if frequent-pickup noise appears); the local-client
    barrel force-close is untouched (it only covers the acting player's own
    dialog; the server guard covers everyone else). Rows:
    `PickupOpenGuardScenarios` (all 10), `VehicleAttachScenarios`
    (mounted reject/allow pair).
 22. **Config hot-reload surface, three reproducers pinned (all three red until
     the fix lands).** (a) A truncated-but-parseable `CarryOnConfig.json`
     (`{"ConfigVersion":6}`) is loaded as "valid", defaults fill the rest, and
     the reload's persist side re-writes those defaults over the user's file
     (the config-watchdog clobbering report).
     Syntactically invalid and empty files, by contrast, throw and are cleanly
     rejected — file and config preserved. (b) `DisableConfigWatcher` is
     defined but never read: the flag cannot stop the watcher. (c) A valid
     reload flips the loaded config but does not re-run the automapper /
     `BehavioralConditioning` boot pass, so `Carryables.Chest` changes do not
     take effect on block types until restart — the reload is a no-op for the
     very setting users edit. All three reproducers reproduced identically on
     VS 1.22.0/1.22.6/1.22.7 (sweep 2026-09-23). Rows:
     `ConfigWatchdogScenarios`  (a), `ConfigWatchdogDisabledScenarios` 
     (b), `ConfigWatchdogScenarios` A1 (c); the planned-but-green rows /
     (invalid/empty) document where the degraded path diverges from the throw
     path.
 23. **The deferral invariant holds on every conditioning run, not just boot.**
     A condition-disabled block is never an automap target: at boot its
     disabled conditional stays mounted through auto-mapping (removed only in
     the final pass), and on a Mode B recondition run pass 0 re-mounts the
     dirty baseline entry (Initialize only, from its pristine JSON) before
     auto-mapping, so the same shield exists before the final pass re-strips
     it. Without the remount, a boot-stripped chest would receive a clone from
     any same-signature donor enabled under a different toggle (the reed chest
     and fancy storage vessel share `Class:BlockGenericTypedContainer` with
     the chest) — the `ReconditionSelectiveEnableScenarios` row caught exactly
     that, red-first. Row:
     `ReconditionSelectiveEnableScenarios.LiveAnvilEnable_…`.
 24. **One dirty mark on a multiblock already repairs the whole multiblock;
      attached signs need their own cells, and the placement spill repairs the
      cell the client guessed.** Four engine/test facts, all probe-verified
      (2026-09-28) and pinned by `PickupPhantomReconcileScenarios` +
      `HandlerPathVanillaWireScenarios`:
      (a) `MarkBlockDirty(controller)` walks the block's multiblock and resyncs
      every part *and* its block entity, so the "other structure cells stay
      phantom-air" finding was wrong as framed — the naive footprint-wide
      repair would have been redundant (G0 pins both halves: full coverage,
      nothing outside the two cells).
      (b) A wall sign is not part of any parent block's multiblock, so both
      pickup answers need explicit sign handling: the failure branch reconciles
      every candidate sign cell, while the success branch reconciles only the
      candidates the client predicted *and* the server did not take — a sign
      left behind is a cell the server never wrote, and a successful pickup
      emits no wire traffic of its own to piggyback on (G2 pins the empty case,
      G3 pins the "not the sign" predicate).
      (c) Claimed cells are reconciled on **both** vanilla channels, and a
      resynced block always arrives paired with a `BlockEntities` entry (G1
      pins that pairing, so a future block-only fix cannot pass).
      (d) The place-down spill branch must mark the **client-predicted**
      child cell (target + the estimated-rotation offset), not the actual
      child cell it found blocked: the client predicted at the estimate, so
      that is where its phantom lives. Reachable only through a rotation
      divergence (estimate 3 vs. actual 2 for a `chest-east` at yaw PI/2), and
      only on the success path (`placedAt == message.PlacedAt` means the
      `WrittenCells` reconcile never runs). Wire-code facts: the pickup denial
      code is the raw `FailureCodes.NoPermission` (`FailCarryAction` sends
      `failureCode` as the code and `pick-up-failed-no-permission` as the
      message key) alongside vanilla's
      `noprivilege-buildbreak-landclaimed`; `Cuboidi`'s max corner is
      **exclusive**. Residual follow-up: `OnPickUpMessage`'s early validation
      branches (malformed message, wrong slot, `cannot-interact`) still fail
      without scanning sign candidates, so a hacked or desynced client can keep
      a phantom sign there; the honest closures are a client-side permission
      filter on the local capture, or extending the scan to those branches.
      Rows: `PickupPhantomReconcileScenarios` (all 6) and the place-down spill
      row in `HandlerPathVanillaWireScenarios`.
 25. **A behavior remount is invisible to both client render-cache layers, so it
     needs a full invalidation — a better cache key would not fix it.** After a
     Mode B recondition the client swaps its `TransformTemplateManager` (correctly
     re-derived per call from the reconciled block registry), but two caches
     still hold materializations of the *old* manager: (a) the plan-cache key
     `CarrySignatureBuilder.BuildTransformPlanSignature` is `block code | BE
     variant type | transformsGroupBase | slot int | stack code | resolver |
     resolversig` and contains no behavior property and no manager identity, so
     every property a slot-level condition strip changes is outside the key; and
     (b) the per-slot sidecar memo in `GetRenderInfoCached` only recomputes on
     its own dirty inputs (plan null, `carriedRevision`, transforms group, stack
     code, `BlockEntityData` reference, missing variant signature) and a remount
     changes none of them — so it returns the pre-recondition `CachedTransformPlan`
     **without ever reaching the plan cache**. Two consequences, both
     non-obvious enough to record: the staleness outlives the 5-minute
     transform-plan TTL, because `PruneTransformPlans` evicts the dictionary key
     while a live sidecar keeps the object reference (so it is "until you drop and
     re-pick", not "5 minutes"); and fixing only the signature would still leave
     the sidecar short-circuiting. Hence `InvalidateRenderCaches()` from
     `RebuildClientDerivedState`, which clears both layers. If a *second*
     remount source ever appears, the alternative is a manager generation counter
     threaded into the signature **and** the sidecar dirty test. Rows:
     `ReconditionRenderCacheScenarios` (all 3),
      `CarryabilitySyncWiringTests.CarryabilitySync_InvalidatesClientRenderCaches`.
26. **The pickup footprint is the multiblock's own configured box, not a
    radius scan.** `CarryFootprint.GetFootprint` returns the target cell for a
    plain block; for a dummy it resolves the controller through `OffsetInv` and,
    when the controller carries a `BlockBehaviorMultiblock`, walks exactly that
    behavior's `IterateOverEach(origin, ...)` box plus the target cell — the same
    enumeration the engine uses to place the structure and to remove its dummies.
    Only a dummy whose controller has no `BlockBehaviorMultiblock` (old-style
    `dy`/`dx`/`dz` state structures with no behavior behind them) falls back to
    the old ±2 scan. Pins the defect: `game:jonas-poweramplifier-sideend-east` is
    1x4x3 with `dy=+3`, so a pickup judged over that row was missing the row's
    cells and G3's verdict came back `not-carryable` instead of `no-permission`.
    One walk keeps the pickup permission check and the engine's own write set in
    agreement, so no multiblock can be picked up under one footprint and placed
under another. Rows: `MultiblockFootprintScenarios` (all 3),
    `WideMultiblockClaimScenarios` -.
27. **The reload gate reads the boot latch, and the join hook is wired on that same
    latch — do not "fix" either back onto the live flag.** The review recommended
    keying the join-time snapshot send off the same *live* flag the reload gate
    read. Phase 0 established that this contradicts the documented "(requires
    restart)" contract in three places, so the recommendation was **rejected** and the
    latch won instead: `CarryOnConfigService.ReconditionOnConfigChangeEnabled` is
    seeded once at `StartPre` and never reassigned, `ReloadFromFile` tests *it* (not
    the reloaded file's value), and `StartServerSide` wires the `PlayerJoin` hook
    conditionally on it at boot. Both now agree, which was the finding's actual
    precondition. The file flag became **inert until restart** — a deliberate
    capability removal, not an oversight — and a mismatch logs a `Warning` naming
    both values instead of arming anything. Keying either side off `Config.…`
    would reintroduce a mid-session switch into a capability the operator never
    enabled for this session. Rows: `BootLatchedModeScenarios`, and
    `docs/technical-overview/config-initialization-and-flow.md` §5e states the
    decision at the gate. Manual restart-adoption check run by the operator
    2026-10-01 against the landed build.
28. **The differential warning names what is lost, not what is absent.**
    `WarnOnDroppedSections` reports a section only when the incoming file omits it
    **and** its applied value differs from a fresh `CarryOnConfig`'s — so a full
    config save logs nothing, and a legitimately minimal file that never configured
    those sections is not nagged. The report is "settings you changed are about to
    be lost", which is why a `DebuggingOptions`-only file names `Carryables` /
    `CarryOptions` but not the sections the operator had at defaults. Rows:
    `ConfigWatchdogScenarios` W15-W17. Do not "simplify" this to a plain
    file-key diff; that would warn on every full save and train operators to
    ignore the one signal that matters.
29. **Reach is validated once, server-side, from the actor's eye - and the
    boundary is verified from far outside, not to the block.** All six
    client-named-target paths call `CarryReach.IsWithinReach` before acting:
    block pickup, place-down, carryable-interact, ground-storage lift,
    dropped-carried-block entity pickup, and entity attach. The predicate
    measures from `Pos + LocalEyePos` (feet for non-player actors) to the
    **centre** of the target cell, or to the entity's `Vec3d`, against
    `CarryOptions.MaxInteractionDistance`, and exempts creative players
    *internally* - so no call site can forget the exemption, and a survival
    row cannot pass by accident. Place-down checks three cells (target, faced
    neighbour, cell above), because a build that starts in reach must not step
    out of it; the ground-storage row pins the `block-out-of-reach` failure
    code that replaced `cannot-interact` there, and the two entity rows pin
    the retained `entity-out-of-reach`. Do not "tidy" the anchor back to
    feet: a feet anchor measures from floor level and false-rejects blocks
    above eye level that the crosshair genuinely reaches. Do **not** add a
    boundary-precision row either - this validation is an anti-abuse measure
    against a modified client, not a precision mechanic, so the rows sit at
    `reach + 8` and nothing asserts a sub-block outcome. A planned
    metric-divergence row was cut for exactly that reason; its premise (that
    feet-vs-eye is observable behaviour) does not hold. Every in-reach
    pickup, place-down, interact, attach and lift already in the suite is the
    real false-reject guard, since all of them run through a gated path at
    ordinary distances. Rows: `HandlerPathReachScenarios` (7),
    `DroppedCarriedBlockPickupScenarios` (2),
    `VehicleAttachScenarios` (2), `GroundStorageBagFlows` (2) - 13 in all.
    Five were proven red-first by reverting the production change: the M9-style
    `HandlerPlaceDown_FarPosition_NearClaimedPlacedAt_Rejected` (Phase 1), and in
    Phase 3 the 3 creative widenings plus the ground-storage failure-code switch.
    The 2 survival entity rows are green-before-green by design, pinning that the
    pre-existing gates still answer after being retargeted; the Phase 1 creative
    rows are green-before-green for the same reason the Phase 3 ones were not -
     the exemption shipped with the gate.
30. **Per-wearer hidden-backpack override: entity resolvers downgrade, never
    upgrade, and the decision is memoized by a signal change token.** A
    registered `IEntityTransformGroupResolver` is consulted in registration
    order after the normal backpack resolution; the built-in
    `carryon:hidden-backpack` resolver downgrades the back-slot group to
    `backpack-none` only while the wearer's own cosmetic-mod state says the
    pack is hidden, so a per-wearer override beats the global
    `BackpackSelectionMode` mapping without turning compensation off for
    players who still show a pack. Three deliberate seams: Hide Gear's
    settings JSON is parsed locally (no assembly reference) and honors the
    PvP force-visible suppression; Combat Overhaul's read is one stable
    watched-attribute key regardless of which mod writes it (real Combat
    Overhaul or the legacy-compat layer vanity-slot forks depend on); Vanity
    is read through its public client API by a reflection scaffold that
    accepts both shipped API generations (`ShouldRenderGear` on 2.4.8,
    `ShouldRenderSlot` on 2.5.1+) by selecting the enum-typed overload and
    resolving `Back` by name, so the server hide-permission fold matches
    Vanity's own render decision. Hands never consults the signals; a signal
    returning a null change token opts the whole list out of memoization,
    and Vanity exposes no token, so it re-evaluates on a 250 ms TTL. Rows:
    `HiddenBackpackSignalScenarios` (5) and the two entity rows in
    `TransformResolverRegistryScenarios`.

## What the suite does not cover (known gaps)

- **Client-side logic** — the interaction state machine and prediction,
  interaction-help HUD, carried label/icon rendering, and the best-effort
  client claim check (`HasPermissionAtClient`) are not headless-executable.
  Client prediction is only exercised indirectly, via the server-side
  `WrittenCells` reconciliation rows and the sign-cell pickup reconciliation
  rows (Decision 24) — the client's *local* capture still applies no
  permission filter, which is what those rows repair from the server side.
- **Client render caches after a carryability recondition** — same reason,
  and called out separately because it is the one client-side gap with a
  *reachable* defect behind it. `CarryRenderCacheManager`'s transform-plan cache
  and its per-slot signature sidecars are never constructed on a dedicated
  server, so no row here can observe a stale pose. What the suite does pin: the
  **input** is reachable, on vanilla content, while the behavior stays present —
  `ReconditionRenderCacheScenarios` G1 (a `CarryablesOnBack.Barrel` flip strips
  the Back slot from a still-carryable barrel and the broadcast snapshot carries
  the strip) — and the **wiring** is correct, via
  `CarryabilitySyncWiringTests`. The behavioral half (does the carried block's
  pose actually change on the next frame, without a re-pick) is a manual
  real-client gate and is not covered here. G3 in that class exists so this
  paragraph does not have to be re-derived.
- **The client→server protobuf wire** — handlers are invoked directly; wire
  serialization is engine code (Atlas tier-1, parked upstream). The
  server→client vanilla wire, in contrast, is tapped directly
  (`VanillaClientWire` — see the vanilla-wire rows).
- **Seat/mount flows** — mounting is a vanilla client-driven flow and stays
  manual. Entity pickup, in contrast, is fully covered: the earlier "no
  manager entry point" parking was superseded by the finding that
  `OnPickupEntityMessage` is the same reflectable internal-message shape
  `HandlerInvoke` already wraps (`PickupEntityMessage` has a public ctor;
  `EntityCarriedBlock.SetCarriedBlockData` is public, giving full control of
  owner/grace/carried tree) — the six
  `DroppedCarriedBlockPickupScenarios` rows (pickup-access policy and
unreadable-adoption gate, both game-mode halves).
- **Attach/Detach/Put/Take/Dismount handler rows** — boat Attach/Detach and
  the vanilla anti-yank guard are covered (`VehicleAttachScenarios`, W4);
  Put/Take (example-mod racks) are covered by the sibling example-mods
  project (`RackEmUpScenarios`/`BarrelRackScenarios`, W5 — see Decisions
  17/18); attaching a carried **entity** (`slot-entity-not-attachable`) is
  covered there too (`EntityCarryAttachScenarios` — the CarryEntityDemo
  `demo-cube` is the in-kind entity-carried-block stand-in); Dismount
  remains unpinned (no example implementor).
- **The per-wearer hidden-backpack render offset** — the dispatcher consult
  that re-positions the carried block lives in the client render path, so the
  rows pin the resolution seam (manager + signals) while the visual outcome
  is the manual verification gate (two wearers, one hiding the pack, per
  hiding mod and per Vanity generation).
- **Reinforced blocks** (`ModSystemBlockReinforcement`) — same
  `HasPermissionAt` seam, different permission source; separate concern, not
  covered.
- **Multiple `carryon:carryable` behaviors on one block code** — the 
  resolver's multi-carryable merge cannot be exercised on vanilla content at
  all: `EnabledCondition` is read only from behavior JSON
  (`BlockBehaviorCarryable.ParseBaseSettings`), and vanilla ships at most one
  carryable per block code, so the resolver's single-candidate early return
  takes every vanilla block. The disabled-primary merge defect and its
  fix are covered in the sibling example-mods project instead, by
  `DisabledPrimaryMergeScenarios` – against the `CarryEntityDemo`
  fixture `carriedemo:disabledbase-overlay-block` (a condition-disabled
  non-override plus an unconditional `overrideExistingProperties` overlay — the
  composition of `conditional-only-block` and `overlaybase-block`). No control
  row is duplicated here, because a vanilla row could not reach the branch.
- **The client-side carryability sync handler** — `OnCarryabilityStateSync` is
  registered on the client channel inside `StartClientSide`, so it runs only in a real
  client process. Atlas's per-class world runs a dedicated server plus a
  `DummyTcpNetClient` that decodes the received byte stream and discards it: it never
  instantiates client mod systems and has no client block registry. Therefore
  `GetBlock`, `SyncCarryabilityBehavior` and `RebuildClientDerivedState` **execute in
  no row in this repository**, and an end-to-end row for it is impossible. What the
  suite pins instead: the poisoned **input** really does cross the mod channel
  (`CarryabilitySyncPayloadScenarios` W2, a snapshot whose `PropertiesJson` is
  `"{not json"` arrives verbatim), a healthy snapshot still arrives whole
  (W3, three entries, `null` `PropertiesJson` preserved as distinct from empty), and
  the fix's **structure** via
  `CarryabilitySyncWiringTests.CarryabilityStateSync_ContainsEveryEntry_AndAlwaysRebuildsDerivedState`
  (W1 — the per-entry body sits behind a catch that logs, and the derived-state
  rebuild is reached from a `finally` rather than merely called). Read the W1 row as
  *"the handler no longer aborts"*. **Never** as "client convergence" or "the client
  stays in sync": a broad enough `catch` satisfies W1, which is exactly why W3 exists,
  and neither row can see what the client does with the snapshot. The behavioral half
  is a manual real-client gate.
- **Cross-type packet order** — `player.Client.Packets<T>(channel)` captures per message
  type, so ordering *between* types is inexpressible: rows assert co-occurrence and
  within-type order only. The reload ordering that lands `CarryabilityStateMessage` after
  `ConfigSyncMessage` (`carryability-sync.md` §1) is therefore documented and reasoned,
  not pinned by a row.
- **A mod that puts a carryable on a multiblock dummy** — **closed by fixture** in
  the ExampleMods suite. `CarryEntityDemo`'s
  `resources/assets/carrieddemo/patches/multiblock-dummy-carryable.json` adds an
  unconditional Hands carryable to the vanilla `game:blocktypes/multiblock` dummy
  family, and `MultiblockDummyCarryableGuardScenarios` drives the guard's controller
  resolution through the **production** `Actions.PickUpIntoHands` path on a
  carryable dummy cell. That is the row this gap used to ask for, and it needs no
  `checkIsCarryable: false`: the patch makes the carryable gate pass on its own.
  The patch is deliberately pathological and broad — every vanilla dummy in that
  suite's world becomes carryable — because the fixture's subject is "does the
  guard resolve the controller when a dummy cell is carryable", and a narrower
  fixture would depend on more new machinery being correct. **It must never be
  copied into a real mod.**
  - Why it is not staged here: `[AtlasMods]` is assembly-level, so staging
    `CarryEntityDemo` into this suite would add its carryable content to every
    class-world and perturb the content-pinning rows (this suite's own census row
    treats "which codes report carryable" as pinned content). Both suites sharing
    that world is why the main suite staying green is itself the evidence that the
    fixture is inert here.
  - `BehaviorAutoMapper` still skips any candidate with no `entityclass`
    (`CarryablesFilters.AutoMapSimilar` cannot make a dummy carryable that way), so
    the patch is doing real work; Phase 0 measured the census growth at exactly the
    150 patched codes with no managed-set perturbation.
  - Row 9 stays the shipped-content statement and stays green. Row 10's
    `checkIsCarryable: false` seam also **stays**: this suite still cannot reach the
    window without it, and deleting it would remove the only evidence that runs in a
    world where vanilla content is untouched. The two rows are not redundant.
- **Not a CarryOn gap:** `BlockMultiblock.GetRotatedBlockCode` hard-codes
   `multiblock-monolithic` when rebuilding a rotated code, so a mod-owned multiblock
   type resolves its rotation lookups into vanilla codes. This is **an engine bug with
   no CarryOn exposure**, not a coverage hole in this mod: `GetRotatedBlockCode` appears
   nowhere in `CarryOn/src`, and the method's only two callers are
   `BlockEntityMicroBlock.RotateModel` and `GenRotatedMaterialIds`, both of which remap
   voxel **material ids for rendering**. Neither places a block, so the consequence is
   that a rotated MicroBlock renders the vanilla dummy's material - cosmetic. It is also
   silent rather than harmless, because the substituted code resolves to a real block
   and so skips both call sites' fallback to the unrotated id. Unreachable in vanilla:
   `game:blocktypes/multiblock` declares a single `type` state. Byte-identical from
   1.22.0 through 1.22.7, so long-standing rather than a regression.
   Pinned by `MultiblockRotationCodeScenarios` in the **ExampleMods** suite, which is
   where `CarryEntityDemo`'s non-`monolithic` `multiblock-carrydemo` type is staged;
   those rows are expected to fail once VS fixes the hard-code, and that failure is the
   intended signal. Triaged in
   the `2026-10-04-getrotatedblockcode-residual-triage` plan (internal tracker).
- **Race-window and timing rows** — the dupe/ghost-block plan's tiny-race and
  80%-progress rows, double `RestoreBlockEntityData` log-scrub assertions,
  chunk unload/reload behavior, and dedicated-server specifics remain
  manual-only.
- **Back-slot place-down via the handler** — place-down rows stay on
  `CarrySlot.Hands` (Back place-down requires empty hands); manager-path
  Back flows are covered in the swap/bookkeeping class.
- **CI on the Linux `vs_server` tarball**
  (separate follow-up); **faction/group claim variants** beyond
  owner/member/granted-player; **other mods' land systems** (Wilderness
  Survival etc.). (The former "config migration of third-party JSON without a
  valid `ConfigVersion`" gap is now closed by `ConfigMigrationScenarios`
  M111-M117/M121 — the flat/structured legacy shapes and the empty document all
  upgrade to the current version.)
