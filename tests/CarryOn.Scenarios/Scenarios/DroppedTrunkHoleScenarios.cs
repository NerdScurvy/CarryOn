using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.API.Event.Data;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Dropped-trunk placement in a confined 1x2 hole: the dropped
    /// multiblock trunk must fill the hole exactly - both footprint cells
    /// (controller + live multiblock dummy) inside the two dug cells, oriented
    /// along the hole's axis - without replacing any block around it (rim,
    /// floor, the air above). One hole dug east-west and one north-south, each
    /// dropped from both ends the player stands in, under the DEFAULT drop
    /// settings: the fixture seeds only Carryables.ChestTrunk, so
    /// CarriedBlockEntity.DropMode stays at its EntityOnFailedPlacement default
    /// (block placement first) and a plain trunk never short-circuits to an
    /// entity spawn - that default is what makes the trunk "drop as a block".
    ///
    /// Determinism pins: the drop search shuffles BFS neighbor order, candidate
    /// ties and facing order (BlockPlacer.GetNeighbors / GetBlockFacings /
    /// unstable List.Sort), so which hole end becomes the CONTROLLER is not
    /// stable - but the hole cells are the only passable cells below the
    /// body-level center (the candidate sort is Y-first,
    /// BlockPlacer.ComparePlacementDistance), and at each hole cell exactly one
    /// facing variant fits (every other variant's dummy hits the solid rim,
    /// which BlockBehaviorMultiblock.CanPlaceBlock rejects). The trunk
    /// therefore always spans the hole; the rows assert the FOOTPRINT SET
    /// equals the two hole cells (never the controller end), the variant is on
    /// the hole's axis, and the exact dummy code per the footprint table
    /// (pinned from assets/survival/blocktypes/wood/chest-trunk.json, same
    /// table the five other trunk classes carry). The standing player's own
    /// feet cell may become the controller cell: the dropped path writes
    /// through SetBlock directly (CarryPlacementService.TryPlaceDownDirect)
    /// and never consults vanilla's entity-intersection check - pinned
    /// expected behavior, not a bug.
    /// </summary>
    [Trait("area", "Drop")]
    [Trait("summary", "A carried trunk dropped while standing in one end of a dug 1x2 hole fills exactly those two cells, oriented along the hole's axis, without replacing any surrounding block.")]
    [AtlasDataFiles("fixtures/TrunkConfig", TargetPath = "ModConfig")]
    public class DroppedTrunkHoleScenarios : CarryOnScenarioBase
    {
        private const string TrunkFirstPart = "trunk";

        private static readonly Dictionary<string, (int Dx, int Dy, int Dz, string DummyCode)> TrunkFootprint = new()
        {
            ["north"] = (1, 0, 0, "game:multiblock-monolithic-p1-0-0"),
            ["east"] = (0, 0, 1, "game:multiblock-monolithic-0-0-p1"),
            ["south"] = (-1, 0, 0, "game:multiblock-monolithic-n1-0-0"),
            ["west"] = (0, 0, -1, "game:multiblock-monolithic-0-0-n1"),
        };

        /// <summary>Variants whose dummy offset runs along X - the only ones that can fit a hole dug east-west.</summary>
        private static readonly string[] EastWestAxisVariants = { "north", "south" };

        /// <summary>Variants whose dummy offset runs along Z - the only ones that can fit a hole dug north-south.</summary>
        private static readonly string[] NorthSouthAxisVariants = { "east", "west" };

        /// <summary>
        /// H1 - a carried trunk dropped while standing in the EAST end of a 1x2
        /// east-west hole fills the hole exactly and clobbers nothing around it.
        /// </summary>
        [Trait("summary", "A carried trunk dropped while standing in the east end of a 1x2 east-west hole fills the hole exactly and clobbers nothing around it.")]
        [Trait("pins", "A dropped trunk's two-cell footprint fills a confined hole and leaves the surrounding ground untouched.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DroppedTrunk_EastWestHole_FromEastEnd_FillsHole_NoClobber()
        {
            BlockPos westEnd = World.Spawn.Offset(8, 0, 8);
            BlockPos eastEnd = World.Spawn.Offset(9, 0, 8);
            await DropTrunkIntoHoleAsync("HoleDropperE", eastEnd, westEnd, EastWestAxisVariants);
        }

        /// <summary>
        /// H2 - the same east-west hole, dropped while standing in the WEST end.
        /// </summary>
        [Trait("summary", "A carried trunk dropped while standing in the west end of the same east-west hole fills the hole exactly and clobbers nothing around it.")]
        [Trait("pins", "The confined-hole fill holds from either end of an east-west hole.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DroppedTrunk_EastWestHole_FromWestEnd_FillsHole_NoClobber()
        {
            BlockPos westEnd = World.Spawn.Offset(8, 0, 8);
            BlockPos eastEnd = World.Spawn.Offset(9, 0, 8);
            await DropTrunkIntoHoleAsync("HoleDropperW", westEnd, eastEnd, EastWestAxisVariants);
        }

        /// <summary>
        /// H3 - a carried trunk dropped while standing in the NORTH end of a 1x2
        /// north-south hole fills the hole exactly and clobbers nothing around it.
        /// </summary>
        [Trait("summary", "A carried trunk dropped while standing in the north end of a 1x2 north-south hole fills the hole exactly and clobbers nothing around it.")]
        [Trait("pins", "The confined-hole fill holds for a north-south hole as well.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DroppedTrunk_NorthSouthHole_FromNorthEnd_FillsHole_NoClobber()
        {
            BlockPos northEnd = World.Spawn.Offset(8, 0, 8);
            BlockPos southEnd = World.Spawn.Offset(8, 0, 9);
            await DropTrunkIntoHoleAsync("HoleDropperN", northEnd, southEnd, NorthSouthAxisVariants);
        }

        /// <summary>
        /// H4 - the same north-south hole, dropped while standing in the SOUTH end.
        /// </summary>
        [Trait("summary", "A carried trunk dropped while standing in the south end of the same north-south hole fills the hole exactly and clobbers nothing around it.")]
        [Trait("pins", "The confined-hole fill holds from either end of a north-south hole.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DroppedTrunk_NorthSouthHole_FromSouthEnd_FillsHole_NoClobber()
        {
            BlockPos northEnd = World.Spawn.Offset(8, 0, 8);
            BlockPos southEnd = World.Spawn.Offset(8, 0, 9);
            await DropTrunkIntoHoleAsync("HoleDropperS", southEnd, northEnd, NorthSouthAxisVariants);
        }

        /// <summary>
        /// Shared row body: dig the hole, acquire a trunk on open ground away
        /// from it, stand in <paramref name="standEnd"/>, drop, then assert the
        /// carried-slot bookkeeping, the drop-event contract, the footprint
        /// filling the hole (set-wise, axis-oriented, exact dummy code), the
        /// single-structure counts, and the no-clobber snapshot invariant.
        /// </summary>
        private async Task DropTrunkIntoHoleAsync(string playerName, BlockPos standEnd, BlockPos farEnd, string[] axisVariants)
        {
            // Premise pins: the seed enables trunk carrying and the drop settings
            // are the shipped defaults - the fixture flips only
            // Carryables.ChestTrunk, so DropMode stays EntityOnFailedPlacement
            // (block placement first, entity only on failed placement) and
            // SpawnAsEntityOnPlacement stays off.
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");
            Assert.Equal(DropMode.EntityOnFailedPlacement, Mod.Config.CarriedBlockEntity.DropMode);
            Assert.False(Mod.Config.CarriedBlockEntity.SpawnAsEntityOnPlacementByDefault,
                "Default SpawnAsEntityOnPlacement must be off for a plain trunk to drop as a block");

            // Dig the 1x2 hole: both surface cells to air, with a solid floor
            // left below (the drop search's HasSupport premise).
            DigCell(standEnd);
            DigCell(farEnd);
            AssertAir(standEnd);
            AssertAir(farEnd);
            AssertSolidFloorBelow(standEnd);
            AssertSolidFloorBelow(farEnd);

            // Acquire the trunk on open ground away from the hole (outside the
            // snapshot and count radii), then carry it to the hole.
            BlockPos fixturePos = World.Spawn.Offset(14, 1, 12);
            BlockPos fixtureDummy = PlaceTrunkFixture("south", fixturePos);

            ITestPlayer player = await JoinPlayer(playerName);
            await player.TeleportTo(World.Spawn.Offset(14, 0, 12));
            var carried = Actions.PickUpIntoHands(player.Entity, fixturePos, out var pickupFailure);
            Assert.True(carried != null, $"Trunk pickup failed: {pickupFailure}");
            Assert.True(carried!.Block?.Code?.ToString()?.StartsWith("game:trunk-", StringComparison.Ordinal) == true,
                $"The carried block should be a trunk, found {carried.Block?.Code}");
            AssertAir(fixturePos);
            AssertAir(fixtureDummy);

            // Stand in the hole (feet cell = the standing hole cell) and
            // snapshot the neighborhood before the drop.
            await player.TeleportTo(standEnd);
            var before = SnapshotNeighborhood(standEnd, farEnd);

            using (SubscribeBlockDropped())
            {
                Actions.Drop(player.Entity, CarrySlot.Hands);
            }

            // Carry cleared; exactly one placed-block drop event for this trunk.
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Single(Drops);
            Assert.True(Drops[0].BlockPlaced,
                "The trunk should drop as a block into the hole, not fall back to entity/items");
            Assert.True(Drops[0].CarriedBlock.Block?.Code?.ToString()?.StartsWith("game:trunk-", StringComparison.Ordinal) == true,
                $"The dropped carry should be the trunk, found {Drops[0].CarriedBlock.Block?.Code}");
            Assert.Same(player.Entity, Drops[0].Entity);

            // The trunk fills the hole exactly: one hole cell holds the trunk
            // controller, the OTHER hole cell holds its live multiblock dummy -
            // never a cell outside the hole. Which end is the controller is not
            // deterministic (shuffled facings / unstable candidate ties), so the
            // footprint is asserted as a set plus the variant's axis membership
            // plus the exact dummy code.
            Block controller;
            BlockPos controllerPos;
            BlockPos partnerPos;
            var standBlock = World.BlockAt(standEnd);
            var farBlock = World.BlockAt(farEnd);
            if (standBlock.Code?.FirstCodePart() == TrunkFirstPart)
            {
                controller = standBlock;
                controllerPos = standEnd.Copy();
                partnerPos = farEnd.Copy();
            }
            else if (farBlock.Code?.FirstCodePart() == TrunkFirstPart)
            {
                controller = farBlock;
                controllerPos = farEnd.Copy();
                partnerPos = standEnd.Copy();
            }
            else
            {
                Assert.Fail($"Neither hole cell holds the dropped trunk: {standEnd} has {standBlock.Code}, {farEnd} has {farBlock.Code}");
                return;
            }

            var side = CarryRotationHelper.GetBlockFacing(controller.Code!);
            Assert.True(side != null, $"Placed trunk {controller.Code} should carry a facing suffix");
            Assert.Contains(side!.Code, axisVariants);

            var (dx, dy, dz, dummyCode) = TrunkFootprint[side.Code!];
            BlockPos dummyPos = controllerPos.Copy().Add(dx, dy, dz);
            Assert.True(IsSameCell(dummyPos, partnerPos),
                $"The {side.Code} variant's dummy offset must land on the other hole cell {partnerPos}, derived {dummyPos}");
            Assert.Equal(dummyCode, World.BlockAt(dummyPos).Code?.ToString());

            // Exactly one structure in the area: one controller, one dummy,
            // nothing else written anywhere.
            var countCenter = player.Entity.Pos.AsBlockPos;
            Assert.Equal(1, CountBlocksWithCodePrefix(countCenter, 5, "game:trunk-"));
            Assert.Equal(1, CountBlocksWithCodePrefix(countCenter, 5, "game:multiblock-monolithic"));

            // No clobber: every cell around the hole - rim at hole level, floor
            // below, air above - holds exactly what it held before the drop,
            // except the two hole cells the trunk now fills.
            AssertAreaUnchangedExcept(before, standEnd, farEnd);
        }

        private List<BlockDroppedEventArgs>? activeDrops;

        private List<BlockDroppedEventArgs> Drops => activeDrops ??= new List<BlockDroppedEventArgs>();

        private void OnBlockDropped(object? sender, BlockDroppedEventArgs e)
        {
            Drops.Add(e);
        }

        private IDisposable SubscribeBlockDropped()
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

        /// <summary>
        /// Places a vanilla trunk fixture with its live multiblock dummy (the
        /// server accessor's SetBlock runs BlockBehaviorMultiblock.OnBlockPlaced
        /// synchronously, which places the dummy cell). Returns the dummy position.
        /// </summary>
        private BlockPos PlaceTrunkFixture(string side, BlockPos controller)
        {
            World.SetBlock($"game:trunk-{side}", controller);
            var (dx, dy, dz, dummyCode) = TrunkFootprint[side];
            var dummyPos = controller.Copy().Add(dx, dy, dz);

            Assert.True(World.BlockAt(controller).Code?.FirstCodePart() == TrunkFirstPart,
                $"Fixture controller at {controller} should hold the trunk, found {World.BlockAt(controller).Code}");
            Assert.Equal(dummyCode, World.BlockAt(dummyPos).Code?.ToString());

            return dummyPos;
        }

        /// <summary>
        /// Digs one hole cell to air. The Atlas SetBlock seam takes asset-location
        /// codes; air is addressed by id 0 through the engine accessor directly.
        /// </summary>
        private void DigCell(BlockPos pos)
        {
            World.Api.World.BlockAccessor.SetBlock(0, pos);
        }

        private void AssertAir(BlockPos pos)
        {
            Assert.True(World.BlockAt(pos).BlockId == 0,
                $"Cell {pos} should be air, found {World.BlockAt(pos).Code}");
        }

        private void AssertSolidFloorBelow(BlockPos holeCell)
        {
            var floor = World.BlockAt(holeCell.DownCopy());
            Assert.True(floor.BlockId != 0 && floor.MatterState == EnumMatterState.Solid,
                $"The hole floor below {holeCell} must be solid for the drop placement's support check, found {floor.Code}");
        }

        private Dictionary<BlockPos, string> SnapshotNeighborhood(BlockPos standEnd, BlockPos farEnd)
        {
            var snapshot = new Dictionary<BlockPos, string>();
            int minX = Math.Min(standEnd.X, farEnd.X) - 3;
            int maxX = Math.Max(standEnd.X, farEnd.X) + 3;
            int minZ = Math.Min(standEnd.Z, farEnd.Z) - 3;
            int maxZ = Math.Max(standEnd.Z, farEnd.Z) + 3;
            int minY = standEnd.Y - 1; // the hole floor
            int maxY = standEnd.Y + 3; // the air above the hole mouth
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
            {
                var pos = new BlockPos(x, y, z);
                snapshot[pos] = World.BlockAt(pos).Code?.ToString() ?? "";
            }

            return snapshot;
        }

        private void AssertAreaUnchangedExcept(Dictionary<BlockPos, string> before, BlockPos exceptA, BlockPos exceptB)
        {
            foreach (var (pos, codeBefore) in before)
            {
                if (IsSameCell(pos, exceptA) || IsSameCell(pos, exceptB)) continue;
                var codeAfter = World.BlockAt(pos).Code?.ToString() ?? "";
                Assert.True(codeAfter == codeBefore,
                    $"Cell {pos} was clobbered by the drop: held {codeBefore} before, holds {codeAfter} after");
            }
        }

        private static bool IsSameCell(BlockPos a, BlockPos b)
        {
            return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        }

        private int CountBlocksWithCodePrefix(BlockPos center, int radius, string prefix)
        {
            var accessor = World.Api.World.BlockAccessor;
            int count = 0;
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -3; dy <= 3; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                var block = accessor.GetBlock(new BlockPos(center.X + dx, center.Y + dy, center.Z + dz));
                if (block.Code?.ToString()?.StartsWith(prefix, StringComparison.Ordinal) == true)
                    count++;
            }

            return count;
        }
    }
}
