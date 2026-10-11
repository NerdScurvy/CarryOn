using System;
using System.IO;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// B7 - an actively-carried block across a recondition. The Phase 0 churn
    /// audit found exactly one instance-keyed cache:
    /// <c>CarriedBlock.CachedCarryableBehavior</c> (property on the CarryOnLib API
    /// model, populated by <see cref="CarryExtensions.GetCarryableBehavior(CarriedBlock)"/>
    /// at <c>CarryExtensions.cs:95-101</c>). A recondition churns behavior identity
    /// (pass 0 re-mounts every dirty entry as a fresh instance), so a value
    /// captured at pickup time kept resolving the carry through the unmounted
    /// pre-churn instance. The snapshot broadcast and the new registry surface are
    /// both correct today - those are this row's premise asserts - the cache was the
    /// one stale path, and Phase 4 removed it: the accessor now resolves the block's
    /// current carryable on every call.
    /// <para>
    /// The row asserts the contract itself - a carry held across a recondition
    /// resolves through the block's *current* behavior instance - not one
    /// implementation's internal state. Phase 0 established that no invalidation
    /// site could reach the live instances (<c>CarriedBlockTreeSerializer.DeserializeSafe</c>
    /// builds a fresh <see cref="CarriedBlock"/> on every read, so enumerating carried
    /// blocks hands back objects nobody is holding), so the accessor was the only
    /// place that could be made correct.
    /// </para>
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Churn row: a block carried across a behavior-relevant recondition must resolve its carryable through the block's current behavior instance, never an unmounted pre-churn one.")]
    [AtlasDataFiles("fixtures/ConvergenceConfig", TargetPath = "ModConfig")]
    public class ConvergenceChurnScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private const string ChestCode = "game:chest-east";

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>
        /// Full-file Mode B payload: every key pinned to the fixture's boot value
        /// except <c>AllowHighCapacityStorageOnBack</c> - a behavior-relevant
        /// CarryOptions key (R4's proof that the flip dirties the gate), moved here
        /// because it changes no Carryables family the row depends on.
        /// </summary>
        private static string FullConfig(bool allowHighCapacityStorageOnBack)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": true
              },
              "CarryablesFilters": {
                "AutoMapSimilar": true
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}},
                "AllowHighCapacityStorageOnBack": {{allowHighCapacityStorageOnBack.ToString().ToLowerInvariant()}}
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": true
              }
            }
            """;

        /// <summary>Polls <see cref="Mod.Config"/> once per tick until <paramref name="predicate"/> holds.</summary>
        private async Task WaitForConfigAsync(Func<CarryOn.Common.Models.CarryOnConfig, bool> predicate, string because, int timeoutTicks = WaitTimeoutTicks)
        {
            try
            {
                await World.Until(() => predicate(Mod.Config), timeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    $"Timed out after {timeoutTicks} ticks waiting for config state - {because}. "
                    + $"Now: Chest={Mod.Config.Carryables.Chest}, "
                    + $"AllowHighCapacityStorageOnBack={Mod.Config.CarryOptions.AllowHighCapacityStorageOnBack}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>Re-seeds the class fixture so rows are order-independent.</summary>
        private async Task ResetToFixtureAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            File.WriteAllText(ConfigPath, FullConfig(allowHighCapacityStorageOnBack: false));
            await WaitForConfigAsync(
                c => c.Carryables.Chest && !c.CarryOptions.AllowHighCapacityStorageOnBack
                    && c.DebuggingOptions.ReconditionOnConfigChange,
                "Mode B baseline (Chest=true, AllowHighCapacityStorageOnBack=false, flag on)");
            await World.Ticks(20); // > 500 ms debounce - next write's event is not swallowed
        }

        /// <summary>
        /// Joins a player, lets the Mode B join-time snapshot send land, then clears packet
        /// captures - so the later broadcast assertion cannot be polluted by the join send.
        /// </summary>
        private async Task<ITestPlayer> JoinAndClearAsync(string name)
        {
            ITestPlayer player = await JoinPlayer(name);
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await World.Ticks(SettleTicks); // join send must land BEFORE the Clear
            player.Client.Clear();
            return player;
        }

        /// <summary>
        /// A chest picked up before a signature-dirty recondition must resolve through the
        /// post-recondition behavior instance. The recondition broadcast a snapshot and
        /// churned the chest's behavior identity (the premise asserts below) - the carried
        /// block's cache is the one path left holding the unmounted pre-churn instance.
        /// </summary>
        [Trait("summary", "A chest picked up before a signature-dirty recondition resolves its carryable through the post-recondition instance, after asserting the behavior identity actually churned.")]
        [Trait("pins", "A carry held across a recondition never resolves a behavior no run mounted.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ActiveCarry_AcrossRecondition_ResolvesCurrentInstance()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ChestCode, pos);
            var chestBlock = World.Api.World.BlockAccessor.GetBlock(pos);
            Assert.True(
                chestBlock.GetBehavior<BlockBehaviorCarryable>() != null,
                "Row premise: the fixture boots with Chest=true, so the chest must start "
                + "carryable before the pickup");

            ITestPlayer player = await JoinAndClearAsync("Tester");

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failure);
            Assert.True(
                carried != null,
                $"Row premise: chest pickup failed: {failure}");
            Assert.Equal(ChestCode, carried?.Block?.Code?.ToString());

            // Resolve the carryable the way the render path does, so the row holds a real
            // pre-churn resolution it can compare the post-recondition one against.
            var preChurn = carried!.GetCarryableBehavior();
            Assert.NotNull(preChurn);

            // The churn: a behavior-relevant CarryOptions flip reconditions while the
            // chest is actively carried.
            File.WriteAllText(ConfigPath, FullConfig(allowHighCapacityStorageOnBack: true));
            await WaitForConfigAsync(
                c => c.CarryOptions.AllowHighCapacityStorageOnBack,
                "AllowHighCapacityStorageOnBack flips to true");
            await World.Ticks(SettleTicks);

            // Premise: the recondition broadcast a snapshot (the client churn path), and
            // the chest's behavior identity churned with it (pass 0 re-mounts the
            // dirty entry as a fresh instance).
            Assert.NotEmpty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            var live = carried!.Block!.GetBehavior<BlockBehaviorCarryable>();
            Assert.True(
                live != null,
                "Row premise: the reconditioned registry must still mount the chest's carryable");
            Assert.True(
                !ReferenceEquals(preChurn, live),
                "Row premise: the recondition must churn the chest's behavior identity - "
                + "pass 0 re-mounts the dirty entry as a fresh instance, so the "
                + "pre-churn instance is unmounted");

            // The accessor: it must resolve through the block's current behavior instance,
            // never through an instance an earlier resolution captured.
            var resolved = carried.GetCarryableBehavior();
            Assert.True(
                ReferenceEquals(resolved, live),
                "a carry held across a recondition must resolve through the current "
                + "behavior instance - GetCarryableBehavior still returns the unmounted "
                + "pre-churn instance, so the carried block keeps resolving a behavior no "
                + "run mounted (the one instance-keyed cache must be revalidated on access)");
        }
    }
}
