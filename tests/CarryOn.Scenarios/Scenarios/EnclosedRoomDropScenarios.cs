using System.Collections.Generic;
using System.Threading.Tasks;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Class A - dropped carried blocks inside a sealed 2x2x2 enclosure with 4
    /// ladders (2 north, 2 south interior walls, body level). Spike-pinned: the
    /// placement flood for the interior feet cell is exactly the 8 interior
    /// cells (ladders are non-passable for the BFS, the rock shell bounds it),
    /// so every mode's outcome is fully contained in the room:
    /// EntityAlways spawns the carried block as an entity on the room floor
    /// (feet at the floor top, nothing placed), while EntityOnFailedPlacement
    /// and Items both place the chest into the player's own feet cell - the
    /// dropped path never consults vanilla's entity-intersection check and the
    /// mode "Items" still tries placement first.
    /// </summary>
    [Trait("area", "Drop")]
    [Trait("summary", "Dropped carried blocks inside a sealed 2x2x2 room: EntityAlways spawns an entity on the room floor while EntityOnFailedPlacement and Items both place the chest into the player's own feet cell, all fully contained in the room.")]
    public class EnclosedRoomDropScenarios : DropModeScenarioBase
    {
        /// <summary>
        /// R1 - EntityAlways inside the sealed room: the chest leaves the hands
        /// as a single carriedblock entity resting on the room floor; no block
        /// is placed anywhere in the room.
        /// </summary>
        [Trait("summary", "EntityAlways inside the sealed room leaves the hands as a single carried-block entity resting on the room floor, with no block placed anywhere.")]
        [Trait("pins", "EntityAlways spawns an entity and never places, even inside a sealed volume.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task EnclosedRoom_EntityAlways_SpawnsEntityInRoom()
        {
            AssertPremise();

            var foot = Pt(12, 4, 12);
            _ = BuildEnclosedRoom(foot);
            var player = await JoinPlayer("RoomDropperE");
            await AcquireChestAsync(player, Pt(20, 1, 2));

            await player.TeleportTo(foot);
            var before = SnapshotRoomShell(foot);

            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.EntityAlways))
            {
                DropHands(player);
            }

            AssertCarryCleared(player);
            Assert.Single(Drops);
            Assert.False(Drops[0].BlockPlaced,
                "EntityAlways must spawn the carried block as an entity, not place a block");
            Assert.Same(player.Entity, Drops[0].Entity);

            var entities = CarriedBlockEntitiesAround(foot, 3);
            Assert.Single(entities);
            var carried = entities[0];
            Assert.True(carried.Pos.Y >= foot.Y && carried.Pos.Y < foot.Y + 0.01,
                $"Entity feet should sit on the room floor at ~{foot.Y}, found {carried.Pos.Y}");
            Assert.True(carried.Pos.X >= foot.X && carried.Pos.X <= foot.X + 1 &&
                        carried.Pos.Z >= foot.Z && carried.Pos.Z <= foot.Z + 1,
                $"Entity must land inside the room interior, found {carried.Pos.X:F2},{carried.Pos.Z:F2}");

            Assert.Equal(0, CountBlocksWithPrefix(foot, 5, 3, "game:chest-"));
            Assert.Equal(0, CountItemEntitiesAround(foot, 5));
            AssertRegionUnchangedExcept(before);
        }

        /// <summary>
        /// R2 - EntityOnFailedPlacement inside the sealed room: placement
        /// succeeds (the dropped path writes through SetBlock and never checks
        /// the player's own occupancy), the chest fills the player's feet cell,
        /// and no entity or item materializes.
        /// </summary>
        [Trait("summary", "EntityOnFailedPlacement inside the sealed room places the chest into the player's own feet cell, with no entity or item.")]
        [Trait("pins", "The dropped path writes through SetBlock and never consults vanilla's entity-intersection check.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task EnclosedRoom_EntityOnFailedPlacement_PlacesChestInPlayerColumn()
        {
            await PlaceChestInRoomAsync("RoomDropperF", DropMode.EntityOnFailedPlacement, assertNoItems: false);
        }

        /// <summary>
        /// R3 - Items mode inside the sealed room: placement is still attempted
        /// first, so this pinning row proves "Items" is not "always items" - the
        /// chest is placed, zero item entities are spawned.
        /// </summary>
        [Trait("summary", "Items mode inside the sealed room still attempts placement first and places the chest into the player's feet cell with zero items.")]
        [Trait("pins", "Items mode is not always-items.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task EnclosedRoom_ItemsMode_PlacesChestInPlayerColumn()
        {
            await PlaceChestInRoomAsync("RoomDropperI", DropMode.Items, assertNoItems: true);
        }

        private async Task PlaceChestInRoomAsync(string playerName, DropMode mode, bool assertNoItems)
        {
            AssertPremise();

            var foot = Pt(12, 4, 12);
            _ = BuildEnclosedRoom(foot);
            var player = await JoinPlayer(playerName);
            await AcquireChestAsync(player, Pt(20, 1, 2));

            await player.TeleportTo(foot);
            var before = SnapshotRoomShell(foot);

            using (SubscribeBlockDropped())
            using (UseDropMode(mode))
            {
                DropHands(player);
            }

            AssertCarryCleared(player);
            Assert.Single(Drops);
            Assert.True(Drops[0].BlockPlaced,
                $"DropMode.{mode} must place the chest inside the enclosed room");
            Assert.Same(player.Entity, Drops[0].Entity);

            var chests = ChestCellsAround(foot, 4, 2);
            Assert.Single(chests);
            Assert.True(IsSameCell(chests[0], foot),
                $"Chest should fill the player's feet cell {foot}, found {chests[0]}");
            Assert.Empty(CarriedBlockEntitiesAround(foot, 4));
            Assert.Equal(0, CountItemEntitiesAround(foot, 4));

            if (assertNoItems)
            {
                Assert.Equal(0, CountItemEntitiesAround(foot, 4));
            }

            AssertRegionUnchangedExcept(before, chests[0]);
        }

        private Dictionary<BlockPos, string> SnapshotRoomShell(BlockPos foot)
        {
            return SnapshotRegion(
                new BlockPos(foot.X - 1, foot.Y - 1, foot.Z - 1),
                new BlockPos(foot.X + 2, foot.Y + 2, foot.Z + 2));
        }

        private static bool IsSameCell(BlockPos a, BlockPos b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    }
}