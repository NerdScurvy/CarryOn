using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Network;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Handler-path pickup rows: the same entry points a real client's PickUpMessage
    /// drives on the server (validation, permission, failure bookkeeping), asserted
    /// end-to-end through world truth, carry state and client-visible packets.
    /// </summary>
    [Trait("area", "Handler path")]
    [Trait("summary", "Handler-path pickup rows driving the real pickup message entry point, asserted end-to-end through world truth, carry state, and client-visible packets.")]
    public class HandlerPathPickUpScenarios : CarryOnScenarioBase
    {
        [Trait("summary", "A Hands pickup through the handler leaves the same end state as the manager path - hands carried, original code preserved, cell air - and sends the client a LockSlotsMessage.")]
        [Trait("pins", "Handler and manager parity plus the success notification.")]
        [Trait("role", "parity")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPickUp_MatchesManagerPath_AndNotifiesClient()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));

            HandlerInvoke.PickUp(Mod, player.Player, new PickUpMessage(pos, CarrySlot.Hands));

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Handler pickup should occupy the hands slot");
            Assert.Equal("game:chest-east",
                Carry.GetCarried(player.Entity, CarrySlot.Hands)?.OriginalBlockCode?.ToString());
            Assert.True(World.BlockAt(pos).BlockId == 0, "Picked-up cell should be air (server truth)");

            var packets = player.Client.Packets<LockSlotsMessage>(Channel);
            Assert.NotEmpty(packets);
        }

        [Trait("summary", "A pickup message naming the Back slot grants no carry, leaves the chest in the world, still bumps the revision, and notifies the client.")]
        [Trait("pins", "The handler only accepts Hands pickups, and rejections are client-observable.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPickUp_WrongSlot_RejectsWithoutStateChange()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            int revisionBefore = Carry.GetCarriedRevision(player.Entity);

            // The handler only accepts Hands pickups (CarryServerHandler.OnPickUpMessage).
            HandlerInvoke.PickUp(Mod, player.Player, new PickUpMessage(pos, CarrySlot.Back));

            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.True(IsChest(World.BlockAt(pos)), "Chest should remain in the world");
            Assert.True(Carry.GetCarriedRevision(player.Entity) > revisionBefore,
                "Rejected action should still touch carried attributes (failure bookkeeping)");
            Assert.NotEmpty(player.Client.Packets<LockSlotsMessage>(Channel));
        }

        [Trait("summary", "A pickup message with a null position is rejected without crashing, granting no carry, still bumping the revision, and notifying the client.")]
        [Trait("pins", "Invalid-data robustness in the pickup handler.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPickUp_MalformedMessage_RejectsWithoutCrash()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            int revisionBefore = Carry.GetCarriedRevision(player.Entity);

            var malformed = new PickUpMessage(pos, CarrySlot.Hands) with { Position = null! };
            HandlerInvoke.PickUp(Mod, player.Player, malformed);

            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.True(IsChest(World.BlockAt(pos)));
            Assert.True(Carry.GetCarriedRevision(player.Entity) > revisionBefore);
            Assert.NotEmpty(player.Client.Packets<LockSlotsMessage>(Channel));
        }

        [Trait("summary", "A second pickup while already carrying keeps the first carry and leaves the second chest in the world.")]
        [Trait("pins", "No duplicate carry and no world loss on an already-carrying rejection.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPickUp_AlreadyCarrying_KeepsFirstCarryAndWorld()
        {
            BlockPos firstPos = World.Spawn.Offset(2, 1, 0);
            BlockPos secondPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", firstPos);
            World.SetBlock("game:chest-north", secondPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));

            var first = Actions.PickUpIntoHands(player.Entity, firstPos, out var firstFailure);
            Assert.True(first != null, $"First pickup failed: {firstFailure}");

            HandlerInvoke.PickUp(Mod, player.Player, new PickUpMessage(secondPos, CarrySlot.Hands));

            Assert.Equal("game:chest-east",
                Carry.GetCarried(player.Entity, CarrySlot.Hands)?.OriginalBlockCode?.ToString());
            Assert.True(IsChest(World.BlockAt(secondPos)), "Second chest should remain in the world");
        }

        private static bool IsChest(Vintagestory.API.Common.Block? block)
        {
            return block != null
                && block.BlockId != 0
                && block.Code != null
                && block.Code.FirstCodePart() == "chest";
        }
    }
}
