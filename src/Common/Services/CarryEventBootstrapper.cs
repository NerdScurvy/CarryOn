using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using Vintagestory.API.Common;

namespace CarryOn.Common.Services
{
    internal sealed class CarryEventBootstrapper(ICarryManager carryManager)
    {
        private readonly List<ICarryEventHandler> registeredHandlers = new();

        /// <summary>
        /// Registers a carry event handler for initialization during <see cref="InitEvents"/>.
        /// </summary>
        public void RegisterEventHandler<T>() where T : ICarryEventHandler, new()
        {
            registeredHandlers.Add(new T());
        }

        /// <summary>
        /// Initializes all registered carry event handlers.
        /// </summary>
        public void InitEvents(ICoreAPI api)
        {
            foreach (var handler in registeredHandlers)
            {
                try
                {
                    handler.Init(carryManager);
                }
                catch (Exception e)
                {
                    api.Logger.Error($"CarryOn: Failed to initialize carry event '{handler.GetType().Name}': {e}");
                }
            }
        }
    }
}
