using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Network;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    [Trait("area", "Handler path")]
    [Trait("summary", "Reach validation on the three carry handlers: a client-named target cell beyond the configured max interaction distance is rejected in survival but allowed in creative, so a modified client cannot act at range.")]
    public class HandlerPathReachScenarios : CarryOnScenarioBase
    {

        [Trait("summary", "A survival pickup of a chest beyond the configured reach is rejected with block-out-of-reach, the block stays in the world, the hands slot stays empty, and a non-blank toast arrives.")]
        [Trait("pins", "The pickup reach gate, and that a refusal does not destroy the target.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPickUp_BeyondMaxInteractionDistance_KeepsBlockInWorld()
        {
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;
            BlockPos far = World.Spawn.Offset(reach + 8, 1, 0);
            World.SetBlock("game:chest-east", far);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            Assert.True(CellCentre(far).DistanceTo(player.Entity.Pos.XYZ) > reach,
                "Row premise: the target cell must sit beyond the configured reach");

            await World.Ticks(8);
            player.Client.Clear();

            HandlerInvoke.PickUp(Mod, player.Player, new PickUpMessage(far, CarrySlot.Hands));

            await World.Ticks(1);
            var wire = VanillaClientWire.Tap(player);

            Assert.True(IsChest(World.BlockAt(far)),
                $"The out-of-reach chest must stay in the world - {wire.Describe()}");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "An out-of-reach pickup must not occupy the hands slot");
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.BlockOutOfReach));

            // The player has to read *something*. CarryOn sends display text (CarryErrorHelper
            // resolves the lang entry server-side and passes it as Message), so assert the
            // text actually arrived rather than only that a packet did - a code with a null
            // or empty Message would leave the HUD with nothing to draw.
            var toast = wire.ErrorsWithCode(FailureCodes.BlockOutOfReach)[0];
            Assert.True(toast.HasServerText,
                "CarryOn toasts carry resolved server text, not a bare code - " + wire.Describe());
            Assert.True(!string.IsNullOrWhiteSpace(toast.VerbatimText),
                "The out-of-reach toast must not be blank on screen - " + wire.Describe());
        }

        [Trait("summary", "A creative player picks the same beyond-reach chest up, occupying the hands slot with no reach error.")]
        [Trait("pins", "Creative is exempt inside the reach predicate, so a survival row cannot pass by accident.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPickUp_BeyondReach_CreativePlayer_PicksUp()
        {
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;
            BlockPos far = World.Spawn.Offset(reach + 8, 1, 0);
            World.SetBlock("game:chest-east", far);

            ITestPlayer player = await JoinPlayer("Creator", EnumGameMode.Creative);
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            Assert.True(CellCentre(far).DistanceTo(player.Entity.Pos.XYZ) > reach,
                "Row premise: the target cell must sit beyond the configured reach");

            await World.Ticks(8);
            player.Client.Clear();

            HandlerInvoke.PickUp(Mod, player.Player, new PickUpMessage(far, CarrySlot.Hands));

            await World.Ticks(1);
            var wire = VanillaClientWire.Tap(player);

            Assert.True(World.BlockAt(far).BlockId == 0,
                $"Creative must pick up beyond reach - {wire.Describe()}");
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A creative pickup beyond reach still occupies the hands slot");
            Assert.Empty(wire.ErrorsWithCode(FailureCodes.BlockOutOfReach));
        }

        [Trait("summary", "A survival place-down at a beyond-reach cell is rejected with block-out-of-reach, the carry is retained, and the target stays air.")]
        [Trait("pins", "The place-down reach gate, and that a refused place-down is not a dropped carry.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_BeyondMaxInteractionDistance_KeepsBlockInWorldAndCarry()
        {
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;

            BlockPos acquire = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", acquire);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, acquire, out var pf) != null,
                $"Chest pickup failed: {pf}");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));

            BlockPos far = World.Spawn.Offset(reach + 8, 1, 0);
            var selection = CarryActions.AimDownAt(far);
            Assert.True(World.BlockAt(far).BlockId == 0, "Row premise: the target cell must be free air");
            Assert.True(CellCentre(far).DistanceTo(player.Entity.Pos.XYZ) > reach,
                "Row premise: the target cell must sit beyond the configured reach");

            await World.Ticks(8);
            player.Client.Clear();

            HandlerInvoke.PlaceDown(Mod, player.Player,
                new PlaceDownMessage(CarrySlot.Hands, selection, far));

            await World.Ticks(1);
            var wire = VanillaClientWire.Tap(player);

            Assert.True(World.BlockAt(far).BlockId == 0,
                $"No placement may happen beyond the configured reach - {wire.Describe()}");
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "An out-of-reach place-down must retain the carry");
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.BlockOutOfReach));
        }

        [Trait("summary", "A survival place-down whose message position is beyond reach but whose claimed placement cell is inside reach is rejected with block-out-of-reach and no invalid-data error.")]
        [Trait("pins", "Reach is decided before the rest of place-down validation, and a near claimed cell cannot launder a far target.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while a far target was written because a near claimed cell passed the can-place check.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_FarPosition_NearClaimedPlacedAt_Rejected()
        {
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;

            BlockPos acquire = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", acquire);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, acquire, out var pf) != null,
                $"Chest pickup failed: {pf}");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));

            BlockPos far = World.Spawn.Offset(reach + 8, 1, 0);
            BlockPos claimed = World.Spawn.Offset(3, 1, 0);
            var selection = CarryActions.AimDownAt(far);
            Assert.True(World.BlockAt(far).BlockId == 0 && World.BlockAt(claimed).BlockId == 0,
                "Row premise: the real target and the claimed placement cell must both be free air");
            Assert.True(CellCentre(far).DistanceTo(player.Entity.Pos.XYZ) > reach,
                "Row premise: message.Position must sit beyond the configured reach");
            Assert.True(CellCentre(claimed).DistanceTo(player.Entity.Pos.XYZ) <= reach,
                "Row premise: the claimed PlacedAt must sit inside the configured reach");

            await World.Ticks(8);
            player.Client.Clear();

            HandlerInvoke.PlaceDown(Mod, player.Player,
                new PlaceDownMessage(CarrySlot.Hands, selection, claimed, new[] { claimed }));

            await World.Ticks(1);
            var wire = VanillaClientWire.Tap(player);

            Assert.True(World.BlockAt(far).BlockId == 0,
                $"A far message.Position must not be written from a near PlacedAt claim - {wire.Describe()}");
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A rejected spoofed place-down must retain the carry");
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.BlockOutOfReach));
            Assert.Empty(wire.ErrorsWithCode(FailureCodes.InvalidData));
        }

        [Trait("summary", "A creative player places the chest at the beyond-reach cell and clears the hands slot with no reach error.")]
        [Trait("pins", "The creative exemption on the place-down reach gate.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_BeyondReach_CreativePlayer_Places()
        {
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;

            BlockPos acquire = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", acquire);

            ITestPlayer player = await JoinPlayer("Creator", EnumGameMode.Creative);
            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, acquire, out var pf) != null,
                $"Chest pickup failed: {pf}");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));

            BlockPos far = World.Spawn.Offset(reach + 8, 1, 0);
            var selection = CarryActions.AimDownAt(far);
            Assert.True(World.BlockAt(far).BlockId == 0, "Row premise: the target cell must be free air");
            Assert.True(CellCentre(far).DistanceTo(player.Entity.Pos.XYZ) > reach,
                "Row premise: the target cell must sit beyond the configured reach");

            await World.Ticks(8);
            player.Client.Clear();

            HandlerInvoke.PlaceDown(Mod, player.Player,
                new PlaceDownMessage(CarrySlot.Hands, selection, far));

            await World.Ticks(1);
            var wire = VanillaClientWire.Tap(player);

            Assert.True(IsChest(World.BlockAt(far)),
                $"Creative must place beyond reach - {wire.Describe()}");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A creative place-down beyond reach still frees the hands slot");
            Assert.Empty(wire.ErrorsWithCode(FailureCodes.BlockOutOfReach));
        }

        [Trait("summary", "A survival interact at a closed wattle gate beyond reach leaves the gate closed, retains the carry, and reports block-out-of-reach.")]
        [Trait("pins", "The interact reach gate is checked inside the acting branch, so a spoofed message cannot mark an arbitrary far block dirty.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerInteract_BeyondMaxInteractionDistance_LeavesTheBlockUntouched()
        {
            Assert.True(Mod.Config.Interactables.Door,
                "Default config must enable Interactables.Door for this row");
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;

            var gate = GateFixture.FindClosedGate(World.Api.World, "wattlegate");
            BlockPos far = World.Spawn.Offset(reach + 8, 1, 0);
            World.SetBlock(gate.Code.ToString(), far);
            Assert.True(World.BlockAt(far).HasBehavior<BlockBehaviorCarryableInteract>(),
                $"Placed {gate.Code} must carry the carryableinteract behavior");

            BlockPos chestPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, chestPos, out var pf) != null,
                $"Chest pickup failed: {pf}");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));

            string before = World.BlockAt(far).Code!.ToString();
            Assert.Equal("closed", GateFixture.StateOf(World.BlockAt(far)));
            Assert.True(CellCentre(far).DistanceTo(player.Entity.Pos.XYZ) > reach,
                "Row premise: the target cell must sit beyond the configured reach");

            await World.Ticks(8);
            player.Client.Clear();

            HandlerInvoke.Interact(Mod, player.Player,
                new InteractMessage(CarryActions.AimDownAt(far)));

            await World.Ticks(1);
            var wire = VanillaClientWire.Tap(player);

            Assert.Equal(before, World.BlockAt(far).Code?.ToString());
            Assert.Equal("closed", GateFixture.StateOf(World.BlockAt(far)));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "An out-of-reach interact must not drop the carried chest");
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.BlockOutOfReach));
        }

        [Trait("summary", "A creative interact toggles the beyond-reach gate to opened, retains the carry, and reports no reach error.")]
        [Trait("pins", "The creative exemption on the interact reach gate.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerInteract_BeyondReach_CreativePlayer_Toggles()
        {
            Assert.True(Mod.Config.Interactables.Door,
                "Default config must enable Interactables.Door for this row");
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;

            var gate = GateFixture.FindClosedGate(World.Api.World, "wattlegate");
            BlockPos far = World.Spawn.Offset(reach + 8, 1, 0);
            World.SetBlock(gate.Code.ToString(), far);
            Assert.True(World.BlockAt(far).HasBehavior<BlockBehaviorCarryableInteract>(),
                $"Placed {gate.Code} must carry the carryableinteract behavior");

            BlockPos chestPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            ITestPlayer player = await JoinPlayer("Creator", EnumGameMode.Creative);
            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, chestPos, out var pf) != null,
                $"Chest pickup failed: {pf}");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));

            Assert.Equal("closed", GateFixture.StateOf(World.BlockAt(far)));
            Assert.True(CellCentre(far).DistanceTo(player.Entity.Pos.XYZ) > reach,
                "Row premise: the target cell must sit beyond the configured reach");

            await World.Ticks(8);
            player.Client.Clear();

            HandlerInvoke.Interact(Mod, player.Player,
                new InteractMessage(CarryActions.AimDownAt(far)));

            await World.Ticks(1);
            var wire = VanillaClientWire.Tap(player);

            Assert.Equal("opened", GateFixture.StateOf(World.BlockAt(far)));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "An interact does not consume the carried chest");
            Assert.Empty(wire.ErrorsWithCode(FailureCodes.BlockOutOfReach));
        }

        private static Vec3d CellCentre(BlockPos pos)
            => new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5);

        private static bool IsChest(Block? block)
        {
            return block != null
                && block.BlockId != 0
                && block.Code != null
                && block.Code.FirstCodePart() == "chest";
        }
    }
}
