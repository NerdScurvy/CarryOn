using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Handlers;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace CarryOn.Utility
{
    public static class EntityHandlerSpawner
    {
        private static readonly string DefaultSpawnerFactoryCode = CarriedBlockEntityFactory.FactoryCode;

        public static bool TrySpawn(ICarryManager carryManager, Entity entity, EntitySpawnContext context)
        {
            var carriedBlock = context.CarriedBlock;

            if (TryGetSpawnerFactory(carryManager, carriedBlock, context.Dropped, out var factory) == false
                || factory == null)
            {
                return false;
            }

            if (!factory.ShouldSpawnAsEntity(carriedBlock, context.Dropped, context.Api)) return false;

            if (context.Api is not ICoreServerAPI) return false;

            var spawnedEntity = factory.SpawnFromCarriedBlock(context);
            if (spawnedEntity == null) return false;

            factory.OnSpawned(spawnedEntity, carriedBlock, context.Api);
            carryManager.RemoveCarried(entity, carriedBlock.Slot);
            return true;
        }

        public static bool IsEntitySpawnApplicable(ICoreAPI api, ICarryManager carryManager, CarriedBlock carriedBlock, bool dropped)
        {
            if (TryGetSpawnerFactory(carryManager, carriedBlock, dropped, out var factory) == false
                || factory == null)
            {
                return false;
            }

            return factory.ShouldSpawnAsEntity(carriedBlock, dropped, api);
        }

        private static bool TryGetSpawnerFactory(ICarryManager carryManager, CarriedBlock carriedBlock, bool dropped, out ICarryEntitySpawnerFactory? factory)
        {
            // Blocks carried with an explicit factory code (e.g. custom entity pickups)
            // resolve through that code. Regular blocks have no code - when they are being
            // dropped (not placed), route them through the default EntityCarriedBlock factory
            // so DropMode EntityAlways / EntityOnFailedPlacement still spawn a block entity.
            var factoryCode = carriedBlock.SpawnerFactoryCode
                              ?? (dropped ? DefaultSpawnerFactoryCode : null);

            if (factoryCode == null || !carryManager.TryGetEntitySpawnerFactory(factoryCode, out factory))
            {
                factory = null;
                return false;
            }

            return factory != null;
        }
    }
}
