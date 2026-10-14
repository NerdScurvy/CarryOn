using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    [Trait("area", "Smoke")]
    [Trait("summary", "Places a vanilla chest and picks it up into the hands slot through the public manager seam, asserting the carryable behavior landed and the world cell it left is now air.")]
    public class PickUpHandsScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// A real vanilla chest placed in the world can be picked up into the hands carry
        /// slot through the public manager seam. Verifies the carryable behavior is applied
        /// (via CarryOn's content patches) on a headless server.
        /// </summary>
        [Trait("summary", "A vanilla chest placed in the world can be picked up into the hands slot, and the world cell it left is now air.")]
        [Trait("pins", "Chests are carryable by default and pickup moves the block rather than cloning it.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ChestOnGround_CanBePickedUp_IntoHands()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);

            Assert.True(carried != null, $"Pickup failed: {failureCode}");
            Assert.Equal("game:chest-east", carried!.Block?.Code?.ToString());
            Assert.Equal(0, World.BlockAt(pos).Id);
        }
    }
}