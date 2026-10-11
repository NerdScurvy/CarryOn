using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Server.Logic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Services
{
    /// <summary>
    /// Server-side drop of a present-but-unreadable carry slot (typically a block whose mod was
    /// removed or disabled). Spawns an <see cref="EntityCarriedBlock"/> from the untouched raw tree
    /// next to the player so the content stays recoverable after the mod is re-enabled, then clears
    /// the slot. Shared by the quick-drop path (<see cref="CarryDropService"/>) and the place-down
    /// path (<see cref="CarryPlacementService"/>).
    /// </summary>
    internal static class UnusableCarriedBlockDrop
    {
        /// <summary>
        /// Attempts to drop an unreadable carry slot as a carried block entity.
        /// </summary>
        /// <returns>True when a carried block entity was spawned and the slot cleared.</returns>
        public static bool TryDrop(ICoreAPI api, ICarryManager carryManager, Entity entity, CarrySlot slot, ITreeAttribute? rawTree, Vec3d? position)
        {
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(carryManager);
            ArgumentNullException.ThrowIfNull(entity);

            if (api.Side != EnumAppSide.Server || rawTree == null) return false;

            var carrySystem = (api as ICoreServerAPI)?.ModLoader.GetModSystem<CarrySystem>();
            var service = carrySystem?.CarriedBlockEntityService;
            if (service == null)
            {
                api.Logger.Warning(
                    "carryon: cannot drop unusable carried block - CarriedBlockEntityService unavailable; carry retained.");
                return false;
            }

            var ownerUid = (entity as EntityPlayer)?.Player?.PlayerUID ?? "unknown";
            var playerPos = position ?? entity.Pos.XYZ;
            var droppedPos = playerPos.Clone();

            var spawned = service.SpawnCarriedBlockEntityWithGravity(rawTree, ownerUid, playerPos, droppedPos);
            if (spawned == null)
            {
                api.Logger.Warning($"carryon: failed to spawn dropped carried block for {ownerUid}; carry retained.");
                return false;
            }

            // The carried block's code is unresolvable so its own place sound cannot be used;
            // play the generic block-place sound at the entity's final (gravity-resolved) position.
            api.World.PlaySoundAt(
                new AssetLocation(SoundPaths.DefaultPlace),
                spawned.Pos.X, spawned.Pos.Y, spawned.Pos.Z);

            carryManager.RemoveCarried(entity, slot);
            return true;
        }
    }
}