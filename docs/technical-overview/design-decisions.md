# Design Decisions

A register of the decisions behind CarryOn's behavior that a maintainer might plausibly want to
change, and the constraint that forced each one.

**How to read this.** Each entry states a decision, the failure or constraint that produced it, and
what is deliberately *not* handled. Entries are numbered `DD-001` sequentially across the whole file,
so one sequence is greppable regardless of theme. Nothing here is a to-do list — if an item was
investigated and closed as a non-defect, it is in
[Considered and rejected](#considered-and-rejected) instead.

**Scope.** Only decisions that are still load-bearing and still plausibly revisitable. Anything
already implemented and documented in a
[technical overview](#technical-overview) document does not get an entry here; this register exists
to hold the *why*, not the *what*.

---

## Conditioning and convergence

Three decisions that only make sense together: they jointly define why a conditioning run is
reversible. See [Behavioral Conditioning](behavioral-conditioning.md) for the pass order itself.

### DD-001 - The pristine boot JSON is the baseline, and is never re-captured

**Decision.** Every CarryOn-type behavior a world boots with is captured once, as pristine
properties JSON, before any conditioning pass can mutate it. Every later run restores from that
capture rather than from current live state. The capture is never overwritten.

**Why.** Making every pass reversible requires a fixed starting point. Re-deriving authored
properties from the patch pipeline on every run would make the *patch pipeline*, not the world
boot, the source of truth — and every pass would then have to be re-audited against it. Treating
live properties as the new baseline instead would make each run's result depend on the previous
run's mutations, which is precisely the compounding this design removes.

**Accepted trade.** A post-boot write to a captured behavior's own properties is **reverted** by
the next run. This is classified restart-required-by-design, not an open defect: a world restart
re-reads every patch, so only in-session edits are lost. Re-deriving authored properties from the
patch pipeline is a separate project. See
[§3.4 of Behavioral Conditioning](behavioral-conditioning.md#34-why-a-live-properties-edit-is-reverted).

### DD-002 - Every pass owns a block before it mutates it

**Decision.** No conditioning pass may mutate a block whose behaviors the baseline does not yet
own. `ConditioningBaseline.EnsureBaseline` runs at every mutation site, including for blocks first
seen long after boot.

**Why.** Ownership is what lets the next run restore. Without it, a behavior registered mid-session
would be mutated without a baseline entry, and the restore pass would have nothing to put back —
making that edit permanently one-way even though every config edit is otherwise reversible.

**Accepted trade.** The baseline grows with anything the passes touch, and is reset per world rather
than per session. A mod that registers a large number of behaviors late pays capture cost it may
not need.

### DD-003 - The automap un-clone happens first, before any derivation

**Decision.** `AutoMapSimilar`, `AutoMatchIgnoreMods` and `AllowedShapeOnlyMatches` edits are made
reversible by un-creating every factory-made clone at the *start* of a run (pass 0), not by
special-casing them later. The work list of touched codes is monotonic for the life of the world.

**Why.** A clone is identified by its factory marker, so it can only be removed by a component that
knows it was made rather than loaded. If removal happened anywhere but first, a clone created by an
earlier run would be indistinguishable from an engine-loaded behavior to the passes in between, and
disabling auto-mapping would leave the clone in place.

**Accepted trade.** A code that stops being touched stays on the work list for the life of the
world, so the un-create sweep keeps checking it. Monotonicity is what lets a clone be found and
removed long after the config stopped re-creating it.

### DD-004 - An overlay never merges onto a condition-disabled primary

**Decision.** The resolver picks its merge base from *enabled* non-overlay behaviors, and excludes
condition-disabled overlays from the overlay set outright. The resolver is config-independent: it
decides which carryable survives and nothing else.

**Why.** A condition-disabled primary is removed from every view by the deferred strip, which runs
after the resolver. If an overlay merged onto it, the overlay's contribution would be silently
dropped at strip time — producing a survivor that *reported* the overlay's carried state while
staying un-carryable. A user would see a block configured un-carryable staying un-carryable for the
wrong reason, with the patch appearing to contribute nothing.

**Accepted trade.** A mod whose overlay relied on merging onto a disabled primary now gets nothing
at all, and must make its own behavior enabled. This is a behavior change for such mods rather than
a silent no-op.

---

## Carryability sync and config reload

One subsystem — the client half of `CarrySystem.Configuration.cs` — and the reason most of its
invariants exist. See [Carryability State Sync](carryability-sync.md) for the full contract.

### DD-005 - The reconditioning mode is a boot-session latch

**Decision.** `ReconditionOnConfigChange` is read once at `StartPre` and never re-derived. The
reload gate consults the captured value, not the reloaded file's own flag.

**Why.** The flag's own config tooltip says "requires restart", so the code has to enforce it. If
the gate re-read the incoming config, a mid-session file edit could change the *shape* of a running
session: a Mode A server would start sending snapshots it never prepared, and a Mode B server that
turned the flag off would silently stop re-syncing while its join hook and boot snapshot cache
stayed wired.

**Accepted trade.** Editing the flag in the config file mid-session does nothing except log a
warning naming both values and stating that the file value takes effect on the next server start.
The user's file is never rewritten to match.

### DD-006 - The sync handler must never wedge

**Decision.** Every snapshot entry is applied through its own try/catch, and the derived-state
rebuild runs from a `finally`. An entry whose block code is syntactically invalid is dropped;
one whose block is not registered yet is deferred and retried; anything else that throws costs only
that entry.

**Why.** The client cannot ask the server to resend. A throw that escapes the handler leaves the
client holding a transform template manager and render caches built from the pre-sync registry — a
silent, persistent wrong-render state with nothing in the log. One mod's malformed output must not
be able to do that.

**Accepted trade.** Partial application is possible by design: a snapshot where one entry throws
leaves that entry stale while the rest apply. The alternative — failing the whole snapshot — costs
every other mod's correct entry for one bad one.

### DD-007 - Both behavior-array shapes are supported, neither is normalized

**Decision.** A synced behavior may be one shared instance across `BlockBehaviors` and
`CollectibleBehaviors`, or a per-view pair. Removal is by exact reference, which handles both. The
shared-instance shape is deliberately left alone rather than split.

**Why.** The engine itself produces the shared shape for a patch-loaded behavior, and a conditioning
remount can produce it too, so the client mirrors inherit it. Splitting would defeat the
reference-identity removal: a shared instance retired once prevents the double-removal a per-view
clone would suffer, while a clone retires from its own view without leaving a half-reconciled twin.

**Accepted trade.** Two code shapes must stay supported, and `RemoveSyncedBehaviors` has to reason
about both. No defect was found that normalizing would fix.

### DD-008 - The client-side config sync does not set the reload-originated flag

**Decision.** `OnClientConfigSync` calls `Replace(reloaded)` with the default
`fromFileReload: false`.

**Why.** It is harmless *only* because the config-persist subscriber early-returns on the client
before any write. If a client-side persist path is ever introduced, this call site becomes a client
writing config, and the guard that stops a reload from clobbering the user's own file would not be
set on this path.

**Accepted trade.** This is a latent fragility, not a fix. Applying `fromFileReload: true` today
would change nothing observable and only add an unexplained flag. If you add a client-side persist
path, this is the line that must change with it.

---

## Claims and permissions

One subsystem — `CarryPickupService` / `CarryPermissionService` — and one security posture.
See [CarryManager and Handler Pipeline](carry-manager-and-handlers.md).

### DD-009 - The claim gate is identity-keyed, so non-player actors bypass it

**Decision.** Claim checks apply to player entities only. A non-player actor — a dropped item, a
projectile, a mod-owned entity — is restricted by reinforcement alone.

**Why.** `Claims.TryAccess` and `TestAccess` are keyed by `IPlayer`, so there is no claim identity to
evaluate for a non-player. "Deny unless permitted" degenerates to "always deny" because there is
nothing to look up, and defaulting the other way would let any non-player actor rebuild every
claim-protected block.

**Accepted trade.** An attachable-creature drop — a tamed creature detaching into a carried item and
reattaching — can land inside a foreign claim and rebuild the block there, because this branch
accepts whenever the cell is unreinforced. Claims do not stop it. Tightening it needs a non-player
identity source the engine's claim API does not provide. The real fix is a reach check on the
placement origin, tracked in [Known Issues](../known-issues.md). Changing this branch to deny-closed
would break every legitimate non-player placement path with no security gain.

### DD-010 - Pickup and placement share one footprint computation

**Decision.** `CarryFootprint.GetFootprint` is the single footprint source for both the pickup
claim gate and the placement write set, and its result is threaded into the attached-sign sweep
rather than recomputed.

**Why.** A shared computation means the pickup gate is exactly as strong as the placement gate it
mirrors — there is no second, weaker footprint implementation to drift. Threading it also matters for
cost: the function reads up to ~125 blocks for a wide structure, so recomputing it for the sign
sweep doubled the per-pickup cost.

**Accepted trade.** The pickup path pays a full footprint computation even when it ultimately
denies. Accepting the cost buys one implementation rather than two that must agree.

### DD-011 - The pickup-open guard runs before the carryable gate

**Decision.** The check for "another player has this container's inventory open" runs *above* the
carryable check, and resolves its controller through `CarryFootprint.ResolveController`.

**Why.** With the carryable gate first, a pickup aimed at a multiblock *dummy* died as
`NotCarryable` and the guard was never reached — so the ordering hid the bug entirely. And a
`BlockMultiblock` dummy is not a `BlockEntityContainer` at all, so resolving the raw clicked
position yields null and the guard can only fail open. Resolving the controller first means the
container checked is always the one whose GUI the player could actually have open.

**Accepted trade.** The guard runs for pickups that were going to be rejected as not-carryable
anyway, so it is on the hot path more often than strictly necessary.

---

## Client prediction and reconciliation

One subsystem, and one security property — the client's optimistic prediction must be cheap to
make and expensive to abuse. See
[CarryManager and Handler Pipeline](carry-manager-and-handlers.md).

### DD-012 - Reconciliation, not resync, is the repair channel

**Decision.** A denied pickup is answered over the same wire that delivers block entities, carrying
the **full cell set** to clear. The attached-sign sweep is taken before the pickup attempt, while
the parent block is still in the world.

**Decision detail that matters:** the client's local path has no permission filter, so it cleared
*every* candidate sign cell optimistically — including the ones the server's claim filter leaves
behind. Single-cell marks cannot repair that, because the client does not know which cells it got
wrong. The server therefore has to know the full candidate set. Taking the scan before the attempt
is what makes it possible at all: after a successful pickup the parent's cell is air and the
footprint would scan empty.

**Why.** A relog alone never fixed a stale client, because the engine's join payload is boot-era
state that no amount of reconnecting refreshes.

**Accepted trade.** Reconciliation costs a broadcast on every denial, and the sign sweep runs on
every server-side pickup to produce the candidate set it needs.

### DD-013 - Client reconcile keys on instance identity, never runtime type

**Decision.** Server-side conditioning strips and client-side reconciliation both remove a behavior
by reference or by its `CreatedByConditioning` marker — never by runtime type.

**Why.** Removal by type destroys coexistence. A block can legitimately carry an *unconditional*
base carryable alongside a *conditional* patch of the same kind; a type-wide filter removes both,
silently deleting the base. Identity-keyed removal leaves the unconditional sibling alone.

**Accepted trade.** Every mutation site must own and mark what it removes rather than filtering a
type, which is more bookkeeping than a type check — and a site that forgets to own its mutation is a
permanent, invisible one-way edit.

### DD-014 - The `WrittenCells` leash is anchored on the actor, not the message

**Decision.** Every client-supplied written cell is distance-checked from **both**
`player.Entity.Pos` (the server's own record of the actor) and the client-claimed `PlacedAt`, and
must pass both.

**Why.** The actor anchor is the security property. `message.Position` is client-supplied and is not
range-validated on this path, so anchoring only there would let a client name any cell as the origin
and then ask the server to dirty-mark cells anywhere in the world — bounded by the 64-cell count cap
and nothing else, because the origin-to-cell distance is whatever the client chose. Intersecting
both anchors is what keeps the count cap meaningful: without the actor anchor, the traffic cost is
`count × cell distance`, and that product is unbounded.

**Accepted trade.** This runs *before* the placement attempt, so the server's own computed
`placedAt` does not exist yet and the second anchor can only be the client-claimed one. A
post-placement re-verify against the server's `placedAt` is a worthwhile follow-up and is not
implemented.

---

### DD-015 - Reach validation is one handler-level predicate, anchored on the server's eye

**Decision.** Every carry path that acts on a client-named target validates reach through a single
shared predicate, `CarryReach.IsWithinReach`, called from the handlers themselves. Distance is
measured from the actor's **eye** position to the **centre** of the target cell, or to the target
entity's own `Vec3d`. Creative players are exempt *inside* the predicate rather than at each call
site. Three pre-existing gates - dropped-carried-block entity pickup, entity attach, and
ground-storage lift - were retargeted onto it rather than left with their own feet-anchored checks.

**Why handler-level, not service-level.** The three older gates sat inside services
(`CarryPickupService`, the attach behavior) and were invisible to anyone reading the handler. A
client-supplied target reaches those services only after the handler has already decided to act on
it, so a service-level check is both late for the security property and hidden from the place a
maintainer looks. Putting the gate in the handler makes the validation visible at the trust boundary
and lets every path share one rule. The cost is that a new handler must remember to call the
predicate; there is no type-level enforcement of that, and DD-014's actor-anchor argument applies to
this gate as much as to the `WrittenCells` leash - a reach check anchored on a client-supplied origin
would constrain nothing.

**Why the eye, and not the feet.** A feet anchor measures from floor level, which *understates* how
far a player can genuinely reach: standing close to a block and looking at a block face above eye
level, the target is comfortably reachable by crosshair but the feet-to-cell-centre distance can
exceed the limit. Anchoring on `LocalEyePos` removes that false reject. It is a small, deliberate
divergence from the three original feet-anchored gates, and it widens rather than narrows what
survival players may do - the gates it replaces were approximate in the permissive direction.

**Why the cell centre, and not the corner.** A cell-corner target is up to ~0.87 blocks nearer than
the cell the player is actually acting on, so a corner measurement is biased by approach direction -
it is stricter for some approach angles than others for no principled reason. The centre is the one
point every measurement of "this block" agrees on.

**Accepted trade.** The footprint is measured from the **primary** cell only. A multiblock whose
primary cell is in reach can still be carried by its edge cells, which can in principle put a cell
just outside the limit into the carry. Checking every footprint cell would make a large multiblock
harder to pick up than a small one, on grounds that have nothing to do with abuse, so the primary
cell is the deliberate unit. Sub-block precision at the boundary is *not* treated as a property worth
pinning: this validation is an anti-abuse measure against a modified client, so the boundary is
verified from far outside (rows sit at `reach + 8`) and no row asserts a specific sub-block outcome.

---

## Reload notices

The chat feedback channel for a reload, and why its two notice classes are treated
differently. See [Config Initialization and Flow](config-initialization-and-flow.md)
for the delivery paths themselves.

### DD-016 - Watcher reload success notices are per-event; only warning-class notices dedup

**Decision.** A watcher reload's `Config reloaded and applied` confirmation is
broadcast to every online `controlserver` holder on *every* reload event. Only
warning-class notices dedup — `sentChatNotices` records
`EnumChatType.CommandError` texts once per session — and the command path never
consults that set, so an explicit `/carryon-reload` always shows its notices to
the invoking player.

**Why.** The two classes answer different questions. A warning (a mode mismatch,
a behavior edit that waits for restart) recurs on *every* reload while its cause
persists, so an editor that saves the file repeatedly would repeat identical
warning lines to admin chat — per-session dedup is what bounds that. The applied
confirmation is the only per-event signal that a given file change landed and was
accepted; suppressing a repeat would tell an admin their latest save did nothing
even though it applied. Volume is bounded by construction: one confirmation per
completed reload — the trailing debounce collapses a rapid save burst into one —
never a poll or timer, and command reloads do not broadcast at all, so the
broadcast count tracks how often the config actually changes.

**Accepted trade.** On a busy multi-admin world, every online admin sees one
confirmation line per reload. If that ever reads as noise, the fallback is
origin-scoped dedup — one applied broadcast per distinct file digest — which
trades away the per-event "your save landed" signal for quiet. Reopen only if
real noise is reported.

---

## Considered and rejected

Investigated and closed. Recorded so they do not read as an implied to-do list.

**Normalizing the two client behavior shapes into per-view instances.** Rejected under
[DD-007](#dd-007--both-behavior-array-shapes-are-supported-neither-is-normalized): it breaks
reference-identity removal and no defect was found that it would fix.

**Re-deriving authored properties from the patch pipeline instead of restoring pristine JSON.**
Rejected under [DD-001](#dd-001--the-pristine-boot-json-is-the-baseline-and-is-never-re-captured):
it makes the patch pipeline the source of truth and would require re-auditing every pass.

**Keying the reload gate off the live config flag instead of the boot latch.** Rejected under
[DD-005](#dd-005--the-reconditioning-mode-is-a-boot-session-latch): it contradicts the documented
"requires restart" contract and lets a file edit reshape a running session.

**Catching the malformed-payload parse inside the behavior-mounting method.** Rejected: the caller
owns the `(code, kind)` context needed for a useful log, so catching lower down would either lose
that context or log the same failure twice.

**Deny-closed claim checks for non-player actors.** Rejected under
[DD-009](#dd-009--the-claim-gate-is-identity-keyed-so-non-player-actors-bypass-it): it breaks every
legitimate non-player placement path with no security gain.

---

## Technical overview

Where the *what* lives, for a decision whose mechanism you need to see:

- [Behavioral Conditioning](behavioral-conditioning.md) — pass order, the baseline, the resolver
- [Carryability State Sync](carryability-sync.md) — the client reconciliation contract
- [Config Initialization and Flow](config-initialization-and-flow.md) — load order, subscribers, the reload gate
- [CarryManager and Handler Pipeline](carry-manager-and-handlers.md) — pickup/place-down paths and their gates
