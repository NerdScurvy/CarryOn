using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Server.Logic.BehavioralConditions
{
    internal class MultipleBehaviorResolver
    {
        /// <summary>
        /// Resolves multiple carryable behaviors for blocks.
        /// <para>
        /// The resolver is config-independent: it decides <i>which</i> carryable survives on a
        /// block, and nothing else. <c>RemoveBaseCarryableBehaviour</c> used to be evaluated
        /// here, and only against <c>CollectibleBehaviors</c> - which left a matched block's
        /// base mounted in <c>BlockBehaviors</c>, so a filtered-out base stayed pickable. The
        /// filter is now a separate union-level pass
        /// (<see cref="BlockFilter.RemoveMatchedBaseCarryableBehaviors"/>) that runs after both
        /// automap passes, because only then are all the views a carryable can live in present
        /// and no further behavior can appear behind the resolver's back.
        /// </para>
        /// <para>
        /// Both views are swept with the same inputs and each view's own instances are removed
        /// by reference, so a decision taken on one view cannot leave a one-sided carryable
        /// behind. At this point in the pipeline a block's carryables are either one shared
        /// instance (engine- or patch-loaded, or a restore remount) referenced from both
        /// arrays, or a per-view pair of automap clones that resolve independently but
        /// identically because both were initialized from the same properties.
        /// </para>
    /// <para>
        /// A merge lands on a mount: the primary's properties are deep-cloned, merged
        /// with each overlay's, and initialized onto a fresh factory-made instance, and the
        /// primary and overlays are retired and marked dirty on the conditioning baseline so
        /// the next run re-mounts them pristine and re-merges. Nothing here mutates a
        /// baseline-owned object, and the merge is re-derived from pristine inputs every run
        /// instead of compounding onto a previously merged instance. Only where a merge lands
        /// changed - which behavior survives, and the non-merge selection, are untouched.
        /// </para>
        /// <para>
        /// Before resolving, the pass also emits the overlay <c>patchPriority</c> authoring
        /// lint (<see cref="WarnOverlaysWithoutPatchPriority"/>) - observational only, never
        /// a resolution input - because this is the one pass that sees every overlay
        /// pristine, before the merge retires it and before the automap clones its merged
        /// shape (see the lint's doc for why that ordering is load-bearing). Skipped merges
        /// are reported the same way (<see cref="WarnMergeSkipped"/>).
        /// </para>
        /// </summary>
        internal void Resolve(ICoreAPI api)
        {
            // One warning per block per run, not per view: the two views are swept
            // independently below and a patch-loaded carryable is one shared instance
            // mounted in both, so a per-view lint would double-report. Per-run, not
            // per-process, because a Mode-B recondition re-runs this pass and the
            // authoring trap is still worth naming then (same contract as
            // WarnOverlaysWithoutPatchPriority).
            var mergeSkippedWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var block in api.World.Blocks)
            {
                if (block.Code == null || block.Id == 0) continue;

                // Own every CarryOn-type behavior on this block before this pass can
                // merge or remove any of it. A block first resolved after boot (a mod
                // registering a second carryable mid-session) is recorded here, so its
                // behaviors are baseline entries the restore pass can re-mount - and the
                // merge can re-run from pristine - instead of becoming one-way removals.
                ConditioningBaseline.EnsureBaseline(block);

                // patchPriority lint: warn on the overlays that omit patchPriority before
                // this pass merges (and then judges) them.
                WarnOverlaysWithoutPatchPriority(block, api);

                var (blockBehaviors, blockMount) = ResolveView(block.BlockBehaviors, block, api, mergeSkippedWarned);
                block.BlockBehaviors = blockMount != null ? [.. blockBehaviors, blockMount] : blockBehaviors;

                var (collectibleBehaviors, collectibleMount) = ResolveView(block.CollectibleBehaviors, block, api, mergeSkippedWarned);
                block.CollectibleBehaviors = collectibleMount != null ? [.. collectibleBehaviors, collectibleMount] : collectibleBehaviors;
            }
        }

        /// <summary>
        /// Overlay <c>patchPriority</c> lint:
        /// warns once per offending behavior per run when a carryable declares
        /// <c>overrideExistingProperties</c> without an explicit <c>patchPriority</c> key,
        /// because such an overlay resolves at effective priority 0 - the merge still
        /// happens (<c>0 &gt;= 0</c>) but the documented ascending-priority overlay
        /// ordering cannot engage, and <c>"priority"</c> is not a recognized spelling, so
        /// the author gets no signal at all. The boundary is <b>missing vs declared</b>:
        /// an explicit <c>patchPriority: 0</c> is a declared value and stays silent.
        /// <para>
        /// Runs at this pass's start, before <see cref="ResolveView"/> merges and before
        /// the automap passes clone, so it only ever judges pristine patch-loaded
        /// instances: the merge mount and the automap clones are initialized from
        /// merged properties and inherit the overlay's
        /// <c>overrideExistingProperties</c> while possibly carrying no
        /// <c>patchPriority</c> key, so a lint after either pass would false-positive on
        /// the instances conditioning itself created. It also deliberately sits before
        /// the lone-carryable short-circuit, so an overlay with nothing to override
        /// still warns.
        /// </para>
        /// <para>
        /// Both views are swept reference-de-duplicated (the <c>UnionCarryables</c> shape),
        /// because a patch-loaded overlay is one shared instance mounted in both arrays -
        /// one warning per offending behavior, not one per view.
        /// </para>
        /// </summary>
        private static void WarnOverlaysWithoutPatchPriority(Block block, ICoreAPI api)
    {
        var seen = new List<BlockBehaviorCarryable>();
        LintView(block.BlockBehaviors);
        LintView(block.CollectibleBehaviors);

        void LintView<T>(T[] behaviors)
        {
            foreach (var carryable in behaviors.OfType<BlockBehaviorCarryable>())
            {
                if (seen.Any(c => ReferenceEquals(c, carryable))) continue;
                seen.Add(carryable);

                if (!carryable.OverrideExistingProperties
                    || carryable.Properties == null
                    || carryable.Properties.KeyExists("patchPriority")) continue;

                var message =
                    $"CarryOn: carryable overlay on '{block.Code}' sets "
                    + "overrideExistingProperties but declares no patchPriority; its "
                    + "effective priority is 0 and overlay merge ordering cannot "
                    + "engage. Declare patchPriority (an integer higher than the "
                    + "carryable being overridden).";
                if (carryable.Properties.KeyExists("priority"))
                {
                    message += " A 'priority' key was found, but the correct key is 'patchPriority'.";
                }
                api.Logger.Warning(message);
            }
        }
    }

/// <summary>
        /// Whether the behavior's <c>enabledCondition</c> currently evaluates to false
        /// against the world config. Condition-disabled behaviors are removed by
        /// <see cref="BlockFilter.RemoveDisabledConditionalBehaviors"/> in a final pass that
        /// runs <b>after</b> this resolver, so nothing selected here may depend on a disabled
        /// behavior - both as a merge anchor and as the property source a mount inherits from
        /// it.
        /// </summary>
        private static bool IsConditionDisabled(BlockBehaviorCarryable behavior, ICoreAPI api)
        {
            if (string.IsNullOrWhiteSpace(behavior.EnabledCondition)) return false;
            return !api.World.Config.EvaluateDotNotationLogic(api, behavior.EnabledCondition);
        }

        /// <summary>
        /// Resolves one view: removes the overridden carryables and returns the view's
        /// survivors plus the merge mount the caller must append (null when the view keeps
        /// an existing keeper, or has nothing to resolve). Returning the mount instead of
        /// adding it keeps this generic sweep independent of the two arrays' element types.
        /// </summary>
        private static (T[] Behaviors, BlockBehaviorCarryable? Mount) ResolveView<T>(T[] behaviors, Block block, ICoreAPI api, HashSet<string> mergeSkippedWarned)
        {
            if (behaviors.Length == 0) return (behaviors, null);

            var behaviorList = behaviors.ToList();
            var carryableList = behaviorList.OfType<BlockBehaviorCarryable>().ToList();

            // A lone carryable is the block's only view of itself, so there is nothing to
            // resolve - and whether it is a "base" the base filter must remove is a question
            // about the union of both views, which this pass deliberately does not answer.
            if (carryableList.Count <= 1)
            {
                return (behaviors, null);
            }

            // Resolve first (it mutates behaviorList), then snapshot the view - tuple
            // elements evaluate left to right, so returning the snapshot from inside the
            // tuple literal would capture the pre-removal list.
            var mount = ResolveMultipleCarryables(behaviorList, carryableList, block, api, mergeSkippedWarned);
            return (behaviorList.ToArray(), mount);
        }

        private static BlockBehaviorCarryable? ResolveMultipleCarryables<T>(
            List<T> behaviorList,
            List<BlockBehaviorCarryable> carryableList,
            Block block,
            ICoreAPI api,
            HashSet<string> mergeSkippedWarned)
        {
            var hasOverrides = carryableList.Any(c => c.OverrideExistingProperties);

            // The keeper is the highest-priority enabled candidate for a plain block, or -
            // when an override contributes properties - a mount this pass creates by merging
            // the primary with its overlays (the merge target is never a raw engine
            // instance, so a baseline-owned object is never mutated in place). The selection
            // itself is unchanged: an unqualified override (disabled, or below the primary's
            // priority) still loses to the primary.
            List<BlockBehaviorCarryable> mergeInputs = [];
            BlockBehaviorCarryable? keepBehavior = hasOverrides
                ? MergeOverridingCarryables(carryableList, mergeInputs, block, api, mergeSkippedWarned)
                : SelectHighestPriority(carryableList, api);

            // The survivor is by construction the highest-priority enabled candidate (or the
            // merge mount), so it always stays mounted: the priority-positive carve-out and
            // the priority-0 base removal both belong to the later union pass, which judges the
            // resolved result rather than this pass's input. An existing keeper is spared the
            // removal below and stays in this view's list; a merge mount is fresh, so every
            // carryable in this view is a merge input, is retired, and the mount is returned
            // for the caller to append.
            var isExistingKeeper = keepBehavior != null && carryableList.Remove(keepBehavior);
            if (isExistingKeeper)
            {
                behaviorList.RemoveAll(r => carryableList.Any(c => ReferenceEquals(r, c)));
            }
            else
            {
                behaviorList.RemoveAll(r => r is BlockBehaviorCarryable);
            }

            // A merge retires its inputs (the primary plus every overlay) and leaves a mount
            // in their place. The inputs are baseline-owned, so mark them dirty: the next
            // run's restore pass re-mounts them pristine and this pass re-merges - determinism
            // by convergence, never by compounding a merge onto a merged instance. The mount
            // itself is factory-made, so the next run's un-create sweep removes it and the
            // re-merge produces a fresh one.
            foreach (var input in mergeInputs)
            {
                ConditioningBaseline.MarkDirty(block, input);
            }

            return isExistingKeeper ? null : keepBehavior;
        }

        private static BlockBehaviorCarryable? SelectHighestPriority(List<BlockBehaviorCarryable> carryableList, ICoreAPI api)
        {
            // Prefer an enabled candidate over a condition-disabled one at the same or
            // higher priority; fall back to all candidates only when every one is disabled
            // (the final removal pass then strips the survivor).
            var enabled = carryableList.Where(c => !IsConditionDisabled(c, api)).ToList();
            var candidates = enabled.Count > 0 ? enabled : carryableList;
            var maxPriority = candidates.Max(m => m.PatchPriority);
            return candidates.FirstOrDefault(p => p.PatchPriority == maxPriority);
        }

        /// <summary>
        /// Returns the keeper for an override-contributing block: a mount carrying the
        /// primary's properties merged with every enabled overlay's, initialized onto a
        /// fresh <see cref="BlockBehaviorCarryable"/> created this run (the merge
        /// target is a mount, never a raw engine instance). The primary and the overlays
        /// are the retired inputs and are collected into <paramref name="mergeInputs"/>
        /// so the caller can mark them dirty on the conditioning baseline.
        /// <para>
        /// When no overlay qualifies (all disabled, or all below the primary's priority)
        /// there is nothing to merge, so the primary itself stays the keeper - this pass
        /// only changes where a merge lands, never which behavior survives.
        /// </para>
        /// <para>
        /// <b>The primary is never a condition-disabled behavior.</b> A mount inherits the
        /// primary's <c>enabledCondition</c>, so merging onto a disabled base hands the
        /// disabled condition to the mount and
        /// <see cref="BlockFilter.RemoveDisabledConditionalBehaviors"/> then strips that too
        /// - the block loses every carryable it declared, including overlays that were
        /// enabled. The selection is therefore partitioned rather than filtered: an enabled
        /// non-override is the only thing that may anchor a merge.
        /// </para>
        /// <para>
        /// With no enabled non-override there is no merge anchor, so the highest-priority
        /// enabled candidate survives <b>alone, unmerged, on its own properties</b>. That is
        /// not a new outcome: the pre-MR in-place merge could not fire onto an already
        /// filtered-out base either, leaving the overlay standalone. Choosing the survivor
        /// explicitly keeps it from being decided by list order and lets the skipped merge
        /// be reported (<see cref="WarnMergeSkipped"/>). When every candidate is disabled
        /// the final removal pass strips whichever one is chosen, so that case names the
        /// same highest-priority candidate rather than an order-dependent one.
        /// </para>
        /// </summary>
        private static BlockBehaviorCarryable? MergeOverridingCarryables(
            List<BlockBehaviorCarryable> carryableList,
            List<BlockBehaviorCarryable> mergeInputs,
            Block block,
            ICoreAPI api,
            HashSet<string> mergeSkippedWarned)
        {
            var ordered = carryableList.OrderBy(c => c.PatchPriority).ToList();

            // The only merge anchor: an enabled, non-override behavior.
            var primaryBehavior = ordered
                .Where(c => !c.OverrideExistingProperties && !IsConditionDisabled(c, api))
                .OrderByDescending(c => c.PatchPriority)
                .FirstOrDefault();

            if (primaryBehavior == null)
            {
                var survivor = SelectHighestPriority(ordered, api);

                // Every carryable this pass retires must be marked dirty here exactly as the
                // merge path does, or the baseline will not re-mount it and the block would
                // degenerate to a lone survivor: later passes would then early-return at
                // "one carryable", never re-evaluating the merge and never re-reporting the
                // skip. (The defective merge this replaces kept both behaviors alive by
                // accident - it marked its inputs dirty - which is why the healthy mirror
                // case still resolved before this branch existed.)
                if (survivor != null)
                {
                    mergeInputs.AddRange(ordered.Where(c => !ReferenceEquals(c, survivor)));
                }

                WarnMergeSkipped(block, api, mergeSkippedWarned, isFullyDisabled: survivor != null && IsConditionDisabled(survivor, api));
                return survivor;
            }

            var overlays = ordered
                .Where(c => c.OverrideExistingProperties
                    && c.PatchPriority >= primaryBehavior.PatchPriority
                    && !IsConditionDisabled(c, api))
                .ToList();

            if (overlays.Count == 0) return primaryBehavior;

            var merged = MergeCarryableProperties(primaryBehavior, overlays);

            var mount = new BlockBehaviorCarryable(block)
            {
                // The mount is factory-made, so the next run's un-create sweep retires it and
                // the re-merge produces a fresh one; the merge never re-applies to it.
                CreatedByConditioning = true
            };
            mount.Initialize(merged);

            mergeInputs.Add(primaryBehavior);
            mergeInputs.AddRange(overlays);

            return mount;
        }

        /// <summary>
        /// Reports an override-contributing block whose merge was skipped because no enabled
        /// non-override existed to anchor it, and whose surviving carryable therefore keeps
        /// its own declared properties instead of the merged result.
        /// <para>
        /// This is a silent-semantics trap rather than a lost feature: the block still works
        /// as a carryable, so nothing else in the pipeline reports it. The author asked for
        /// a patch that is not taking effect, and <c>patchPriority</c> or
        /// <c>overrideExistingProperties</c> - the two keys that read as if they did - are
        /// both present and both correct. The only truthful explanation is the condition on
        /// the base, so the warning names the block and says so.
        /// </para>
        /// <para>
        /// Once per block per run: the two views are swept separately and a patch-loaded
        /// carryable is one shared instance mounted in both, so a per-view warning would
        /// double-report. Deliberately <b>not</b> suppressed per process - a Mode B
        /// recondition re-runs this pass and the trap is still worth naming, on the same
        /// terms as <see cref="WarnOverlaysWithoutPatchPriority"/>.
        /// </para>
        /// </summary>
        private static void WarnMergeSkipped(Block block, ICoreAPI api, HashSet<string> mergeSkippedWarned, bool isFullyDisabled)
        {
            if (block.Code == null || !mergeSkippedWarned.Add(block.Code.ToString())) return;

            var outcome = isFullyDisabled
                ? "every declared carryable on it is condition-disabled, so the block will carry nothing at all until the config enables one"
                : "the surviving carryable keeps its own properties, so the overlay's are not applied";
            api.Logger.Warning(
                $"CarryOn: carryable merge skipped on '{block.Code}' - no enabled non-override "
                + "carryable exists to patch (its enabledCondition evaluates false against the "
                + $"current config), so {outcome}. If the overlay was meant to modify that "
                + "carryable, drop the condition from the base or set one that the config can enable.");
        }

        private static JsonObject MergeCarryableProperties(
            BlockBehaviorCarryable baseBehavior,
            List<BlockBehaviorCarryable> overlays)
        {
            var merged = baseBehavior?.Properties?.Token as JObject;
            merged = merged != null ? (JObject)merged.DeepClone() : new JObject();

            foreach (var overlay in overlays)
            {
                if (overlay?.Properties?.Token is JObject overlayObj)
                {
                    merged.Merge(overlayObj, new JsonMergeSettings
                    {
                        MergeArrayHandling = MergeArrayHandling.Replace,
                        MergeNullValueHandling = MergeNullValueHandling.Merge
                    });
                }
            }

            return new JsonObject(merged);
        }
    }
}
