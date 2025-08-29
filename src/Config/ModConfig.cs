using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using static CarryOn.API.Common.CarryCode;

namespace CarryOn.Config
{
    public class ModConfig
    {
        public CarryOnConfig ServerConfig { get; private set; }
        public IWorldAccessor World { get; private set; }

        public void Init(ICoreAPI api)
        {
            World = api.World;
            if (api.Side == EnumAppSide.Server)
            {
                const int currentVersion = 2;
                try
                {
                    var loadedConfig = api.LoadModConfig<CarryOnConfig>(ConfigFile);
                    if (loadedConfig != null)
                    {
                        loadedConfig.UpgradeVersion();
                    }
                    else
                    {
                        loadedConfig = new CarryOnConfig(currentVersion);
                    }

                    // Save the upgraded or default config back to the file
                    api.StoreModConfig(loadedConfig, ConfigFile);

                    ServerConfig = loadedConfig;

                }
                catch (Exception ex)
                {
                    // Log the exception and create a default config but not save it
                    api.Logger.Error("CarryOn: Exception loading config: " + ex);
                    ServerConfig = new CarryOnConfig(currentVersion);
                }

                var worldConfig = api?.World?.Config;

                if (worldConfig == null)
                {
                    api.Logger.Error("CarryOn: Unable to access world config. CarryOn features may not work correctly.");
                    return;
                }

                if (ServerConfig == null)
                {
                    api.Logger.Error("CarryOn: ServerConfig did not load correctly. CarryOn features may not work correctly.");
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
                        worldConfig.RemoveAttribute(key);
                    }
                }
                else
                {
                    api.Logger.Warning("CarryOn: World.Config is not a TreeAttribute; skipping legacy key cleanup.");
                }

                // Save the value to the world config so it is available for both server and client
                worldConfig.GetOrAddTreeAttribute(ModId).MergeTree(ServerConfig.ToTreeAttribute());

            }

        }

    }
}