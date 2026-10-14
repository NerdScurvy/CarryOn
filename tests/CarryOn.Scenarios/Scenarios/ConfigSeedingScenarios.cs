using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// [AtlasDataFiles] seeds the server's data path before boot, so CarryOn's
    /// ModConfig.Load sees a CarryOnConfig.json it did not generate. The seed lands in
    /// ModConfig/ and flips the loaded model + world config tree. A carryable disabled in
    /// the seed (Carryables.Chest=false) must fail pickup end to end: the conditionally-
    /// disabled behavior is now removed only after the automapper has run (final pass),
    /// so the disabled chest is never re-enabled from a "similar" enabled block.
    /// </summary>
    [Trait("area", "Config seeding")]
    [Trait("summary", "Seeds ModConfig before boot so a config-disabled carryable is rejected end to end while an enabled one still works, proving the seed reaches the loaded model and world config tree.")]
    [AtlasDataFiles("fixtures/ModConfig", TargetPath = "ModConfig")]
    public class ConfigSeedingScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// The seed disables chests; the chest block must keep its carryable behavior off
        /// and reject pickup with the not-carryable failure code, staying in the world.
        /// Regression guard for the deferred condition-removal fix: a chest whose
        /// seed config disables it must not be stripped early enough to lose the
        /// not-carryable failure code.
        /// </summary>
        [Trait("summary", "With the seed disabling Carryables.Chest, pickup rejects not-carryable, the chest stays in the world and the hands slot stays empty.")]
        [Trait("pins", "A Carryables toggle set false is effective end to end and the automapper never re-enables a disabled block from a similar enabled source.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ChestDisabled_BySeedConfig_CannotBePickedUp()
        {
            Assert.False(Mod.Config.Carryables.Chest, "Seed did not disable chest carryables");

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);

            Assert.True(carried == null, "Chest must not be pickable when Carryables.Chest=false");
            Assert.Equal(FailureCodes.NotCarryable, failureCode);
            Assert.Equal("game:chest-east", World.BlockAt(pos).Code?.ToString());
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// The seed leaves crates enabled; a crate must still be pickable, proving the
        /// seeded config boots cleanly and does not disable carryables wholesale.
        /// </summary>
        [Trait("summary", "A crate the seed leaves enabled still picks up normally, clearing the world cell.")]
        [Trait("pins", "The seed boots cleanly and does not disable carryables wholesale.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task CrateEnabled_BySeedConfig_StillPicksUp()
        {
            Assert.True(Mod.Config.Carryables.Crate, "Seed unexpectedly changed crate carryables");

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:crate", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);

            Assert.True(carried != null, $"Crate pickup failed: {failureCode}");
            Assert.Equal("game:crate", carried?.Block?.Code?.ToString());
            Assert.Equal(0, World.BlockAt(pos).Id);
        }
    }
}