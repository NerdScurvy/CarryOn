using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Entities;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Logic;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using static CarryOn.API.Common.Models.CarryConstants;

namespace CarryOn.Common.Handlers
{
    /// <summary>
    /// Default handler for <see cref="EntityCarriedBlock"/> entities. Manages the lifecycle
    /// of the built-in dropped block entity type in both directions:
    /// pickup (entity to carried block) via <see cref="ICarryableEntityPickupHandler"/>
    /// and placement (carried block to entity) via <see cref="ICarryEntitySpawnerFactory"/>.
    /// </summary>
    public class CarriedBlockEntityHandler : ICarryableEntityPickupHandler
    {
        private readonly IConfigProvider configProvider;

        public static string HandlerCode { get; } = GetCarryCode("carriedblock");

        public CarriedBlockEntityHandler(IConfigProvider configProvider)
        {
            this.configProvider = configProvider;
        }

        private CarriedBlockEntityConfig? Config => configProvider.Config.CarriedBlockEntity;

        /// <inheritdoc/>
        public float GetInteractDelay(Entity entity, ICoreAPI api)
            => entity.GetBehavior<EntityBehaviorCarryable>()?.InteractDelay ?? CarryCodes.Defaults.PickUpSpeed;

        /// <inheritdoc/>
        public bool CanPickup(EnumGameMode gameMode, string? playerUid, Entity entity, ICoreAPI api)
        {
            var ecb = entity as EntityCarriedBlock;
            if (ecb == null) return false;

            var cfg = Config;
            if (cfg == null) return true;

            return CarriedBlockAccessPolicy.CanPickup(
                gameMode, playerUid, ecb.OwnerUid,
                cfg.PickupAccess, cfg.GracePeriodSeconds,
                ecb.DropTimeRealTicks);
        }

        // ── Pickup direction: Entity to CarriedBlock ──

        /// <inheritdoc/>
        public CarriedBlock? SerializeToCarriedBlock(Entity entity, CarrySlot slot, ICoreAPI api)
        {
            var ecb = entity as EntityCarriedBlock;
            var tree = ecb?.CarriedBlockTree;
            if (tree == null) return null;

            var result = CarriedBlockTreeSerializer.DeserializeSafe(tree, api, slot);
            if (result.IsFailed)
            {
                api.World.Logger.Warning(
                    $"carryon: refusing to pick up carried block entity {entity.EntityId} - carried data is unusable ({result.Reason}); the raw tree stays on the entity untouched. Inspect with '/carryon debug dump'.");
                return null;
            }

            var carriedBlock = result.Carried;
            if (carriedBlock == null) return null;

            return new CarriedBlock(
                carriedBlock.Slot,
                carriedBlock.ItemStack,
                carriedBlock.BlockEntityData,
                carriedBlock.AttachedBlocks,
                carriedBlock.OriginalBlockCode,
                carriedBlock.OriginalMeshAngle,
                HandlerCode,
                carriedBlock.SpawnAsEntityOnPlacement);
        }

        /// <inheritdoc/>
        public void OnPickedUp(Entity entity, IPlayer player, CarriedBlock? carriedBlock, ICoreAPI api)
        {
            var pickupSound = carriedBlock?.Block?.Sounds?.Place.Location
                ?? new AssetLocation(CarryCodes.SoundPaths.DefaultPlace);
            api.World.PlaySoundAt(pickupSound, player.Entity);
            entity.Die(EnumDespawnReason.PickedUp, null);
        }

    }
}
