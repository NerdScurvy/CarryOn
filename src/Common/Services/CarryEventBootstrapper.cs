using System;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using Vintagestory.API.Common;

namespace CarryOn.Common.Services
{
    /// <summary>
    /// Discovers and initializes carry event providers from loaded mod assemblies.
    /// </summary>
    internal sealed class CarryEventBootstrapper
    {
        /// <summary>
        /// Gets the core API for mod and logging access.
        /// </summary>
        public ICoreAPI Api { get; }

        /// <summary>
        /// Gets the owning carry system.
        /// </summary>
        public CarrySystem CarrySystem { get; }

        /// <summary>
        /// Gets the carry manager passed to discovered carry event initializers.
        /// </summary>
        public ICarryManager CarryManager { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CarryEventBootstrapper"/> class.
        /// </summary>
        /// <param name="api">Core API instance.</param>
        /// <param name="carrySystem">Owning carry system.</param>
        /// <param name="carryManager">Carry manager facade.</param>
        public CarryEventBootstrapper(ICoreAPI api, CarrySystem carrySystem, ICarryManager carryManager)
        {
            Api = api ?? throw new ArgumentNullException(nameof(api));
            CarrySystem = carrySystem ?? throw new ArgumentNullException(nameof(carrySystem));
            CarryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
        }

        /// <summary>
        /// Scans non-core mod assemblies for <see cref="ICarryEvent"/> implementations and initializes them.
        /// </summary>
        /// <param name="api">Core API used for mod enumeration and logging.</param>
        public void InitEvents(ICoreAPI api)
        {
            var ignoreMods = new[] { "game", "creative", "survival" };

            var assemblies = api.ModLoader.Mods.Where(m => !ignoreMods.Contains(m.Info.ModID))
                                               .Select(s => s.Systems)
                                               .SelectMany(o => o.ToArray())
                                               .Select(t => t.GetType().Assembly)
                                               .Distinct();

            foreach (var assembly in assemblies)
            {
                foreach (Type type in assembly.GetTypes().Where(t => t.GetInterfaces().Contains(typeof(ICarryEvent))))
                {
                    try
                    {
                        (Activator.CreateInstance(type) as ICarryEvent)?.Init(CarryManager);
                    }
                    catch (Exception e)
                    {
                        api.Logger.Error(e.Message);
                    }
                }
            }
        }
    }
}