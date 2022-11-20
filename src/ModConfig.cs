using Vintagestory.API.Common;

namespace CarryOn
{
    static class ModConfig
    {
        public static CarryOnConfig Config;

        private const string ConfigFile = "CarryOnConfig.json";

        public static void ReadConfig(ICoreAPI api)
        {
            try
            {
                Config = LoadConfig(api);

                if (Config == null)
                {
                    GenerateConfig(api);
                    Config = LoadConfig(api);
                }
                else
                {
                    GenerateConfig(api, Config);
                }
            }
            catch
            {
                GenerateConfig(api);
                Config = LoadConfig(api);
            }

            var worldConfig = api.World.Config;

            worldConfig.SetBool(CarrySystem.ModId + ":AnvilEnabled", Config.AnvilEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":BarrelEnabled", Config.BarrelEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":BunchOCandlesEnabled", Config.BunchOCandlesEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":ChandelierEnabled", Config.ChandelierEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":ChestLabeledEnabled", Config.ChestLabeledEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":ChestTrunkEnabled", Config.ChestTrunkEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":ChestEnabled", Config.ChestEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":CrateEnabled", Config.CrateEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":CrateLegacyEnabled", Config.CrateLegacyEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":DisplayCaseEnabled", Config.DisplayCaseEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":FlowerpotEnabled", Config.FlowerpotEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":ForgeEnabled", Config.ForgeEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":LogWithResinEnabled", Config.LogWithResinEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":MoldRackEnabled", Config.MoldRackEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":MoldsEnabled", Config.MoldsEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":LootVesselEnabled", Config.LootVesselEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":OvenEnabled", Config.OvenEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":PlanterEnabled", Config.PlanterEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":QuernEnabled", Config.QuernEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":ShelfEnabled", Config.ShelfEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":SignEnabled", Config.SignEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":StationaryBasketEnabled", Config.StationaryBasketEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":StorageVesselEnabled", Config.StorageVesselEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":ToolRackEnabled", Config.ToolRackEnabled);
            worldConfig.SetBool(CarrySystem.ModId + ":TorchHolderEnabled", Config.TorchHolderEnabled);
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
    }
}