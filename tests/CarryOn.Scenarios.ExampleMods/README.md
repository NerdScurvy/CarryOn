# CarryOn Example-Mods Atlas Suite

This project is the end-to-end migration verification for
`ICarryableBlockSlotHandler` — the put/take surface the release notes
describe. It exists separately from the main scenario suite
(`..\CarryOn.Scenarios\`) because `[AtlasMods]` is assembly-level: staging
the example mods (and their carryable content) into the main suite would
perturb its content-pinning rows. It shares the main suite's harness sources
(`CarryOnScenarioBase`, `CarryActions`, `HandlerInvoke`, `VanillaClientWire`)
via `Compile` links; everything else is identical mechanics (real headless
server, simulated players, class-per-world boots). Rows join players through
the shared base's `JoinPlayer` wrapper, so like the main suite every row runs
with players in **forced survival** (Atlas's default creative hides real
divergences — see Decision 19 in the main suite's README); RackEmUp's and
BarrelRackDemo's transfer paths carry no game-mode branches, which the
post-flip green runs confirm.

- **Coverage index:** [INVENTORY.md](INVENTORY.md) — generated from the test
  sources (class list, row names, fixture wiring, totals). Do not edit by
  hand; regenerate from the repo root with
  `pwsh -File scripts\generate-test-inventory.ps1`.
  Its `-Verify` switch fails when the committed file has drifted.
- **Row narrative:** per-row intent, red-first evidence, and premise/control
  caveats are hand-written prose maintained in the maintainer's private
  working notes, not in this repo. This README keeps the durable parts: how
  to run the suite, the behavior decisions the rows encode, and the known
  gaps.
- **Trait annotations:** every class and row here carries the same xUnit
  `[Trait]` vocabulary as the main suite (`area` here is always
  `Example mods`) — see "Trait annotations" in
  `..\CarryOn.Scenarios\README.md` for the table and the authoring rules
  (one physical line per trait; `-Verify` fails on a missing class
  `area`/`summary` or row `summary`).

## Running the suite

```powershell
pwsh -File run-tests.ps1
pwsh -File run-tests.ps1 -Filter "FullyQualifiedName~MultiCarryableCoexistenceScenarios"
pwsh -File run-tests.ps1 -Filter "FullyQualifiedName~RackEmUpScenarios"
```

Preconditions (enforced, fail-fast): `VINTAGE_STORY` points at a Vintage
Story install (e.g. `C:\VintageStory\1.22.6`); `VINTAGE_STORY_DATA` points at
a data root whose `CarryOnDev\Mods\` contains exactly one `foodshelves*.zip`
(the third-party dependency, staged by the project's `StageFoodShelves` build
target as `foodshelves.zip` beside the test assembly); and the CarryOnLib,
RackEmUp, BarrelRackDemo, and CarryEntityDemo checkouts are siblings of this
repo. The script Release-builds all five mods (CarryOnLib, CarryOn, RackEmUp,
BarrelRackDemo, CarryEntityDemo — warnings are errors) and then runs
`dotnet test`.

Staged mods (AssemblyInfo `AtlasMods`, resolved relative to the test
assembly's output — this project is a sibling of `CarryOn.Scenarios`, so the
`..` counts match the main suite's): `bin/CarryOn.zip` (5×`..`),
`CarryOnLib/RackEmUp/BarrelRackDemo/CarryEntityDemo` zips (6×`..`), and
`foodshelves.zip`. The plan's spec said four zips; BarrelRackDemo's real-world
dependency on foodshelves, and CarryEntityDemo's entity-pickup fixture (added
with `EntityCarryAttachScenarios`), make it six — the deviations are recorded
in the plans' status logs.

## Behavior decisions encoded by the rows

1. **The seed mirrors production, not shipped defaults.** The rows seed
   `Carryables.Mold` **and** `Carryables.MoldRack` true (both default false).
   CarryOn's own mold-rack patch gates the rack's carryable behavior on
   `MoldRack`; RackEmUp's ungated override entry cannot escape that gate (the
   `MultipleBehaviorResolver` merge keeps the base entry's
   `enabledCondition`), so with shipped defaults the rack has no carryable
   behavior and put/take silently no-op — no client error at all. The dev
   world config enables both, which is the production setup the rows verify.
   Full analysis: Decision 18 in `..\CarryOn.Scenarios\README.md`.
2. **Rejection observables follow the failure-code contract.** Occupied and
   incompatible puts produce mod-specific failure codes → IngameError toasts
   on the vanilla wire (asserted via `ErrorsWithCode`); the empty-slot take
   produces `FailureCodes.Default` → nothing is sent (asserted via
   `Assert.Empty(wire.Errors)`). A transfer target that cannot be *resolved*
   (the ungated-gate case above) also sends nothing — an observability gap
   the rows document rather than paper over.
3. **`OnClientTransferToggled` stays unpinned** (client-side prediction hook;
   same manual-only class as the client state machine in the main suite's
   gaps section).
4. **The demo-cube is the in-kind entity-carry stand-in.** No shipping example
   mod implements the entity-pickup surface, so `EntityCarryAttachScenarios`
   stages CarryEntityDemo, whose `demo-cube` is picked up through the real
   `ICarryableEntityPickupHandler`/`ICarryEntitySpawnerFactory` path to yield
   exactly the entity-carried-block shape the rejection keys on (`BlockEntityData
   == null`, `SpawnerFactoryCode != null`). It is not a felled-log mod: the
   production semantics under test (refuse to store an entity-carried block in
   an attachable slot) are entity-type-independent, so the cube is sufficient
   and keeps the suite free of environment-dependent fixtures.
5. **Attachable-entity slots vs. block-rack slots are different paths.**
   `slot-entity-not-attachable` fires only on the **attach-into-entity-slot**
   path (`EntityBehaviorAttachableCarryable.CanAttach`), which is what the
   cargo-boat rows pin. A block-hosted rack taking an entity-carried block
   through `ICarryableBlockSlotHandler` is a genuinely separate surface (and
   one that currently drops `SpawnerFactoryCode` at the `TryPutCarryable`
   boundary) — out of scope here, noted as a future follow-up if a real
   log-rack-style mod surfaces it.
6. **The red-first rows were the Phase-2 acceptance contract, and it was met
   with no test-row changes.** Pre-fix, the flatten defect made the flatten
   irreversible within a booted world (a once-removed behavior was never
   resurrected by a reload), so the RED rows' exact asserts (`patchPriority`
   survivor, Back-slot pickup, carryable-after-reload) were written to flip
   green with **no test-row changes** when the fix landed — which they did
   (verified alongside the main suite). The premise probe is isolated in its
   own class because a pristine boot-disabled state cannot be held in the
   flipped class-world; both classes' worlds are same-flag fixtures so this
   does not perturb the other classes.
7. **The condition key is an existing carryable, not a demo-only one.** The
   experiment blocks gate on `carryon.Carryables.Chest` — a shipped key with
   vanilla-mediated semantics — rather than a demo-only config key, so the rows
   exercise the real conditioning/config pipeline end-to-end with zero
   `CarryOnConfig` model changes.
8. **The straddle fixture authors its dummies in the `game:` domain, and its
   sign assertion is written against the engine's own pick.** A mod cannot add
   blocktypes to the vanilla `game:` domain, so the dummy family
   (`game:multiblock-carrydemo-p1-0-0` … `p3-0-0`) lives there in
   CarryEntityDemo and the controller stays in `carrieddemo:`. Separately,
   `VSSurvivalMod.BlockSign.OnPickBlock` rewrites a wall sign's code to
   `ground-north`/`wall-north` (pre-existing vanilla behavior, not a CarryOn
   defect), so it does not assert a placed block code: it asserts against
   `SafeOnPickBlock(...)` and pins `OriginalLocalFace == BlockFacing.WEST`,
   which is the axis the claim/footprint defect actually turned on. The
   behavior-driven footprint itself is main-suite Decision 26.

## Known gaps

- **The five `MultiblockDummyCarryableGuardScenarios` rows are swept cross-version:
  green on every supported install, no drift found.** The first full sweep
  (2026-10-04/05, `tests/CarryOn.Scenarios/run-versions.ps1` with `-Suite`) ran this
  suite on all eight supported installs, 1.22.0–1.22.7 — 47/47 on every one,
  alongside the main suite's 219/219 — so the guard's resolution now has
  cross-version evidence on disk (the suite-suffixed TRX files under the main
  suite's `TestResults\`). Every claim these rows rest on remains engine-owned
  (that `game:blocktypes/multiblock` has no `behaviors` key at all, that
  `trunk-south` writes exactly one dummy at `dx = -1`, that
  `BlockMultiblock.OnBlockRemoved` leaves the controller standing, and that
  `BlockMultiblock` overrides `OnPickBlock` to route a dummy to its controller):
  green today on every install, and *watched* on every install going forward —
  the sweep's per-suite `decompilePinned` list classifies these rows (plus
  `MultiCarryableBasePremiseScenarios`, `RackEmUpScenarios`, and the two
  `MultiblockRotationCodeScenarios` rows) as engine-owned, so a future install
  where the engine moves reports as vanilla drift, not a mod regression. Scope and
  classification rules:
  the `2026-10-04-version-sweep-both-suites-and-compile-pass` plan (internal tracker).
- The attach-a-carried-entity surface (`slot-entity-not-attachable`) is now
  covered by `EntityCarryAttachScenarios`; **Dismount** still has no shipping
  example implementor and stays unpinned (see the main suite's README).
- The client mirror is not assertable here: the Atlas harness boots a
  server only, so the client handlers (`OnCarryabilityStateSync`,
  `SyncCarryabilityBehavior`) never run. Phase 3 landed the client-side fix as
  code + reasoning: the sync now removes only what the client owns — its
  earlier snapshot mirrors (by reference) plus the engine-loaded conditional
  behaviors (by the same predicate the server's deferred strip uses) — never
  type-wide, so an unconditional base survives client-side reconciliation too,
  and a snapshot whose surviving behavior is unconditional mounts nothing (the
  engine base already provides it; the client runs no resolver to collapse
  duplicates). Residuals, by protocol design: the snapshot expresses only the
  conditional per-kind state, so a config-**excluded** coexistence block's
  unconditional base (removed server-side by the intentional type-wide
  exclusion pass) can linger client-side until reconnect, and the skip-mount
  assumes client/server assets match. The server-side coexistence pinned by
  – (plus the green control rows) remains the assertable contract.
- The foodshelves staging depends on the dev data root's zip; a CI recipe
  (pinned foodshelves version) is a separate follow-up, same as the main
  suite's Linux `vs_server` CI gap.
- **`BlockMultiblock.GetRotatedBlockCode` is pinned here, and it is an engine bug
  rather than a CarryOn gap.** `MultiblockRotationCodeScenarios` measures it against
  this suite's own non-`monolithic` family,
  `game:multiblock-carrydemo-p1-0-0`: angle `0` returns the block's own code, while
  `90`/`180`/`270` return `game:multiblock-monolithic-*` because the engine rebuilds
  the path from a literal `"multiblock-monolithic"`, keeping only the domain. The pin
  lives in this suite because `CarryEntityDemo`'s `multiblock-carrydemo` type is
  staged only here, and the main suite must not see it. **The rows assert a defect:**
  they are expected to fail once VS derives the type segment from the block's own
  code path, and that failure is the intended signal - do not loosen the assertion to
  make them green.
  The consequence is cosmetic, not a placement failure: the method's only two callers
  are `BlockEntityMicroBlock.RotateModel` and `GenRotatedMaterialIds`, both remapping
  voxel *material ids for rendering*, so a rotated MicroBlock renders the vanilla
  dummy's material. It is silent rather than harmless, because the substituted code
  resolves to a real block and skips both call sites' fallback to the unrotated id.
  Unreachable in vanilla (`game:blocktypes/multiblock` declares one `type` state) and
  byte-identical from 1.22.0 to 1.22.7. Triaged in
  the `2026-10-04-getrotatedblockcode-residual-triage` plan (internal tracker).
