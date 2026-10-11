using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// G5–G8 - the <c>RemoveBaseCarryableBehaviour</c> shape matrix, on four
    /// CarryEntityDemo blocktypes added for these rows.
    /// <para>
    /// Each row drives the filter through a live edit so it observes the
    /// <b>transition</b>: with the filter cleared it asserts the "before" distribution
    /// (which array holds the base, and at what priority), then re-applies the filter and
    /// asserts both arrays end up empty. Asserting only the end state would hide which
    /// side the pass failed to clear, and a boot-time row could not distinguish "the pass
    /// emptied both" from "the base was never there".
    /// </para>
    /// <para>
    /// All four blocks reuse <c>multicarry-block</c>'s exact <c>class: Block</c> and
    /// <c>block/basic/cube</c> shape: the automapper ignores <c>class: Block</c> and the
    /// default cube shape, so staging them adds no new match-table signature keys and
    /// cannot perturb the other classes' rows.
    /// </para>
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "Shape matrix for the base filter: each row measures the unmatched distribution, then re-applies the filter and asserts the matched pass empties both arrays, with the last row proving in-session reversibility.")]
    [AtlasDataFiles("fixtures/RemoveBaseSymmetryConfig", TargetPath = "ModConfig")]
    public class RemoveBaseShapeScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private const string ZeroPriorityMulticarry = "carrieddemo:zeropriority-multicarry-block";
        private const string OverlayBase = "carrieddemo:overlaybase-block";
        private const string LoneSharedBase = "carrieddemo:lonesharedbase-block";
        private const string UnconditionalBase = "carrieddemo:unconditionalbase-block";

        /// <summary>All codes the class fixture's base filter matches.</summary>
        private static readonly string[] MatchedCodes =
        [
            "carrieddemo:conditional-only-block",
            "carrieddemo:multicarry-block",
            ZeroPriorityMulticarry,
            OverlayBase,
            LoneSharedBase,
            UnconditionalBase
        ];

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        private static string FullConfig(string[] removeBase)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": true
              },
              "CarryablesFilters": {
                "AutoMapSimilar": true,
                "AutoMatchIgnoreMods": ["mcrate"],
                "RemoveBaseCarryableBehaviour": [{{string.Join(", ", removeBase.Select(c => $"\"{c}\""))}}],
                "RemoveCarryableBehaviour": ["game:banner", "game:clutter-devastation"]
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", CultureInfo.InvariantCulture)}},
                "AllowHighCapacityStorageOnBack": false,
                "PickupGroundStorageBagWhileCarrying": false
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": true
              }
            }
            """;

        /// <summary>Re-seeds the class fixture so rows are order-independent.</summary>
        private async Task ResetToFixtureAsync()
        {
            await World.Ticks(30);
            File.WriteAllText(ConfigPath, FullConfig(MatchedCodes));
            await WaitForFilterAsync(MatchedCodes, "re-seeding the class fixture's base filter");
            await World.Ticks(20);
        }

        private async Task WaitForFilterAsync(IReadOnlyList<string> removeBase, string because)
        {
            try
            {
                await World.Until(
                    () => Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour.SequenceEqual(removeBase),
                    WaitTimeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    $"Timed out after {WaitTimeoutTicks} ticks waiting for the reloaded base filter - {because}. "
                    + $"Now: [{string.Join(", ", Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour)}].");
            }
        }

        /// <summary>Clears the filter in-session and settles, exposing the unstripped state.</summary>
        private async Task ClearFilterAsync()
        {
            File.WriteAllText(ConfigPath, FullConfig([]));
            await WaitForFilterAsync([], "clearing RemoveBaseCarryableBehaviour in-session");
            await World.Ticks(SettleTicks);
        }

        /// <summary>Re-applies the class fixture's filter in-session and settles.</summary>
        private async Task ApplyFilterAsync()
        {
            File.WriteAllText(ConfigPath, FullConfig(MatchedCodes));
            await WaitForFilterAsync(MatchedCodes, "re-applying the class fixture's base filter");
            await World.Ticks(SettleTicks);
        }

        /// <summary>
        /// Priority is read through <see cref="CarryOnParityAssert.MaxPriority"/> - i.e. from
        /// the raw arrays - rather than through <c>GetBehavior&lt;T&gt;()</c>. Conditioning
        /// reassigns <c>block.BlockBehaviors</c> to a new array, so a behavior cache built at
        /// registration can go stale and report no behavior for a block whose array provably
        /// holds one; the raw array is the truth these rows assert, and it is the same
        /// object the mirror contract is about.
        /// </summary>
        /// <remarks>
        /// The player is cached rather than joined per call: the class host keeps one world for
        /// every scenario in the class, and a second <c>JoinPlayer("Tester")</c> is rejected as a
        /// duplicate account reconnect (which would also kick the first player mid-row). G8 needs
        /// a player in step 2 and again in step 3, so the handle is shared.
        /// </remarks>
        private async Task AssertPickupDeniedAsync(BlockPos pos)
        {
            var player = await TesterAsync();
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failure);
            Assert.True(
                carried == null,
                $"pickup must be denied for a block whose base the filter removed: {failure}");
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
        }

        private ITestPlayer? tester;

        /// <summary>The one test player this class's world admits, joined on first use.</summary>
        private async Task<ITestPlayer> TesterAsync()
        {
            tester ??= await JoinPlayer("Tester");
            return tester;
        }

        /// <summary>
        /// G5 - two unconditional priority-0 carryables. Resolution keeps one priority-0
        /// winner, and the base pass must then empty <b>both</b> arrays: the removal is a
        /// union, not limited to the lone-single shape.
        /// </summary>
        [Trait("summary", "Two unconditional priority-0 carryables resolve to one priority-0 winner; the matched pass must clear the union of both arrays and deny pickup.")]
        [Trait("pins", "A matched priority-0 winner is removed from every array, not only the side the resolver swept.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed until the symmetric base pass landed, where the priority-0 winner survived block-side before and after.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MatchedMulticarry_ZeroPriorityWinner_RemovedFromBothArrays()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();
            Assert.Contains(ZeroPriorityMulticarry, Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ZeroPriorityMulticarry, pos);

            // Before: unmatched, the resolved winner exists at priority 0. Which array holds
            // it is the engine's business, so the row measures and reports it rather than
            // assuming - what matters is that the pass must clear the union.
            await ClearFilterAsync();
            var before = CarryOnParityAssert.Inspect(World.BlockAt(pos));
            Assert.True(
                before.InEitherSide && before.MaxPriority == 0,
                $"Row premise: '{ZeroPriorityMulticarry}' must resolve to a priority-0 winner when "
                + $"unmatched. {before.Describe()}");

            // After: matched, both arrays must be empty.
            await ApplyFilterAsync();
            CarryOnParityAssert.AssertRemovalCovered(
                before,
                World.BlockAt(pos),
                "the block matched RemoveBaseCarryableBehaviour and its effective survivor is a "
                + "priority-0 base, so the pass must clear the union of both arrays - not only "
                + "the side the resolver happened to sweep");

            await AssertPickupDeniedAsync(pos);
        }

        /// <summary>
        /// G6 - an overlay merge whose survivor is a priority-0 base. The merge itself must
        /// be left alone; the merged survivor is still a priority-0 base, so the union must
        /// end up empty - including the overlay's own instance, so the merge leaves no stray.
        /// <para>
        /// The overlay declares <c>patchPriority: 0</c> deliberately. The merge overwrites
        /// scalars from the overlay's properties
        /// (<c>MultipleBehaviorResolver.MergeCarryableProperties</c>), which was measured
        /// directly: with the same overlay at <c>patchPriority: 5</c> the merged survivor
        /// reports priority 5, and the carve-out then correctly keeps it. That is a
        /// different case; this row pins the priority-0 survivor the base pass must remove.
        /// </para>
        /// </summary>
        [Trait("summary", "The merged survivor of a priority-0 base plus an overlay is still a priority-0 base, so the matched pass must empty the union and leave no stray overlay instance.")]
        [Trait("pins", "The merge is left alone while the priority-0 merged survivor is removed from both arrays.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed until the symmetric base pass landed, where the merged survivor survived block-side.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MatchedOverlayMerge_ZeroPriorityBase_RemovedFromBothArrays()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();
            Assert.Contains(OverlayBase, Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(OverlayBase, pos);

            // Before: unmatched, the merged survivor exists at priority 0.
            await ClearFilterAsync();
            var before = CarryOnParityAssert.Inspect(World.BlockAt(pos));
            Assert.True(
                before.InEitherSide && before.MaxPriority == 0,
                $"Row premise: '{OverlayBase}' must resolve to a priority-0 merged survivor when "
                + $"unmatched. {before.Describe()}");

            // After: matched, the union must be empty.
            await ApplyFilterAsync();
            CarryOnParityAssert.AssertRemovalCovered(
                before,
                World.BlockAt(pos),
                "the merged survivor is a priority-0 base, so the union must be emptied and "
                + "the merge must leave no stray overlay instance");

            await AssertPickupDeniedAsync(pos);
        }

        /// <summary>
        /// G7 - the lone-base case, and the row that pins the union gate.
        /// <para>
        /// <b>Premise corrected.</b> This row originally asserted the base
        /// was mounted in the block side only, on the belief that the engine feeds a
        /// blocktype's <c>behaviors</c> array to the block form alone. It does not: measured
        /// unfiltered, a lone unconditional blocktype carryable is <b>one instance shared by
        /// both arrays</b> (<c>block-side=1, collectible-side=1, shared-instance=True</c>). The
        /// <c>block-side=1, collectible-side=0</c> was an artifact of the very
        /// asymmetry under test - the old resolver destructively emptied the collectible side and
        /// nothing could put it back, so the row was measuring post-bug state and mistaking it
        /// for the engine's shape. The demo blocks whose collectible side <i>is</i> fed by
        /// CarryOn (<c>multicarry-block</c>, <c>conditional-only-block</c>) are conditional, and
        /// reach the collectible view through reconcile remounts - which is why their
        /// both-arrays reading never proved anything about an unconditional base.
        /// </para>
        /// <para>
        /// The row keeps the contract it was written for - the matched pass must clear the
        /// <b>union</b> and deny pickup - and the lone shape is the discriminating case for it:
        /// it is exactly the shape the old lone-base branch half-handled, removing the base from
        /// the collectible view and leaving the shared instance mounted in the block view, so
        /// <c>IsCarryable()</c> stayed true and a filtered-out base was still pickable.
        /// </para>
        /// </summary>
        [Trait("summary", "The lone priority-0 base shared by both views must be removed from the union, closing the shape the old lone-base branch half-handled and kept pickable.")]
        [Trait("pins", "A shared lone base is removed from every array conditioning can see, so it no longer reports carryable.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed until the symmetric base pass landed, where the shared instance was removed from one view and left mounted in the other.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MatchedLoneBase_SharedAcrossViews_RemovedFromTheUnion()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();
            Assert.Contains(LoneSharedBase, Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(LoneSharedBase, pos);

            // Before: unmatched, a lone priority-0 base shared by both views.
            await ClearFilterAsync();
            var before = CarryOnParityAssert.Inspect(World.BlockAt(pos));
            Assert.True(
                before.InBothSides && before.MaxPriority == 0,
                $"Row premise: '{LoneSharedBase}' must hold a lone priority-0 base when unmatched, "
                + $"and the engine shares that one instance across both views. Actual: {before.Describe()}");

            // After: matched, the union must be empty.
            await ApplyFilterAsync();
            CarryOnParityAssert.AssertRemovalCovered(
                before,
                World.BlockAt(pos),
                "the block's only carryable is a priority-0 base, and a matched base must be "
                + "removed from every array conditioning can see - a pass that inspects one view "
                + "leaves the shared instance mounted in the other and the block pickable");

            await AssertPickupDeniedAsync(pos);
        }

        /// <summary>
        /// G8 - the hard reversibility contract on the base pass: stripping a matched
        /// priority-0 base must be reversible <b>in-session</b>. The row walks
        /// unmatched → matched → unmatched again and asserts the surface returns to its
        /// measured starting state array for array, with both views symmetric throughout.
        /// <para>
        /// Restoration is asserted against the <i>measured</i> pre-state rather than
        /// "present in both arrays": the pass must put the base back exactly where it took it
        /// from, so a fix that restored it into a view the pass never emptied fails here just as
        /// a fix that never restores it does. This block's base is unconditional, which is the
        /// point - reconcile only owns conditional behaviors, so nothing else would give it back.
        /// </para>
        /// </summary>
        [Trait("summary", "Walks unmatched then matched then unmatched in one session: stripping a matched priority-0 base must be reversible, restoring the base to the same view it was removed from array for array.")]
        [Trait("pins", "The base pass is reversible in-session, not a one-way strip, on an unconditional base reconcile does not own.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed until the symmetric base pass landed, at the re-apply step where the removal was not reversible in-session.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MatchedUnconditionalBase_PrefixRemovalRestoresInSession()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();
            Assert.Contains(UnconditionalBase, Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(UnconditionalBase, pos);

            // Step 1 - unmatched: measure the starting surface.
            await ClearFilterAsync();
            var start = CarryOnParityAssert.Inspect(World.BlockAt(pos));
            Assert.True(
                start.InBlockSide && start.IsCarryable,
                $"step 1 premise: '{UnconditionalBase}' must hold its base before any strip. "
                + $"Actual: {start.Describe()}");

            // Step 2 - matched: the union emptied, pickup denied.
            await ApplyFilterAsync();
            CarryOnParityAssert.AssertRemovalCovered(
                start,
                World.BlockAt(pos),
                "step 2 (matched): the filter matched, so the priority-0 base must be cleared "
                + "from every array that holds one");
            await AssertPickupDeniedAsync(pos);

            // Step 3 - unmatched again: the base must be back, in-session, in the same view
            // it came from, with no world reload. A base pass that only ever removes cannot
            // satisfy this.
            await ClearFilterAsync();
            CarryOnParityAssert.AssertRestoredTo(
                start,
                World.BlockAt(pos),
                "step 3 (unmatched again): removing the prefix in-session must restore the base "
                + "to the same view it was removed from - the base pass must be reversible, "
                + "not a one-way strip");

            ITestPlayer player = await TesterAsync();
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failure);
            Assert.True(
                carried != null,
                $"the restored block must be carryable again in the same session: {failure}");
            Assert.Equal(UnconditionalBase, carried?.Block?.Code?.ToString());
        }
    }
}
