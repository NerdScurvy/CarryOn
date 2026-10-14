using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Network;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Seeded-config interact rows: Interactables.Door=false must remove the gate's
    /// carryableinteract behavior (config-gated patch removal in the final
    /// conditioning pass) and the automapper must not re-add it from the still-enabled
    /// Storage/Barrel interact sources - the interact-side twin of the chest-toggle
    /// automapper regression. The interact handler is then a no-op against the gate.
    /// </summary>
    [Trait("area", "Handler path")]
    [Trait("summary", "Seeded Interactables.Door=false: the gate's carryableinteract behavior is removed by config-gated patch removal and not re-added by the automapper, so the interact handler leaves the gate closed.")]
    [AtlasDataFiles("fixtures/DoorOffConfig", TargetPath = "ModConfig")]
    public class HandlerPathInteractDoorOffScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// With Interactables.Door seeded off, driving the interact handler at a wattle
        /// gate while carrying leaves the gate closed - no toggle, carry retained.
        /// </summary>
        [Trait("summary", "With Interactables.Door seeded off, a placed wattle gate carries no carryableinteract behavior and the interact handler leaves it closed, retaining the carry.")]
        [Trait("pins", "The door config gate removes the behavior and the automapper cannot re-add it from the still-enabled sources.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WattleGate_InteractDisabledByConfig_DoesNotToggle()
        {
            Assert.False(Mod.Config.Interactables.Door,
                "Seed did not disable Interactables.Door");

            var gate = GateFixture.FindClosedGate(World.Api.World, "wattlegate");
            BlockPos gatePos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(gate.Code.ToString(), gatePos);

            BlockPos chestPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));

            Assert.False(World.BlockAt(gatePos).HasBehavior<BlockBehaviorCarryableInteract>(),
                "Interactables.Door=false must remove the gate's carryableinteract behavior " +
                "(and the automapper must not re-add it from the still-enabled sources)");

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var pickupFailure);
            Assert.True(carried != null, $"Chest pickup failed: {pickupFailure}");

            string before = World.BlockAt(gatePos).Code!.ToString();

            HandlerInvoke.Interact(Mod, player.Player,
                new InteractMessage(CarryActions.AimDownAt(gatePos)));

            Assert.Equal(before, World.BlockAt(gatePos).Code?.ToString());
            Assert.Equal("closed", GateFixture.StateOf(World.BlockAt(gatePos)));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "The disabled interact must not drop the carried chest");
        }
    }
}
