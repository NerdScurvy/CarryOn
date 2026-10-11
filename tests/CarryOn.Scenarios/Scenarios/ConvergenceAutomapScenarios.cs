using System;
using System.IO;
using System.Linq;
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
    /// B5a/B5b - the automapper's two "off" edits (C5): the clones a prior run
    /// made are not removed when automapping is disabled afterwards. The
    /// automapper only ever adds; <c>AutoMapSimilar=false</c> and a gained
    /// <c>AutoMatchIgnoreMods</c> domain both merely stop <i>new</i> clones, so
    /// the block keeps a carryable that no fresh boot with the same config
    /// would give it. The Phase 2/3 convergence is the fix: the clone is
    /// baseline-owned and un-created at the start of every run, and a run with
    /// automapping off does not re-create it.
    /// <para>
    /// <c>game:flowerpot-amber</c> is the family member the flowerpot patch's
    /// variant selectors miss, so the automapper gives it a per-view clone pair
    /// from the patched siblings' donor keys - the same fixture the exclusion
    /// rows (E) use, asserted here at the registry level (the clone lives on
    /// the registry block; no placement is needed).
    /// </para>
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Automapper convergence rows: turning AutoMapSimilar off, or ignoring the donor domain, must clear a boot-era clone so an off edit converges to the fresh-boot surface rather than leaving automap residue.")]
    [AtlasDataFiles("fixtures/ConvergenceConfig", TargetPath = "ModConfig")]
    public class ConvergenceAutomapScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        /// <summary>The unpatched flowerpot family member the boot run clones.</summary>
        private const string CloneCode = "game:flowerpot-amber";

        private static readonly string[] DefaultIgnoreMods = ["mcrate"];

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>
        /// Full-file Mode B payload: every key pinned to the fixture's boot value
        /// except the one the row moves, so the only behavior-surface delta is the
        /// automap key under test.
        /// </summary>
        private static string FullConfig(bool autoMapSimilar, string[]? ignoreMods = null)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": true
              },
              "CarryablesFilters": {
                "AutoMapSimilar": {{autoMapSimilar.ToString().ToLowerInvariant()}},
                "AutoMatchIgnoreMods": [{{string.Join(", ", (ignoreMods ?? DefaultIgnoreMods).Select(m => $"\"{m}\""))}}]
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
                    + $"Now: AutoMapSimilar={Mod.Config.CarryablesFilters.AutoMapSimilar}, "
                    + $"AutoMatchIgnoreMods=[{string.Join(", ", Mod.Config.CarryablesFilters.AutoMatchIgnoreMods)}], "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>Re-seeds the class fixture so rows are order-independent.</summary>
        private async Task ResetToFixtureAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            File.WriteAllText(ConfigPath, FullConfig(autoMapSimilar: true));
            await WaitForConfigAsync(
                c => c.CarryablesFilters.AutoMapSimilar
                    && c.CarryablesFilters.AutoMatchIgnoreMods.SequenceEqual(DefaultIgnoreMods),
                "re-seeding the class fixture (AutoMapSimilar=true, default ignore mods)");
            await World.Ticks(20); // > 500 ms debounce - next write's event is not swallowed
        }

        /// <summary>The cloned registry block with its measured boot state, or a row-premise failure.</summary>
        private (Block Block, CarryableParity Before) ClonedBlockWithBootState()
        {
            var clone = World.Api!.World.GetBlock(new AssetLocation(CloneCode));
            Assert.True(
                clone != null,
                $"Row premise: the registry must hold '{CloneCode}'");
            var before = CarryOnParityAssert.Inspect(clone!);
            Assert.True(
                before.InBothSides && !before.SharedInstance,
                $"Row premise: '{CloneCode}' must hold the boot run's automap clone as a "
                + $"per-view pair (patched siblings donate the keys; the amber variant is "
                + $"unpatched, so what it holds can only be a clone). Actual: {before.Describe()}");
            return (clone!, before);
        }

        /// <summary>
        /// B5a - turning <c>AutoMapSimilar</c> off must converge to the fresh-boot
        /// surface: with automapping disabled, no run would clone the unpatched
        /// family member, so the clone the boot run made must be gone from both
        /// arrays. The automapper has no removal path, so today the clone persists.
        /// </summary>
        [Trait("summary", "Flipping AutoMapSimilar off clears the boot-era clone of game:flowerpot-amber from both behavior arrays.")]
        [Trait("pins", "An off edit converges to the fresh-boot surface instead of leaving automap residue.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the automapper only ever added, so the boot-era clone survived a config no fresh boot would clone it under.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task AutomapDisabled_BootClone_GoneFromBothArrays()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();

            var (clone, _) = ClonedBlockWithBootState();

            File.WriteAllText(ConfigPath, FullConfig(autoMapSimilar: false));
            await WaitForConfigAsync(
                c => !c.CarryablesFilters.AutoMapSimilar,
                "config flips AutoMapSimilar to false");
            await World.Ticks(SettleTicks);

            CarryOnParityAssert.AssertAbsentFromBoth(
                clone,
                "AutoMapSimilar=false must converge to the fresh-boot surface - a fresh "
                + "boot with this config would never clone the unpatched 'game:flowerpot-amber', "
                + "so the boot-era clone must be gone from both arrays, not merely un-refreshed "
                + "(C5a: the automapper only ever adds)");
        }

        /// <summary>
        /// B5b - adding the <c>game</c> domain to <c>AutoMatchIgnoreMods</c> must
        /// likewise converge: ignoring the donor mod's domain stops new clones, and
        /// the clones the boot run already made must not survive the edit.
        /// </summary>
        [Trait("summary", "Adding the game domain to AutoMatchIgnoreMods likewise clears the boot-era clone of game:flowerpot-amber from both behavior arrays.")]
        [Trait("pins", "The automap ignore filter is convergent, not merely forward-blocking.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the ignore list gated only new clones and had no removal path for the boot-era clone.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DonorDomainIgnored_BootClone_GoneFromBothArrays()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();

            var (clone, _) = ClonedBlockWithBootState();

            File.WriteAllText(ConfigPath, FullConfig(
                autoMapSimilar: true,
                ignoreMods: [.. DefaultIgnoreMods, "game"]));
            await WaitForConfigAsync(
                c => c.CarryablesFilters.AutoMatchIgnoreMods.Contains("game"),
                "config adds the game domain to AutoMatchIgnoreMods");
            await World.Ticks(SettleTicks);

            CarryOnParityAssert.AssertAbsentFromBoth(
                clone,
                "ignoring the donor domain must converge to the fresh-boot surface - a "
                + "fresh boot with this config would never clone 'game:flowerpot-amber', so "
                + "the boot-era clone must be gone from both arrays (C5b: the ignore list "
                + "gates new clones only; it has no removal path)");
        }
    }
}
