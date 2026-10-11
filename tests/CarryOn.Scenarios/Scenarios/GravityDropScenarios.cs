using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Class B - dropped carried blocks under gravity: tall drops over flat
    /// ground (G1-G3), drops over / while submerged in a rimmed still-water
    /// pool (G4-G9), and tall drops over partial path/slab surfaces
    /// (G10/G11). All world reads use ABSOLUTE coordinates: the superflat
    /// test terrain tops out at Spawn.Y+1 (ground top 3.0 with Spawn.Y=2),
    /// and the 3-deep pool basin built at Pt(PoolX,0,PoolZ) fills the
    /// absolute band basinMin.Y..basinMin.Y+2 (water cells 2, 3, 4).
    /// Spike-pinned behavior the rows assert: block placements land on the
    /// player's column at the first air/replaceable cell above a base (the
    /// ground surface, the top water layer when dropping over water, or the
    /// lowest water cells when submerged), while the EntityAlways path
    /// resolves feet through GravitySpawnHelper (solid/path surface top,
    /// water surface cell + 0.5, or the player's own feet when starting in
    /// liquid). Each row is its own RollbackWorld scenario, so counts are
    /// per-row.
    /// </summary>
    [Trait("area", "Drop")]
    [Trait("summary", "Dropped carried blocks under gravity: tall drops over flat ground, drops over and submerged in a still-water pool, and drops over partial-height path and slab surfaces, asserting where block placements and EntityAlways spawns land.")]
    public class GravityDropScenarios : DropModeScenarioBase
    {
        private const int HeightX = 10;
        private const int HeightZ = 10;
        private const int PoolX = 18;
        private const int PoolZ = 14;
        private const int PoolDepth = 3;

        /// <summary>
        /// G1 - EntityAlways dropped four cells above flat ground: the chest
        /// leaves the hands as a single carriedblock entity resting exactly on
        /// the ground surface in the player's column; nothing is placed.
        /// </summary>
        [Trait("summary", "An EntityAlways drop from height over flat ground spawns one carriedblock entity resting on the ground surface in the player's column, with nothing placed and no items.")]
        [Trait("pins", "The EntityAlways fallback spawns an entity rather than placing a block.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropFromHeight_EntityAlways_LandsOnGround()
        {
            AssertPremise();

            BlockPos drop = Pt(HeightX, 7, HeightZ);      // feet well above the soil
            BlockPos landing = LandingCell(HeightX, HeightZ);
            double groundTop = World.Spawn.Y + 1.0;       // absolute surface height
            var player = await JoinPlayer("HeightDropper");
            await AcquireChestAsync(player, Pt(19, 1, HeightZ));

            await player.TeleportTo(drop);
            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.EntityAlways))
            {
                DropHands(player);
            }

            AssertCarryCleared(player);
            Assert.Single(Drops);
            Assert.False(Drops[0].BlockPlaced, "EntityAlways must spawn an entity, not place a block");
            Assert.Same(player.Entity, Drops[0].Entity);

            var entities = CarriedBlockEntitiesAround(landing, 1);
            Assert.Single(entities);
            AssertEntityOnSurface(entities[0], drop, groundTop);

            Assert.Equal(0, CountBlocksWithPrefix(drop, 5, 5, "game:chest-"));
            Assert.Equal(0, CountItemEntitiesAround(landing, 3));
        }

        /// <summary>
        /// G2 - EntityOnFailedPlacement dropped six cells above flat ground:
        /// placement succeeds (drops always try placement first) and the chest
        /// fills the first air cell above the ground in the player's column.
        /// </summary>
        [Trait("summary", "An EntityOnFailedPlacement drop from height over flat ground places the chest in the first air cell above the ground in the player's column.")]
        [Trait("pins", "Drops try placement first and fill the player's own column.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropFromHeight_EntityOnFailedPlacement_PlacesOnGroundColumn()
        {
            await PlaceOnGroundColumnAsync("HeightDropperF", DropMode.EntityOnFailedPlacement);
        }

        /// <summary>
        /// G3 - Items mode dropped twelve cells above flat ground: same as G2,
        /// proving Items mode still attempts placement instead of dropping the
        /// stack immediately.
        /// </summary>
        [Trait("summary", "An Items-mode drop from height over flat ground still places the chest first, in the player's column.")]
        [Trait("pins", "Items mode attempts placement before falling back to items.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropFromHeight_ItemsMode_PlacesOnGroundColumn()
        {
            await PlaceOnGroundColumnAsync("HeightDropperI", DropMode.Items);
        }

        private async Task PlaceOnGroundColumnAsync(string playerName, DropMode mode)
        {
            AssertPremise();

            BlockPos drop = Pt(HeightX, 15, HeightZ);
            BlockPos landing = LandingCell(HeightX, HeightZ);
            var player = await JoinPlayer(playerName);
            await AcquireChestAsync(player, Pt(19, 1, HeightZ));

            await player.TeleportTo(drop);
            using (SubscribeBlockDropped())
            using (UseDropMode(mode))
            {
                DropHands(player);
            }

            AssertCarryCleared(player);
            Assert.Single(Drops);
            Assert.True(Drops[0].BlockPlaced,
                $"DropMode.{mode} must place the chest before considering its fallback");
            Assert.Same(player.Entity, Drops[0].Entity);

            var chests = ChestCellsAround(landing, 1, 1);
            Assert.Single(chests);
            Assert.True(IsSameCell(chests[0], landing),
                $"Chest should fill the ground-level cell below the drop {landing}, found {chests[0]}");
            Assert.Empty(CarriedBlockEntitiesAround(landing, 3));
            Assert.Equal(0, CountItemEntitiesAround(landing, 3));
        }

        // ---- G4-G9: the water pool ----

        /// <summary>
        /// G4 - EntityAlways dropped above a still-water pool: the chest entity
        /// floats half-submerged at the water surface (feet at poolTopY + 0.5),
        /// and no water layer is replaced.
        /// </summary>
        [Trait("summary", "An EntityAlways drop over a still-water pool leaves the entity half-submerged at the water surface, inside the pool footprint, with no water layer replaced.")]
        [Trait("pins", "EntityAlways resolves feet to the water surface plus 0.5 and never replaces water.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropOverTwoWaterLayers_EntityAlways_HalfSubmerged()
        {
            AssertPremise();

            BlockPos basinMin = Pt(PoolX, 0, PoolZ);
            await BuildWaterPool(basinMin, PoolDepth);
            int poolTopY = basinMin.Y + PoolDepth - 1;   // absolute top water cell
            BlockPos surfaceCenter = new(basinMin.X, poolTopY, basinMin.Z);
            var player = await JoinPlayer("PoolDropper");
            await AcquireChestAsync(player, Pt(24, 1, 18));

            await player.TeleportTo(Pt(PoolX, 6, PoolZ));
            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.EntityAlways))
            {
                DropHands(player);
            }

            AssertCarryCleared(player);
            Assert.Single(Drops);
            Assert.False(Drops[0].BlockPlaced, "EntityAlways over water must spawn an entity, not place a block");
            Assert.Same(player.Entity, Drops[0].Entity);

            var entities = CarriedBlockEntitiesAround(surfaceCenter, 2);
            Assert.Single(entities);
            AssertTrue(entities[0].Pos.Y >= poolTopY + 0.5 && entities[0].Pos.Y <= poolTopY + 0.506,
                $"Entity feet should float at water surface {poolTopY + 0.5:F2}, found {entities[0].Pos.Y}");
            AssertPoolArea(entities[0], basinMin);

            AssertWaterLayer(basinMin, poolTopY);         // top layer untouched
            AssertWaterLayer(basinMin, poolTopY - 1);     // lower layer intact
            Assert.Equal(0, CountBlocksWithPrefix(surfaceCenter, 4, 3, "game:chest-"));
            Assert.Equal(0, CountItemEntitiesAround(surfaceCenter, 4));
        }

        /// <summary>
        /// G5/G6 - EntityOnFailedPlacement and Items dropped above a still-water
        /// pool: placement wins by replacing the top water layer (water cells are
        /// replaceable and in the flood); the lower layer stays intact and no
        /// entity/item materializes.
        /// </summary>
        [Trait("summary", "An EntityOnFailedPlacement drop over a still-water pool replaces the top water layer with the chest, leaving the lower layer intact.")]
        [Trait("pins", "The pool's top layer is the first placeable cell for a placement drop.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropOverTwoWaterLayers_EntityOnFailedPlacement_PlacesInTopLayer()
        {
            await PlaceChestInWaterAsync("PoolDropperF", DropMode.EntityOnFailedPlacement);
        }

        [Trait("summary", "An Items-mode drop over a still-water pool places the chest into the top water layer, leaving the lower layer intact and spawning no items.")]
        [Trait("pins", "Items mode places before it drops items, even over water.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropOverTwoWaterLayers_ItemsMode_PlacesInTopLayer()
        {
            await PlaceChestInWaterAsync("PoolDropperI", DropMode.Items);
        }

        private async Task PlaceChestInWaterAsync(string playerName, DropMode mode)
        {
            AssertPremise();

            BlockPos basinMin = Pt(PoolX, 0, PoolZ);
            await BuildWaterPool(basinMin, PoolDepth);
            int poolTopY = basinMin.Y + PoolDepth - 1;
            BlockPos surfaceCenter = new(basinMin.X, poolTopY, basinMin.Z);
            var player = await JoinPlayer(playerName);
            await AcquireChestAsync(player, Pt(24, 1, 18));

            await player.TeleportTo(Pt(PoolX, 6, PoolZ));
            using (SubscribeBlockDropped())
            using (UseDropMode(mode))
            {
                DropHands(player);
            }

            AssertCarryCleared(player);
            Assert.Single(Drops);
            Assert.True(Drops[0].BlockPlaced,
                $"DropMode.{mode} over water must place the chest into the top water layer");
            Assert.Same(player.Entity, Drops[0].Entity);

            var chests = ChestCellsAround(surfaceCenter, 2, 2);
            Assert.Single(chests);
            Assert.True(chests[0].Y == poolTopY,
                $"Chest must replace the top water layer at y={poolTopY}, found {chests[0].Y}");
            AssertInsideBasin(chests[0], basinMin);

            AssertWaterLayer(basinMin, poolTopY - 1);     // lower layer intact
            Assert.Empty(CarriedBlockEntitiesAround(surfaceCenter, 4));
            Assert.Equal(0, CountItemEntitiesAround(surfaceCenter, 4));
        }

        /// <summary>
        /// G7 - EntityAlways dropped while submerged: the carried block spawns
        /// as an entity at the player's own position (exact feet cell, X/Z may
        /// sit on the cell's integer boundary); the water column is untouched.
        /// </summary>
        [Trait("summary", "An EntityAlways drop while submerged spawns the entity at the player's own submerged feet cell, with all water layers intact.")]
        [Trait("pins", "Starting in liquid, EntityAlways resolves feet to the player's own position.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropUnderwater_EntityAlways_SpawnsAtPlayerPosition()
        {
            AssertPremise();

            BlockPos basinMin = Pt(PoolX, 0, PoolZ);
            await BuildWaterPool(basinMin, PoolDepth);
            int poolTopY = basinMin.Y + PoolDepth - 1;
            BlockPos feet = new(basinMin.X, poolTopY, basinMin.Z);   // inside the top water cell
            var player = await JoinPlayer("WaterDropper");
            await AcquireChestAsync(player, Pt(24, 1, 18));

            await player.TeleportTo(feet);
            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.EntityAlways))
            {
                DropHands(player);
            }

            AssertCarryCleared(player);
            Assert.Single(Drops);
            Assert.False(Drops[0].BlockPlaced, "EntityAlways underwater must spawn an entity at the player");
            Assert.Same(player.Entity, Drops[0].Entity);

            var entities = CarriedBlockEntitiesAround(feet, 1);
            Assert.Single(entities);
            var e = entities[0];
            Assert.True(e.Pos.X >= feet.X && e.Pos.X <= feet.X + 1 && e.Pos.Z >= feet.Z && e.Pos.Z <= feet.Z + 1,
                $"Entity must spawn within the player's feet cell, found {e.Pos.X:F2},{e.Pos.Z:F2}");
            Assert.True(Math.Abs(e.Pos.Y - feet.Y) < 0.5,
                $"Entity must spawn at the player's submerged feet (~{feet.Y}), found {e.Pos.Y}");

            AssertWaterLayer(basinMin, poolTopY);
            AssertWaterLayer(basinMin, poolTopY - 1);
            AssertWaterLayer(basinMin, poolTopY - 2);
            Assert.Equal(0, CountBlocksWithPrefix(feet, 4, 3, "game:chest-"));
            Assert.Equal(0, CountItemEntitiesAround(feet, 4));
        }

        /// <summary>
        /// G8/G9 - EntityOnFailedPlacement and Items dropped while submerged:
        /// the water cells are passable/replaceable so placement reaches the
        /// LOWEST water cells of the pool (not the cells beside the player);
        /// zero entities and zero items.
        /// </summary>
        [Trait("summary", "An EntityOnFailedPlacement drop while submerged places the chest on the lowest water cell of the pool, not the cells beside the player.")]
        [Trait("pins", "Water cells are passable, so a submerged placement walks down to the pool floor.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropUnderwater_EntityOnFailedPlacement_PlacesOnPoolFloor()
        {
            await PlaceChestOnPoolFloorAsync("WaterDropperF", DropMode.EntityOnFailedPlacement);
        }

        [Trait("summary", "An Items-mode drop while submerged places the chest on the lowest water cell of the pool, with zero entities and zero items.")]
        [Trait("pins", "Items mode's submerged placement matches the placement-mode outcome.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropUnderwater_ItemsMode_PlacesOnPoolFloor()
        {
            await PlaceChestOnPoolFloorAsync("WaterDropperI", DropMode.Items);
        }

        private async Task PlaceChestOnPoolFloorAsync(string playerName, DropMode mode)
        {
            AssertPremise();

            BlockPos basinMin = Pt(PoolX, 0, PoolZ);
            await BuildWaterPool(basinMin, PoolDepth);
            int poolTopY = basinMin.Y + PoolDepth - 1;
            BlockPos feet = new(basinMin.X, poolTopY, basinMin.Z);
            var player = await JoinPlayer(playerName);
            await AcquireChestAsync(player, Pt(24, 1, 18));

            await player.TeleportTo(feet);
            using (SubscribeBlockDropped())
            using (UseDropMode(mode))
            {
                DropHands(player);
            }

            AssertCarryCleared(player);
            Assert.Single(Drops);
            Assert.True(Drops[0].BlockPlaced,
                $"DropMode.{mode} underwater must place the chest onto the pool floor");
            Assert.Same(player.Entity, Drops[0].Entity);

            var chests = ChestCellsAround(basinMin, 2, 2);
            Assert.Single(chests);
            Assert.True(chests[0].Y == basinMin.Y,
                $"Chest must land at the lowest reachable water cell (y={basinMin.Y}), found {chests[0].Y}");
            AssertInsideBasin(chests[0], basinMin);

            AssertWaterLayer(basinMin, poolTopY);
            AssertWaterLayer(basinMin, poolTopY - 1);
            Assert.Empty(CarriedBlockEntitiesAround(feet, 4));
            Assert.Equal(0, CountItemEntitiesAround(feet, 4));
        }

        // ---- G10/G11: partial-height surfaces ----

        /// <summary>
        /// G10 - EntityAlways dropped while standing on a stone path sitting on
        /// the ground: the entity lands on the path's own surface height
        /// (0.9375, read from the block at runtime), never the full cell above
        /// and never through it.
        /// </summary>
        [Trait("summary", "An EntityAlways drop while standing on a stone path lands the entity on the path's own surface height rather than the full cell above.")]
        [Trait("pins", "EntityAlways uses the substrate's collision surface for partial-height blocks.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropFromHeightOverPath_EntityAlways_LandsOnPathSurface()
        {
            await LandOnPartialSurfaceAsync("PathDropper", Pt(17, 1, 16), "game:stonepath-free");
        }

        /// <summary>
        /// G11 - EntityAlways dropped while standing on a stone path slab: the
        /// entity lands on the slab's own surface height (0.4375, read from the
        /// block at runtime) - the sub-full-height landing companion of G1.
        /// </summary>
        [Trait("summary", "An EntityAlways drop while standing on a stone path slab lands the entity on the slab's own surface height, the sub-full-height companion of the flat-ground row.")]
        [Trait("pins", "The partial-surface landing holds for a slab's lower collision height.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropFromHeightOverSlab_EntityAlways_LandsOnSlabSurface()
        {
            await LandOnPartialSurfaceAsync("SlabDropper", Pt(15, 1, 16), "game:stonepathslab-free");
        }

        private async Task LandOnPartialSurfaceAsync(string playerName, BlockPos ground, string substrateCode)
        {
            AssertPremise();

            World.SetBlock(substrateCode, ground);
            Assert.Equal(substrateCode, World.BlockAt(ground).Code?.ToString());
            AssertSolidBelow(ground);
            double surfaceTop = World.BlockAt(ground).CollisionBoxes?[0].Y2 ?? 1.0;

            var player = await JoinPlayer(playerName);
            await AcquireChestAsync(player, Pt(23, 1, 16));
            await player.TeleportTo(ground.UpCopy());   // feet above the substrate surface

            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.EntityAlways))
            {
                DropHands(player);
            }

            AssertCarryCleared(player);
            Assert.Single(Drops);
            Assert.False(Drops[0].BlockPlaced, "EntityAlways must spawn an entity on the partial surface");
            Assert.Same(player.Entity, Drops[0].Entity);

            var entities = CarriedBlockEntitiesAround(ground, 1);
            Assert.Single(entities);
            var e = entities[0];
            double feetY = ground.Y + surfaceTop;
            Assert.True(e.Pos.Y >= feetY && e.Pos.Y <= feetY + 0.006,
                $"Entity feet must rest on the substrate surface at ~{feetY:F3}, found {e.Pos.Y:F3}");
            Assert.True(Math.Abs(e.Pos.X - (ground.X + 0.5)) < 0.01 && Math.Abs(e.Pos.Z - (ground.Z + 0.5)) < 0.01,
                $"Entity must stay in the player's column, found {e.Pos.X:F2},{e.Pos.Z:F2}");

            Assert.Equal(substrateCode, World.BlockAt(ground).Code?.ToString());
            Assert.Equal(0, CountBlocksWithPrefix(ground, 4, 2, "game:chest-"));
            Assert.Equal(0, CountItemEntitiesAround(ground, 4));
        }

        // ---- shared helpers for this class ----

        /// <summary>The first air cell above the superflat ground surface (abs y = Spawn.Y + 1).</summary>
        private BlockPos LandingCell(int dx, int dz) => new(World.Spawn.X + dx, World.Spawn.Y + 1, World.Spawn.Z + dz);

        private void AssertEntityOnSurface(Entity e, BlockPos drop, double surfaceY)
        {
            Assert.True(e.Pos.Y >= surfaceY && e.Pos.Y <= surfaceY + 0.006,
                $"Entity feet should rest on the ground at ~{surfaceY}, found {e.Pos.Y}");
            Assert.True(Math.Abs(e.Pos.X - (drop.X + 0.5)) < 0.01 && Math.Abs(e.Pos.Z - (drop.Z + 0.5)) < 0.01,
                $"Entity must stay in the player's column, found {e.Pos.X:F2},{e.Pos.Z:F2}");
        }

        private void AssertTrue(bool cond, string msg) => Assert.True(cond, msg);

        private void AssertPoolArea(Entity e, BlockPos basinMin)
        {
            Assert.True(e.Pos.X >= basinMin.X && e.Pos.X <= basinMin.X + 1
                    && e.Pos.Z >= basinMin.Z && e.Pos.Z <= basinMin.Z + 1,
                $"Entity must be inside the pool footprint, found {e.Pos.X:F2},{e.Pos.Z:F2}");
        }

        private void AssertInsideBasin(BlockPos p, BlockPos basinMin)
        {
            Assert.True(p.X >= basinMin.X && p.X <= basinMin.X + 1 && p.Z >= basinMin.Z && p.Z <= basinMin.Z + 1,
                $"Expected a pool-footprint cell, found {p}");
        }

        private void AssertWaterLayer(BlockPos basinMin, int y)
        {
            Assert.Equal(WaterCode, World.BlockAt(new BlockPos(basinMin.X, y, basinMin.Z)).Code?.ToString());
        }

        private static bool IsSameCell(BlockPos a, BlockPos b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    }
}