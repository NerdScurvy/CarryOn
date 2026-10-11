using System;
using System.IO;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Mode B rows for the "false at start" half of the CarryablesOnBack repro: the class fixture boots with
    /// <c>CarryablesOnBack.Barrel = false</c>, so the boot conditioning pass strips the
    /// barrel's Back slot before any reload. Flipping the flag to true must then
    /// restore the slot - expected RED today (restore only fires when the whole
    /// behavior is absent, and the barrel keeps its hands behavior).
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Mode B boot-off rows for the CarryablesOnBack toggle: the fixture boots with CarryablesOnBack.Barrel=false so the boot pass strips the barrel's Back slot, and a later flip to true must restore it.")]
    [AtlasDataFiles("fixtures/OnBackBarrelOffConfig", TargetPath = "ModConfig")]
    public class CarryablesOnBackBootOffScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;          // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;                // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        private void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

        private static string FullConfigC(bool barrelOnBack)
            => $$"""
            {
              "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
              "Carryables": {
                "Barrel": true
              },
              "CarryablesOnBack": {
                "Barrel": {{barrelOnBack.ToString().ToLowerInvariant()}}
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}}
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": true
              }
            }
            """;

        private async Task SelfSeedBaselineAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            WriteConfig(FullConfigC(barrelOnBack: false));
            await WaitForConfigAsync(
                c => c.Carryables.Barrel && !c.CarryablesOnBack.Barrel
                    && c.CarryOptions.InteractSpeedMultiplier == SeedSpeed
                    && c.DebuggingOptions.ReconditionOnConfigChange,
                "Mode B baseline (Barrel carryable, onBack=false, speed=2.0, flag on)");
            await World.Ticks(20); // > 500 ms debounce - next write's event is not swallowed
        }

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
                    + $"Now: Carryables.Barrel={Mod.Config.Carryables.Barrel}, "
                    + $"CarryablesOnBack.Barrel={Mod.Config.CarryablesOnBack.Barrel}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        private Block ResolveBarrel()
        {
            var barrel = World.Api.World.GetBlock(new AssetLocation("barrel"));
            Assert.NotNull(barrel);
            Assert.NotNull(barrel!.Code);
            return barrel;
        }

        private string BarrelCode => ResolveBarrel().Code!.ToString();

        private BlockBehaviorCarryable? BarrelBehavior() => ResolveBarrel().GetBehavior<BlockBehaviorCarryable>();

        /// <summary>
        /// N5 - the "false at start" repro: booting with onBack=false strips the Back
        /// slot at boot (fixture proof), and flipping it to true must restore it - live
        /// Slots and end-to-end pickup into the back slot. Expected RED today.
        /// </summary>
        [Trait("summary", "Booting with onBack=false strips the barrel's Back slot at boot while the hands slot stays live, and flipping the flag true afterwards restores the Back slot and the end-to-end back pickup.")]
        [Trait("pins", "A Back slot stripped at boot is restored when the onBack condition is re-enabled, exactly like a live strip.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed against the pre-fix head, where restore only fired when the whole behavior was absent and the barrel kept its hands behavior.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BootOff_OnBackEnable_RestoresBackSlot()
        {
            Assert.NotNull(World.Api);

            // The fixture landed: the boot pass stripped the Back slot, hands stayed live.
            var bootBehavior = BarrelBehavior();
            Assert.NotNull(bootBehavior);
            Assert.True(bootBehavior!.Slots[CarrySlot.Hands] != null, "hands slot follows the block-level condition, untouched by onBack=false");
            Assert.Null(bootBehavior.Slots[CarrySlot.Back]);

            await SelfSeedBaselineAsync();

            WriteConfig(FullConfigC(barrelOnBack: true));
            await WaitForConfigAsync(c => c.CarryablesOnBack.Barrel, "CarryablesOnBack.Barrel flips to true");
            await World.Ticks(SettleTicks);

            var behavior = BarrelBehavior();
            Assert.NotNull(behavior);
            Assert.True(behavior!.Slots[CarrySlot.Back] != null,
                "Back slot was not restored after enabling CarryablesOnBack.Barrel post-boot");

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(BarrelCode, pos);
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var backCarried = Actions.PickUpIntoSlot(player.Entity, pos, CarrySlot.Back, out var backCode);
            Assert.True(backCarried != null, $"Back pickup failed after post-boot enable: {backCode}");
            Assert.NotNull(World.Api);
        }
    }
}
