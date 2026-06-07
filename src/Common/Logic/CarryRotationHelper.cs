using System;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Logic
{
    public static class CarryRotationHelper
    {
        private const float TwoPi = (float)(2 * Math.PI);
        private const float HalfPi = (float)(Math.PI / 2);

        private static readonly float AngleNorth = 0f;
        private static readonly float AngleEast = HalfPi;
        private static readonly float AngleSouth = (float)Math.PI;
        private static readonly float AngleWest = (float)(3 * Math.PI / 2);

        private static readonly float[] CardinalAngles =
        [
            0f,
            HalfPi,
            (float)Math.PI,
            (float)(3 * Math.PI / 2)
        ];

        private static readonly BlockFacing[] NorthWestSouthEast = [BlockFacing.NORTH, BlockFacing.WEST, BlockFacing.SOUTH, BlockFacing.EAST];
        private static readonly BlockFacing[] NorthEastSouthWest = [BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST];

        private const float CardinalEpsilon = 0.001f;

        public static float NormalizeAngle(float angle)
        {
            angle %= TwoPi;
            if (angle < 0) angle += TwoPi;
            return angle;
        }

        public static int NormalizeSteps(int steps)
        {
            return ((steps % 4) + 4) % 4;
        }

        public static float GetMeshAngle(BlockFacing facing)
        {
            switch (facing.Code)
            {
                case "north": return AngleNorth;
                case "east": return AngleEast;
                case "south": return AngleSouth;
                case "west": return AngleWest;
                default: return AngleNorth;
            }
        }

        public static BlockFacing? GetBlockFacing(Block block)
        {
            if (block?.Code?.Path == null) return null;
            return GetBlockFacing(block.Code);
        }

        public static BlockFacing? GetBlockFacing(AssetLocation code)
        {
            if (code?.Path == null) return null;
            var parts = code.Path.Split('-');
            return parts.Length > 0 ? BlockFacing.FromCode(parts[parts.Length - 1]) : null;
        }

        public static BlockPos RotateOffset(BlockPos offset, int steps)
        {
            steps = NormalizeSteps(steps);
            if (steps == 0) return offset.Copy();
            int x = offset.X, z = offset.Z;
            return steps switch
            {
                1 => new BlockPos(z, offset.Y, -x),
                2 => new BlockPos(-x, offset.Y, -z),
                3 => new BlockPos(-z, offset.Y, x),
                _ => offset.Copy()
            };
        }

        public static BlockFacing RotateFacing(BlockFacing facing, int steps)
        {
            int idx = Array.IndexOf(NorthWestSouthEast, facing);
            if (idx < 0) return facing;
            steps = NormalizeSteps(steps);
            return NorthWestSouthEast[(idx + steps) % 4];
        }

        public static bool HasNonCardinalRotation(CarriedBlock carriedBlock)
        {
            if (carriedBlock.BlockEntityData == null) return false;
            if (!carriedBlock.BlockEntityData.HasAttribute("meshAngle"))
                return false;
            var angle = NormalizeAngle(carriedBlock.BlockEntityData.GetFloat("meshAngle"));
            foreach (var cardinal in CardinalAngles)
            {
                if (Math.Abs(angle - cardinal) < CardinalEpsilon)
                    return false;
            }
            return true;
        }

        public static Block? GetWallSignForFacing(IWorldAccessor world, AssetLocation originalCode, BlockFacing newFacing)
        {
            var parts = originalCode.Path.Split('-');
            if (parts.Length < 2) return null;
            var basePath = string.Join("-", parts, 0, parts.Length - 1);
            var newCode = new AssetLocation(originalCode.Domain, $"{basePath}-{newFacing.Code}");
            return world.GetBlock(newCode);
        }

        public static int GetActualRotationSteps(CarriedBlock carriedBlock, IWorldAccessor world, BlockPos parentPos)
        {
            var originalMeshAngle = carriedBlock.OriginalMeshAngle;
            if (originalMeshAngle.HasValue)
            {
                var placedBE = world.BlockAccessor.GetBlockEntity(parentPos);
                if (placedBE != null)
                {
                    var tempAttr = new TreeAttribute();
                    placedBE.ToTreeAttributes(tempAttr);
                    if (tempAttr.HasAttribute("meshAngle"))
                    {
                        var placedMeshAngle = tempAttr.GetFloat("meshAngle");
                        var delta = NormalizeAngle(placedMeshAngle - originalMeshAngle.Value);
                        var rawSteps = delta / HalfPi;
                        var steps = (int)Math.Round(rawSteps);
                        return NormalizeSteps(steps);
                    }
                }
            }

            var originalCode = carriedBlock.OriginalBlockCode;
            if (originalCode != null)
            {
                var originalFacing = GetBlockFacing(originalCode);
                if (originalFacing != null)
                {
                    var placedBlock = world.BlockAccessor.GetBlock(parentPos);
                    var placedFacing = GetBlockFacing(placedBlock);
                    if (placedFacing != null)
                    {
                        int fromIdx = Array.IndexOf(NorthEastSouthWest, originalFacing);
                        int toIdx = Array.IndexOf(NorthEastSouthWest, placedFacing);
                        if (fromIdx >= 0 && toIdx >= 0)
                            return NormalizeSteps(toIdx - fromIdx);
                    }
                }
            }

            return 0;
        }

        public static int EstimatePreflightRotation(CarriedBlock carriedBlock, Entity entity, BlockSelection selection, bool dropped)
        {
            if (!carriedBlock.OriginalMeshAngle.HasValue) return 0;

            float placedMeshAngle;
            if (dropped)
            {
                var meshFacing = selection.Face;
                if (meshFacing == null || meshFacing.IsVertical) return 0;
                placedMeshAngle = -GetMeshAngle(meshFacing);
            }
            else
            {
                var yaw = NormalizeAngle((float)entity.Pos.Yaw);
                placedMeshAngle = (float)((yaw + Math.PI) % TwoPi);
            }

            var originalAngle = carriedBlock.OriginalMeshAngle.Value;
            var delta = NormalizeAngle(placedMeshAngle - originalAngle);
            var steps = (int)Math.Round(delta / HalfPi);
            return NormalizeSteps(steps);
        }
    }
}
