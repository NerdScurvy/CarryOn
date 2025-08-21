using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Common;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.ServerMods.NoObf;

namespace CarryOn.Utility
{
    public class BlockPlacer
    {

        private ICoreAPI Api { get; }

        private IBlockAccessor BlockAccessor => Api?.World?.BlockAccessor;

        public BlockPlacer(ICoreAPI api)
        {
            Api = api ?? throw new ArgumentNullException(nameof(api));
        }


        public class BlockPlacementX
        {
            public BlockPos Position { get; set; }
            public float Rotation { get; set; } = 0;
        }

        public class BlockPlacement1
        {
            public Block Block { get; set; }
            public BlockPos Position { get; set; }

            public override bool Equals(object obj)
            {
                if (obj is BlockPlacement1 other)
                    return Position.Equals(other.Position);
                return false;
            }

            public override int GetHashCode()
            {
                return Position.GetHashCode();
            }

        }


        /// <summary>
        /// Finds all connected passable blocks (air, ladders, etc.) within a given radius.
        /// Uses BFS flood-fill from startPos.
        /// </summary>
        public HashSet<BlockPos> GetAccessibleArea(BlockPos startPos, int maxDistance)
        {
            var visited = new HashSet<BlockPos>();
            var queue = new Queue<BlockPos>();

            queue.Enqueue(startPos);
            visited.Add(startPos);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in GetNeighbors(current))
                {
                    var block = BlockAccessor.GetBlock(neighbor);
                    if (!visited.Contains(neighbor) &&
                        IsPassable(block) &&
                        startPos.DistanceTo(neighbor) <= maxDistance)
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return visited;
        }


        /// <summary>
        /// Find the closest valid placement for a chest around the player.
        /// Priority: directly under player, then nearby accessible air blocks (down first, then sides, then up).
        /// </summary>
        public BlockSelection FindBlockPlacement(Block droppedBlock, BlockPos centrePos, int searchRadius, int maxUpSearch = 3)
        {

            var accessible = GetAccessibleArea(centrePos, searchRadius);

            // Step 1: check downward until solid support
            for (int y = centrePos.Y; y >= 0; y--)
            {

                var pos = new BlockPos(centrePos.X, y, centrePos.Z);
                var blockAtPos = BlockAccessor.GetBlock(pos);
                //var placement = new BlockPlacement { Position = pos, Block = blockAtPos };
                //  if (!accessible.Contains(placement)) continue;
                if (!IsPassable(blockAtPos)) break; // Stop if we hit a non-passable block

                // Check if we can place the block here
                Api.Logger.Debug("Checking position {0}", new BlockPos(centrePos.X, y, centrePos.Z));
                var blockSelection = CheckCanPlaceBlock(droppedBlock, pos);
                if (blockSelection != null)
                {
                    return blockSelection;
                }


            }

            return null;

            /*             // Step 2: search within accessible area, sorted by distance (down bias)
                        var candidates = new List<BlockPos>();
                        foreach (var placement in accessible)
                        {

                            if (IsGasOrLiquid(placement) && HasSupport(droppedBlock, placement.Position))
                                candidates.Add(placement.Position);
                        }

                        candidates.Sort((a, b) =>
                        {
                            // sort by vertical distance first (favor down)
                            int dy = (a.Y - centrePos.Y).CompareTo(b.Y - centrePos.Y);
                            if (dy != 0) return dy;
                            return Distance(centrePos, a).CompareTo(Distance(centrePos, b));
                        });

                        if (candidates.Count > 0)
                            return [candidates[0]];

                        // Step 3: try upwards a few blocks
                        for (int up = 1; up <= maxUpSearch; up++)
                        {
                            var pos = new BlockPos(centrePos.X, centrePos.Y + up, centrePos.Z);
                            var blockAtPos = BlockAccessor.GetBlock(pos);
                            var placement = new BlockPlacement { Position = pos, Block = blockAtPos };
                            if (accessible.Contains(placement) && IsGasOrLiquid(placement) && HasSupport(droppedBlock, placement.Position))
                                return [pos];
                        }

                        // nothing found
                        return null; */
        }

        private bool IsPassable(Block block)
        {
            return IsGasOrLiquid(block) || IsNonSolid(block);
        }

        private bool IsGasOrLiquid(Block block)
        {
            return block.MatterState is EnumMatterState.Liquid or EnumMatterState.Gas;
        }

        private bool IsNonSolid(Block block)
        {
            return !block.SideSolid.All;
        }

        private float Distance(BlockPos a, BlockPos b)
        {
            int dx = a.X - b.X;
            int dy = a.Y - b.Y;
            int dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private IEnumerable<BlockPos> GetNeighbors(BlockPos blockPos, bool onlyHorizontal = false)
        {
            yield return blockPos.NorthCopy();
            yield return blockPos.SouthCopy();
            yield return blockPos.EastCopy();
            yield return blockPos.WestCopy();
            if (onlyHorizontal) yield break;
            yield return blockPos.UpCopy();
            yield return blockPos.DownCopy();
        }

        BlockSelection CheckCanPlaceBlock(Block droppedBlock, BlockPos position)
        {
            var hasSupport = HasSupport(droppedBlock, position);

            var behavior = droppedBlock.GetBehavior<BlockBehaviorCarryable>();
            var offset = behavior?.MultiblockOffset;
            if (offset != null)
            {
                foreach (var neighbor in GetNeighbors(position, onlyHorizontal: true))
                {
                    // Check if neighbor is a valid placement position
                    var testBlock = BlockAccessor.GetBlock(neighbor);
                    if (!testBlock.IsReplacableBy(droppedBlock))
                    {
                        Api.Logger.Debug("Neighbor {0} at {1} is not replacable by {2}", testBlock.Code, neighbor, droppedBlock.Code);
                        continue;
                    }

                    // If not supported in either position, skip
                    if (!hasSupport && !HasSupport(droppedBlock, neighbor))
                    {
                        Api.Logger.Warning("No support for {0} at {1} or neighbor {2}", droppedBlock.Code, position, neighbor);
                        continue;
                    }


                    var blockFacing = GetFacing(position, neighbor);
                    Api.Logger.Debug("Using offset {0} for placement at {1} with facing {2}", offset, position, blockFacing);
                    return new BlockSelection
                    {
                        Position = position,
                        Face = blockFacing

                    };
                }
                /* 
                                // Try original offset direction
                                var multiPos = position.AddCopy(offset);
                                var testBlock = BlockAccessor.GetBlock(multiPos);
                                if (testBlock.IsReplacableBy(droppedBlock) && (HasSupport(droppedBlock, position) || HasSupport(droppedBlock, multiPos)))
                                {
                                    return position;
                                }

                                // Try opposite offset direction
                                var oppositeOffset = new Vec3i(-offset.X, -offset.Y, -offset.Z);
                                var oppositePos = position.AddCopy(oppositeOffset);
                                var testBlockOpposite = BlockAccessor.GetBlock(oppositePos);
                                if (testBlockOpposite.IsReplacableBy(droppedBlock) && (HasSupport(droppedBlock, position) || HasSupport(droppedBlock, oppositePos)))
                                {
                                    return new BlockPlacement
                                    {
                                        Position = oppositePos,
                                        Rotation = 0 // TDOD Default rotation
                                    };
                                } */

                return null;
            }
            else
            {
                // Single block: just check support for main position
                if (!HasSupport(droppedBlock, position))
                {
                    return null;
                }
                return new BlockSelection
                {
                    Position = position,
                    Face = GetRandomFacing(),
                    HitPosition = new Vec3d(0.5, 0.5, 0.5), // Center hit position
                };
            }
        }

        bool HasSupport(Block droppedBlock, BlockPos pos)
        {
            var below = pos.DownCopy();
            var blockBelow = BlockAccessor.GetBlock(below);
            if (blockBelow == null || blockBelow.IsReplacableBy(droppedBlock) || IsGasOrLiquid(blockBelow))
            {
                Api.Logger.Debug("No support below {0} - The block is {1}", pos, blockBelow?.Code);
                return false;
            }
            return true;
        }

        private BlockFacing GetFacing(BlockPos mainPos, BlockPos neighborPos)
        {
            int dx = neighborPos.X - mainPos.X;
            int dz = neighborPos.Z - mainPos.Z;

            // Only horizontal directions
            if (dx == 1 && dz == 0) return BlockFacing.NORTH;
            if (dx == -1 && dz == 0) return BlockFacing.SOUTH;
            if (dx == 0 && dz == 1) return BlockFacing.EAST;
            if (dx == 0 && dz == -1) return BlockFacing.WEST;

            // Fallback
            return BlockFacing.NORTH;
        }

        private BlockFacing GetRandomFacing()
        {
            // Get a random horizontal direction
            var directions = new[] { "north", "east", "south", "west" };
            var randomIndex = new Random().Next(directions.Length);
            return BlockFacing.FromCode(directions[randomIndex]);
        }

        /*         private bool HasSupport(Block block, BlockPlacement placement, BlockPlacement neighborPlacement = null)
                {
                    // support = solid below OR solid to any side
                    var below = placement.Position.DownCopy();
                    var blockBelow = Api.World.BlockAccessor.GetBlock(below);

                    if (IsGasOrLiquid(blockBelow)) return false;
                    if (blockBelow.IsReplacableBy(block)) return false;

                    // If checking for trunk/multiblock, also check neighbor
                    if (neighborPlacement != null)
                    {
                        var neighborBelow = neighborPlacement.Position.DownCopy();
                        var neighborBlockBelow = Api.World.BlockAccessor.GetBlock(neighborBelow);
                        if (IsGasOrLiquid(neighborBlockBelow)) return false;
                        if (neighborBlockBelow.IsReplacableBy(block)) return false;
                    }

                    return true;
                } */
    }
}