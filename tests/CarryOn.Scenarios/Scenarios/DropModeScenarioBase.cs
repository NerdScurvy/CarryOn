using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using CarryOn.API.Common.Models;
using CarryOn.API.Event.Data;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Shared fixtures and probes for the dropped-block DropMode scenario
    /// classes (EnclosedRoomDropScenarios, GravityDropScenarios,
    /// DropOnClaimScenarios). All geometry stays inside the Atlas writable zone
    /// around spawn (spike-pinned ~±24 cells), keeps fixtures ≥ 6 cells from its
    /// row's count radii, and uses the DropFlows/DroppedTrunkHole conventions
    /// (survival joins, config restore in finally, BlockDropped subscription,
    /// snapshot/count helpers).
    /// </summary>
    public abstract class DropModeScenarioBase : CarryOnScenarioBase
    {
        protected const string ShellCode = "game:rock-granite";
        protected const string LadderCode = "game:ladder-stick-east";
        protected const string ChestCode = "game:chest-east";
        protected const string WaterCode = "game:water-still-7";
        protected const string CarriedBlockEntityCode = "carryon:carriedblock";
        protected const string ItemEntityCode = "game:item";

        protected BlockPos Pt(int dx, int dy, int dz) => World.Spawn.Offset(dx, dy, dz);

        /// <summary>
        /// Pins the shipped defaults each row builds on: chests are carryable,
        /// the default DropMode is EntityOnFailedPlacement, and ordinary blocks
        /// never spawn as entities on placement (so every row reflects the
        /// dropped-carry path, not the placed-entity short circuit).
        /// </summary>
        protected void AssertPremise()
        {
            Assert.True(Mod.Config.Carryables.Chest, "Seed must keep chest carrying enabled");
            Assert.Equal(DropMode.EntityOnFailedPlacement, Mod.Config.CarriedBlockEntity.DropMode);
            Assert.False(Mod.Config.CarriedBlockEntity.SpawnAsEntityOnPlacementByDefault,
                "Default SpawnAsEntityOnPlacement must be off for a plain chest to drop as a block");
        }

        /// <summary>
        /// Flips the carried-block DropMode for the duration of a drop and
        /// restores the baseline afterwards (the DropFlows config-restore
        /// pattern).
        /// </summary>
        protected IDisposable UseDropMode(DropMode mode)
        {
            var previous = Mod.Config.CarriedBlockEntity.DropMode;
            Mod.Config.CarriedBlockEntity.DropMode = mode;
            return new UnsubscribeAction(() => Mod.Config.CarriedBlockEntity.DropMode = previous);
        }

        /// <summary>
        /// Places a chest fixture at air level, stands the player beside it,
        /// picks the chest into the hands carry, and asserts the fixture cell is
        /// consumed. Returns the carried block.
        /// </summary>
        protected async Task<CarriedBlock> AcquireChestAsync(ITestPlayer player, BlockPos fixturePos)
        {
            World.SetBlock(ChestCode, fixturePos);
            Assert.True((World.BlockAt(fixturePos).Code?.ToString() ?? "").StartsWith("game:chest-", StringComparison.Ordinal),
                $"Fixture {fixturePos} should hold a chest, found {World.BlockAt(fixturePos).Code}");
            await player.TeleportTo(fixturePos.DownCopy());

            var carried = Actions.PickUpIntoHands(player.Entity, fixturePos, out var failure);
            Assert.True(carried != null, $"Chest pickup failed: {failure}");
            Assert.True(carried!.Block?.Code?.ToString()?.StartsWith("game:chest-", StringComparison.Ordinal) == true,
                $"Carried block should be a chest, found {carried.Block?.Code}");
            AssertAir(fixturePos);
            return carried;
        }

        /// <summary>Drops the hands carry while subscribed to the block-dropped event.</summary>
        protected void DropHands(ITestPlayer player)
        {
            Actions.Drop(player.Entity, CarrySlot.Hands);
        }

        /// <summary>Pins that the drop fully released the hands carry (nothing may be retained).</summary>
        protected void AssertCarryCleared(ITestPlayer player)
        {
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
        }

        // ---- BlockDropped subscription (DropFlows/DroppedTrunkHole copy) ----

        protected List<BlockDroppedEventArgs>? activeDrops;

        protected List<BlockDroppedEventArgs> Drops => activeDrops ??= new List<BlockDroppedEventArgs>();

        protected void OnBlockDropped(object? sender, BlockDroppedEventArgs e) => Drops.Add(e);

        protected IDisposable SubscribeBlockDropped()
        {
            Drops.Clear();
            Carry.CarryEvents.BlockDropped += OnBlockDropped;
            return new UnsubscribeAction(() => Carry.CarryEvents.BlockDropped -= OnBlockDropped);
        }

        private sealed class UnsubscribeAction(Action dispose) : IDisposable
        {
            private readonly Action dispose = dispose;
            public void Dispose() => dispose();
        }

        // ---- Geometry fixtures ----

        /// <summary>
        /// Builds a self-contained 2x2x2 room above terrain with a 1-cell solid
        /// shell: floor at interiorFoot.Y - 1, interior feet/body levels at
        /// interiorFoot.Y / +1, ceiling at +2, and 4 ladders on the north and
        /// south interior walls at body level (so they stay out of the drop ray
        /// and the placement flood). Returns the interior cells.
        /// </summary>
        protected List<BlockPos> BuildEnclosedRoom(BlockPos interiorFoot)
        {
            var accessor = World.Api.World.BlockAccessor;
            for (int x = interiorFoot.X - 1; x <= interiorFoot.X + 2; x++)
            for (int y = interiorFoot.Y - 1; y <= interiorFoot.Y + 2; y++)
            for (int z = interiorFoot.Z - 1; z <= interiorFoot.Z + 2; z++)
                World.SetBlock(ShellCode, new BlockPos(x, y, z));
            for (int x = interiorFoot.X; x <= interiorFoot.X + 1; x++)
            for (int y = interiorFoot.Y; y <= interiorFoot.Y + 1; y++)
            for (int z = interiorFoot.Z; z <= interiorFoot.Z + 1; z++)
                accessor.SetBlock(0, new BlockPos(x, y, z));
            World.SetBlock(LadderCode, new BlockPos(interiorFoot.X, interiorFoot.Y + 1, interiorFoot.Z));
            World.SetBlock(LadderCode, new BlockPos(interiorFoot.X + 1, interiorFoot.Y + 1, interiorFoot.Z));
            World.SetBlock(LadderCode, new BlockPos(interiorFoot.X, interiorFoot.Y + 1, interiorFoot.Z + 1));
            World.SetBlock(LadderCode, new BlockPos(interiorFoot.X + 1, interiorFoot.Y + 1, interiorFoot.Z + 1));

            AssertPinnedRoom(interiorFoot);

            var cells = new List<BlockPos>();
            for (int x = interiorFoot.X; x <= interiorFoot.X + 1; x++)
            for (int y = interiorFoot.Y; y <= interiorFoot.Y + 1; y++)
            for (int z = interiorFoot.Z; z <= interiorFoot.Z + 1; z++)
                cells.Add(new BlockPos(x, y, z));
            return cells;
        }

        /// <summary>
        /// Pins the sealed-room fixture unconditionally (shell solid, interior
        /// air, body-level ladders) so geometry regressions fail the setup, not
        /// a row assertion.
        /// </summary>
        private void AssertPinnedRoom(BlockPos foot)
        {
            Assert.Equal(ShellCode, World.BlockAt(new BlockPos(foot.X - 1, foot.Y - 1, foot.Z - 1)).Code?.ToString());
            Assert.Equal(ShellCode, World.BlockAt(new BlockPos(foot.X - 1, foot.Y + 2, foot.Z - 1)).Code?.ToString());
            Assert.True(World.BlockAt(new BlockPos(foot.X, foot.Y, foot.Z)).BlockId == 0,
                "Room feet cell must be air");
            Assert.Equal(LadderCode, World.BlockAt(new BlockPos(foot.X, foot.Y + 1, foot.Z)).Code?.ToString());
        }

        /// <summary>
        /// Builds a rimmed water pool at the given interior volume (X/Z corners
        /// + depth) with still-water in every cell and a solid rock ring at the
        /// surface (top layer) rim; waits a few ticks for the water to settle.
        /// Result: the surface layer stays liquid - a drop never finds a drained
        /// basin.
        /// </summary>
        protected async Task BuildWaterPool(BlockPos basinMin, int depth)
        {
            // basinMin is a spawn-relative corner via Pt. Water replaces terrain at
            // y = basinMin.Y..basinMin.Y+depth-1, rim ring of rock at the surface (top layer).
            int topY = basinMin.Y + depth - 1;
            for (int x = basinMin.X; x <= basinMin.X + 1; x++)
            for (int y = basinMin.Y; y <= topY; y++)
            for (int z = basinMin.Z; z <= basinMin.Z + 1; z++)
                World.SetBlock(WaterCode, new BlockPos(x, y, z));
            for (int x = basinMin.X - 1; x <= basinMin.X + 2; x++)
            for (int z = basinMin.Z - 1; z <= basinMin.Z + 2; z++)
            {
                if (x >= basinMin.X && x <= basinMin.X + 1 && z >= basinMin.Z && z <= basinMin.Z + 1) continue;
                World.SetBlock(ShellCode, new BlockPos(x, topY, z));
            }

            await World.Ticks(20);
            Assert.Equal(WaterCode, World.BlockAt(basinMin).Code?.ToString());
            Assert.Equal(WaterCode, World.BlockAt(new BlockPos(basinMin.X, topY, basinMin.Z)).Code?.ToString());
        }

        // ---- Counts / scans ----

        protected bool IsCarriedBlockEntity(Entity e) => e.Code?.ToString() == CarriedBlockEntityCode;

        protected bool IsItemEntity(Entity e) => e.Code?.ToString() == ItemEntityCode;

        /// <summary>Counts spawned carried-chest entities whose block cell is within the radius box of <paramref name="center"/>.</summary>
        protected int CountCarriedBlockEntitiesAround(BlockPos center, int radius)
        {
            int n = 0;
            foreach (var e in World.Api.World.LoadedEntities.Values)
            {
                if (!IsCarriedBlockEntity(e)) continue;
                var p = e.Pos.AsBlockPos;
                if (Math.Abs(p.X - center.X) <= radius && Math.Abs(p.Y - center.Y) <= radius && Math.Abs(p.Z - center.Z) <= radius) n++;
            }
            return n;
        }

        /// <summary>Returns the spawned carried-chest entities whose block cell is within the radius box of <paramref name="center"/>.</summary>
        protected List<Entity> CarriedBlockEntitiesAround(BlockPos center, int radius)
        {
            var found = new List<Entity>();
            foreach (var e in World.Api.World.LoadedEntities.Values)
            {
                if (!IsCarriedBlockEntity(e)) continue;
                var p = e.Pos.AsBlockPos;
                if (Math.Abs(p.X - center.X) <= radius && Math.Abs(p.Y - center.Y) <= radius && Math.Abs(p.Z - center.Z) <= radius) found.Add(e);
            }
            return found;
        }

        protected int CountItemEntitiesAround(BlockPos center, int radius)
        {
            int n = 0;
            foreach (var e in World.Api.World.LoadedEntities.Values)
            {
                if (!IsItemEntity(e)) continue;
                var p = e.Pos.AsBlockPos;
                if (Math.Abs(p.X - center.X) <= radius && Math.Abs(p.Y - center.Y) <= radius && Math.Abs(p.Z - center.Z) <= radius) n++;
            }
            return n;
        }

        /// <summary>Counts blocks whose full code starts with the prefix inside the radius box (dy loops the vertical span, DroppedTrunkHole convention).</summary>
        protected int CountBlocksWithPrefix(BlockPos center, int radius, int yRadius, string prefix)
        {
            int count = 0;
            var accessor = World.Api.World.BlockAccessor;
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -yRadius; dy <= yRadius; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                var block = accessor.GetBlock(new BlockPos(center.X + dx, center.Y + dy, center.Z + dz));
                if (block.Code?.ToString()?.StartsWith(prefix, StringComparison.Ordinal) == true) count++;
            }
            return count;
        }

        protected bool IsChest(Block block) => block.Code?.FirstCodePart() == "chest";

        /// <summary>Lists cells holding a chest-* within the box centered on <paramref name="center"/>.</summary>
        protected List<BlockPos> ChestCellsAround(BlockPos center, int radius, int yRadius)
        {
            var found = new List<BlockPos>();
            var accessor = World.Api.World.BlockAccessor;
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -yRadius; dy <= yRadius; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                var pos = new BlockPos(center.X + dx, center.Y + dy, center.Z + dz);
                if (IsChest(accessor.GetBlock(pos))) found.Add(pos);
            }
            return found;
        }

        protected void AssertAir(BlockPos pos)
        {
            Assert.True(World.BlockAt(pos).BlockId == 0, $"Cell {pos} should be air, found {World.BlockAt(pos).Code}");
        }

        protected void AssertSolidBelow(BlockPos pos)
        {
            var floor = World.BlockAt(pos.DownCopy());
            Assert.True(floor.BlockId != 0 && floor.MatterState == EnumMatterState.Solid,
                $"Cell below {pos} must be solid, found {floor.Code}");
        }

        // ---- No-clobber snapshot ----

        /// <summary>Snapshots block codes over a region; the caller re-reads and asserts equality (except exempt cells).</summary>
        protected Dictionary<BlockPos, string> SnapshotRegion(BlockPos min, BlockPos max)
        {
            var snapshot = new Dictionary<BlockPos, string>();
            for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
            {
                var pos = new BlockPos(x, y, z);
                snapshot[pos] = World.BlockAt(pos).Code?.ToString() ?? "";
            }
            return snapshot;
        }

        protected void AssertRegionUnchangedExcept(Dictionary<BlockPos, string> before, params BlockPos[] except)
        {
            foreach (var (pos, codeBefore) in before)
            {
                if (except.Any(e => e.X == pos.X && e.Y == pos.Y && e.Z == pos.Z)) continue;
                var codeAfter = World.BlockAt(pos).Code?.ToString() ?? "";
                Assert.True(codeAfter == codeBefore,
                    $"Cell {pos} was clobbered by the drop: held {codeBefore} before, holds {codeAfter} after");
            }
        }
    }
}