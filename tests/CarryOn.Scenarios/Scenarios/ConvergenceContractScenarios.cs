using System;
using System.IO;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// B8 - the convergence contract, frozen by Phase 0: <b>N successive mixed
    /// edits must leave the same surface a fresh boot with the final config
    /// would have.</b> Every per-pass ownership record is an optimization of
    /// this contract, never a substitute for it; every other B row is one
    /// edit's slice of it, and this row composes three edits of three
    /// different kinds - a family flip off, an automap filter off, the family
    /// flip back on - ending on a config whose fresh-boot surface is well
    /// defined and checkable.
    /// <para>
    /// The final config (Chest=true, AutoMapSimilar=false) mounts the chest -
    /// captured at boot, enabled, reconcile-owned - and would never clone
    /// <c>game:flowerpot-amber</c>, the unpatched flowerpot family member the
    /// boot run (automap on) cloned. The chest is the green control; the clone
    /// is the divergence: the automapper has no removal path, so today the
    /// boot-era clone survives edits no fresh boot would repeat it under.
    /// </para>
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "The convergence contract composed: N successive mixed config edits must leave the same carried-behavior surface a fresh boot with the final config would produce.")]
    [AtlasDataFiles("fixtures/ConvergenceConfig", TargetPath = "ModConfig")]
    public class ConvergenceContractScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private const string ChestCode = "game:chest-east";
        private const string CloneCode = "game:flowerpot-amber";

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>
        /// Full-file Mode B payload: every key pinned to the fixture's boot value
        /// except the two the sequence moves (Chest, AutoMapSimilar).
        /// </summary>
        private static string FullConfig(bool chest, bool autoMapSimilar)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": {{chest.ToString().ToLowerInvariant()}}
              },
              "CarryablesFilters": {
                "AutoMapSimilar": {{autoMapSimilar.ToString().ToLowerInvariant()}}
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}},
                "AllowHighCapacityStorageOnBack": false,
                "PickupGroundStorageBagWhileCarrying": false
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": true
              }
            }
            """;

        /// <summary>Polls <see cref="Mod.Config"/> once per tick until <paramref name="predicate"/> holds.</summary>
        private async Task WaitForConfigAsync(System.Func<CarryOn.Common.Models.CarryOnConfig, bool> predicate, string because, int timeoutTicks = WaitTimeoutTicks)
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
                    + $"AutoMapSimilar={Mod.Config.CarryablesFilters.AutoMapSimilar}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>Re-seeds the class fixture so rows are order-independent.</summary>
        private async Task ResetToFixtureAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            File.WriteAllText(ConfigPath, FullConfig(chest: true, autoMapSimilar: true));
            await WaitForConfigAsync(
                c => c.Carryables.Chest && c.CarryablesFilters.AutoMapSimilar
                    && c.DebuggingOptions.ReconditionOnConfigChange,
                "Mode B baseline (Chest=true, AutoMapSimilar=true, flag on)");
            await World.Ticks(20); // > 500 ms debounce - next write's event is not swallowed
        }

        /// <summary>
        /// Three successive mixed edits (family off, filter off, family back on) must
        /// leave the surface a fresh boot with the final config would produce: the chest
        /// mounted (green control - the captured-conditional path is reversible today),
        /// the boot-era flowerpot-amber clone gone (divergence - with automapping off no
        /// fresh boot would clone it, but the automapper only ever adds).
        /// </summary>
        [Trait("summary", "After a family-off, automap-filter-off, family-back-on sequence the chest is mounted as a fresh boot would mount it and the boot-era flowerpot-amber clone is gone.")]
        [Trait("pins", "N successive config edits converge to the fresh-boot surface of the final config.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MixedEdits_FinalSurface_EqualsFreshBootSurface()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();

            var chest = World.Api.World.GetBlock(new AssetLocation(ChestCode));
            var clone = World.Api.World.GetBlock(new AssetLocation(CloneCode));
            Assert.True(
                chest != null && clone != null,
                $"Row premise: the registry must hold '{ChestCode}' and '{CloneCode}'");

            // Edit 1 - family flip off: the chest's carryable strips.
            File.WriteAllText(ConfigPath, FullConfig(chest: false, autoMapSimilar: true));
            await WaitForConfigAsync(
                c => !c.Carryables.Chest && c.CarryablesFilters.AutoMapSimilar,
                "edit 1 flips Carryables.Chest to false");
            await World.Ticks(SettleTicks);

            // Edit 2 - automap filter off: no new clone can be made from here on.
            File.WriteAllText(ConfigPath, FullConfig(chest: false, autoMapSimilar: false));
            await WaitForConfigAsync(
                c => !c.Carryables.Chest && !c.CarryablesFilters.AutoMapSimilar,
                "edit 2 flips AutoMapSimilar to false");
            await World.Ticks(SettleTicks);

            // Edit 3 - family flip back on: the chest's carryable returns.
            File.WriteAllText(ConfigPath, FullConfig(chest: true, autoMapSimilar: false));
            await WaitForConfigAsync(
                c => c.Carryables.Chest && !c.CarryablesFilters.AutoMapSimilar,
                "edit 3 flips Carryables.Chest back to true");
            await World.Ticks(SettleTicks);

            // The fresh-boot surface for the final config (Chest=true,
            // AutoMapSimilar=false): the chest is captured and enabled, so a fresh
            // boot mounts it - the green control (the captured-conditional leg of
            // the sequence is reversible today, and must stay reversible).
            CarryOnParityAssert.AssertPresentInBoth(
                chest!,
                "the convergence contract: after the mixed sequence the chest must be "
                + "mounted exactly as a fresh boot with the final config would mount it");

            // The divergence: a fresh boot with AutoMapSimilar=false would never clone
            // the unpatched family member, but the clone the FIRST run (the boot run,
            // automap on) made is still mounted.
            CarryOnParityAssert.AssertAbsentFromBoth(
                clone!,
                "the convergence contract: N successive edits must converge to the "
                + "fresh-boot surface of the final config - with AutoMapSimilar=false a "
                + $"fresh boot would never clone '{CloneCode}', so the boot-era clone must "
                + "be gone from both arrays, not still mounted because the automapper "
                + "only ever adds (the C5a divergence, composed into a sequence)");
        }
    }
}
