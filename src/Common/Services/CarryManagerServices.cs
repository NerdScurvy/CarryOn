using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Interfaces;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CarryOn.Common.Services
{
    internal sealed class CarryManagerServices
    {
        public CarryStateService State { get; }
        public CarryPermissionService Permission { get; }
        public CarryPickupService Pickup { get; }
        public GroundStorageItemPlacement GroundStorage { get; }
        public CarryPlacementService Placement { get; }
        public CarryDropService Drop { get; }
        public CarryEventBootstrapper EventBootstrapper { get; }

        public CarryManagerServices(ICoreAPI api, IConfigProvider configProvider, ICarryManager carryManager)
        {
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(configProvider);
            ArgumentNullException.ThrowIfNull(carryManager);

            State = new CarryStateService(api, configProvider, null);
            Permission = new CarryPermissionService(carryManager, configProvider);
            Pickup = new CarryPickupService(api, carryManager, Permission, configProvider);
            GroundStorage = new GroundStorageItemPlacement(api, carryManager);
            Placement = new CarryPlacementService(api, carryManager, Permission, configProvider, GroundStorage);
            Drop = new CarryDropService(api, carryManager, configProvider, GroundStorage);
            EventBootstrapper = new CarryEventBootstrapper(carryManager);
        }

        internal void ConfigureServerChannel(IServerNetworkChannel serverChannel)
        {
            ArgumentNullException.ThrowIfNull(serverChannel);
            State.ConfigureServerChannel(serverChannel);
        }
    }
}