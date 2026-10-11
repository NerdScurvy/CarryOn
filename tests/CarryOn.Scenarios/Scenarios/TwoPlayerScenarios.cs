using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.API.Event.Data;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Two-player scenarios over the public manager seam with a second simulated client
    /// joined into the same world: carry state is server-authoritative and per-entity (the
    /// second player neither shares nor mirrors the first player's slots), world mutations
    /// and events are shared for every participant, and the carrying-player vs
    /// fresh-player pickup asymmetry is enforced through the same public calls.
    /// </summary>
    [Trait("area", "Sync/Network")]
    [Trait("summary", "Two-player scenarios with a second simulated client: carry state is server-authoritative and per-entity, world mutations and events are shared, and the carrying-player versus fresh-player pickup asymmetry holds through identical public calls.")]
    public class TwoPlayerScenarios : CarryOnScenarioBase
    {
        private List<BlockRemovedEventArgs>? activeRemovals;
        private List<BlockRemovedEventArgs> Removals => activeRemovals ??= new List<BlockRemovedEventArgs>();

        private void OnBlockRemoved(object? sender, BlockRemovedEventArgs e)
        {
            Removals.Add(e);
        }

        private IDisposable SubscribeBlockRemoved()
        {
            Removals.Clear();
            Carry.CarryEvents.BlockRemoved += OnBlockRemoved;
            return new UnsubscribeAction(() => Carry.CarryEvents.BlockRemoved -= OnBlockRemoved);
        }

        private sealed class UnsubscribeAction(Action dispose) : IDisposable
        {
            private readonly Action dispose = dispose;

            public void Dispose() => dispose();
        }

        /// <summary>
        /// A pickup by one player mutates the shared server state exactly once: a single
        /// BlockRemoved event, the emptied world cell, and the picker's own slots and
        /// revision. The second joined client is a different entity - its own slots and
        /// revision are untouched, proving the state is per-entity server data, not a
        /// per-client or global mirror.
        /// </summary>
        [Trait("summary", "With a second client joined, one player's pickup fires exactly one BlockRemoved event, advances that player's slot and revision, empties the world cell, and leaves the other player's slots and revision untouched.")]
        [Trait("pins", "Carry state is per-entity server data, not per-client or global, and removal events fire exactly once.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickupByOnePlayer_MutatesSharedServerState_OtherPlayerUntouched()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);
            var cellCode = World.BlockAt(pos)?.Code?.ToString();

            ITestPlayer tester = await JoinPlayer("Tester");
            ITestPlayer driver = await JoinPlayer("Driver");
            await tester.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await driver.TeleportTo(World.Spawn.Offset(4, 0, 2));

            int revTester0 = Carry.GetCarriedRevision(tester.Entity);
            int revDriver0 = Carry.GetCarriedRevision(driver.Entity);

            using (SubscribeBlockRemoved())
            {
                Assert.True(Actions.PickUpIntoHands(tester.Entity, pos, out var pickupCode) != null, $"Tester pickup failed: {pickupCode}");
            }

            Assert.Single(Removals);
            Assert.Equal(pos, Removals[0].Position);

            Assert.Equal(cellCode, Carry.GetCarried(tester.Entity, CarrySlot.Hands)?.OriginalBlockCode?.ToString());
            Assert.True(Carry.IsCarrySlotOccupied(tester.Entity, CarrySlot.Hands));
            Assert.True(Carry.GetCarriedRevision(tester.Entity) > revTester0,
                $"Tester's revision should advance on its own pickup ({revTester0} -> {Carry.GetCarriedRevision(tester.Entity)})");

            Assert.Null(Carry.GetCarried(driver.Entity, CarrySlot.Hands));
            Assert.False(Carry.IsCarrySlotOccupied(driver.Entity, CarrySlot.Hands));
            Assert.Equal(revDriver0, Carry.GetCarriedRevision(driver.Entity));

            Assert.Equal(0, World.BlockAt(pos).Id);
        }

        /// <summary>
        /// The per-role asymmetry through identical public calls: once Tester carries in
        /// hands, the very same pickup call that just succeeded rejects with
        /// AlreadyCarrying and leaves the world untouched, while the fresh player picks
        /// the same block up fine.
        /// </summary>
        [Trait("summary", "The same pickup that succeeds for one player rejects with already-carrying once he carries and leaves the world untouched, while the fresh player picks the same block up.")]
        [Trait("pins", "Per-role asymmetry is enforced through identical public calls.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task CarryingPlayerSecondPickupRejected_FreshPlayerSucceeds()
        {
            BlockPos firstPos = World.Spawn.Offset(2, 1, 0);
            BlockPos secondPos = World.Spawn.Offset(2, 1, 2);
            World.SetBlock("game:chest-east", firstPos);
            World.SetBlock("game:chest-east", secondPos);
            var firstCode = World.BlockAt(firstPos)?.Code?.ToString();
            var secondCode = World.BlockAt(secondPos)?.Code?.ToString();

            ITestPlayer tester = await JoinPlayer("Tester");
            ITestPlayer driver = await JoinPlayer("Driver");
            await tester.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await driver.TeleportTo(World.Spawn.Offset(4, 0, 2));

            Assert.True(Actions.PickUpIntoHands(tester.Entity, firstPos, out _) != null, "Tester's first pickup failed");

            Assert.Null(Actions.PickUpIntoHands(tester.Entity, secondPos, out var testerCode));
            Assert.Equal(FailureCodes.AlreadyCarrying, testerCode);
            Assert.Equal(secondCode, World.BlockAt(secondPos)?.Code?.ToString());

            Assert.True(Actions.PickUpIntoHands(driver.Entity, secondPos, out var driverCode) != null, $"Driver pickup failed: {driverCode}");

            Assert.Equal(firstCode, Carry.GetCarried(tester.Entity, CarrySlot.Hands)?.OriginalBlockCode?.ToString());
            Assert.Equal(secondCode, Carry.GetCarried(driver.Entity, CarrySlot.Hands)?.OriginalBlockCode?.ToString());
        }

        /// <summary>
        /// Both players hold the build-or-break grant for the same cell on the default
        /// world, and after one player empties the cell the mutation is shared server
        /// state: the other player's pickup at the same position rejects for everyone
        /// alike, not only for the player who emptied it.
        /// </summary>
        [Trait("summary", "Both players hold the grant at one cell, and after one player empties it the other player's pickup at the same position rejects as not-carryable.")]
        [Trait("pins", "World mutations are shared server state, so a cleared cell rejects for everyone alike.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BothPlayersGrantedPermission_ClearedCellRejectsForAll()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer tester = await JoinPlayer("Tester");
            ITestPlayer driver = await JoinPlayer("Driver");
            await tester.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await driver.TeleportTo(World.Spawn.Offset(4, 0, 2));

            Assert.True(Carry.HasPermissionAt(tester.Entity, pos));
            Assert.True(Carry.HasPermissionAt(driver.Entity, pos));

            Assert.True(Actions.PickUpIntoHands(tester.Entity, pos, out _) != null, "Tester pickup failed");

            Assert.Null(Actions.PickUpIntoHands(driver.Entity, pos, out var driverCode));
            Assert.Equal(FailureCodes.NotCarryable, driverCode);
            Assert.Null(Carry.GetCarried(driver.Entity, CarrySlot.Hands));
        }
    }
}
