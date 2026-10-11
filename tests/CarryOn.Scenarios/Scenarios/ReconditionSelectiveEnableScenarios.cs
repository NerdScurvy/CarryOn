using System;
using System.IO;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Mode B selective-enable row (<c>DebuggingOptions.ReconditionOnConfigChange = true</c>,
    /// seeded by the class fixture): the world boots with <c>Carryables.Chest</c> and
    /// <c>Carryables.Anvil</c> both disabled, and a live config edit enables only the
    /// anvil. After the recondition, a placed chest, labeled chest and anvil are all
    /// attempted: only the anvil may be carryable - the still-disabled chest family
    /// must stay non-carryable (no recondition resurrection, and no automap clone:
    /// the chest and labeled chest are behaviorless from the boot strip onward, so a
    /// broken recondition pass would expose them as automap targets - the regression
    /// shape behind the instance-owned reconcile fix), while the anvil must
    /// be fully functional end to end: pick it up, then place it down again.
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Mode B selective-enable row: booting with the chest and anvil toggles off, a live edit that enables only the anvil must move exactly the anvil family and never resurrect the still-disabled chests.")]
    [AtlasDataFiles("fixtures/ReconditionSelectiveEnableConfig", TargetPath = "ModConfig")]
    public class ReconditionSelectiveEnableScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;          // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;                // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private const string ChestCode = "game:chest-east";
        private const string LabeledChestCode = "game:labeledchest-east";
        private const string AnvilCode = "game:anvil-iron";

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        private void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

        /// <summary>
        /// Full-file Mode B payload: keeps the recondition flag on and pins every
        /// signature-relevant key to the fixture's boot values except the one this row
        /// moves (Anvil), so the reload's behavior surface delta is exactly the anvil
        /// enable. Omitted keys would deserialize to defaults - Anvil's default is
        /// true, so it must always be written explicitly.
        /// </summary>
        private static string FullConfig(bool chest, bool anvil)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": {{chest.ToString().ToLowerInvariant()}},
                "Anvil": {{anvil.ToString().ToLowerInvariant()}}
              },
              "CarryablesFilters": {
                "AutoMapSimilar": true
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}},
                "AllowHighCapacityStorageOnBack": false
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
                    + $"Now: Chest={Mod.Config.Carryables.Chest}, Anvil={Mod.Config.Carryables.Anvil}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>
        /// Chest and anvil toggles both off at boot; a live edit enables only the anvil;
        /// then each placed block is attempted in turn. Only the anvil may be carried -
        /// and it must survive a full round trip (pickup, then putdown).
        /// </summary>
        [Trait("summary", "With chest and anvil disabled at boot, a live anvil-only enable makes only the anvil carryable (pickup and putdown) while the chest and labeled chest stay denied.")]
        [Trait("pins", "A Mode B selective enable moves exactly the toggled family, and still-disabled conditional blocks are never automap targets on any run.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while a boot-stripped behaviorless chest received a carryable cloned from a same-signature donor at recondition time.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task LiveAnvilEnable_OnlyAnvilCarryable_ChestsStayDenied()
        {
            Assert.NotNull(World.Api);

            // Boot guard - proves the fixture landed (an Anvil=true default would
            // degrade this row into the ordinary chest-disabled case).
            Assert.False(Mod.Config.Carryables.Chest, "fixture must boot with Carryables.Chest = false");
            Assert.False(Mod.Config.Carryables.Anvil, "fixture must boot with Carryables.Anvil = false");
            Assert.True(Mod.Config.DebuggingOptions.ReconditionOnConfigChange, "fixture must boot in Mode B");

            // Place the three blocks while both toggles are still off.
            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            BlockPos labeledPos = World.Spawn.Offset(4, 1, 0);
            BlockPos anvilPos = World.Spawn.Offset(6, 1, 0);
            World.SetBlock(ChestCode, chestPos);
            World.SetBlock(LabeledChestCode, labeledPos);
            World.SetBlock(AnvilCode, anvilPos);

            // Pre-flip: none of the three is carryable (boot strip removed the
            // disabled conditionals).
            Assert.Null(World.BlockAt(chestPos)?.GetBehavior<BlockBehaviorCarryable>());
            Assert.Null(World.BlockAt(labeledPos)?.GetBehavior<BlockBehaviorCarryable>());
            Assert.Null(World.BlockAt(anvilPos)?.GetBehavior<BlockBehaviorCarryable>());

            // A player is in-game while the admin flips only the anvil toggle.
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(4, 0, 2));

            WriteConfig(FullConfig(chest: false, anvil: true));
            await WaitForConfigAsync(
                c => c.Carryables.Anvil && !c.Carryables.Chest,
                "config flips Carryables.Anvil to true, Chest stays false");
            await World.Ticks(SettleTicks); // recondition completes inside the reload dispatch

            // Post-flip registry: the anvil regained its carryable; the chest family
            // did not - and received no automap substitute while behaviorless.
            Assert.Null(World.BlockAt(chestPos)?.GetBehavior<BlockBehaviorCarryable>());
            Assert.Null(World.BlockAt(labeledPos)?.GetBehavior<BlockBehaviorCarryable>());
            Assert.NotNull(World.BlockAt(anvilPos)?.GetBehavior<BlockBehaviorCarryable>());

            // Attempt the chest: pickup must be denied, nothing carried.
            var carriedChest = Actions.PickUpIntoHands(player.Entity, chestPos, out var chestFailure);
            Assert.True(carriedChest == null,
                $"Chest pickup must stay denied after the anvil-only enable (Chest=false): {chestFailure}");
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));

            // Attempt the labeled chest: same denial.
            var carriedLabeled = Actions.PickUpIntoHands(player.Entity, labeledPos, out var labeledFailure);
            Assert.True(carriedLabeled == null,
                $"Labeled chest pickup must stay denied after the anvil-only enable (Chest=false): {labeledFailure}");
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));

            // Attempt the anvil: pickup succeeds, then the putdown round trip.
            var carriedAnvil = Actions.PickUpIntoHands(player.Entity, anvilPos, out var anvilFailure);
            Assert.True(carriedAnvil != null, $"Anvil pickup after the live enable failed: {anvilFailure}");
            Assert.Equal(AnvilCode, carriedAnvil?.Block?.Code?.ToString());

            BlockPos target = World.Spawn.Offset(8, 1, 0);
            var placedAt = Actions.PlaceDown(player.Player, CarrySlot.Hands, CarryActions.AimDownAt(target), out var placeFailure);
            Assert.True(placedAt != null, $"Anvil putdown failed: {placeFailure}");
            Assert.Equal(target, placedAt);
            Assert.Equal(AnvilCode, World.BlockAt(target)?.Code?.ToString());
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }
    }
}
