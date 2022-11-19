namespace CarryOn
{
    class CarryOnConfig
    {
        public bool CarryableAnvilEnabled = true;
        public bool CarryableBarrelEnabled = true;
        public bool CarryableChestLabeledEnabled = true;
        public bool CarryableChestTrunkEnabled = false;
        public bool CarryableChestEnabled = true;
        public bool CarryableCrateLegacyEnabled = true;
        public bool CarryableCrateEnabled = true;
        public bool CarryableLogWithResinEnabled = false;
        public bool CarryableLootVesselEnabled = true;
        public bool CarryablePlanterEnabled = true;
        public bool CarryableQuernEnabled = true;
        public bool CarryableStationaryBasketEnabled = true;
        public bool CarryableStorageVesselEnabled = true;

        public CarryOnConfig()
        {
        }

        public CarryOnConfig(CarryOnConfig previousConfig)
        {
            CarryableAnvilEnabled = previousConfig.CarryableAnvilEnabled;
            CarryableBarrelEnabled = previousConfig.CarryableBarrelEnabled;
            CarryableChestLabeledEnabled = previousConfig.CarryableChestLabeledEnabled;
            CarryableChestTrunkEnabled = previousConfig.CarryableChestTrunkEnabled;
            CarryableChestEnabled = previousConfig.CarryableChestEnabled;
            CarryableCrateLegacyEnabled = previousConfig.CarryableCrateLegacyEnabled;
            CarryableCrateEnabled = previousConfig.CarryableCrateEnabled;
            CarryableLogWithResinEnabled = previousConfig.CarryableLogWithResinEnabled;
            CarryableLootVesselEnabled = previousConfig.CarryableLootVesselEnabled;
            CarryablePlanterEnabled = previousConfig.CarryablePlanterEnabled;
            CarryableQuernEnabled = previousConfig.CarryableQuernEnabled;
            CarryableStationaryBasketEnabled = previousConfig.CarryableStationaryBasketEnabled;
            CarryableStorageVesselEnabled = previousConfig.CarryableStorageVesselEnabled;
        }
    }
}