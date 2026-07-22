using System;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Utility
{
    public static class CarryRotationHelper
    {
        private const float TwoPi = (float)(2 * Math.PI);
        private const float HalfPi = (float)(Math.PI / 2);

        private static readonly float AngleNorth = 0f;
        private static readonly float AngleEast = HalfPi;
        private static readonly float AngleSouth = (float)Math.PI;
        private static readonly float AngleWest = (float)(3 * Math.PI / 2);

        private static readonly BlockFacing[] NorthWestSouthEast = [BlockFacing.NORTH, BlockFacing.WEST, BlockFacing.SOUTH, BlockFacing.EAST];
        private static readonly BlockFacing[] NorthEastSouthWest = [BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST];

        private static readonly BlockFacing[] MeshAngleFacing = [BlockFacing.SOUTH, BlockFacing.WEST, BlockFacing.NORTH, BlockFacing.EAST];

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

        public static Block? GetWallSignForFacing(IWorldAccessor world, AssetLocation originalCode, BlockFacing newFacing)
        {
            return GetRotatedVariantBlock(world, originalCode, newFacing);
        }

        public static Block? GetRotatedVariantBlock(IWorldAccessor world, AssetLocation originalCode, BlockFacing rotatedFacing)
        {
            var parts = originalCode.Path.Split('-');
            if (parts.Length < 2) return null;
            var basePath = string.Join("-", parts, 0, parts.Length - 1);
            var newCode = new AssetLocation(originalCode.Domain, $"{basePath}-{rotatedFacing.Code}");
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

        public static int GetBaseFacingRotationSteps(CarriedBlock carriedBlock)
        {
            var originalFacing = GetOriginalFacing(carriedBlock);
            var baseFacing = GetBaseFacing(carriedBlock);

            if (originalFacing == null || baseFacing == null)
                return 0;

            int fromIdx = Array.IndexOf(NorthEastSouthWest, originalFacing);
            int toIdx = Array.IndexOf(NorthEastSouthWest, baseFacing);

            if (fromIdx < 0 || toIdx < 0)
                return 0;

            return NormalizeSteps(toIdx - fromIdx);
        }

        public static BlockFacing? GetOriginalFacing(CarriedBlock carriedBlock)
        {
            if (carriedBlock.OriginalMeshAngle.HasValue)
            {
                var angle = NormalizeAngle(carriedBlock.OriginalMeshAngle.Value);
                foreach (var facing in NorthEastSouthWest)
                {
                    if (Math.Abs(angle - GetMeshAngle(facing)) < CardinalEpsilon)
                        return facing;
                }
            }

            if (carriedBlock.OriginalBlockCode != null)
                return GetBlockFacing(carriedBlock.OriginalBlockCode);

            return GetBlockFacing(carriedBlock.Block);
        }

        public static BlockFacing? GetBaseFacing(CarriedBlock carriedBlock)
        {
            var baseFacing = GetBlockFacing(carriedBlock.Block);
            return baseFacing ?? BlockFacing.NORTH;
        }

        public static int GetOriginalToModelDefaultSteps(CarriedBlock carriedBlock, string? modelDefaultFacing = "east")
        {
            var defaultIdx = ModelDefaultFacingToSteps(modelDefaultFacing);

            if (carriedBlock.OriginalMeshAngle.HasValue)
            {
                var angle = NormalizeAngle(carriedBlock.OriginalMeshAngle.Value);
                var meshAngleStep = (int)Math.Round(angle / HalfPi) % 4;
                if (meshAngleStep >= 0 && meshAngleStep < MeshAngleFacing.Length)
                {
                    var originalFacing = MeshAngleFacing[meshAngleStep];
                    int originalIdx = Array.IndexOf(NorthWestSouthEast, originalFacing);
                    if (originalIdx >= 0)
                        return NormalizeSteps(originalIdx - defaultIdx);
                }
            }

            if (carriedBlock.OriginalBlockCode != null)
            {
                var originalFacing = GetBlockFacing(carriedBlock.OriginalBlockCode);
                if (originalFacing != null)
                {
                    int originalIdx = Array.IndexOf(NorthWestSouthEast, originalFacing);
                    if (originalIdx >= 0)
                        return NormalizeSteps(originalIdx - defaultIdx);
                }
            }

            var baseFacing = GetBlockFacing(carriedBlock.Block);
            if (baseFacing != null)
            {
                int baseIdx = Array.IndexOf(NorthWestSouthEast, baseFacing);
                if (baseIdx >= 0)
                    return NormalizeSteps(baseIdx - defaultIdx);
            }

            return 0;
        }

        private static int ModelDefaultFacingToSteps(string? facing)
        {
            return facing?.ToLowerInvariant() switch
            {
                "north" => 0,
                "west" => 1,
                "south" => 2,
                "east" => 3,
                _ => 3
            };
        }

        public static float FacingToYRotationDegrees(BlockFacing facing)
        {
            return facing.Code switch
            {
                "north" => 0f,
                "east" => 90f,
                "south" => 180f,
                "west" => 270f,
                _ => 0f
            };
        }

        public static Block? ResolveRenderBlock(IWorldAccessor world, CarriedBlock carriedBlock, string? rootRenderVariant, string? rootRenderFacing)
        {
            if (string.IsNullOrEmpty(rootRenderVariant))
                return null;

            var code = carriedBlock.OriginalBlockCode ?? carriedBlock.Block.Code;
            var path = code.Path;
            var parts = path.Split('-');

            if (parts.Length < 2) return null;

            var facing = string.IsNullOrEmpty(rootRenderFacing) ? "east" : rootRenderFacing;

            string newPath;
            if (parts.Length == 2)
            {
                newPath = $"{parts[0]}-{rootRenderVariant}-{facing}";
            }
            else
            {
                var basePath = string.Join("-", parts, 0, parts.Length - 2);
                newPath = $"{basePath}-{rootRenderVariant}-{facing}";
            }

            var newCode = new AssetLocation(code.Domain, newPath);
            var resolved = world.GetBlock(newCode);
            return resolved?.Id != 0 ? resolved : null;
        }
    }
}
