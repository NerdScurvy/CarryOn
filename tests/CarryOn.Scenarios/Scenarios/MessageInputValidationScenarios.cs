using System;
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
    /// Server-side input validation for the client-to-server carry messages. A client
    /// can send a <c>Face</c> byte beyond the engine's face array or omit the hit
    /// position; the server paths that rebuild a <c>BlockSelection</c> from those
    /// fields must reject the malformed message rather than throw while rebuilding it.
    /// Each row drives the real handler with a malformed record and asserts the handler
    /// returns without an exception.
    /// </summary>
    [Trait("area", "Common/Network")]
    [Trait("summary", "Malformed client carry messages (a face index beyond the engine's face array) are rejected by the server handlers instead of throwing while the selection is rebuilt.")]
    public class MessageInputValidationScenarios : CarryOnScenarioBase
    {
        /// <summary>One past the last index of the engine's face array, so indexing it throws.</summary>
        private const byte HostileFace = 200;

        /// <summary>
        /// A place-down message whose face index is out of range must be rejected with a
        /// failure, not throw while the handler rebuilds the selection. Before the fix,
        /// the selection getter indexed the engine's face array unguarded.
        /// </summary>
        [Trait("summary", "A place-down message with a face index beyond the engine's face array is rejected by the handler without throwing and leaves the carry untouched.")]
        [Trait("pins", "The place-down server handler rejects an out-of-range face instead of throwing while rebuilding the selection.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, where the selection getter indexed the engine's face array and the handler call threw.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PlaceDownMessage_HostileFace_RejectedWithoutThrowing()
        {
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            var malformed = new PlaceDownMessage(CarrySlot.Hands, GroundStorageFixture.TargetSlotSelection(pos), pos)
                with { Face = HostileFace };

            var thrown = Record.Exception(() => HandlerInvoke.PlaceDown(Mod, player.Player, malformed));

            Assert.Null(thrown);
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// An interact message whose face index is out of range must be rejected, not
        /// throw while the handler rebuilds the selection for an in-reach ground-storage
        /// target. Before the fix, the selection getter indexed the engine's face array
        /// unguarded.
        /// </summary>
        [Trait("summary", "An interact message with a face index beyond the engine's face array is rejected by the handler without throwing.")]
        [Trait("pins", "The interact server handler rejects an out-of-range face instead of throwing while rebuilding the selection.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, where the selection getter indexed the engine's face array and the handler call threw.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task InteractMessage_HostileFace_RejectedWithoutThrowing()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            GroundStorageFixture.PlaceEmptyStorage(World.Api.World, pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var malformed = new InteractMessage(GroundStorageFixture.TargetSlotSelection(pos))
                with { Face = HostileFace };

            var thrown = Record.Exception(() => HandlerInvoke.Interact(Mod, player.Player, malformed));

            Assert.Null(thrown);
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// A ground-storage bag pick-up message whose face index is out of range must be
        /// rejected, not throw while the handler rebuilds the selection. Before the fix,
        /// the selection getter indexed the engine's face array unguarded.
        /// </summary>
        [Trait("summary", "A ground-storage bag pick-up message with a face index beyond the engine's face array is rejected by the handler without throwing and the storage keeps its bag.")]
        [Trait("pins", "The ground-storage bag pick-up server handler rejects an out-of-range face instead of throwing while rebuilding the selection.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, where the selection getter indexed the engine's face array and the handler call threw.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickUpGroundStorageBagMessage_HostileFace_RejectedWithoutThrowing()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            GroundStorageFixture.PlaceBag(World.Api.World, pos, GroundStorageFixture.BagCode);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var malformed = new PickUpGroundStorageBagMessage(GroundStorageFixture.TargetSlotSelection(pos))
                with { Face = HostileFace };

            var thrown = Record.Exception(() => HandlerInvoke.PickUpGroundStorageBag(Mod, player.Player, malformed));

            Assert.Null(thrown);
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Equal("game:groundstorage", World.BlockAt(pos).Code?.ToString());
            Assert.NotNull(World.Api);
        }
    }
}
