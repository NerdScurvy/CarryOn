using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Common;

namespace CarryOn.Server.Logic.BehavioralConditions
{
    internal class BlockFilter
    {
        private readonly CarryOnConfig config;

        internal BlockFilter(CarryOnConfig config)
        {
            this.config = config;
        }

        /// <summary>
        /// Removes conditional behaviors from blocks that are not enabled by the
        /// EnabledCondition - **by instance**, so a condition-disabled behavior's
        /// unconditional (or differently-conditioned) sibling of the same type survives.
        /// <para>
        /// The removal is mirrored to <c>CollectibleBehaviors</c> by
        /// <see cref="RemoveBehaviorAcrossViews"/> rather than by plain reference equality,
        /// because the two instance classes behave differently: a patch-loaded or remounted
        /// behavior is one object referenced from both arrays (reference removal is exact), while
        /// an automap clone is built once per view, so the collectible-side call has to resolve
        /// the twin. Reference equality alone silently no-op'd on the twin and left a
        /// condition-disabled block carryable through that view, since <c>IsCarryable()</c> is
        /// an OR over both arrays.
        /// </para>
        /// <para>
        /// Empty/whitespace <c>EnabledCondition</c> means "no condition" (never removed
        /// here, matching the resolver's <c>IsConditionDisabled</c> guard), which removes
        /// the pass's dependency on how the engine evaluates an empty condition string.
        /// </para>
        /// </summary>
        internal void RemoveDisabledConditionalBehaviors(ICoreAPI api)
        {
            if (api.World == null || api.World.Config == null)
            {
                api.Logger.Error("CarryOn: World or World.Config is null in RemoveDisabledConditionalBehaviors");
                return;
            }

            var worldConfig = api.World.Config;

            foreach (var block in api.World.Blocks.Where(b => b.BlockBehaviors.Any(beh => beh is IConditionalBlockBehavior) == true))
            {
                // Own everything this pass can mutate before it mutates it - a
                // behavior registered after the boot capture becomes a baseline entry
                // here, not a one-way strip.
                ConditioningBaseline.EnsureBaseline(block);

                var conditionalBehaviors = block.BlockBehaviors.OfType<IConditionalBlockBehavior>().ToList();

                foreach (var behavior in conditionalBehaviors)
                {
                    if (!string.IsNullOrWhiteSpace(behavior.EnabledCondition)
                        && !worldConfig.EvaluateDotNotationLogic(api, behavior.EnabledCondition))
                    {
                        var instance = (BlockBehavior)behavior;
                        ConditioningBaseline.MarkDirty(block, instance);
                        block.BlockBehaviors = RemoveBehavior(block.BlockBehaviors, instance);
                        block.CollectibleBehaviors = RemoveBehaviorAcrossViews(block.CollectibleBehaviors, instance);
                        continue;
                    }
                    behavior.ProcessConditions(api, block);
                    // ProcessConditions is the destructive path for carryables - it
                    // drops condition-disabled slots and rewrites the properties JSON in
                    // place - so every call on a carryable marks its baseline entry
                    // dirty: the next run remounts the entry from pristine JSON before
                    // the passes re-derive, which is what lets a condition flip that
                    // re-enables a dropped slot converge to the fresh-boot slots. The
                    // interact's ProcessConditions is a provable no-op
                    // (BlockBehaviorCarryableInteract.cs:96-98) - nothing to mark.
                    if (behavior is BlockBehaviorCarryable)
                        ConditioningBaseline.MarkDirty(block, (BlockBehavior)behavior);
                }
            }
        }

        /// <summary>
        /// Removes the effective remaining priority-0 base carryable from every block whose code
        /// matches a <c>RemoveBaseCarryableBehaviour</c> prefix.
        /// <para>
        /// The decision is made on the post-resolution <b>union</b> of <c>BlockBehaviors</c> and
        /// <c>CollectibleBehaviors</c>, because the two views do not always hold the same thing:
        /// a carryable declared by a blocktype's own <c>behaviors</c> entry - and any other
        /// engine- or patch-loaded carryable, including a pass-0 baseline remount - is a single
        /// instance referenced from both arrays, an automap clone is one instance per view, and
        /// some blocks mount a carryable in one view only. Judging each array independently - as
        /// the resolver used to - therefore either matched nothing (a filter with no
        /// collectible-side entry) or removed a different object than the other view held.
        /// </para>
        /// <para>
        /// By the time this pass runs the resolver has condensed each view to at most one
        /// survivor, so the union is that survivor - once for a shared instance, once per view
        /// for a clone pair. A priority-positive survivor anywhere in the union keeps the whole
        /// block: only a priority-0 survivor is a "base", and keeping a patched block is the
        /// point of <c>patchPriority</c>. Each view's own entries are then removed
        /// <b>by reference</b> from both arrays, so the removal covers the union without a
        /// type-wide sweep - a differently-conditioned sibling of the same type survives, and
        /// the type-wide <c>RemoveCarryableBehaviour</c> rule stays a separate, later pass.
        /// </para>
        /// <para>
        /// Every removal is marked dirty on the conditioning baseline, so the next
        /// run's restore pass remounts the base from its pristine JSON before the
        /// passes re-derive - converging to the fresh-boot surface for whatever
        /// config the next run sees, instead of a one-way strip. The remount doubles
        /// as the shield that keeps a matched block from becoming an automap clone
        /// target mid-run, and this pass re-empties it in the same run.
        /// </para>
        /// </summary>
        internal void RemoveMatchedBaseCarryableBehaviors(ICoreAPI api)
        {
            var loggingEnabled = config.DebuggingOptions?.LoggingEnabled ?? false;
            var filters = config.CarryablesFilters;
            if (filters == null) return;

            var removeArray = filters.RemoveBaseCarryableBehaviour;
            if (removeArray == null || removeArray.Length == 0) return;

            foreach (var block in api.World.Blocks.Where(b => b.Code != null && b.Id != 0))
            {
                var code = block.Code.ToString()!;
                if (!MatchesAnyPrefix(code, removeArray)) continue;

                var survivors = UnionCarryables(block);
                if (survivors.Count == 0) continue;

                // Carve-out: a priority-positive survivor is a patch, not a base.
                if (survivors.Any(c => c.PatchPriority > 0)) continue;

                // Own the block's remaining behaviors before removing them, then
                // mark every removed survivor dirty - the baseline restores them the
                // next run, whether this filter still matches by then or not.
                ConditioningBaseline.EnsureBaseline(block);
                foreach (var survivor in survivors)
                {
                    ConditioningBaseline.MarkDirty(block, survivor);
                }
                block.BlockBehaviors = RemoveBehaviorsByReference(block.BlockBehaviors, survivors);
                block.CollectibleBehaviors = RemoveBehaviorsByReference(block.CollectibleBehaviors, survivors);

                if (loggingEnabled)
                {
                    api.Logger.Debug($"CarryOn Removed Base Carryable Behavior: {code}");
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="code"/> starts with any of the configured filter prefixes.
        /// Shared by the base filter's removal pass and its restore so the two cannot drift in
        /// what they consider a match, and so a block is shielded and stripped by exactly the
        /// same rule. Empty entries are skipped: <c>StartsWith("")</c> is true for every code,
        /// which would turn one stray config entry into "strip the base from the whole game".
        /// </summary>
        internal static bool MatchesAnyPrefix(string code, string[] prefixes)
            => prefixes.Any(match => !string.IsNullOrEmpty(match) && code.StartsWith(match));

        /// <summary>
        /// The block's carryables across both views, de-duplicated by reference so a shared
        /// instance (mounted in both arrays) is judged once while a per-view clone pair is
        /// judged as the two distinct objects it is.
        /// </summary>
        private static List<BlockBehaviorCarryable> UnionCarryables(Block block)
        {
            var union = new List<BlockBehaviorCarryable>();
            foreach (var carryable in block.BlockBehaviors.OfType<BlockBehaviorCarryable>())
            {
                if (!union.Any(c => ReferenceEquals(c, carryable))) union.Add(carryable);
            }
            foreach (var carryable in block.CollectibleBehaviors.OfType<BlockBehaviorCarryable>())
            {
                if (!union.Any(c => ReferenceEquals(c, carryable))) union.Add(carryable);
            }
            return union;
        }

        /// <summary>
        /// Removes carryable behaviors from blocks that are excluded in the config.
        /// The removal is type-wide on both arrays, so every instance it takes -
        /// engine-loaded or conditional, a bare appended one or an automap clone - is
        /// first owned by the conditioning baseline (<see cref="ConditioningBaseline.EnsureBaseline"/>)
        /// and then marked dirty (<see cref="ConditioningBaseline.MarkDirty"/>),
        /// which is what makes the exclusion a reversible edit: the next run's restore
        /// pass remounts every removed engine instance from its pristine JSON, and
        /// un-creates the clones by their factory marker. A clone the automapper no
        /// longer re-creates under a changed config is simply gone after the un-create -
        /// the surface converges to the fresh-boot surface for that config.
        /// </summary>
        internal void RemoveExcludedCarryableBehaviors(ICoreAPI api)
        {
            var loggingEnabled = config.DebuggingOptions?.LoggingEnabled ?? false;
            var filters = config.CarryablesFilters;
            if (filters == null) return;

            var removeArray = filters.RemoveCarryableBehaviour;
            if (removeArray == null || removeArray.Length == 0)
            {
                return;
            }

            foreach (var block in api.World.Blocks.Where(b => b.Code != null))
            {
                foreach (var remove in removeArray)
                {
                    if (block.Code.ToString().StartsWith(remove))
                    {
                        // Own every carryable this type-wide removal is about
                        // to take, then mark each one dirty - clones carry no baseline
                        // entry, so for them this is the work-list note that lets the
                        // next run's un-create sweep find them.
                        var removed = UnionCarryables(block);
                        ConditioningBaseline.EnsureBaseline(block);
                        foreach (var instance in removed)
                        {
                            ConditioningBaseline.MarkDirty(block, instance);
                        }

                        var count = block.BlockBehaviors.Length;
                        block.BlockBehaviors = RemoveBehaviorsOfType(block.BlockBehaviors, typeof(BlockBehaviorCarryable));
                        block.CollectibleBehaviors = RemoveBehaviorsOfType(block.CollectibleBehaviors, typeof(BlockBehaviorCarryable));

                        if (count != block.BlockBehaviors.Length && loggingEnabled)
                        {
                            api.Logger.Debug($"CarryOn Removed Carryable Behavior: {block.Code}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Removes all behaviors of a given type from an array of behaviors.
        /// </summary>
        internal static T[] RemoveBehaviorsOfType<T>(T[] behaviors, Type typeToRemove)
        {
            var behaviorList = behaviors.ToList();
            behaviorList.RemoveAll(r => typeToRemove.IsInstanceOfType(r));
            return behaviorList.ToArray();
        }

        /// <summary>
        /// Removes a single behavior instance by reference, leaving any sibling of the
        /// same type untouched. Used where removing the *instance* - not its runtime
        /// type - is what conditioning owns (reconcile, disabled-conditional strip).
        /// </summary>
        internal static T[] RemoveBehavior<T>(T[] behaviors, T instance)
            => RemoveBehaviorsByReference(behaviors, Enumerable.Repeat(instance, 1));

        /// <summary>
        /// Removes the same logical behavior from a view that may hold a <i>different</i>
        /// instance of it. Reference removal is tried first and is exact whenever the two
        /// views share one object (patch-loaded behaviors, pass-0 baseline remounts). The automapper
        /// instead builds one clone per view from the same properties, so for those the
        /// collectible view holds a twin that is not reference-equal to the block-side
        /// instance; the twin is then resolved by runtime type, condition and priority, which
        /// is what a clone pair shares because both were initialized from one donor's
        /// properties. A differently-conditioned or differently-prioritized sibling of the same
        /// type is left mounted.
        /// </summary>
        private static CollectibleBehavior[] RemoveBehaviorAcrossViews(CollectibleBehavior[] behaviors, BlockBehavior instance)
        {
            var byReference = RemoveBehavior(behaviors, instance);
            if (byReference.Length != behaviors.Length) return byReference;

            var twin = FindViewTwin(behaviors, instance);
            return twin == null ? behaviors : RemoveBehavior(behaviors, twin);
        }

        /// <summary>
        /// The per-view counterpart of <paramref name="instance"/> in <paramref name="behaviors"/>,
        /// or null when this view holds no twin of it. Both automapper passes build one
        /// clone per view from a single donor's properties, so both clone kinds resolve
        /// here by the shape they share: runtime type plus the donor's condition (and,
        /// for carryables, the donor's patch priority).
        /// </summary>
        private static BlockBehavior? FindViewTwin(CollectibleBehavior[] behaviors, BlockBehavior instance)
        {
            switch (instance)
            {
                case BlockBehaviorCarryable carryable:
                    foreach (var candidate in behaviors.OfType<BlockBehaviorCarryable>())
                    {
                        if (string.Equals(candidate.EnabledCondition, carryable.EnabledCondition, StringComparison.Ordinal)
                            && candidate.PatchPriority == carryable.PatchPriority)
                        {
                            return candidate;
                        }
                    }
                    return null;

                case BlockBehaviorCarryableInteract interact:
                    foreach (var candidate in behaviors.OfType<BlockBehaviorCarryableInteract>())
                    {
                        if (string.Equals(candidate.EnabledCondition, interact.EnabledCondition, StringComparison.Ordinal))
                        {
                            return candidate;
                        }
                    }
                    return null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Removes behavior instances by reference from an array of behaviors. Reference
        /// equality is essential here: generics must not route through an overridden
        /// <c>Equals</c>, and the same behavior object may appear in both the block
        /// behaviors and collectible behaviors arrays.
        /// </summary>
        internal static T[] RemoveBehaviorsByReference<T>(T[] behaviors, IEnumerable<T> instances)
        {
            if (behaviors.Length == 0) return behaviors;
            var behaviorList = behaviors.ToList();
            foreach (var instance in instances)
            {
                behaviorList.RemoveAll(b => ReferenceEquals(b, instance));
            }
            return behaviorList.ToArray();
        }
    }
}
