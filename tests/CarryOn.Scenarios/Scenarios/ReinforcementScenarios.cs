using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Reinforcement gate coverage: CarryOn's permission service denies pickup at a
    /// reinforced cell for EVERY survival player - including the player who applied
    /// the reinforcement (HasPermissionAt checks IsReinforced after the permission
    /// pipeline and before the claim rules, with no owner carve-out). That is
    /// deliberately stricter than vanilla, where the reinforcing owner retains
    /// break/remove privileges (ModSystemBlockReinforcement.GetAccessFlags); a
    /// survival player must remove the reinforcement before carrying the block.
    /// Creative players bypass the reinforcement check entirely - the second row
    /// opts into creative deliberately to pin that bypass (suite README, Decision
    /// 19: creative opt-ins exist only for rows whose subject is the bypass).
    /// Reinforcements are applied through vanilla's own
    /// ModSystemBlockReinforcement.StrengthenBlock so the gate is exercised against
    /// the real reinforcement store, not a mock.
    /// </summary>
    [Trait("area", "Claims/Permissions")]
    [Trait("summary", "Reinforcement is applied through vanilla's own reinforcement store, and the rows pin both game-mode halves of the reinforcement gate: survival denial for every player including the reinforcing owner, and the deliberate creative bypass.")]
    public class ReinforcementScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// A reinforced chest cannot be picked up in survival even by the player who
        /// reinforced it: the manager answers <c>reinforced</c> so the handler composes
        /// the reinforcement-specific toast, the chest stays in the world, and no carry
        /// slot is occupied. Before the reason came out of the gate this was
        /// <c>no-permission</c>, which named "permission" and not the actual obstacle.
        /// </summary>
        [Trait("summary", "A chest reinforced through vanilla's own reinforcement store cannot be picked up in survival even by the reinforcing player: the manager answers reinforced, the chest stays in the world, and no carry is granted.")]
        [Trait("pins", "The deliberately stricter-than-vanilla stance with no owner carve-out.")]
        [Trait("role", "regression")]
        [Trait("decision", "20")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ReinforcedChest_PickUpInSurvival_Denied_EvenForReinforcingOwner()
        {
            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            ITestPlayer player = await JoinPlayer("Reinforcer");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            var reinforcements = World.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
            Assert.True(reinforcements != null,
                "VSSurvivalMod's reinforcement system must be loaded in the class-world");
            Assert.True(reinforcements!.StrengthenBlock(chestPos, player.Player, 10),
                "Reinforcement must be applied to the chest cell");
            Assert.True(reinforcements.IsReinforced(chestPos),
                "Premise: the chest cell must read as reinforced");

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var failure);
            Assert.True(carried == null, $"Reinforced pickup must be denied, got a carry ({failure})");
            Assert.Equal(FailureCodes.Reinforced, failure);
            Assert.Equal("game:chest-east", World.BlockAt(chestPos).Code.ToString());
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// The creative twin: a creative player picks the same reinforced chest up
        /// without touching the reinforcement. Pins the bypass the survival row pins
        /// against - one of the four server-side creative bypasses (Decision 19).
        /// </summary>
        [Trait("summary", "A creative player picks the same reinforced chest up without touching the reinforcement: the chest is carried and leaves the world.")]
        [Trait("pins", "The creative bypass of the reinforcement check.")]
        [Trait("role", "regression")]
        [Trait("decision", "19")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ReinforcedChest_PickUpInCreative_BypassesReinforcement()
        {
            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            ITestPlayer player = await JoinPlayer("Reinforcer", EnumGameMode.Creative);
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            var reinforcements = World.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
            Assert.True(reinforcements != null,
                "VSSurvivalMod's reinforcement system must be loaded in the class-world");
            Assert.True(reinforcements!.StrengthenBlock(chestPos, player.Player, 10),
                "Reinforcement must be applied to the chest cell");
            Assert.True(reinforcements.IsReinforced(chestPos),
                "Premise: the chest cell must read as reinforced");

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var failure);
            Assert.True(carried != null, $"Creative pickup must bypass reinforcement: {failure}");
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Equal("game:chest-east",
                carried?.ItemStack?.Collectible?.Code?.ToString());
            Assert.True(World.BlockAt(chestPos).Id == 0,
                "The reinforced chest must leave the world when picked up");
        }
    }
}
