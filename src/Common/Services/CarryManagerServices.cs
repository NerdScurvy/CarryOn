using CarryOn.API.Common.Interfaces;
using Vintagestory.API.Common;

namespace CarryOn.Common.Services
{
    internal sealed class CarryManagerServices(ICoreAPI api, IConfigProvider configProvider, ICarryManager carryManager)
    {
        public CarryStateService State { get; } = new(configProvider, null);
        public CarryPermissionService Permission { get; } = new(carryManager);
        public CarryPickupService Pickup { get; } = new(api, carryManager);
        public CarryPlacementService Placement { get; } = new(api, carryManager);
        public CarryAttachmentService Attachment { get; } = new(api, configProvider, carryManager);
        public CarryDropService Drop { get; } = new(api, carryManager);
        public CarryEventBootstrapper EventBootstrapper { get; } = new(carryManager);
    }
}