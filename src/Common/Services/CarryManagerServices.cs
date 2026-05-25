using System;
using CarryOn.API.Common.Interfaces;
using Vintagestory.API.Common;

namespace CarryOn.Common.Services
{
    /// <summary>
    /// Aggregates domain services used by <see cref="API.Common.CarryManager"/>.
    /// </summary>
    internal sealed class CarryManagerServices
    {
        /// <summary>
        /// Gets the carried-state service.
        /// </summary>
        public CarryStateService State { get; }

        /// <summary>
        /// Gets the pickup and placement service.
        /// </summary>
        public CarryPlacementService Placement { get; }

        /// <summary>
        /// Gets the attach and detach service.
        /// </summary>
        public CarryAttachmentService Attachment { get; }

        /// <summary>
        /// Gets the drop handling service.
        /// </summary>
        public CarryDropService Drop { get; }

        /// <summary>
        /// Gets the event bootstrap service.
        /// </summary>
        public CarryEventBootstrapper EventBootstrapper { get; }

        /// <summary>
        /// Initializes all carry manager domain services.
        /// </summary>
        /// <param name="api">Core API instance.</param>
        /// <param name="carrySystem">Owning carry system.</param>
        /// <param name="carryManager">Carry manager facade.</param>
        public CarryManagerServices(ICoreAPI api, CarrySystem carrySystem, ICarryManager carryManager)
        {
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(carrySystem);
            ArgumentNullException.ThrowIfNull(carryManager);

            State = new CarryStateService(api, carrySystem, carryManager);
            Placement = new CarryPlacementService(api, carrySystem, carryManager);
            Attachment = new CarryAttachmentService(api, carrySystem, carryManager);
            Drop = new CarryDropService(api, carrySystem, carryManager);
            EventBootstrapper = new CarryEventBootstrapper(api, carrySystem, carryManager);
        }
    }
}