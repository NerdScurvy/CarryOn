using System;
using CarryOn.API.Common.Interfaces;
using Vintagestory.API.Common;

namespace CarryOn.Common.Services
{
    internal sealed class CarryManagerServices(ICoreAPI api, CarrySystem carrySystem, ICarryManager carryManager)
    {
        public CarryStateService State { get; } = new(carrySystem);
        public CarryPlacementService Placement { get; } = new(api, carryManager);
        public CarryAttachmentService Attachment { get; } = new(api, carrySystem, carryManager);
        public CarryDropService Drop { get; } = new(api, carryManager);
        public CarryEventBootstrapper EventBootstrapper { get; } = new(carryManager);
    }
}