using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// L3 premise row, config-honesty half - the row that makes the fix's *purpose*
    /// falsifiable: with <c>Interactables.Storage = false</c> seeded, the interact
    /// automapper still twin-creates the fired storage vessels at the start of every
    /// conditioning pass (the automap runs before the strip), and the strip must
    /// remove the twin from both views. On the pre-fix head it could not: the twin
    /// was appended without <c>Initialize</c>, so its <c>EnabledCondition</c> was
    /// <c>""</c> and <c>RemoveDisabledConditionalBehaviors</c> skipped it
    /// (<c>BlockFilter</c> strips only a non-empty condition that evaluates false) -
    /// the blank twin kept granting interact-while-carrying after the user disabled
    /// it. Red by construction at the first assertion on that head; the fix gave the
    /// twin the donor's condition, which makes it strippable.
    /// <para>
    /// Mirrors <c>HandlerPathInteractDoorOffScenarios</c>'s seeded-config shape, but
    /// asserts on an automapped <b>twin</b>, not a patched donor: that row's wattlegate
    /// is a directly-patched source block, and the EntityClass-based interact
    /// automapper never reaches gates anyway (door/gate entityClasses are
    /// <c>Generic</c>/null), so it cannot cover the twin's lifecycle.
    /// </para>
    /// <para>
    /// Both runs are asserted, not just the first - the convergence discipline
    /// <c>ConvergenceAutomapScenarios</c> applies to the carryable pass. The automap
    /// re-creates its twins on every pass and runs <i>before</i> the strip, so a
    /// one-pass assertion would pass even if the strip failed to converge on the
    /// second run (donor interactions are dirty-remounted at pass 0, which puts a
    /// fresh donor back in front of the automapper).
    /// </para>
    /// <para>
    /// Fixture: <c>StorageOffConfig</c> seeds <c>Interactables.Storage = false</c> and
    /// Mode B (<c>ReconditionOnConfigChange = true</c>) so a mid-row config edit can
    /// trigger the second conditioning run; <c>AutoMapSimilar = true</c> is pinned,
    /// also its default.
    /// </para>
    /// </summary>
    [Trait("area", "Handler path")]
    [Trait("summary", "With Interactables.Storage seeded off in Mode B, the automapped interact twin is stripped from both behavior arrays at boot and stays stripped after a second conditioning run.")]
    [AtlasDataFiles("fixtures/StorageOffConfig", TargetPath = "ModConfig")]
    public class InteractTwinStripScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch

        /// <summary>The measured automapped interact twin (see InteractTwinPayloadScenarios).</summary>
        private const string TwinCode = "game:storagevessel-blue-fired";

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>
        /// Full-file Mode B payload for the run-2 trigger: every signature key pinned
        /// to the fixture's boot value except <c>Carryables.Chest</c>, so the only
        /// behavior-surface delta is the dirty bit that makes the gate re-run
        /// conditioning - <c>Interactables.Storage</c> stays off and
        /// <c>AutoMapSimilar</c> stays on through both runs.
        /// </summary>
        private static string FullConfig(bool chest) => $$"""
        {
          "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
          "Carryables": {
            "Chest": {{chest.ToString().ToLowerInvariant()}}
          },
          "CarryablesFilters": {
            "AutoMapSimilar": true
          },
          "Interactables": {
            "Storage": false
          },
          "DebuggingOptions": {
            "ReconditionOnConfigChange": true
          }
        }
        """;

        /// <summary>Polls <see cref="Mod.Config"/> once per tick until <paramref name="predicate"/> holds.</summary>
        private async Task WaitForConfigAsync(System.Func<CarryOnConfig, bool> predicate, string because, int timeoutTicks = WaitTimeoutTicks)
        {
            try
            {
                await World.Until(() => predicate(Mod.Config), timeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    $"Timed out after {timeoutTicks} ticks waiting for config state - {because}. "
                    + $"Now: Carryables.Chest={Mod.Config.Carryables.Chest}, "
                    + $"Interactables.Storage={Mod.Config.Interactables.Storage}, "
                    + $"AutoMapSimilar={Mod.Config.CarryablesFilters.AutoMapSimilar}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>
        /// The twin must hold no interact behavior in either view - the OR-semantics
        /// counterpart of <c>CarryOnParityAssert.AssertAbsentFromBoth</c>, read off the
        /// raw arrays (conditioning reassigns them; a GetBehavior cache can go stale).
        /// </summary>
        private static void AssertTwinStrippedFromBoth(Block block, string when)
        {
            var blockSide = block.BlockBehaviors.OfType<BlockBehaviorCarryableInteract>().Count();
            var collectibleSide = block.CollectibleBehaviors.OfType<BlockBehaviorCarryableInteract>().Count();
            Assert.True(blockSide == 0 && collectibleSide == 0,
                $"Interactables.Storage=false must strip the automapped interact twin from BOTH "
                + $"arrays ({when}) - a twin still present means it was appended without "
                + "Initialize, so its EnabledCondition is empty and the strip skips it: "
                + "interact-while-carrying would survive a disabled toggle (the config-honesty "
                + "hole L3 closes; see BehaviorAutoMapper's interact pass). "
                + $"{block.Code}: block-side={blockSide}, collectible-side={collectibleSide}");
        }

        /// <summary>
        /// L3 - with <c>Interactables.Storage = false</c>, the automapped twin is
        /// stripped by the boot run's final pass, and stays stripped after a second
        /// conditioning run that re-runs the automap before the strip.
        /// </summary>
        [Trait("summary", "With Interactables.Storage off, the automapped interact twin is stripped from both the block-side and collectible-side arrays on the boot run and again after a dirty re-triggered second run.")]
        [Trait("pins", "A disabled toggle removes automapped interact twins end to end, converging across runs.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the twin's empty enabled condition made it unstrippable and interact-while-carrying survived the disabled toggle.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StorageOff_AutomappedTwin_StrippedOnBootAndSecondRun()
        {
            Assert.NotNull(World.Api);

            // Seed premises - a missed fixture flag degrades the row to a default
            // (Storage=true) world where nothing is supposed to be stripped.
            Assert.False(Mod.Config.Interactables.Storage,
                "Seed did not disable Interactables.Storage");
            Assert.True(Mod.Config.CarryablesFilters.AutoMapSimilar,
                "Seed did not keep CarryablesFilters.AutoMapSimilar on - without it the interact "
                + "automapper never creates the twin this row measures");
            Assert.True(Mod.Config.DebuggingOptions.ReconditionOnConfigChange,
                "Seed did not boot Mode B - without it no config edit can trigger the second "
                + "conditioning run this row asserts after");

            var twin = World.Api!.World.GetBlock(new AssetLocation(TwinCode));
            Assert.True(twin != null && twin.Id != 0,
                $"Row premise: the registry must hold '{TwinCode}' (the fired storage vessel - "
                + "clay/fired/storagevessel.json is not matched by the clay/storagevessel* interact "
                + "patch glob, so it can only ever hold an automapper twin)");

            // Run 1: the boot conditioning pass. The automap created the twin before the
            // strip ran; under the pre-fix code the blank twin survives it.
            AssertTwinStrippedFromBoth(twin!, "boot run (run 1)");

            // Run 2: a dirty flip that moves only Carryables.Chest in the signature, so
            // the gate re-runs conditioning with Storage still off and automap still on.
            File.WriteAllText(ConfigPath, FullConfig(chest: false));
            await WaitForConfigAsync(
                c => !c.Carryables.Chest,
                "config flips Carryables.Chest to false (run 2 trigger; Interactables.Storage stays off)");
            await World.Ticks(SettleTicks); // recondition + broadcast complete inside the reload

            var afterSecondRun = World.Api!.World.GetBlock(new AssetLocation(TwinCode));
            Assert.NotNull(afterSecondRun);
            AssertTwinStrippedFromBoth(afterSecondRun!,
                "second conditioning run (run 2) - pass 0 dirty-remounts the stripped donor "
                + "interactions, the automap (:60) re-creates the twin from them, and the strip "
                + "(:64) must remove it again");

            Assert.NotNull(World.Api);
        }
    }
}
