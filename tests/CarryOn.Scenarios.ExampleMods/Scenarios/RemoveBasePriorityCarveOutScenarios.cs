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
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// G3/G4 - the <c>RemoveBaseCarryableBehaviour</c> priority carve-out.
    /// <para>
    /// The filter is instance-targeted and only ever removes a <b>priority-0</b> base. A
    /// matched block whose surviving carryable has <c>patchPriority &gt; 0</c> - either
    /// because it is the block's only behavior, or because it won a multi-carryable
    /// resolution - must keep it. These two rows are the tripwires for the Phase-2 fix:
    /// they are green today and must stay green after the base pass stops taking the
    /// removal flag asymmetrically and starts operating on the effective survivor.
    /// </para>
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "Priority carve-out controls: the base filter only ever removes a priority-0 base, so a matched block whose surviving carryable has positive priority must stay carryable.")]
    [AtlasDataFiles("fixtures/RemoveBaseSymmetryConfig", TargetPath = "ModConfig")]
    public class RemoveBasePriorityCarveOutScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private const string ConditionalOnlyBlock = "carrieddemo:conditional-only-block";
        private const string MulticarryBlock = "carrieddemo:multicarry-block";

        /// <summary>All codes the class fixture's base filter matches.</summary>
        private static readonly string[] MatchedCodes =
        [
            ConditionalOnlyBlock,
            MulticarryBlock,
            "carrieddemo:zeropriority-multicarry-block",
            "carrieddemo:overlaybase-block",
            "carrieddemo:lonesharedbase-block",
            "carrieddemo:unconditionalbase-block"
        ];

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>
        /// Full-file payload. <paramref name="removeBase"/> is the whole list, so a row can
        /// add or drop prefixes and the only behavior-surface delta is that list.
        /// </summary>
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

        /// <summary>
        /// Atlas stages a class fixture once per class, so a row that writes the config
        /// leaks it into the next row of this class. Every row re-seeds the fixture's own
        /// filter first, which is what makes the rows order-independent (the same
        /// discipline <c>MultiCarryableCoexistenceScenarios</c> uses).
        /// </summary>
        private async Task ResetToFixtureAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            File.WriteAllText(ConfigPath, FullConfig(MatchedCodes));
            await WaitForFilterAsync(MatchedCodes, "re-seeding the class fixture's base filter");
            await World.Ticks(20); // > 500 ms debounce - a later write's event is not swallowed
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

        private BlockBehaviorCarryable? CarryableOf(string code)
            => World.Api!.World.GetBlock(new AssetLocation(code))?.GetBehavior<BlockBehaviorCarryable>();

        /// <summary>
        /// G3 - a matched block whose only carryable is priority 1 must keep it: the filter
        /// removes a priority-0 <i>base</i>, never a positive-priority behavior. The chest
        /// toggle is irrelevant to this row (the demo behavior is unconditional), which is
        /// the point - the carve-out is priority-driven, not condition-driven.
        /// </summary>
        [Trait("summary", "A matched block whose only carryable is priority 1 keeps it: the filter removes a priority-0 base, never a positive-priority behavior, regardless of the chest condition.")]
        [Trait("pins", "The base filter's carve-out is priority-driven, not condition-driven.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MatchedLonePatch_PriorityCarveOut_StaysCarryable()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();
            Assert.True(Mod.Config.Carryables.Chest, "fixture must boot with Carryables.Chest = true");
            Assert.Contains(ConditionalOnlyBlock, Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ConditionalOnlyBlock, pos);

            var survivor = CarryableOf(ConditionalOnlyBlock);
            Assert.True(
                survivor != null && survivor.PatchPriority == 1,
                $"Row premise: '{ConditionalOnlyBlock}' must resolve to a priority-1 carryable at boot");

            CarryOnParityAssert.AssertPresentInBoth(
                World.BlockAt(pos),
                "the block is matched by RemoveBaseCarryableBehaviour, but its only carryable "
                + "is priority 1, and the filter only ever removes a priority-0 base");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failure);
            Assert.True(
                carried != null,
                $"a matched block whose only carryable is priority 1 must stay carryable: {failure}");
            Assert.Equal(ConditionalOnlyBlock, carried?.Block?.Code?.ToString());
        }

        /// <summary>
        /// G4 - the multi-carryable carve-out. The block resolves to two instances
        /// (priority 2 base, priority 1 conditional); the priority-2 winner must survive in
        /// both arrays and stay the survivor across a reload that re-applies the filter.
        /// </summary>
        [Trait("summary", "The matched two-instance block still resolves to its priority-2 base in both arrays, before and after an in-session reload that re-applies the same filter.")]
        [Trait("pins", "A repeat pass must not lose the priority-2 winner of a matched multi-carryable block.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MatchedMulticarry_BaseWinsAndStaysAcrossReload()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();
            Assert.Contains(MulticarryBlock, Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(MulticarryBlock, pos);

            var survivor = CarryableOf(MulticarryBlock);
            Assert.True(
                survivor != null && survivor.PatchPriority == 2,
                $"Row premise: '{MulticarryBlock}' must resolve to its priority-2 base at boot");

            CarryOnParityAssert.AssertPresentInBoth(
                World.BlockAt(pos),
                "the block is matched, but its effective survivor is the priority-2 base, "
                + "which the filter must not remove");

            // Re-apply the same filter in-session: a matched multi-carryable block must
            // still resolve to the same winner, not lose it to a repeat pass.
            await World.Ticks(SettleTicks);
            File.WriteAllText(ConfigPath, FullConfig(MatchedCodes));
            await WaitForFilterAsync(MatchedCodes, "re-applying the identical base filter");
            await World.Ticks(SettleTicks);

            var afterReload = CarryableOf(MulticarryBlock);
            Assert.True(
                afterReload != null && afterReload.PatchPriority == 2,
                $"the priority-2 base must remain the survivor after a reload that re-applies "
                + $"the filter, found {afterReload?.PatchPriority.ToString() ?? "no carryable"}");
            CarryOnParityAssert.AssertPresentInBoth(
                World.BlockAt(pos), "the priority-2 base must stay mirrored in both arrays after the reload");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failure);
            Assert.True(carried != null, $"matched multicarry pickup failed: {failure}");
            Assert.Equal(MulticarryBlock, carried?.Block?.Code?.ToString());
        }
    }
}
