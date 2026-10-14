using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using CarryOn.Common.Models;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// <c>RemoveBaseCarryableBehaviour</c> Mode B recondition rows (R2/G2/R3).
    /// <para>
    /// These rows drive the filter through a live config edit rather than a boot fixture,
    /// because the base pass runs on every conditioning pass: a filter that is correct at
    /// boot can still leave a half-applied state after a reload, and only the reload path
    /// exercises reconcile-then-resolve ordering on a block that is already conditioned.
    /// </para>
    /// <para>
    /// Row-writing rules these rows follow (learned from a false Phase 0 result): every
    /// write is a <b>full-file</b> payload, omitted keys deserialize to defaults, the live
    /// values each row depends on are asserted before proceeding, and the identical-config
    /// control (G2) asserts the signature gate held - no recondition fired - instead of
    /// merely observing an unchanged surface.
    /// </para>
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "RemoveBaseCarryableBehaviour Mode B rows: a live full-file filter edit reconditions the block so adding the chest prefix strips its base from both arrays, an identical rewrite fires no pass, and removing the prefix restores it.")]
    [AtlasDataFiles("fixtures/RemoveBaseReconditionConfig", TargetPath = "ModConfig")]
    public class RemoveBaseReconditionScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private const string ChestCode = "game:chest-east";

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>
        /// Full-file Mode B payload. Only <paramref name="removeBase"/> differs from the
        /// fixture, and every signature-relevant section is either pinned to the fixture
        /// value or left to the same default the fixture inherits, so the only
        /// behavior-surface delta is the base filter itself.
        /// </summary>
        private static string FullConfig(params string[] removeBase)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": true
              },
              "CarryablesFilters": {
                "AutoMapSimilar": true,
                "AutoMatchIgnoreMods": ["mcrate"],
                "RemoveBaseCarryableBehaviour": [{{string.Join(", ", removeBase.Select(m => $"\"{m}\""))}}],
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

        /// <summary>Writes the payload and blocks until the live config shows the new filter.</summary>
        private async Task WriteAndAwaitFilterAsync(IReadOnlyList<string> removeBase, string because)
        {
            File.WriteAllText(ConfigPath, FullConfig(removeBase.ToArray()));
            try
            {
                await World.Until(
                    () => Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour.SequenceEqual(removeBase),
                    WaitTimeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    $"Timed out after {WaitTimeoutTicks} ticks waiting for the reloaded filter - {because}. "
                    + $"Now: [{string.Join(", ", Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour)}], "
                    + $"Chest={Mod.Config.Carryables.Chest}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>
        /// Restores the fixture's filter and lets the pass settle. Every row calls this
        /// first: Atlas re-stages a class fixture once per class, not once per row, so a
        /// row that writes the config leaks it into whichever row of this class runs next.
        /// Making each row establish its own precondition is what keeps the rows
        /// order-independent (the identical-config control G2 in particular would otherwise
        /// inherit a filtered config from R2 and assert nothing).
        /// </summary>
        private async Task ResetToFixtureAsync()
        {
            await WriteAndAwaitFilterAsync(
                new[] { "woodchests:wtrunk" },
                "restoring the class fixture's RemoveBaseCarryableBehaviour before the row");
            await World.Ticks(SettleTicks);
        }

        /// <summary>Counts recondition passes fired while <paramref name="body"/> runs.</summary>
        private async Task<int> CountReconditionsAsync(Func<Task> body)
        {
            var passes = 0;
            void Handler(CarryOnConfig _) => passes++;
            Mod.ConfigService.OnBehavioralReconditioned += Handler;
            try
            {
                await body();
            }
            finally
            {
                Mod.ConfigService.OnBehavioralReconditioned -= Handler;
            }
            return passes;
        }

        /// <summary>
        /// R2 - the live add of <c>"game:chest-"</c> must strip the matched base from both
        /// arrays. Today the resolver passes the removal flag only to the collectible-side
        /// sweep (<c>MultipleBehaviorResolver.cs:53-54</c>), so the block-side base survives
        /// the reload and the newly filtered chest is still carryable.
        /// </summary>
        [Trait("summary", "Adding the chest prefix to the base filter in-session reconditions the block and strips the matched base from both behavior arrays, denying pickup.")]
        [Trait("pins", "The reload path resolves the base removal in both views instead of only the collectible-side sweep.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with the block-side base surviving the reload, leaving the newly filtered chest still carryable.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MatchedChestAddedByRecondition_RemovedFromBothArrays()
        {
            Assert.NotNull(World.Api);

            // Own precondition: another row in this class may have left the shared data
            // path filtered.
            await ResetToFixtureAsync();

            // Boot state: the fixture's default filter matches no staged block, so the
            // chest is carryable in both arrays before the edit.
            Assert.Equal(new[] { "woodchests:wtrunk" }, Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour);
            Assert.True(Mod.Config.Carryables.Chest, "fixture must boot with Carryables.Chest = true");
            Assert.True(Mod.Config.DebuggingOptions.ReconditionOnConfigChange, "fixture must boot in Mode B");

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ChestCode, chestPos);
            CarryOnParityAssert.AssertPresentInBoth(
                World.BlockAt(chestPos), "pre-edit baseline: the unfiltered chest carries a base in both arrays");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var passes = await CountReconditionsAsync(async () =>
            {
                await WriteAndAwaitFilterAsync(
                    new[] { "game:chest-" },
                    "the edit adds the chest prefix to RemoveBaseCarryableBehaviour");
                await World.Ticks(SettleTicks);
            });

            Assert.True(
                passes >= 1,
                $"The filter edit must trigger at least one recondition pass, saw {passes}");
            CarryOnParityAssert.AssertAbsentFromBoth(
                World.BlockAt(chestPos),
                "the reload matched the chest prefix, so the effective remaining priority-0 "
                + "base must be removed from both behavior arrays");

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var failure);
            Assert.True(
                carried == null,
                $"A base-filtered chest must not be carryable after the reload: {failure}");
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// G2 - the clean-reload control. An identical-config rewrite must not recondition
        /// at all (the signature gate holds), so the chest keeps its base in both arrays.
        /// This row is what makes R2 meaningful: without it, an R2 pass could be a reload
        /// that never ran the base pass at all rather than a correct removal.
        /// </summary>
        [Trait("summary", "An identical-config rewrite fires zero recondition passes and leaves the chest carryable in both behavior arrays.")]
        [Trait("pins", "The signature gate holds: a no-op reload does not recondition.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task IdenticalConfigReload_NoRecondition_ChestStaysCarryable()
        {
            Assert.NotNull(World.Api);

            await ResetToFixtureAsync();

            var before = Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour;
            Assert.Equal(new[] { "woodchests:wtrunk" }, before);

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ChestCode, chestPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var passes = await CountReconditionsAsync(async () =>
            {
                // Byte-identical payload: the signature must be unchanged.
                File.WriteAllText(ConfigPath, FullConfig(before.ToArray()));
                await World.Until(() => Mod.Config.DebuggingOptions.ReconditionOnConfigChange, WaitTimeoutTicks);
                await World.Ticks(SettleTicks);
            });

            Assert.True(
                passes == 0,
                $"An identical-config reload must not recondition (the signature gate must hold), saw {passes}");
            Assert.Equal(before, Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour);

            CarryOnParityAssert.AssertPresentInBoth(
                World.BlockAt(chestPos),
                "an identical-config reload must not recondition, so the base stays in both arrays");

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var failure);
            Assert.True(carried != null, $"Chest pickup after a no-op reload failed: {failure}");
            Assert.Equal(ChestCode, carried?.Block?.Code?.ToString());
        }

        /// <summary>
        /// R3 - the reversibility guard. Adding the prefix and then removing it again must
        /// return the base to both arrays: pass 0 re-mounts the dirty baseline entry, and
        /// the resolver must not strip it. Goes RED only if the base pass strips without
        /// marking the entry dirty (the intermediate state was the asymmetry; the
        /// inverse reload restored the block under the old shield too).
        /// </summary>
        [Trait("summary", "Adding the base-filter prefix and then removing it returns the chest's base to both behavior arrays and restores carryability.")]
        [Trait("pins", "The base pass is not a permanent strip, so the filter is reversible in-session.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MatchedChestRemovedByRecondition_BaseReturnsToBothArrays()
        {
            Assert.NotNull(World.Api);

            await ResetToFixtureAsync();

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ChestCode, chestPos);
            CarryOnParityAssert.AssertPresentInBoth(
                World.BlockAt(chestPos), "pre-edit baseline: the unfiltered chest carries a base in both arrays");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            // Add the prefix, then remove it again - the full on/off cycle.
            await WriteAndAwaitFilterAsync(
                new[] { "game:chest-" },
                "the first edit adds the chest prefix to RemoveBaseCarryableBehaviour");
            await World.Ticks(SettleTicks);

            await WriteAndAwaitFilterAsync(
                new[] { "woodchests:wtrunk" },
                "the second edit removes the chest prefix from RemoveBaseCarryableBehaviour");
            await World.Ticks(SettleTicks);

            CarryOnParityAssert.AssertPresentInBoth(
                World.BlockAt(chestPos),
                "removing the prefix must restore the chest's base in both arrays, because "
                + "the base pass is not a permanent strip");

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var failure);
            Assert.True(
                carried != null,
                $"Chest must be carryable again after the prefix is removed: {failure}");
            Assert.Equal(ChestCode, carried?.Block?.Code?.ToString());
            Assert.Equal(ChestCode, Carry.GetCarried(player.Entity, CarrySlot.Hands)?.Block?.Code?.ToString());
        }
    }
}
