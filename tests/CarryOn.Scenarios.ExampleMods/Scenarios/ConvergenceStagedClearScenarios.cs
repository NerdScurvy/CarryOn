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
    /// B3 - the staged clear that burned the old record (C3) on an
    /// <b>unconditional</b> base (<c>carrieddemo:unconditionalbase-block</c>,
    /// the G7 shared-instance shape). With both prefixes active, clearing
    /// only the base prefix made the restore pass remount the base from its
    /// record and drop the record - and the still-matching exclusion strip
    /// then removed the remount unrecorded. The intermediate state looked
    /// correctly excluded, so the burn was invisible; the final clear then had
    /// nothing left.
    /// <para>
    /// Red-first: written against the pre-fix head, where that sequence left
    /// the block unrestorable. Green since the pass-0 baseline, where there is
    /// no record to burn: pass 0 re-mounts from the pristine boot JSON on every
    /// run, so the final clear converges like a fresh boot regardless of what
    /// the intermediate state did.
    /// </para>
    /// <para>
    /// One row per class by construction: Atlas's <c>RollbackWorld</c> restores
    /// the world save but not the blocktype registry, and xUnit guarantees no
    /// within-class order, so every row that ends on a stripped block owns its
    /// own class, and its own server boot, for a fresh registry.
    /// </para>
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "Staged clear on a lone unconditional base: clearing only the base prefix must leave the block excluded, and the later exclusion clear must still restore it.")]
    [AtlasDataFiles("fixtures/ConvergenceRestoreConfig", TargetPath = "ModConfig")]
    public class ConvergenceStagedClearScenarios : CarryOnScenarioBase
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
        /// B3 - the staged clear. With both prefixes active, clearing only the base
        /// prefix must still leave the block correctly excluded, and clearing the
        /// exclusion afterwards must bring the base back. Red until the pass-0
        /// baseline replaced the record that the intermediate state used to burn.
        /// </summary>
        [Trait("summary", "Clearing only the base prefix must leave the block correctly excluded, and the later exclusion clear must still restore the base, walked unmatched then matched and back in one session.")]
        [Trait("pins", "The intermediate state must not burn the record the final clear needs; the staged clear converges like a fresh boot.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the pre-fix head, where the intermediate state burned the record the final clear needed.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StagedClear_BaseFirstThenExclusion_BaseNotRestored()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();

            var (block, before) = BlockWithUnfilteredState();

            // Both prefixes at once (as B2).
            await WriteAndAwaitFiltersAsync(
                [BlockCode],
                [.. DefaultExclusion, BlockCode],
                "the edit adds the block to both filters");
            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertRemovalCovered(
                before,
                block,
                "the base prefix matches, so the pass must clear the union of both arrays");

            // Clear the base prefix only: pass 0 re-mounts the boot behavior, and the
            // still-matching exclusion strips the remount again. The intermediate state
            // is indistinguishable from correct (premise, green today).
            await WriteAndAwaitFiltersAsync(
                DefaultBaseFilter,
                [.. DefaultExclusion, BlockCode],
                "the edit clears only the base prefix");
            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertAbsentFromBoth(
                block,
                "the intermediate state: the exclusion prefix still matches, so the "
                + "re-mounted base is correctly stripped again - which is also the state a "
                + "burned record used to hide behind");

            // Clear the exclusion: pass 0 re-mounts the boot behavior again.
            await WriteAndAwaitFiltersAsync(
                DefaultBaseFilter,
                DefaultExclusion,
                "the edit clears the exclusion prefix");
            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertRestoredTo(
                before,
                block,
                "clearing the exclusion must restore the base - a fresh boot with the "
                + "cleared config would mount it. The block is still not carryable and "
                + "the arrays are empty, so the last strip did not mark the entry dirty "
                + "and pass 0 had no pristine JSON to re-mount (C3: the old record was "
                + "burned by the intermediate state)");
        }
    }
}
