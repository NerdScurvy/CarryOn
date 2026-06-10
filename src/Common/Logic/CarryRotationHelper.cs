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

        private static readonly BlockFacing[] NorthWestSouthEast = [BlockFacing.NORTH, BlockFacing.WEST, BlockFacing.SOUTH, BlockFacing.EAST];
        private static readonly BlockFacing[] NorthEastSouthWest = [BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST];

        // Mapping from normalized meshAngle step index to BlockFacing.
        // VS stores meshAngle using Atan2(-dx, dz) convention where:
        //   South=0, West=π/2, North=π, East=3π/2 (or -π/2)
        // After NormalizeAngle and dividing by (π/2), the step index maps to:
        //   0→South, 1→West, 2→North, 3→East
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

        /// <summary>
        /// Computes rotation steps from the parent block's original facing to its base (render-default) facing.
        /// During carry, the parent block is rendered in its base orientation (the ItemStack from OnPickBlock,
        /// which typically normalizes facing variants). Attached-block offsets were captured relative to the
        /// original world-facing, so they must be rotated by this delta to align with the base-oriented render.
        /// </summary>
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

        /// <summary>
        /// Gets the original facing of a carried block, preferring meshAngle over block code.
        /// </summary>
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

        /// <summary>
        /// Gets the base (render-default) facing of a carried block.
        /// This is the facing of the ItemStack's block code, which is what OnPickBlock returns.
        /// If the base item has no facing variant, the default is North (0 rotation steps).
        /// </summary>
        public static BlockFacing? GetBaseFacing(CarriedBlock carriedBlock)
        {
            var baseFacing = GetBlockFacing(carriedBlock.Block);
            return baseFacing ?? BlockFacing.NORTH;
        }

        /// <summary>
        /// Computes rotation steps from the original world facing to the model's default render
        /// orientation. Attached block offsets must be rotated by this amount to convert from world
        /// block coordinates to the model's pre-rotation coordinate space.
        /// Uses OriginalMeshAngle directly (which encodes rotation from model default in VS convention:
        /// south = 0, east = π/2, north = π, west = 3π/2) for meshAngle-based blocks, and
        /// OriginalBlockCode facing for variant-based blocks.
        /// </summary>
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

        /// <summary>
        /// Converts a BlockFacing to a Y-rotation in degrees for model transforms.
        /// North = 0, East = 90, South = 180, West = 270.
        /// </summary>
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

        /// <summary>
        /// Resolves the block variant to use for rendering a carried block, based on its
        /// <see cref="DefaultRenderVariant"/> and <see cref="DefaultRenderFacing"/> configuration.
        /// 
        /// When a block is picked up, <c>OnPickBlock</c> may normalize the block code (e.g.
        /// <c>sign-wall-north</c> becomes <c>sign-ground-north</c>). The render pipeline uses
        /// the ItemStack's block, which always resolves to the normalized variant. This method
        /// reconstructs the desired render variant by:
        /// <list type="number">
        ///   <item>Starting from <see cref="CarriedBlock.OriginalBlockCode"/> (preserves the original variant).</item>
        ///   <item>Replacing the variant segment with <paramref name="defaultRenderVariant"/> (e.g. "wall").</item>
        ///   <item>Setting the facing to <paramref name="defaultRenderFacing"/> (e.g. "north").</item>
        /// </list>
        /// Falls back to the carried block's base block if resolution fails.
        /// </summary>
        public static Block? ResolveRenderBlock(IWorldAccessor world, CarriedBlock carriedBlock, string? defaultRenderVariant, string? defaultRenderFacing)
        {
            if (string.IsNullOrEmpty(defaultRenderVariant))
                return null;

            var code = carriedBlock.OriginalBlockCode ?? carriedBlock.Block.Code;
            var path = code.Path;
            var parts = path.Split('-');

            if (parts.Length < 2) return null;

            var facing = string.IsNullOrEmpty(defaultRenderFacing) ? "east" : defaultRenderFacing;

            string newPath;
            if (parts.Length == 2)
            {
                newPath = $"{parts[0]}-{defaultRenderVariant}-{facing}";
            }
            else
            {
                var basePath = string.Join("-", parts, 0, parts.Length - 2);
                newPath = $"{basePath}-{defaultRenderVariant}-{facing}";
            }

            var newCode = new AssetLocation(code.Domain, newPath);
            var resolved = world.GetBlock(newCode);
            return resolved?.Id != 0 ? resolved : null;
        }
    }
}
