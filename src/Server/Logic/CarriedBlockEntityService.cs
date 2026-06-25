using System;
using CarryOn.API.Common.Models;
using CarryOn.Common.Entities;
using CarryOn.Common.Logic;
using CarryOn.Common.Services;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Server.Logic
{
    public sealed class CarriedBlockEntityService
    {
        // When floating in liquid: entity feet at blockY + 0.5
        // (entity from blockY+0.5 to blockY+1.0 = top at liquid surface)
        private const float LiquidSubmergeOffset = 0.5f;

        private readonly ICoreAPI api;

        public CarriedBlockEntityService(ICoreAPI api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
        }

        public EntityCarriedBlock? SpawnCarriedBlockEntity(
            CarriedBlock carriedBlock,
            string ownerUid,
            Vec3d position,
            bool randomYaw = false)
        {
            if (carriedBlock == null) return null;

            var carriedTree = CarriedBlockTreeSerializer.Serialize(carriedBlock);
            if (carriedTree == null) return null;

            var entityType = api.World.GetEntityType(new AssetLocation(CarryCode.ModId, "carriedblock"));

            var entity = api.World.ClassRegistry.CreateEntity(entityType) as EntityCarriedBlock;
            if (entity == null) return null;

            var zFightOffset = (float)(api.World.Rand.NextDouble() * 0.005);
            entity.Pos.SetPos(position.X, position.Y + zFightOffset, position.Z);
            entity.Pos.Yaw = randomYaw ? (float)(api.World.Rand.NextDouble() * GameMath.TWOPI) : 0f;
            entity.Pos.Pitch = 0f;
            entity.Pos.Roll = 0f;

            entity.SetCarriedBlockData(carriedTree, ownerUid, api.World.Calendar.TotalDays, DateTime.UtcNow.Ticks);

            entity.World = api.World;

            api.World.SpawnEntity(entity);

            var block = carriedBlock.Block;
            if (block != null)
            {
                var placeSound = block.Sounds?.Place.Location ?? new AssetLocation(SoundPath.DefaultPlace);
                api.World.PlaySoundAt(placeSound, position.X, position.Y, position.Z);
            }

            api.World.Logger.Notification(
                "[{0}] Spawned CarriedBlockEntity for {1} ({2}) at {3}",
                ModId,
                carriedBlock.Block?.Code ?? "unknown",
                ownerUid,
                position);

            return entity;
        }

        /// <summary>
        /// Spawns a CarriedBlockEntity with gravity — resolves the spawn position
        /// by iterating downward through passable blocks to find the floor.
        /// </summary>
        public EntityCarriedBlock? SpawnCarriedBlockEntityWithGravity(
            CarriedBlock carriedBlock,
            string ownerUid,
            Vec3d candidatePos,
            Vec3d? fallbackPos = null,
            bool randomYaw = false,
            float scale = 1.0f)
        {
            var resolvedPos = ResolveSpawnPosition(candidatePos, fallbackPos, scale);
            return SpawnCarriedBlockEntity(carriedBlock, ownerUid, resolvedPos, randomYaw);
        }

        /// <summary>
        /// Resolves the spawn position by applying gravity — iterates downward
        /// through passable blocks from one below the candidate position to find
        /// the first solid surface or liquid to land on.
        /// </summary>
        public Vec3d ResolveSpawnPosition(Vec3d candidatePos, Vec3d? fallbackPos = null, float scale = 1.0f)
        {
            var startPos = candidatePos.AsBlockPos;
            var blockAccessor = api.World.BlockAccessor;

            // If starting in a liquid, spawn at the candidate position directly
            if (blockAccessor.GetBlock(startPos).IsLiquid())
                return candidatePos;

            var x = startPos.X;
            var z = startPos.Z;

            for (int y = startPos.Y; y >= 0; y--)
            {
                var pos = new BlockPos(x, y, z);
                var block = blockAccessor.GetBlock(pos);

                if (block.IsLiquid())
                {
                    var targetPos = new Vec3d(x + 0.5, y + LiquidSubmergeOffset, z + 0.5);
                    if (HasRoomAt(targetPos, blockAccessor, scale))
                        return targetPos;

                    continue;
                }

                if (!IsPassable(block))
                {
                    var surfaceTop = GetBlockSurfaceTop(block);
                    var targetPos = new Vec3d(x + 0.5, y + surfaceTop, z + 0.5);
                    if (HasRoomAt(targetPos, blockAccessor, scale, pos))
                        return targetPos;

                    return targetPos;
                }
            }

            return fallbackPos ?? candidatePos;
        }

        /// <summary>
        /// Checks if the entity hitbox has clearance at the given position.
        /// Returns false if any solid block intersects the AABB.
        /// Optionally excludes a surface block (the block the entity sits on).
        /// </summary>
        private static bool HasRoomAt(Vec3d pos, IBlockAccessor blockAccessor, float scale = 1.0f, BlockPos? surfaceBlockPos = null)
        {
            var half = 0.5f * scale;
            var minX = (int)Math.Floor(pos.X - half);
            var maxX = (int)Math.Floor(pos.X + half);
            var minY = (int)Math.Floor(pos.Y - half);
            var maxY = (int)Math.Floor(pos.Y + half);
            var minZ = (int)Math.Floor(pos.Z - half);
            var maxZ = (int)Math.Floor(pos.Z + half);

            for (int bx = minX; bx <= maxX; bx++)
            {
                for (int by = minY; by <= maxY; by++)
                {
                    for (int bz = minZ; bz <= maxZ; bz++)
                    {
                        if (surfaceBlockPos != null && bx == surfaceBlockPos.X && by == surfaceBlockPos.Y && bz == surfaceBlockPos.Z)
                            continue;

                        var block = blockAccessor.GetBlock(new BlockPos(bx, by, bz));
                        if (block == null) continue;
                        if (block.IsLiquid()) continue;
                        if (IsPassable(block)) continue;

                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether a block is passable — entities can fall through it.
        /// </summary>
        private static bool IsPassable(Block block)
        {
            if (block.Id == 0) return true;
            if (block.Replaceable >= 6000) return true;
            if (block.MatterState == EnumMatterState.Gas) return true;

            return false;
        }

        /// <summary>
        /// Returns the top surface Y (block-local 0-1 range) for the given block's
        /// collision shape. Falls back to 1.0 (full block height) if the block has
        /// no collision boxes.
        /// </summary>
        private static float GetBlockSurfaceTop(Block block)
        {
            var boxes = block.CollisionBoxes;
            if (boxes == null || boxes.Length == 0)
                return 1.0f;

            float maxY = 0f;
            foreach (var box in boxes)
            {
                if (box.Y2 > maxY)
                    maxY = box.Y2;
            }
            return maxY;
        }
    }
}
