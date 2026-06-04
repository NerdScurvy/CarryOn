using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Server.Logic
{
    public class ModConfig
    {
        public CarryOnConfig? ServerConfig { get; private set; }
        public IWorldAccessor? World { get; private set; }

        public void Load(ICoreAPI api)
        {
            var coreApi = api ?? throw new ArgumentNullException(nameof(api));

            World = coreApi.World;
            if (coreApi.Side != EnumAppSide.Server)
            {
                return;
            }
            else
            {
                const int currentVersion = 3;
                var logger = coreApi.Logger;

                try
                {
                    var loadedConfig = coreApi.LoadModConfig<CarryOnConfig>(ConfigFile);
                    if (loadedConfig != null)
                    {
                        loadedConfig.UpgradeVersion();
                    }
                    else
                    {
                        loadedConfig = new CarryOnConfig(currentVersion);
                    }

                    // Save the upgraded or default config back to the file
                    coreApi.StoreModConfig(loadedConfig, ConfigFile);

                    ServerConfig = loadedConfig;

                }
                catch (Exception ex)
                {
                    // Log the exception and create a default config but not save it
                    logger?.Error("CarryOn: Exception loading config: " + ex);
                    ServerConfig = new CarryOnConfig(currentVersion);
                }

                var worldConfig = coreApi.World?.Config;

                if (worldConfig == null)
                {
                    logger?.Error("CarryOn: Unable to access world config. CarryOn features may not work correctly.");
                    return;
                }

                if (ServerConfig == null)
                {
                    logger?.Error("CarryOn: ServerConfig did not load correctly. CarryOn features may not work correctly.");
                    return;
                }

                // Cleanup old world config: Remove all keys starting with "carryon:"
                if (worldConfig is TreeAttribute tree)
                {
                    var keysToRemove = new List<string>();
                    foreach (var key in tree.Keys)
                    {
                        if (key.StartsWith("carryon:", StringComparison.OrdinalIgnoreCase))
                        {
                            keysToRemove.Add(key);
                        }
                    }
                    foreach (var key in keysToRemove)
                    {
                        tree.RemoveAttribute(key);
                    }

                    // Save the value to the world config so it is available for both server and client
                    tree.GetOrAddTreeAttribute(ModId).MergeTree(ServerConfig.ToTreeAttribute());
                }
                else
                {
                    
                    logger?.Warning("CarryOn: World.Config is not a TreeAttribute; skipping legacy key cleanup.");
                }
            }

        }

    }
}