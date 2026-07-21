using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Interfaces;
using Vintagestory.API.Common;

namespace CarryOn.Common.Services
{
    internal sealed class CarryManagerServices
    {
        public CarryStateService State { get; }
        public CarryPermissionService Permission { get; }
        public CarryPickupService Pickup { get; }
        public CarryPlacementService Placement { get; }
        public CarryAttachmentService Attachment { get; }
        public CarryDropService Drop { get; }
        public CarryEventBootstrapper EventBootstrapper { get; }

        public CarryManagerServices(ICoreAPI api, IConfigProvider configProvider, ICarryManager carryManager)
        {
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(configProvider);
            ArgumentNullException.ThrowIfNull(carryManager);

            State = new CarryStateService(configProvider, null);
            Permission = new CarryPermissionService(carryManager, configProvider);
            Pickup = new CarryPickupService(api, carryManager, configProvider);
            Placement = new CarryPlacementService(api, carryManager);
            Attachment = new CarryAttachmentService(api, configProvider, carryManager);
            Drop = new CarryDropService(api, carryManager, configProvider);
            EventBootstrapper = new CarryEventBootstrapper(carryManager);
        }
    }
}