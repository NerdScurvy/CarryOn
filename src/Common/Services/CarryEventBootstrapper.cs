using System;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using Vintagestory.API.Common;

namespace CarryOn.Common.Services
{
    internal sealed class CarryEventBootstrapper(ICarryManager carryManager)
    {

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
                        (Activator.CreateInstance(type) as ICarryEvent)?.Init(carryManager);
                    }
                    catch (Exception e)
                    {
                        api.Logger.Error($"CarryOn: Failed to initialize carry event '{type.Name}': {e}");
                    }
                }
            }
        }
    }
}