namespace CarryOn
{
    class CarryOnConfig
    {
        public bool AnvilEnabled = true;
        public bool BarrelEnabled = true;
        public bool BunchOCandlesEnabled;
        public bool ChandelierEnabled;
        public bool ChestLabeledEnabled = true;
        public bool ChestTrunkEnabled;
        public bool ChestEnabled = true;
        public bool CrateLegacyEnabled = true;
        public bool CrateEnabled = true;
        public bool DisplayCaseEnabled;
        public bool FlowerpotEnabled;
        public bool ForgeEnabled;
        public bool LogWithResinEnabled;
        public bool LootVesselEnabled = true;
        public bool MoldRackEnabled;
        public bool MoldsEnabled;
        public bool OvenEnabled;
        public bool PlanterEnabled = true;
        public bool QuernEnabled = true;
        public bool ShelfEnabled;
        public bool SignEnabled;
        public bool StationaryBasketEnabled = true;
        public bool StorageVesselEnabled = true;
        public bool ToolRackEnabled;
        public bool TorchHolderEnabled;

        public CarryOnConfig()
        {
        }

        public CarryOnConfig(CarryOnConfig previousConfig)
        {
            AnvilEnabled = previousConfig.AnvilEnabled;
            BarrelEnabled = previousConfig.BarrelEnabled;
            BunchOCandlesEnabled = previousConfig.BunchOCandlesEnabled;
            ChandelierEnabled = previousConfig.ChandelierEnabled;
            ChestLabeledEnabled = previousConfig.ChestLabeledEnabled;
            ChestTrunkEnabled = previousConfig.ChestTrunkEnabled;
            ChestEnabled = previousConfig.ChestEnabled;
            CrateLegacyEnabled = previousConfig.CrateLegacyEnabled;
            CrateEnabled = previousConfig.CrateEnabled;
            DisplayCaseEnabled = previousConfig.DisplayCaseEnabled;
            FlowerpotEnabled = previousConfig.FlowerpotEnabled;
            ForgeEnabled = previousConfig.ForgeEnabled;
            LogWithResinEnabled = previousConfig.LogWithResinEnabled;
            LootVesselEnabled = previousConfig.LootVesselEnabled;
            MoldRackEnabled = previousConfig.MoldRackEnabled;
            MoldsEnabled = previousConfig.MoldsEnabled;
            OvenEnabled = previousConfig.OvenEnabled;
            PlanterEnabled = previousConfig.PlanterEnabled;
            QuernEnabled = previousConfig.QuernEnabled;
            ShelfEnabled = previousConfig.ShelfEnabled;
            SignEnabled = previousConfig.SignEnabled;
            StationaryBasketEnabled = previousConfig.StationaryBasketEnabled;
            StorageVesselEnabled = previousConfig.StorageVesselEnabled;
            ToolRackEnabled = previousConfig.ToolRackEnabled;
            TorchHolderEnabled = previousConfig.ToolRackEnabled;
        }
    }
}