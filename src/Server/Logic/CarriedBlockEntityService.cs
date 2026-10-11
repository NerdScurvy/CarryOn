using System;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Entities;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Server.Logic
{
    public sealed class CarriedBlockEntityService
    {
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

            var entityType = api.World.GetEntityType(new AssetLocation(CarryCodes.ModId, "carriedblock"));

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
                var placeSound = block.Sounds?.Place.Location ?? new AssetLocation(SoundPaths.DefaultPlace);
                api.World.PlaySoundAt(placeSound, position.X, position.Y, position.Z);
            }

            api.World.Logger.Notification(
                "[{0}] Spawned CarriedBlockEntity for {1} ({2}) at {3}",
                ModId,
                DisplayName(carriedBlock.Block?.Code),
                ownerUid,
                position);

            return entity;
        }

        /// <summary>
        /// Spawns a CarriedBlockEntity with gravity - resolves the spawn position
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
        /// Spawns a CarriedBlockEntity directly from an untouched carried slot tree. Used when the
        /// carried block's code is no longer registered (missing-block): the tree cannot be parsed
        /// into a <see cref="CarriedBlock"/>, but its raw contents (stack, block entity data,
        /// attached children) must still be preserved so the player can recover them after
        /// re-enabling the source mod via the entity pickup path.
        /// </summary>
        public EntityCarriedBlock? SpawnCarriedBlockEntity(
            ITreeAttribute rawCarriedTree,
            string ownerUid,
            Vec3d position)
        {
            if (rawCarriedTree == null) return null;

            var entityType = api.World.GetEntityType(new AssetLocation(CarryCodes.ModId, "carriedblock"));

            var entity = api.World.ClassRegistry.CreateEntity(entityType) as EntityCarriedBlock;
            if (entity == null) return null;

            var zFightOffset = (float)(api.World.Rand.NextDouble() * 0.005);
            entity.Pos.SetPos(position.X, position.Y + zFightOffset, position.Z);
            entity.Pos.Yaw = (float)(api.World.Rand.NextDouble() * GameMath.TWOPI);
            entity.Pos.Pitch = 0f;
            entity.Pos.Roll = 0f;

            entity.SetCarriedBlockData(rawCarriedTree, ownerUid, api.World.Calendar.TotalDays, DateTime.UtcNow.Ticks);

            entity.World = api.World;

            api.World.SpawnEntity(entity);

            var storedCode = rawCarriedTree.GetItemstack(AttributeKeys.CarriedBlockData.Stack)?.Collectible?.Code;
            api.World.Logger.Notification(
                "[{0}] Spawned CarriedBlockEntity from raw carried tree (block not registered / missing) for {1} ({2}) at {3}",
                ModId,
                DisplayName(storedCode?.ToString()),
                ownerUid,
                position);

            return entity;
        }

        /// <summary>
        /// Spawns a CarriedBlockEntity directly from an untouched carried slot tree with gravity;
        /// see the <see cref="CarriedBlock"/> overload of
        /// <see cref="SpawnCarriedBlockEntityWithGravity(CarriedBlock, string, Vec3d, Vec3d?, bool, float)"/>.
        /// </summary>
        public EntityCarriedBlock? SpawnCarriedBlockEntityWithGravity(
            ITreeAttribute rawCarriedTree,
            string ownerUid,
            Vec3d candidatePos,
            Vec3d? fallbackPos = null)
        {
            var resolvedPos = ResolveSpawnPosition(candidatePos, fallbackPos, scale: 1.0f);
            return SpawnCarriedBlockEntity(rawCarriedTree, ownerUid, resolvedPos);
        }

        /// <summary>
        /// Resolves the spawn position by applying gravity - delegates to
        /// <see cref="GravitySpawnHelper.ResolveDropPosition"/> which scans
        /// downward through passable blocks to find the first solid surface
        /// or liquid to land on.
        /// </summary>
        public Vec3d ResolveSpawnPosition(Vec3d candidatePos, Vec3d? fallbackPos = null, float scale = 1.0f)
        {
            return GravitySpawnHelper.ResolveDropPosition(
                api.World, api.World.BlockAccessor,
                candidatePos, ScaleFootprint(scale), fallbackPos);
        }

        /// <summary>
        /// Builds the position-relative clearance footprint for a carried
        /// block scaled by <paramref name="scale"/>: bottom-center origin,
        /// square base of width <paramref name="scale"/>, height
        /// <paramref name="scale"/>.
        /// </summary>
        private static Cuboidf ScaleFootprint(float scale)
        {
            var half = 0.5f * scale;
            return new Cuboidf(-half, 0f, -half, half, scale, half);
        }
    }
}
