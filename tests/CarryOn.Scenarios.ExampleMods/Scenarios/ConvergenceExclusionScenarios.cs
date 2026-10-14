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
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// B1 - the exclusion-only cycle on an <b>unconditional</b> base, the one
    /// ownership shape the old captured/reconcile machinery could not cover
    /// (C1/C2). The block is <c>carrieddemo:unconditionalbase-block</c>: a
    /// lone unconditional priority-0 base, one instance shared by both views
    /// (the G7 finding), never captured by the old conditional-only capture and
    /// never an automap target (Block class + cube shape + no entityclass).
    /// The row measures the pre-state, walks the filters through full-file
    /// Mode B edits, and asserts the hard reversibility contract against the
    /// measured pre-state.
    /// <para>
    /// Red-first: written against the pre-fix head, where the exclusion strip
    /// kept no record and clearing the entry restored nothing. Green since the
    /// pass-0 baseline - the strip now marks the entry dirty and pass 0
    /// re-mounts it from the pristine boot JSON.
    /// </para>
    /// <para>
    /// One row per class by construction: Atlas's <c>RollbackWorld</c> restores
    /// the world save but not the blocktype registry, and xUnit guarantees no
    /// within-class order, so every row that ends on a stripped block owns its
    /// own class, and its own server boot, for a fresh registry.
    /// </para>
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "Exclusion-only cycle on a lone unconditional base: adding the block to the exclusion array strips it type-wide, and clearing the entry must restore the base.")]
    [AtlasDataFiles("fixtures/ConvergenceRestoreConfig", TargetPath = "ModConfig")]
    public class ConvergenceExclusionScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private const string BlockCode = "carrieddemo:unconditionalbase-block";

        private static readonly string[] DefaultBaseFilter = ["woodchests:wtrunk"];
        private static readonly string[] DefaultExclusion = ["game:banner", "game:clutter-devastation"];

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>
        /// Full-file Mode B payload: only the two filter arrays move, every other
        /// key pinned to the fixture's boot value or left to the same default.
        /// </summary>
        private static string FullConfig(string[] removeBase, string[] removeCarryable)
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
                "RemoveCarryableBehaviour": [{{string.Join(", ", removeCarryable.Select(c => $"\"{c}\""))}}]
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

        /// <summary>Writes the payload and blocks until the live config shows both filter arrays.</summary>
        private async Task WriteAndAwaitFiltersAsync(
            IReadOnlyList<string> removeBase,
            IReadOnlyList<string> removeCarryable,
            string because)
        {
            File.WriteAllText(ConfigPath, FullConfig([.. removeBase], [.. removeCarryable]));
            try
            {
                await World.Until(
                    () => Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour.SequenceEqual(removeBase)
                        && Mod.Config.CarryablesFilters.RemoveCarryableBehaviour.SequenceEqual(removeCarryable),
                    WaitTimeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    $"Timed out after {WaitTimeoutTicks} ticks waiting for the reloaded filters - {because}. "
                    + $"Now: base=[{string.Join(", ", Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour)}], "
                    + $"exclude=[{string.Join(", ", Mod.Config.CarryablesFilters.RemoveCarryableBehaviour)}].");
            }
        }

        /// <summary>Re-seeds the class fixture so rows are order-independent.</summary>
        private async Task ResetToFixtureAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            await WriteAndAwaitFiltersAsync(
                DefaultBaseFilter,
                DefaultExclusion,
                "re-seeding the class fixture's filters");
            await World.Ticks(20); // > 500 ms debounce - next write's event is not swallowed
        }

        /// <summary>The block with its measured unfiltered state, or a row-premise failure.</summary>
        private (Block Block, CarryableParity Before) BlockWithUnfilteredState()
        {
            var block = World.Api!.World.GetBlock(new AssetLocation(BlockCode));
            Assert.True(
                block != null,
                $"Row premise: the registry must hold '{BlockCode}' (CarryEntityDemo staged?)");
            var before = CarryOnParityAssert.Inspect(block!);
            Assert.True(
                before.InBothSides && before.SharedInstance && before.MaxPriority == 0,
                $"Row premise: '{BlockCode}' must hold its unconditional base in both arrays "
                + $"as one shared priority-0 instance when unfiltered. Actual: {before.Describe()}");
            return (block!, before);
        }

        /// <summary>
        /// B1 - the exclusion-only cycle. The exclusion strip is type-wide, so it
        /// must mark what it removed: adding the block to
        /// <c>RemoveCarryableBehaviour</c> strips it, and clearing the entry has to
        /// bring the unconditional base back. Red until the pass-0 baseline landed
        /// (the old strip recorded nothing and the base was never captured).
        /// </summary>
        [Trait("summary", "Adding the block to the exclusion list strips it type-wide, then clearing the entry must bring the unconditional base back, asserted against the measured pre-state array for array.")]
        [Trait("pins", "Clearing an exclusion restores the stripped unconditional base, so an unowned base's return is covered.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the pre-fix head, where the exclusion strip recorded nothing and the base was never re-mounted.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ExclusionOnly_AddedThenCleared_BaseNotRestored()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();

            var (block, before) = BlockWithUnfilteredState();

            // Exclusion-only: strips type-wide, marking each removal dirty for pass 0.
            await WriteAndAwaitFiltersAsync(
                DefaultBaseFilter,
                [.. DefaultExclusion, BlockCode],
                "the edit adds the block to RemoveCarryableBehaviour only");
            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertRemovalCovered(
                before,
                block,
                "the exclusion strip is type-wide, so the unconditional base must be gone "
                + "from both arrays");

            // Cleared: pass 0 re-mounts the boot behavior the strip marked dirty.
            await WriteAndAwaitFiltersAsync(
                DefaultBaseFilter,
                DefaultExclusion,
                "the edit clears the exclusion entry");
            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertRestoredTo(
                before,
                block,
                "clearing the exclusion must restore the stripped base - a fresh boot with "
                + "the cleared config would mount it. The block is not carryable and the "
                + "arrays are empty, so the exclusion pass did not mark its removal dirty "
                + "and pass 0 had nothing to re-mount (C1/C2: an unowned base's return)");
        }
    }
}
