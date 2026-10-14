using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using Vintagestory.API.Config;
using CarryOn.Utility;
using CarryOn.Common.Behaviors;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// <c>RemoveBaseCarryableBehaviour</c> boot rows (R1/G1).
    /// <para>
    /// <c>RemoveBaseCarryableBehaviour</c> is a union-gated, instance-targeted filter: a
    /// block whose code matches a prefix loses the <i>effective remaining priority-0
    /// base</i> from both behavior arrays, and nothing else. It is not a type-wide
    /// removal, so a block whose code does not match must keep its carryable.
    /// </para>
    /// <para>
    /// Both rows assert the per-array mirror invariant through
    /// <see cref="CarryOnParityAssert"/> rather than <c>IsCarryable()</c> alone: the
    /// latter is an OR over both arrays, so a removal that only reached one view still
    /// reports "carryable" and the divergence stays invisible.
    /// </para>
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "RemoveBaseCarryableBehaviour boot rows: a prefix-matched chest loses its lone priority-0 base from both behavior arrays and denies pickup, while an unmatched code keeps its carryable through a full pickup and putdown round trip.")]
    [AtlasDataFiles("fixtures/RemoveBaseCarryableConfig", TargetPath = "ModConfig")]
    public class RemoveBaseCarryableScenarios : CarryOnScenarioBase
    {
        private const float SeedSpeed = 2.0f;

        private const string ChestCode = "game:chest-east";
        private const string LabeledChestCode = "game:labeledchest-east";

        /// <summary>
        /// R1 - a matched lone priority-0 base must leave both arrays. Today the resolver
        /// only passes the removal flag to the collectible-side sweep
        /// (<c>MultipleBehaviorResolver.cs:53-54</c>), so the block-side copy survives and
        /// the chest stays carryable: a filtered-out base that players can still pick up.
        /// </summary>
        [Trait("summary", "A chest whose code matches the configured base-filter prefix must hold no carryable in either behavior array and deny pickup.")]
        [Trait("pins", "A matched prefix removes the effective remaining priority-0 base from both arrays, not only the collectible-side view.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with the chest block-side base surviving (block-side=1, collectible-side=0) and the block still reporting carryable.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MatchedChest_LonePriorityZeroBase_RemovedFromBothArrays()
        {
            Assert.NotNull(World.Api);

            // Fixture guard - without the prefix this row silently degrades into the
            // ordinary carryable-chest case.
            var filters = Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour;
            Assert.Contains("game:chest-", filters);
            Assert.True(
                ChestCode.StartsWith("game:chest-"),
                $"Row premise: '{ChestCode}' must match the configured prefix 'game:chest-'");

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ChestCode, chestPos);

            // The matched base must be gone from both arrays, and pickup denied.
            CarryOnParityAssert.AssertAbsentFromBoth(
                World.BlockAt(chestPos),
                "RemoveBaseCarryableBehaviour matched the chest's lone priority-0 base, "
                + "which must be removed from both behavior arrays");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var failure);
            Assert.True(
                carried == null,
                $"A base-matched chest must not be carryable (pickup must be denied): {failure}");
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// G1 - the control: the filter is a prefix match, so a code outside it
        /// (<c>game:labeledchest-east</c> does not start with <c>game:chest-</c>) keeps
        /// its carryable in both arrays and still works end to end.
        /// </summary>
        [Trait("summary", "A labeled chest outside the base-filter prefix keeps its carryable in both behavior arrays and survives a full pickup and putdown round trip.")]
        [Trait("pins", "The base filter is a prefix match: a code outside the prefix is left untouched in both views.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task UnmatchedLabeledChest_KeepsCarryableInBothArrays()
        {
            Assert.NotNull(World.Api);

            var filters = Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour;
            Assert.DoesNotContain("game:labeledchest-", filters);
            Assert.False(
                LabeledChestCode.StartsWith("game:chest-"),
                $"Row premise: '{LabeledChestCode}' must NOT match the configured prefix 'game:chest-'");

            BlockPos labeledPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(LabeledChestCode, labeledPos);

            CarryOnParityAssert.AssertPresentInBoth(
                World.BlockAt(labeledPos),
                "the labeled chest is outside the RemoveBaseCarryableBehaviour prefix, "
                + "so its base must survive in both arrays");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, labeledPos, out var failure);
            Assert.True(carried != null, $"Unmatched labeled chest pickup failed: {failure}");
            Assert.Equal(LabeledChestCode, carried?.Block?.Code?.ToString());

            // Full round trip, so the row proves function and not just a resident behavior.
            BlockPos target = World.Spawn.Offset(4, 1, 0);
            var placedAt = Actions.PlaceDown(
                player.Player, CarrySlot.Hands, CarryActions.AimDownAt(target), out var placeFailure);
            Assert.True(placedAt != null, $"Labeled chest putdown failed: {placeFailure}");
            Assert.Equal(LabeledChestCode, World.BlockAt(target)?.Code?.ToString());
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
        }
    }
}
