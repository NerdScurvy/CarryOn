using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Models;
using CarryOn.Server.Logic;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CarryOn.Common.Handlers
{
    public class CarriedBlockEntityFactory : ICarryEntitySpawnerFactory
    {
        private readonly IConfigProvider configProvider;
        private readonly ICoreAPI api;

        public static string FactoryCode { get; } = CarriedBlockEntityHandler.HandlerCode;

        public CarriedBlockEntityFactory(ICoreAPI api, IConfigProvider configProvider)
        {
            this.api = api;
            this.configProvider = configProvider;
        }

        public bool ShouldSpawnAsEntity(CarriedBlock carriedBlock, bool dropped, ICoreAPI api)
        {
            if (dropped) return true;

            // Hand placement: only re-spawn when the carried block signals it (custom entity
            // blocks) or the server config default opts normal blocks in. A normal block
            // dropped-as-entity then re-picked keeps its (unset) signal, so it places as a block.
            return CarryEntityPlacementPolicy.ShouldSpawnAsEntityOnPlacement(carriedBlock, configProvider.Config.CarriedBlockEntity);
        }

        public Entity? SpawnFromCarriedBlock(EntitySpawnContext context)
        {
            var carrySystem = context.Api.ModLoader.GetModSystem<CarrySystem>();
            var entityService = carrySystem?.CarriedBlockEntityService;
            if (entityService == null) return null;

            var cfg = configProvider.Config.CarriedBlockEntity;
            var randomYaw = context.Dropped && (cfg?.RandomDropRotation == true);
            var scale = cfg?.Scale ?? 1.0f;

            return entityService.SpawnCarriedBlockEntityWithGravity(
                context.CarriedBlock, context.OwnerUid, context.Position,
                randomYaw: randomYaw, scale: scale);
        }

        public void OnSpawned(Entity entity, CarriedBlock carriedBlock, ICoreAPI api)
        {
        }
    }
}
