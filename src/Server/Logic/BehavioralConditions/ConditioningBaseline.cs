using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Common.Behaviors;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Server.Logic.BehavioralConditions
{
    /// <summary>
    /// The per-world conditioning baseline: every
    /// <see cref="BlockBehaviorCarryable"/> / <see cref="BlockBehaviorCarryableInteract"/>
    /// the world booted with, captured before any conditioning pass mutates it, keyed by
    /// block code. The baseline is what makes every later conditioning run a
    /// <b>convergence</b> to the boot surface instead of an incremental mutation:
    /// <see cref="RestoreBaseline"/> runs as pass 0 of
    /// <see cref="BehavioralConditioning.Init"/> and, on every run after the first,
    /// un-creates the instances CarryOn's factory constructed, re-mounts every dirty
    /// entry from its pristine JSON, and records any unmarked behavior a later
    /// registration appended - all before any pass can mutate a block. After pass 0 the
    /// surface equals a fresh boot of this world for everything the previous runs
    /// touched, and the six derivation passes re-derive the configured surface from
    /// there.
    /// <para>
/// Design invariants: the
        /// pristine JSON is never mutated and never re-captured from live state (it is the
        /// fresh-boot reference for this world, forever - the deliberate
        /// boot-pristine-wins trade that makes every pass reversible, at the cost of
        /// reverting a live properties edit; see
        /// <c>docs/technical-overview/behavioral-conditioning.md</c>);
    /// <see cref="EnsureBaseline"/> runs before any pass mutates a block;
    /// every mutation site calls <see cref="MarkDirty"/>; the
    /// <see cref="BlockBehaviorCarryable.CreatedByConditioning"/> marker is not
    /// serialized.
    /// </para>
    /// <para>
    /// The state is static and reset per world at the boot site
    /// (<see cref="ResetBaseline"/>, the same lifecycle hook the old pristine capture
    /// used): the baseline otherwise survives world boots in-process (world reloads,
    /// test hosts), and a stale baseline would restore a previous world's surface onto
    /// blocks this world never booted with.
    /// </para>
    /// <para>
    /// <b>Mode A holds no baseline, on purpose</b> (<see cref="CaptureEnabled"/>). Mode A
    /// runs conditioning exactly once per world, so there is no second run to converge
    /// from and a capture would be write-only dead state - its only reader,
    /// <c>ManagedCodes</c> -&gt; <c>BuildStateMessage</c>, is the Mode B boot-snapshot
    /// cache. So <see cref="RestoreBaseline"/> and <see cref="EnsureBaseline"/> are inert
    /// under Mode A rather than merely skipping the initial capture: skipping only the
    /// capture would leave <see cref="EnsureBaseline"/> free to repopulate late from
    /// already-mutated state, which is the hazard this design exists to avoid - a live
    /// properties edit being silently reverted.
    /// <para>
    /// This is a one-line invariant with a sharp edge: if Mode A ever gets a second
    /// conditioning run - a command, a chunk-load re-conditioning, a registry-ready
    /// re-init - the missing capture becomes a live one-way-edit bug, because nothing
    /// would restore the boot surface first. That run must either be rejected, or this
    /// gate must be revisited with it.
    /// </para>
    /// </para>
    /// </summary>
    internal static class ConditioningBaseline
    {
        /// <summary>
        /// Whether this world captures a baseline at all. Seeded once per world from the
        /// boot-time mode latch (<c>CarrySystem.AssetsFinalize</c>), never from a
        /// reloaded config - a mid-session file edit must not be able to turn the
        /// capture on or off.
        /// <para>
        /// Defaults to <c>true</c> so that forgetting the seeding call degrades to
        /// today's behaviour (a captured-but-unused baseline, which is inert) rather
        /// than to no baseline under Mode B, where convergence genuinely needs one.
        /// </para>
        /// </summary>
        private static bool captureEnabled = true;

        /// <summary>
        /// False in Mode A, where the baseline is deliberately not captured - see the
        /// type summary.
        /// </summary>
        internal static bool CaptureEnabled => captureEnabled;

        /// <summary>
        /// Seeds <see cref="CaptureEnabled"/> for this world. Called from the boot site
        /// that owns the mode latch, and nowhere else.
        /// </summary>
        internal static void SetCaptureEnabled(bool enabled)
            => captureEnabled = enabled;

        /// <summary>Where a baseline entry's behavior is restored from.</summary>
        internal enum RestoreSource
        {
            /// <summary>The entry's pristine JSON - mount a fresh instance from it.</summary>
            Json,

            /// <summary>
            /// The entry's recorded instance - the behavior was captured with a blank
            /// properties JSON (appended without <c>Initialize</c>), so the recorded
            /// object is the only restore source. Safe because such a behavior is never
            /// destructively mutated.
            /// </summary>
            InstanceReference
        }

        /// <summary>
        /// One boot-time CarryOn-type behavior of one block, with the views it was
        /// captured in (both set = one shared instance referenced from both arrays) and
        /// the pristine properties to remount it from. <see cref="Instance"/> tracks the
        /// live object that currently represents the entry - the engine's original, or
        /// the most recent remount - so a remount can remove the stale object
        /// <b>by reference</b> before appending its replacement, and a strip can find
        /// the entry it just mutated or removed.
        /// </summary>
        internal sealed class BaselineEntry
        {
            public required string BlockCode;
            public required string Kind;

            /// <summary>Whether the behavior was captured in <c>BlockBehaviors</c>.</summary>
            public required bool BlockSide;

            /// <summary>Whether the behavior was captured in <c>CollectibleBehaviors</c>.</summary>
            public required bool CollectibleSide;

            /// <summary>The properties JSON as captured, before any pass mutated the behavior.</summary>
            public required string PristineJson;

            public RestoreSource Restore;

            /// <summary>
            /// Set by any pass that mutates or removes the entry's live behavior, so the
            /// next run's <see cref="RestoreBaseline"/> remounts the entry from
            /// <see cref="PristineJson"/> before the passes re-derive.
            /// </summary>
            public bool Dirty;

            /// <summary>The live object that currently represents this entry.</summary>
            public required BlockBehavior Instance;
        }

        /// <summary>
        /// The per-world baseline, keyed by block code - one list per code because a
        /// block can boot with several CarryOn-type behaviors (a conditional base and
        /// an overlay patch, a per-view engine pair, a carryable and an interact).
        /// </summary>
        private static readonly Dictionary<string, List<BaselineEntry>> entries = new();

        /// <summary>
        /// The per-world work list: every block code any pass has marked dirty or the
        /// automapper has appended a clone to. All marked (factory-made) instances live
        /// on work-list codes by construction, so the un-create sweep only has to look
        /// here. Monotonic for the life of the world - a code that stops being touched
        /// stays listed, so a clone an earlier run created is still un-created after
        /// the config stops re-creating it - the un-create sweep is what makes that
        /// reversible.
        /// </summary>
        private static readonly HashSet<string> touchedCodes = new();

        /// <summary>Every code in the baseline, for the carryability snapshot's managed set.</summary>
        internal static IEnumerable<string> ManagedCodes => entries.Keys;

        /// <summary>
        /// Clears the baseline and the work list so the next pass re-captures from
        /// freshly loaded assets. Called once per world at the boot site, before the
        /// first <see cref="BehavioralConditioning.Init"/>.
        /// </summary>
        internal static void ResetBaseline()
        {
            entries.Clear();
            touchedCodes.Clear();
        }

        /// <summary>
        /// Pass 0. On the first run of a world, captures the baseline and returns -
        /// nothing has been mutated yet, so there is nothing to restore. On every
        /// later run, in this order: (1) <b>un-create</b> - remove every
        /// factory-constructed (marker-carrying) behavior from both views on the
        /// work-list codes, which is the un-clone mechanism that makes
        /// <c>AutoMapSimilar</c>, <c>AutoMatchIgnoreMods</c> and
        /// <c>AllowedShapeOnlyMatches</c> edits reversible; (2) <b>re-mount</b> - for
        /// every dirty entry, remove the entry's stale live object by reference and
        /// mount a fresh instance from the pristine JSON into the recorded views (one
        /// shared instance when both flags are set), or re-add the recorded object for
        /// an <see cref="RestoreSource.InstanceReference"/> entry, and clear the flag;
        /// (3) <b>extend</b> - <see cref="EnsureBaseline"/> every work-list block, so a
        /// behavior registered after the boot capture is owned before any pass can
        /// mutate it.
        /// <para>
        /// <paramref name="fullRestore"/> (the <c>ReconditionFullRestore</c>
        /// DebuggingOptions key, default false) treats every baseline entry as dirty -
        /// the full-restore fallback for live debugging and the convergence
        /// cross-check.
        /// </para>
        /// <para>
        /// Inert under Mode A: the gate above holds no baseline to restore, and capturing
        /// one would be dead state. Do not "fix" this by capturing without restoring - see
        /// the type summary for the hazard that invites.
        /// </para>
        /// </summary>
        internal static void RestoreBaseline(ICoreAPI api, bool fullRestore)
        {
            if (!CaptureEnabled) return;

            if (entries.Count == 0)
            {
                CaptureAll(api);
                return;
            }

            UnCreateMarkedInstances(api);
            RemountDirtyEntries(api, fullRestore);
            ExtendOverTouchedCodes(api);
        }

        /// <summary>
        /// Records every unmarked <see cref="BlockBehaviorCarryable"/> /
        /// <see cref="BlockBehaviorCarryableInteract"/> the block currently holds that
        /// the baseline does not know yet. Marker-carrying instances are factory-made
        /// (remounts, automap clones) - the un-create sweep owns them, never the
        /// baseline. Called at the boot capture for every block, and before any pass
        /// mutates a block it has not seen - the rule that makes post-boot
        /// registrations owned).
        /// </summary>
        internal static void EnsureBaseline(Block? block)
        {
            // Inert under Mode A - see the type summary. Returning here rather than only
            // skipping the boot capture is what stops a late call from repopulating the
            // baseline from already-mutated state.
            if (!CaptureEnabled) return;

            var code = block?.Code?.ToString();
            if (string.IsNullOrEmpty(code) || block == null) return;

            if (!entries.TryGetValue(code, out var list))
            {
                list = new List<BaselineEntry>();
                entries[code] = list;
            }

            foreach (var behavior in block.BlockBehaviors)
                RecordIfNew(code, list, behavior, blockSide: true);
            foreach (var behavior in block.CollectibleBehaviors)
                RecordIfNew(code, list, behavior, blockSide: false);
        }

        /// <summary>
        /// Marks the baseline entry that owns <paramref name="instance"/> dirty and
        /// adds the block to the work list - the one-line bookkeeping every mutation
        /// site owes. A factory-made instance (an automap clone) has no baseline
        /// entry by design: only the work-list addition applies, which is what lets a
        /// later run un-create it.
        /// </summary>
        internal static void MarkDirty(Block block, BlockBehavior instance)
        {
            var code = block?.Code?.ToString();
            if (string.IsNullOrEmpty(code)) return;

            touchedCodes.Add(code);
            if (!entries.TryGetValue(code, out var list)) return;

            var entry = list.FirstOrDefault(e => ReferenceEquals(e.Instance, instance));
            if (entry != null) entry.Dirty = true;
        }

        /// <summary>
        /// Adds a block code to the work list without marking an entry dirty - the
        /// automapper's appends: the clones it leaves behind are factory-made, so the
        /// next run's un-create sweep must be able to find them, but nothing was
        /// mutated that a remount has to restore.
        /// </summary>
        internal static void NoteTouched(string? blockCode)
        {
            if (!string.IsNullOrEmpty(blockCode)) touchedCodes.Add(blockCode);
        }

        private static void CaptureAll(ICoreAPI api)
        {
            foreach (var block in api.World.Blocks)
            {
                if (block?.Code == null) continue;
                EnsureBaseline(block);
            }
        }

        /// <summary>
        /// Records one behavior into the code's entry list when it is an unmarked
        /// CarryOn-type behavior the baseline does not hold yet. A behavior already
        /// recorded by reference (the shared instance seen from the other view) only
        /// completes its view flags - the pristine JSON, restore source and instance
        /// are captured once, at first sighting.
        /// </summary>
        private static void RecordIfNew(string code, List<BaselineEntry> list, CollectibleBehavior behavior, bool blockSide)
        {
            switch (behavior)
            {
                case BlockBehaviorCarryable carryable:
                    if (carryable.CreatedByConditioning) return;
                    RecordEntry(list, code, BlockBehaviorCarryable.Name, carryable, blockSide,
                        carryable.Properties?.Token?.ToString() ?? carryable.CurrentPropertiesJson);
                    return;
                case BlockBehaviorCarryableInteract interact:
                    if (interact.CreatedByConditioning) return;
                    RecordEntry(list, code, BlockBehaviorCarryableInteract.Name, interact, blockSide,
                        interact.CurrentPropertiesJson);
                    return;
            }
        }

        private static void RecordEntry(
            List<BaselineEntry> list, string code, string kind, BlockBehavior instance, bool blockSide, string? pristineJson)
        {
            var existing = list.FirstOrDefault(e => ReferenceEquals(e.Instance, instance));
            if (existing != null)
            {
                if (blockSide) existing.BlockSide = true;
                else existing.CollectibleSide = true;
                return;
            }

            list.Add(new BaselineEntry
            {
                BlockCode = code,
                Kind = kind,
                BlockSide = blockSide,
                CollectibleSide = !blockSide,
                PristineJson = pristineJson ?? string.Empty,
                Restore = string.IsNullOrWhiteSpace(pristineJson)
                    ? RestoreSource.InstanceReference
                    : RestoreSource.Json,
                Instance = instance
            });
        }

        private static void UnCreateMarkedInstances(ICoreAPI api)
        {
            foreach (var code in touchedCodes)
            {
                var block = api.World.GetBlock(new AssetLocation(code));
                if (block == null || block.Id == 0) continue;

                block.BlockBehaviors = RemoveMarked(block.BlockBehaviors);
                block.CollectibleBehaviors = RemoveMarked(block.CollectibleBehaviors);
            }
        }

        private static T[] RemoveMarked<T>(T[] behaviors) where T : CollectibleBehavior
            => behaviors.Where(b => !IsMarked(b)).ToArray();

        private static bool IsMarked(CollectibleBehavior behavior)
            => behavior switch
            {
                BlockBehaviorCarryable carryable => carryable.CreatedByConditioning,
                BlockBehaviorCarryableInteract interact => interact.CreatedByConditioning,
                _ => false
            };

        private static void RemountDirtyEntries(ICoreAPI api, bool fullRestore)
        {
            foreach (var (code, list) in entries)
            {
                if (list.Count == 0) continue;

                var block = api.World.GetBlock(new AssetLocation(code));
                if (block == null || block.Id == 0) continue;

                foreach (var entry in list)
                {
                    if (!entry.Dirty && !fullRestore) continue;

                    // Remove the entry's stale live object by reference from both
                    // views - a no-op when a strip already removed it, exact when it
                    // is still mounted (a prior run's remount, or the engine's own
                    // instance condition-processed in place).
                    block.BlockBehaviors = RemoveInstance(block.BlockBehaviors, entry.Instance);
                    block.CollectibleBehaviors = RemoveInstance(block.CollectibleBehaviors, entry.Instance);

                    if (entry.Restore == RestoreSource.InstanceReference)
                    {
                        if (entry.BlockSide) block.BlockBehaviors = [.. block.BlockBehaviors, entry.Instance];
                        if (entry.CollectibleSide) block.CollectibleBehaviors = [.. block.CollectibleBehaviors, entry.Instance];
                    }
                    else
                    {
                        var behavior = MountFromBaseline(block, entry);
                        if (entry.BlockSide) block.BlockBehaviors = [.. block.BlockBehaviors, behavior];
                        if (entry.CollectibleSide) block.CollectibleBehaviors = [.. block.CollectibleBehaviors, behavior];
                        entry.Instance = behavior;
                    }

                    entry.Dirty = false;
                    api.Logger.Debug($"CarryOn: Restored baseline {entry.Kind} behavior on {code}");
                }
            }
        }

        private static void ExtendOverTouchedCodes(ICoreAPI api)
        {
            foreach (var code in touchedCodes)
            {
                var block = api.World.GetBlock(new AssetLocation(code));
                if (block == null || block.Id == 0) continue;
                EnsureBaseline(block);
            }
        }

        /// <summary>
        /// Constructs a fresh behavior of the entry's kind from its pristine JSON. The
        /// JSON is parsed per call, so an entry restored into both views as one shared
        /// instance gets its own copy of the properties. The factory is one of the only
        /// two places the <see cref="BlockBehaviorCarryable.CreatedByConditioning"/>
        /// marker is set (the other is the automapper's appends).
        /// </summary>
        private static BlockBehavior MountFromBaseline(Block block, BaselineEntry entry)
        {
            var behavior = entry.Kind == BlockBehaviorCarryable.Name
                ? (BlockBehavior)new BlockBehaviorCarryable(block)
                : new BlockBehaviorCarryableInteract(block);
            behavior.Initialize(new JsonObject(JToken.Parse(entry.PristineJson)));
            MarkFactoryMade(behavior);
            return behavior;
        }

        private static void MarkFactoryMade(BlockBehavior behavior)
        {
            switch (behavior)
            {
                case BlockBehaviorCarryable carryable: carryable.CreatedByConditioning = true; return;
                case BlockBehaviorCarryableInteract interact: interact.CreatedByConditioning = true; return;
            }
        }

        private static BlockBehavior[] RemoveInstance(BlockBehavior[] behaviors, BlockBehavior instance)
            => behaviors.Where(b => !ReferenceEquals(b, instance)).ToArray();

        private static CollectibleBehavior[] RemoveInstance(CollectibleBehavior[] behaviors, BlockBehavior instance)
            => behaviors.Where(b => !ReferenceEquals(b, instance)).ToArray();
    }
}
