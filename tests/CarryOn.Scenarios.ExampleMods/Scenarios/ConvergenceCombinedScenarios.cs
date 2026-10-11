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
    /// B2 - the green control on an <b>unconditional</b> base
    /// (<c>carrieddemo:unconditionalbase-block</c>, the G7 shared-instance
    /// shape): base + exclusion prefixes added and then cleared
    /// <b>together</b>. Green through the whole sequence, before and after the
    /// pass-0 rewrite: the base pass marks what it removes even while the
    /// exclusion is also active, and clearing both prefixes in one write brings
    /// the base back. G8 already walks the base-only edit; this row pins that
    /// the exclusion's simultaneous presence does not consume the base's
    /// restoration.
    /// <para>
    /// The class stands alone (one row, one server boot) beside its two
    /// red-first siblings (<c>ConvergenceExclusionScenarios</c> B1,
    /// <c>ConvergenceStagedClearScenarios</c> B3): Atlas's
    /// <c>RollbackWorld</c> restores the world save but not the blocktype
    /// registry, and xUnit guarantees no within-class order, so every sibling
    /// owns its own class.
    /// </para>
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "Green control on a lone unconditional base: base and exclusion prefixes added and then cleared together must still restore the base.")]
    [AtlasDataFiles("fixtures/ConvergenceRestoreConfig", TargetPath = "ModConfig")]
    public class ConvergenceCombinedScenarios : CarryOnScenarioBase
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
        /// B2 - the green control: base + exclusion prefixes added and cleared
        /// <b>together</b> in one write, so the base must come back. G8 walks the
        /// base-only edit; this row pins that the exclusion's simultaneous presence
        /// - at removal time and at clear time - does not stand in the way.
        /// </summary>
        [Trait("summary", "Adding both the base and exclusion prefixes in one write then clearing them together must restore the unconditional base, so the exclusion's simultaneous presence never consumes the base's restoration.")]
        [Trait("pins", "The base's restoration does not depend on the exclusion's absence at removal or at clear time.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BaseAndExclusion_ClearedTogether_BaseRestored()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();

            var (block, before) = BlockWithUnfilteredState();

            // Both prefixes at once: the base strip removes and marks the entry dirty;
            // the exclusion strip finds nothing left (premise, green today).
            await WriteAndAwaitFiltersAsync(
                [BlockCode],
                [.. DefaultExclusion, BlockCode],
                "the edit adds the block to both filters");
            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertRemovalCovered(
                before,
                block,
                "the base prefix matches, so the pass must clear the union of both arrays "
                + "(the exclusion prefix matching too must not spare it)");

            // Both cleared in one write: pass 0 re-mounts the boot behavior once.
            await WriteAndAwaitFiltersAsync(
                DefaultBaseFilter,
                DefaultExclusion,
                "the edit clears both prefixes in one write");
            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertRestoredTo(
                before,
                block,
                "clearing both prefixes together must restore the base in the same write - "
                + "the dirty flag the base pass set while the exclusion was also active is "
                + "still set, and the exclusion no longer strips the remount (G8 covers the "
                + "base-only edit; this control adds the exclusion present at removal time "
                + "and at clear time)");
        }
    }
}
