using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Server.Logic
{
    public class BlockPlacer
    {

        public ICoreAPI Api { get; }

        public static Random Rand { get; } = Random.Shared;

        public IBlockAccessor BlockAccessor => Api?.World?.BlockAccessor;

        public BlockPlacer(ICoreAPI api)
        {
            Api = api ?? throw new ArgumentNullException(nameof(api));
        }

        /// <summary>
        /// Finds all connected passable blocks (air, liquids and rain permeable) within a given radius.
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
                        IsPassable(block, neighbor) &&
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
        /// Get a valid placement position for a block with gravity consideration.
        /// TODO: Consider caching visited positions for performance.
        /// </summary>
        private BlockSelection GetValidPlacementWithGravity(Block droppedBlock, BlockPos startPos)
        {
            for (int y = startPos.Y; y >= 0; y--)
            {
                var pos = new BlockPos(startPos.X, y, startPos.Z);
                var block = BlockAccessor.GetBlock(pos);
                if (!IsPassable(block, pos)) break; // Stop if we hit a non-passable block

                // Check if we can place the block here
                Api.Logger.Debug("Checking position {0}", new BlockPos(startPos.X, y, startPos.Z));
                var blockSelection = CheckCanPlaceBlock(droppedBlock, pos);
                if (blockSelection != null)
                {
                    return blockSelection;
                }
            }

            return null;
        }

        /// <summary>
        /// Find the closest valid placement for a chest around the player.
        /// Priority: directly under player, then nearby accessible air blocks (down first, then sides, then up).
        /// </summary>
        public BlockSelection FindBlockPlacement(Block droppedBlock, BlockPos centrePos, int searchRadius)
        {

            var accessible = GetAccessibleArea(centrePos, searchRadius);

            List<BlockPos> candidates = accessible.ToList();

            candidates.Sort((a, b) =>
                        {
                            // sort by vertical distance first (favor down)
                            int dy = (a.Y - centrePos.Y).CompareTo(b.Y - centrePos.Y);
                            if (dy != 0) return dy;
                            return centrePos.DistanceTo(a).CompareTo(centrePos.DistanceTo(b));
                        });

            foreach(var candidate in candidates)
            {
                var placement = GetValidPlacementWithGravity(droppedBlock, candidate);
                if (placement != null)
                {
                    Api.Logger.Debug("Found valid placement at {0}", candidate);
                    return placement;
                }
            }

            return null;

        }

        /// <summary>
        /// Check if a block is passable (air, liquid, rain permeable or an open door/trapdoor).
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private bool IsPassable(Block block , BlockPos pos)
        {

            var multiblockOrigin = BlockUtils.GetMultiblockOriginSelection(BlockAccessor, new BlockSelection() { Position = pos, Block = block });

            var testBlock = multiblockOrigin?.Block ?? block;

            if (testBlock.HasBehavior<BlockBehaviorDoor>())
            {
                var blockEntity = BlockAccessor.GetBlockEntity(multiblockOrigin.Position);
                var doorBehavior = blockEntity?.GetBehavior<BEBehaviorDoor>();

                return !doorBehavior?.Opened ?? false;
            }

           if (testBlock.HasBehavior<BlockBehaviorTrapDoor>())
            {
                var blockEntity = BlockAccessor.GetBlockEntity(multiblockOrigin.Position);
                var doorBehavior = blockEntity?.GetBehavior<BEBehaviorTrapDoor>();

                return doorBehavior?.Opened ?? false;
            }                

            // Check if the block is gas, liquid, or rain permeable with the assumption that rain permeable blocks are non-solid and the player can access beyond them;
            return IsGasOrLiquid(testBlock) || testBlock.RainPermeable;
        }

        /// <summary>
        /// Check if a block is gas or liquid.
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private bool IsGasOrLiquid(Block block)
        {
            return block.MatterState is EnumMatterState.Liquid or EnumMatterState.Gas;
        }

        /// <summary>
        /// Get the neighboring block positions around a given block position and return in a random order.
        /// </summary>
        private IEnumerable<BlockPos> GetNeighbors(BlockPos blockPos, bool onlyHorizontal = false)
        {
            var neighbors = new List<BlockPos>
            {
                blockPos.NorthCopy(),
                blockPos.SouthCopy(),
                blockPos.EastCopy(),
                blockPos.WestCopy()
            };
            if (!onlyHorizontal)
            {
                neighbors.Add(blockPos.UpCopy());
                neighbors.Add(blockPos.DownCopy());
            }

            for (int i = neighbors.Count - 1; i > 0; i--)
            {
                int j = Rand.Next(i + 1);
                (neighbors[j], neighbors[i]) = (neighbors[i], neighbors[j]);
            }

            foreach (var neighbor in neighbors)
                yield return neighbor;
        }

        public BlockSelection CheckCanPlaceBlock(Block droppedBlock, BlockPos position)
        {
            var testBlock = BlockAccessor.GetBlock(position);
            if (!testBlock.IsReplacableBy(droppedBlock)) return null;

            var hasSupport = HasSupport(droppedBlock, position);

            var behavior = droppedBlock.GetBehavior<BlockBehaviorCarryable>();
            var offset = behavior?.MultiblockOffset;
            if (offset != null)
            {

                var hole = new List<BlockPos>();

                foreach (var neighbor in GetNeighbors(position, onlyHorizontal: true))
                {
                    // Check if neighbor is a valid placement position
                    testBlock = BlockAccessor.GetBlock(neighbor);
                    if (!testBlock.IsReplacableBy(droppedBlock))
                    {
                        Api.Logger.Debug("Neighbor {0} at {1} is not replacable by {2}", testBlock.Code, neighbor, droppedBlock.Code);
                        continue;
                    }

                    // If not supported in either position, skip
                    if (!hasSupport && !HasSupport(droppedBlock, neighbor))
                    {
                        Api.Logger.Warning("No support for {0} at {1} or neighbor {2}", droppedBlock.Code, position, neighbor);
                        hole.Add(neighbor.Copy());
                        continue;
                    }


                    var blockFacing = GetFacingForMultiblock(position, neighbor);
                    Api.Logger.Debug("Using offset {0} for placement at {1} with facing {2}", offset, position, blockFacing);
                    return new BlockSelection
                    {
                        Position = position,
                        Face = blockFacing

                    };
                }

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


        private BlockFacing GetFacingForMultiblock(BlockPos mainPos, BlockPos neighborPos)
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
            var randomIndex = Rand.Next(directions.Length);
            return BlockFacing.FromCode(directions[randomIndex]);
        }

    }
}