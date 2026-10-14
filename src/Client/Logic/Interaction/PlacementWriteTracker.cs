using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Logic.Interaction
{
    /// <summary>
    /// Records which cells the client-side placement prediction actually wrote, so the
    /// server can reconcile/re-sync those cells when its authoritative placement diverges.
    /// </summary>
    internal sealed class PlacementWriteTracker
    {
        private readonly IWorldAccessor world;
        private readonly CarriedBlock carriedBlock;
        private readonly BlockPos[] sampledPositions;
        private readonly int[] sampledBeforeIds;

        public PlacementWriteTracker(IWorldAccessor world, CarriedBlock carriedBlock, BlockPos selectionPosition)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.carriedBlock = carriedBlock ?? throw new ArgumentNullException(nameof(carriedBlock));
            if (selectionPosition == null) throw new ArgumentNullException(nameof(selectionPosition));

            int radiusXZ = GetRegionRadiusXZ();
            int radiusY = GetRegionRadiusY();

            var positions = new List<BlockPos>();
            for (int x = selectionPosition.X - radiusXZ; x <= selectionPosition.X + radiusXZ; x++)
            {
                for (int y = selectionPosition.Y - radiusY; y <= selectionPosition.Y + radiusY; y++)
                {
                    for (int z = selectionPosition.Z - radiusXZ; z <= selectionPosition.Z + radiusXZ; z++)
                    {
                        var pos = new BlockPos(x, y, z);
                        if (world.BlockAccessor.IsValidPos(pos))
                            positions.Add(pos);
                    }
                }
            }

            sampledPositions = positions.ToArray();
            sampledBeforeIds = new int[sampledPositions.Length];
            for (int i = 0; i < sampledPositions.Length; i++)
                sampledBeforeIds[i] = world.BlockAccessor.GetBlock(sampledPositions[i]).Id;
        }

        public BlockPos[] GetWrittenCells(BlockPos placedAt)
        {
            if (placedAt == null) return Array.Empty<BlockPos>();

            var written = new List<BlockPos>();
            for (int i = 0; i < sampledPositions.Length; i++)
            {
                int currentId = world.BlockAccessor.GetBlock(sampledPositions[i]).Id;
                if (currentId != sampledBeforeIds[i] && currentId != 0)
                    written.Add(sampledPositions[i]);
            }

            if (written.Count == 0) return Array.Empty<BlockPos>();

            if (carriedBlock.HasAttachedBlocks)
            {
                int steps = CarryRotationHelper.GetActualRotationSteps(carriedBlock, world, placedAt);
                foreach (var child in carriedBlock.AttachedBlocks!)
                {
                    var rotatedOffset = CarryRotationHelper.RotateOffset(child.RelativeOffset, steps);
                    var childPos = placedAt.Copy();
                    childPos.Add(rotatedOffset);
                    if (!written.Contains(childPos))
                        written.Add(childPos);
                }
            }

            return written.ToArray();
        }

        private int GetRegionRadiusXZ()
        {
            int max = 1;
            foreach (var child in carriedBlock.AttachedBlocks ?? Array.Empty<AttachedCarriedBlock>())
                max = Math.Max(max, Math.Max(Math.Abs(child.RelativeOffset.X), Math.Abs(child.RelativeOffset.Z)));
            return max + 1;
        }

        private int GetRegionRadiusY()
        {
            int max = 0;
            foreach (var child in carriedBlock.AttachedBlocks ?? Array.Empty<AttachedCarriedBlock>())
                max = Math.Max(max, Math.Abs(child.RelativeOffset.Y));
            return Math.Max(1, max + 1);
        }
    }
}