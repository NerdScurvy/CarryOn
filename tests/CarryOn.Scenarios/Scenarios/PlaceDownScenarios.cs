using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    [Trait("area", "Smoke")]
    [Trait("summary", "Picks a chest up into hands and places it down onto an adjacent empty cell through the public manager seam, asserting the reported cell and the restored block.")]
    public class PlaceDownScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// A chest carried in the hands can be placed down onto an empty cell through the
        /// public manager seam, restoring the block in the world.
        /// </summary>
        [Trait("summary", "A chest carried in hands is placed down onto an adjacent empty cell, reporting the actual target cell and restoring a chest block there.")]
        [Trait("pins", "The pickup and place-down round-trip restores the block and placedAt reports the actual cell.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task CarriedChest_CanBePlacedDown_OnGround()
        {
            BlockPos pickup = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pickup);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pickup, out var pickupFailure);
            Assert.True(carried != null, $"Pickup failed: {pickupFailure}");

            BlockPos target = World.Spawn.Offset(3, 1, 0);
            var placedAt = Actions.PlaceDown(player.Player, CarrySlot.Hands, CarryActions.AimDownAt(target), out var placeFailure);

            Assert.True(placedAt != null, $"Placedown failed: {placeFailure}");
            Assert.Equal(target, placedAt);
            Assert.True(IsChest(World.BlockAt(target)), "Expected a chest at the target cell");
        }

        private static bool IsChest(Block? block)
        {
            return block != null
                && block.BlockId != 0
                && block.Code != null
                && block.Code.FirstCodePart() == "chest";
        }
    }
}