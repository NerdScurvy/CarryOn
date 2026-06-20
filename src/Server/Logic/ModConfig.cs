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
        public CarryOnConfig? Config { get; private set; }

        public void Load(ICoreAPI api)
        {
            var coreApi = api ?? throw new ArgumentNullException(nameof(api));
            if (coreApi.Side != EnumAppSide.Server) return;

            try
            {
                var loadedConfig = coreApi.LoadModConfig<CarryOnConfig>(ConfigFile);
                if (loadedConfig != null)
                {
                    loadedConfig.UpgradeVersion();
                    Config = loadedConfig;
                }
                else
                {
                    Config = new CarryOnConfig(CurrentConfigVersion);
                }

                coreApi.StoreModConfig(Config, ConfigFile);
            }
            catch (Exception ex)
            {
                coreApi.Logger?.Error("CarryOn: Exception loading config: " + ex);
                Config = new CarryOnConfig(CurrentConfigVersion);
            }

            var worldConfig = coreApi.World?.Config;
            if (worldConfig == null)
            {
                coreApi.Logger?.Error("CarryOn: Unable to access world config. CarryOn features may not work correctly.");
                return;
            }

            // Cleanup old world config: Remove all keys starting with "carryon:"
            if (worldConfig is TreeAttribute tree)
            {
                var keysToRemove = new List<string>();
                foreach (var key in tree.Keys)
                {
                    if (key.StartsWith(CarryCode.WorldConfigPrefix, StringComparison.OrdinalIgnoreCase))
                        keysToRemove.Add(key);
                }
                foreach (var key in keysToRemove)
                    tree.RemoveAttribute(key);
            }

            // Sync to world config so JSON patching and clients can access values
            worldConfig.GetOrAddTreeAttribute(ModId).MergeTree(Config.ToTreeAttribute());
        }
    }
}