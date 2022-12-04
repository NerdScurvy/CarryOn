using Vintagestory.API.Common;

namespace CarryOn
{
    static class ModConfig
    {
        public static CarryOnClientConfig ClientConfig;
        public static CarryOnConfig ServerConfig;

        private const string ConfigFile = "CarryOnConfig.json";
        private const string ClientConfigFile = "CarryOnClientConfig.json";

        public static void ReadConfig(ICoreAPI api)
        {
            if (api.Side == EnumAppSide.Server)
            {
                try
                {
                    ServerConfig = LoadConfig(api);

                    if (ServerConfig == null)
                    {
                        GenerateConfig(api);
                        ServerConfig = LoadConfig(api);
                    }
                    else
                    {
                        GenerateConfig(api, ServerConfig);
                    }
                }
                catch
                {
                    GenerateConfig(api);
                    ServerConfig = LoadConfig(api);
                }

                if (api.Side == EnumAppSide.Server)
                {
                    var worldConfig = api.World.Config;

                    worldConfig.SetBool(CarrySystem.ModId + ":AnvilEnabled", ServerConfig.AnvilEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":BarrelEnabled", ServerConfig.BarrelEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":BunchOCandlesEnabled", ServerConfig.BunchOCandlesEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ChandelierEnabled", ServerConfig.ChandelierEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ChestLabeledEnabled", ServerConfig.ChestLabeledEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ChestTrunkEnabled", ServerConfig.ChestTrunkEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ChestEnabled", ServerConfig.ChestEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":CrateEnabled", ServerConfig.CrateEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":CrateLegacyEnabled", ServerConfig.CrateLegacyEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":DisplayCaseEnabled", ServerConfig.DisplayCaseEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":FlowerpotEnabled", ServerConfig.FlowerpotEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ForgeEnabled", ServerConfig.ForgeEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":LogWithResinEnabled", ServerConfig.LogWithResinEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":MoldRackEnabled", ServerConfig.MoldRackEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":MoldsEnabled", ServerConfig.MoldsEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":LootVesselEnabled", ServerConfig.LootVesselEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":OvenEnabled", ServerConfig.OvenEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":PlanterEnabled", ServerConfig.PlanterEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":QuernEnabled", ServerConfig.QuernEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ShelfEnabled", ServerConfig.ShelfEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":SignEnabled", ServerConfig.SignEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":StationaryBasketEnabled", ServerConfig.StationaryBasketEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":StorageVesselEnabled", ServerConfig.StorageVesselEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":ToolRackEnabled", ServerConfig.ToolRackEnabled);
                    worldConfig.SetBool(CarrySystem.ModId + ":TorchHolderEnabled", ServerConfig.TorchHolderEnabled);
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