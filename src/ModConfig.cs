using Vintagestory.API.Common;

namespace CarryOn
{
    static class ModConfig
    {
        public static CarryOnClientConfig ClientConfig;

        private const string ConfigFile = "CarryOnConfig.json";
        private const string ClientConfigFile = "CarryOnClientConfig.json";

        public static void ReadConfig(ICoreAPI api)
        {
            if (api.Side == EnumAppSide.Server)
            {
                CarryOnConfig serverConfig;
                try
                {
                    serverConfig = LoadConfig(api);

                    if (serverConfig == null)
                    {
                        GenerateConfig(api);
                        serverConfig = LoadConfig(api);
                    }
                    else
                    {
                        GenerateConfig(api, serverConfig);
                    }
                }
                catch
                {
                    GenerateConfig(api);
                    serverConfig = LoadConfig(api);
                }

                if (api.Side == EnumAppSide.Server)
                {
                    var worldConfig = api.World.Config;

                    worldConfig.SetBool(CarrySystem.ModId + ":AnvilEnabled", serverConfig.AnvilEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":BarrelEnabled", serverConfig.BarrelEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":BunchOCandlesEnabled", serverConfig.BunchOCandlesEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ChandelierEnabled", serverConfig.ChandelierEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ChestLabeledEnabled", serverConfig.ChestLabeledEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ChestTrunkEnabled", serverConfig.ChestTrunkEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ChestEnabled", serverConfig.ChestEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":CrateEnabled", serverConfig.CrateEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":CrateLegacyEnabled", serverConfig.CrateLegacyEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":DisplayCaseEnabled", serverConfig.DisplayCaseEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":FlowerpotEnabled", serverConfig.FlowerpotEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ForgeEnabled", serverConfig.ForgeEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":LogWithResinEnabled", serverConfig.LogWithResinEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":MoldRackEnabled", serverConfig.MoldRackEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":MoldsEnabled", serverConfig.MoldsEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":LootVesselEnabled", serverConfig.LootVesselEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":OvenEnabled", serverConfig.OvenEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":PlanterEnabled", serverConfig.PlanterEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":QuernEnabled", serverConfig.QuernEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ShelfEnabled", serverConfig.ShelfEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":SignEnabled", serverConfig.SignEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":StationaryBasketEnabled", serverConfig.StationaryBasketEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":StorageVesselEnabled", serverConfig.StorageVesselEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ToolRackEnabled", serverConfig.ToolRackEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":TorchHolderEnabled", serverConfig.TorchHolderEnabled);
                }
            }
            else
            {
                // Load client side config
                try
                {
                    ClientConfig = LoadClientConfig(api);

                    if (ClientConfig == null)
                    {
                        GenerateClientConfig(api);
                        ClientConfig = LoadClientConfig(api);
                    }
                    else
                    {
                        GenerateClientConfig(api, ClientConfig);
                    }
                }
                catch
                {
                    GenerateClientConfig(api);
                    ClientConfig = LoadClientConfig(api);
                }
            }
        }

        private static CarryOnConfig LoadConfig(ICoreAPI api)
        {
            return api.LoadModConfig<CarryOnConfig>(ConfigFile);
        }

        private static void GenerateConfig(ICoreAPI api)
        {
            api.StoreModConfig(new CarryOnConfig(), ConfigFile);
        }

        private static void GenerateConfig(ICoreAPI api, CarryOnConfig previousConfig)
        {
            api.StoreModConfig(new CarryOnConfig(previousConfig), ConfigFile);
        }

        private static CarryOnClientConfig LoadClientConfig(ICoreAPI api)
        {
            return api.LoadModConfig<CarryOnClientConfig>(ClientConfigFile);
        }

        private static void GenerateClientConfig(ICoreAPI api)
        {
            api.StoreModConfig(new CarryOnClientConfig(), ClientConfigFile);
        }
        private static void GenerateClientConfig(ICoreAPI api, CarryOnClientConfig previousConfig)
        {
            api.StoreModConfig(new CarryOnClientConfig(previousConfig), ClientConfigFile);
        }
    }
}