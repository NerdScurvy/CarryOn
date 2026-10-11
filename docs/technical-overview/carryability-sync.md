# Carryability State Sync

This document explains how the **server's decided carryability surface** reaches the **client's
local block registry** — what is sent, when, and exactly how the client reconciles onto it.

It is the companion to
[Behavioral Conditioning](behavioral-conditioning.md), which decides the surface, and to
[Config Initialization and Flow](config-initialization-and-flow.md) §5e–§5g, which covers the
config-broadcast side of the same reload. This document covers the client half, which is
implemented entirely in `src/CarrySystem.Configuration.cs`.

It reflects the current implementation:

- Two different packets cross the wire, and they are **not** interchangeable:
  `ConfigSyncMessage` carries the whole config and is sent in every mode; `CarryabilityStateMessage`
  carries only the post-conditioning result and is sent only in Mode B.
- Mode B is a **boot-session latch**, captured once at `StartPre`. A mid-session file edit cannot
  switch modes, and the reload gate reads the latch rather than the reloaded file's own flag.
- A snapshot is **full, not a delta**. Every managed code emits both a carryable and a
  carryable-interact entry every time, with properties JSON when present and `null` when stripped,
  so the client reconciles presence *and* absence for both kinds from one message.
- The client applies each entry **independently**. One malformed entry costs that entry, never the
  snapshot, and the handler cannot wedge the client into a half-synced state with no log.
- An entry whose block is not in the client registry yet is **deferred and retried**, not dropped
  forever — and is exempt from the shrink pass so its earlier mirror survives until it resolves.
- Two behavior-array shapes are supported **on purpose** and neither is normalized.

---

## 0. Scope

- `src/CarrySystem.Configuration.cs` — the whole client reconciliation path, plus the two
  server-side senders
- `src/Server/Logic/BehavioralConditions/BehavioralConditioning.cs` — `BuildStateMessage`
- `src/Common/Network/CarryabilityStateMessage.cs` — the wire shape
- `src/CarrySystem.Lifecycle.cs` — boot snapshot capture, join-hook wiring
- `src/Common/Logic/CarryOnConfigService.cs` — the latch field, the dirty gate
- `src/CarrySystem.cs` — the latch field itself

---

## 1. The two wires

CarryOn pushes two packets to clients, and conflating them is the usual source of confusion.

| | `ConfigSyncMessage` | `CarryabilityStateMessage` |
|---|---|---|
| Payload | the whole serialized `CarryOnConfig` | `BlockCode` + `Kind` + `PropertiesJson` per managed code |
| Sent when | every `OnConfigChanged` | a Mode B recondition passes the dirty gate, or a player joins in Mode B |
| Sent in mode | **A and B** | **B only** |
| Consumer | `OnClientConfigSync` | `OnCarryabilityStateSync` |
| Effect | the client adopts the server's config | the client adopts the server's conditioning *result* |

The config broadcast is unconditional because the client genuinely needs the config. The
carryability snapshot is not: the client never runs conditioning, so in Mode A there is nothing to
tell it beyond what the engine's own join payload already carried at boot.

**Why the result has to be sent at all.** A server-side conditioning pass mutates behaviors on the
*server's* world blocks. The client's copy of those blocks was transferred once at join, and a
config reload does not re-transfer it. Without the snapshot a Mode B edit changes what the server
accepts while the client keeps rendering and interacting against its stale copy. The engine's own
join payload cannot fix this — it is boot-era state and stays stale after every subsequent reload.

**Ordering matters when both fire.** A reload raises `OnConfigChanged` (config broadcast) and then
`OnBehavioralReconditioned` (carryability broadcast) in that order, so the snapshot lands last and
the client ends the reload on the correct surface. If that order ever inverted, a client would
briefly reconcile onto the previous surface. See
[Config Initialization and Flow](config-initialization-and-flow.md) §5f for the full subscriber
chain.

---

## 2. What counts as a managed code

A code is **managed** if the conditioning pipeline knows about it at all. The set is:

- every code the per-world conditioning baseline holds — the capture of every CarryOn-type behavior
  at world load, whether it is currently stripped or restored; **plus**
- every code currently carrying a conditional behavior, which additionally covers automapped twins
  because those first appear *after* the capture pass.

"Managed" therefore means *CarryOn has something to say about this code*, not *this code is
carryable*. Both directions must be expressible, which is why the shrink pass in §5.5 exists.

For each managed code the snapshot emits **exactly two entries** — one carryable, one
carryable-interact — so the two kinds are always reconciled together and a code can never end up
with a stale carryable but a fresh interact twin. `PropertiesJson` is the behavior's
`CurrentPropertiesJson`, normalized: whitespace-only is normalized to `null`, and `null` means
*absent after conditioning*.

Automapped interact twins are the history behind that claim. Until CarryOn `da8e117` the
automapper created them without `Initialize`, so their `CurrentPropertiesJson` was empty,
`Normalize` collapsed it to `null`, and the snapshot reported a functionally present server
behavior as *absent* — which told the client to mount no interact behavior for the code at all.
The automapper now copies the donor's properties onto each twin, so those interact entries carry
real JSON. The visible effect is that **the interact snapshot surface grows**: codes whose only
interact behavior is an automapped twin move from a `null` payload to a real one. That is the
intended fix and belongs in the release notes.

> A code that is unmanaged but still carries a behavior is out of scope by construction: the
> baseline never captured it and no conditional was found, so there is no conditioned result to
> report.

---

## 3. Mode B is a boot-session latch

`ReconditionOnConfigChange` is a **boot-time decision**. Its config tooltip says so, and the code
enforces it: the flag is read once in `StartPre` into `CarrySystem.reconditionOnConfigChange` and
seeded into `ConfigService.ReconditionOnConfigChangeEnabled`, which is the only value the reload
gate ever consults.

The gate in `ReloadFromFile` must read the **latch**, never the reloaded file's own flag. A
mid-session file edit that flipped the flag would otherwise change the *shape* of the session
mid-flight: a Mode A server would start sending snapshots it had never prepared, and a Mode B
server that turned the flag off would silently stop re-syncing while the join hook and boot
snapshot cache stayed wired. Re-deriving mode from the incoming config is the defect this design
prevents.

Under Mode A the latch being `false` is what leaves three things unwired for the whole session:

| | Mode A | Mode B |
|---|---|---|
| `PlayerJoin` snapshot hook | never wired | wired |
| boot snapshot cache (`carryabilitySnapshot`) | never built | built at `AssetsFinalize` |
| `AppliedBehaviorSignature` (dirty-gate baseline) | never seeded | seeded at `AssetsFinalize` |
| conditioning runs | once, at `AssetsFinalize` | once at boot, then per dirty reload |

The gate is on the latch rather than on "is Mode B" for the last three rows too: `Mode A` still
runs conditioning exactly once at boot, because that pass is what establishes the derived surface
for a fresh world — it is only the *reload-triggered* re-run that is gated off.

> **Mode A holds no conditioning baseline, by construction.** Baseline capture exists so a *second*
> run can converge from a known start; Mode A has no second run. If a future change gives Mode A one
> — a command, a chunk-load re-init — the missing capture becomes a live one-way-edit bug, and the
> `SetCaptureEnabled` gate must move with it. This is called out because the coupling is invisible
> from the call site.

---

## 4. The dirty gate

Mode B does not re-run conditioning on *every* reload. `ReloadFromFile` compares
`Config.ComputeBehaviorSurfaceSignature()` against the `AppliedBehaviorSignature` recorded after
the last successful pass, and re-runs only on a difference:

```csharp
if (reconditionOnConfigChange)                        // the boot latch
{
    var signature = reloaded.ComputeBehaviorSurface();
    if (AppliedBehaviorSignature == null
        || !string.Equals(AppliedBehaviorSignature, signature, StringComparison.Ordinal))
    {
        new BehavioralConditioning().Init(serverApi, Config);
        AppliedBehaviorSignature = signature;          // refreshed only after a successful pass
        OnBehavioralReconditioned?.Invoke(Config);     // -> rebuild + broadcast the snapshot
    }
}
```

`AppliedBehaviorSignature` is refreshed **only after a successful pass**, so a pass that throws
leaves the old signature in place and the next reload retries instead of treating the surface as
converged.

The signature covers only the **behavior surface** — the config fields that can change which blocks
end up carryable. Editing a field outside it (a color, a keybind, a render toggle) therefore costs
the config broadcast and nothing else: no pass, no snapshot. That is the point of hashing a
signature rather than diffing the config.

> The baseline for this comparison is seeded at `AssetsFinalize` from the same latch value, at the
> same boot site, so the two cannot disagree. Under Mode A the value would be write-only dead
> state — `AppliedBehaviorSignature`'s only reader is this compare, which is itself gated on the
> same latch.

---

## 5. The client reconciliation

`OnCarryabilityStateSync` is the whole client half. It must satisfy one hard requirement: **it must
not wedge.** The client cannot ask the server to resend, and a throw that escapes here leaves the
client holding a transform template manager and render caches built from the pre-sync registry —
a silent, persistent wrong-render state with nothing in the log.

**What the harness can and cannot pin.** The server half of this contract is asserted (managed-set
contents, payload normalization, the join and broadcast sends); the client half —
`SyncCarryabilityBehavior` and `RebuildClientDerivedState` — executes in no test in the
repository. Client reconciliation is therefore documented here, not machine-verified: the same
harness limit the `OnCarryabilityStateSync` hardening recorded when it closed, stated rather
than implied as converged.

### 5.1 Three client-side containers

| Field | Holds | Lifetime |
|---|---|---|
| `syncedCarryabilityCodes` | managed codes currently applied | until the code leaves the managed set |
| `pendingCarryabilityEntries` | entries whose block is not in the client registry yet | until they resolve |
| `syncedOwnedMirrors` | per code + kind, the mirror instances the client mounted | until removed by a later sync |

`syncedCarryabilityCodes` contains **only codes successfully applied in the current sync**. That is
what makes the shrink pass (§5.5) exact: absence from this set means "the last sync decided this
code is no longer managed".

`syncedOwnedMirrors` is keyed by *(code, kind)* and holds the **actual instances** the client
mounted, not a description of them. Removal is therefore by reference.

### 5.2 Per-entry isolation

Every entry is applied through `TryApplyEntry`, which catches per entry and logs the offending
`(BlockCode, kind)` before continuing. There are three distinct outcomes:

| Outcome | Cause | Retried? |
|---|---|---|
| **applied** | the block resolved and the behavior mounted | — |
| **deferred** | the block is not in the client registry yet | yes, on the next snapshot |
| **dropped** | the `BlockCode` is syntactically invalid | no — no retry can ever succeed |

The dropped case is a permanent authoring error: `new AssetLocation(code)` throws on a malformed
code, so retrying is pointless by construction. It is logged on every snapshot rather than once,
because it needs fixing and repetition is what makes it findable in a log.

The other throw vector is a malformed `PropertiesJson`, thrown by `JToken.Parse`. That one is
**deliberately not caught** in the method that parses it: `TryApplyEntry` owns the entry's
`(code, kind)` context for the log, so catching lower down would either lose that context or log
the same failure twice. The exception is caught one frame up, where the context still exists.

Derived-state rebuild runs from a `finally`, so the transform template manager and render caches
are never left built from the pre-sync registry even when an entry throws. That is the other half
of not wedging, and it is why the rebuild is not simply the last statement of the handler.

### 5.3 Deferral and retry

A deferred entry is **not skipped forever**:

- it is retried at the **head of the next snapshot**, before that snapshot's own entries, so a code
  that was merely *early* — its block had not finished registering when the snapshot landed —
  still applies even if the newer snapshot no longer mentions it;
- retry is **gated on snapshot arrival, not a timer**, so it can never spin;
- each entry logs its deferral **once** (deduped on `(BlockCode, Kind)`), so a permanently
  unresolvable code stays diagnosable without spamming every snapshot.

Two consequences of deferral are easy to get wrong, and both are load-bearing:

- **A deferred code does not enter `syncedCarryabilityCodes`.** It is not "applied", so it cannot
  claim to be managed-and-applied.
- **A deferred code is exempt from the shrink pass.** Its entry is still live; its block is simply
  absent from the client registry. Tearing down its mirror would drop the engine-loaded conditionals
  the entry has not yet replaced, and would force the eventual retry to re-mount from scratch for no
  gain.

So a code mounted by an earlier snapshot keeps its mirror tracked until its entry either resolves or
genuinely leaves the managed set. The retry, the exemption from `seen`, and the retention in
`syncedCarryabilityCodes` are a single invariant — removing any one of the three leaves orphaned
mirrors or lost behavior.

### 5.4 Two behavior shapes, deliberately not normalized

After reconciliation a synced behavior may appear in `BlockBehaviors` and `CollectibleBehaviors` as:

- **one shared instance** — the engine's own shape for a patch-loaded behavior, and the shape a
  pass-0 remount can produce; or
- **per-view clones**.

The mirror records whichever shape it mounted, and `RemoveSyncedBehaviorsOfKind` removes by **exact
reference**, so both reconcile correctly.

Neither shape is normalized, and the shared-instance case is left alone rather than split. The
reason is that `RemoveSyncedBehaviors` is written to handle both, and splitting would defeat it: a
shared instance retired once prevents the double-removal that a per-view clone would otherwise
suffer, while a clone is retired from its own view without leaving a half-reconciled twin.
Normalizing to per-view instances would break the reference-identity removal and fix no observed
defect. Test-side counterpart: `CarryOnParityAssert.SharedInstance`.

Removal is by reference **or by the same predicate the server's deferred strip uses** —
`IsConditionalBehavior`, never runtime type — so an unconditional sibling of the same kind always
survives. A sync must never be able to delete a behavior the server never conditioned.

### 5.5 The shrink pass

A full snapshot means codes that *left* the managed set are simply absent. Anything previously
applied and not in the current snapshot is therefore retired:

1. every code in the previous `syncedCarryabilityCodes` that is neither in `seen` nor in
   `PendingCodes()` has its mirrors removed and its mirror record dropped;
2. `syncedCarryabilityCodes` is then replaced wholesale with `seen`;
3. codes that are still pending but were previously synced are re-added, so step 2 does not drop a
   code that is merely waiting on its block (see §5.3).

Without step 3 the shrink pass would tear down a deferred code's mirror *and* forget it was
tracking it, leaving the eventual retry to start from nothing.

The transform template manager is rebuilt **from the reconciled registry**, and the render caches
are **fully invalidated** afterwards. Full invalidation rather than a signature check is required
because both cache layers are blind to a remounted behavior: the plan-cache key omits every
behavior property and the manager identity, and the per-slot sidecar memo bypasses the plan cache
unless one of its own dirty inputs changed. A remounted behavior is therefore invisible to any
keyed check, and only a blanket invalidation catches it.

The interaction-help HUD reflects the registry on the next help query, so it needs no explicit
refresh here.

---

## 6. Why a snapshot has no deltas

The snapshot is full rather than incremental, which costs bandwidth and buys two properties worth
more:

- **Reconciliation is idempotent.** Re-applying an identical snapshot produces identical state, so a
  duplicated or reordered packet cannot corrupt anything.
- **Absence is expressible.** A delta protocol has to distinguish "unchanged" from "removed"; a full
  snapshot states removal by omission. Since the disable direction is the one that was historically
  broken — a relog alone never fixed it — making removal cheap and unambiguous is worth more than
  the bytes saved.

The payload is minimal for the same reason: the client needs a *replacement behavior*, so it needs
the post-condition properties JSON and nothing else. It does not need to know which pass produced
it, what the config was, or how many candidates the resolver weighed.

---

## 7. Stated fragilities

These are known and accepted, not defects to fix opportunistically. Each records what would have to
change if the assumption behind it stopped holding.

**A client-side config sync calls `Replace(reloaded)` with the default `fromFileReload: false`.**
Harmless **only** because the persist subscriber early-returns on the client (`if (ServerApi == null)
return;` comes before any write). If a client-side persist path is ever introduced, this call site
becomes a client writing config, and the `IsReloadOriginated` guard that protects the server's
file from being clobbered by its own reload would not be set on this path. The fix would be
`Replace(reloaded, fromFileReload: true)` here; it is not applied today because applying it with no
persist path behind it would change nothing and only add an unexplained flag.

**A deferred code that never resolves stays out of sync on the client indefinitely.** It logs once
per entry and is retried on every snapshot. If its block never registers locally, the client
silently lacks that carryable for the rest of the session — there is no timeout, because a code
that is merely slow to register must not be given up on.

---

## See Also

- [Behavioral Conditioning](behavioral-conditioning.md) — how the server-side surface is decided,
  and why a pass re-run from the baseline is reversible.
- [Config Initialization and Flow](config-initialization-and-flow.md) — the config-broadcast side:
  the subscriber chain (§5f), the reorder guarantee (§5g), and the Mode A/B gate (§5e).
- [CarryOnConfig.json Reference](../carryon-config.md) — the config surface that feeds this
  pipeline, including which edits make the behavior signature dirty.
