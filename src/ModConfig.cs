using Vintagestory.API.Common;

namespace CarryOn
{
    static class ModConfig
    {
        private static CarryOnConfig config;

        private const string ConfigFile = "CarryOnConfig.json";

        public static void ReadConfig(ICoreAPI api)
        {
            try
            {
                config = LoadConfig(api);

                if (config == null)
                {
                    GenerateConfig(api);
                    config = LoadConfig(api);
                }
                else
                {
                    GenerateConfig(api, config);
                }
            }
            catch
            {
                GenerateConfig(api);
                config = LoadConfig(api);
            }

            var worldConfig = api.World.Config;

            worldConfig.SetBool("CarryableAnvilEnabled", config.CarryableAnvilEnabled);
            worldConfig.SetBool("CarryableBarrelEnabled", config.CarryableBarrelEnabled);
            worldConfig.SetBool("CarryableChestLabeledEnabled", config.CarryableChestLabeledEnabled);
            worldConfig.SetBool("CarryableChestTrunkEnabled", config.CarryableChestTrunkEnabled);
            worldConfig.SetBool("CarryableChestEnabled", config.CarryableChestEnabled);
            worldConfig.SetBool("CarryableCrateLegacyEnabled", config.CarryableCrateLegacyEnabled);
            worldConfig.SetBool("CarryableLogWithResinEnabled", config.CarryableLogWithResinEnabled);
            worldConfig.SetBool("CarryableCrateEnabled", config.CarryableCrateEnabled);
            worldConfig.SetBool("CarryableLootVesselEnabled", config.CarryableLootVesselEnabled);
            worldConfig.SetBool("CarryablePlanterEnabled", config.CarryablePlanterEnabled);
            worldConfig.SetBool("CarryableQuernEnabled", config.CarryableQuernEnabled);
            worldConfig.SetBool("CarryableStationaryBasketEnabled", config.CarryableStationaryBasketEnabled);
            worldConfig.SetBool("CarryableStorageVesselEnabled", config.CarryableStorageVesselEnabled);
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