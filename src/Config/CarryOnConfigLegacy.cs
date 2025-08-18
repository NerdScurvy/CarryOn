namespace CarryOn.Config
{
    class CarryOnConfigLegacy
    {

        public bool AnvilEnabled = true;
        public bool BarrelEnabled = true;
        public bool BookshelfEnabled;
        public bool BunchOCandlesEnabled;
        public bool ChandelierEnabled;
        public bool ChestLabeledEnabled = true;
        public bool ChestTrunkEnabled;
        public bool ChestEnabled = true;
        public bool ClutterEnabled;
        public bool CrateLegacyEnabled = true;
        public bool CrateEnabled = true;
        public bool DisplayCaseEnabled;
        public bool FlowerpotEnabled;
        public bool ForgeEnabled;
        public bool HenboxEnabled;
        public bool LogWithResinEnabled;
        public bool LootVesselEnabled = true;
        public bool MoldRackEnabled;
        public bool MoldsEnabled;
        public bool OvenEnabled;
        public bool PlanterEnabled = true;
        public bool QuernEnabled = true;
        public bool ShelfEnabled;
        public bool SignEnabled;
        public bool ReedBasketEnabled = true;
        public bool StorageVesselEnabled = true;
        public bool ToolRackEnabled;
        public bool TorchHolderEnabled;

        public bool BackSlotEnabled = true;
        public bool AllowChestTrunksOnBack;
        public bool AllowLargeChestsOnBack;
        public bool AllowCratesOnBack;

        public bool InteractDoorEnabled { get; set; } = true;
        public bool InteractStorageEnabled { get; set; } = true;

        public string[] NonGroundBlockClasses = ["BlockWater", "BlockLava"];

        public string[] AutoMatchIgnoreMods = ["mcrate"];

        public string[] AllowedShapeOnlyMatches = ["block/clay/lootvessel", "block/wood/chest/normal", "block/wood/trunk/normal", "block/reed/basket-normal"];

        public string[] RemoveBaseCarryableBehaviour = ["woodchests:wtrunk"];

        public string[] RemoveCarryableBehaviour = ["game:banner"];

        public bool LoggingEnabled { get; set; }

        public bool HarmonyPatchEnabled = true;

        public bool AllowSprintWhileCarrying = false;
        public bool IgnoreCarrySpeedPenalty = false;
        public bool RemoveInteractDelayWhileCarrying = false;
        public float InteractSpeedMultiplier = 1.0f;

        public CarryOnConfigLegacy()
        {
        }

        public CarryOnConfigLegacy(CarryOnConfigLegacy previousConfig)
        {
            if (previousConfig == null)
            {
                throw new System.ArgumentNullException(nameof(previousConfig));
            }

            // Carryables
            AnvilEnabled = previousConfig.AnvilEnabled;
            BarrelEnabled = previousConfig.BarrelEnabled;
            BookshelfEnabled = previousConfig.BookshelfEnabled;
            BunchOCandlesEnabled = previousConfig.BunchOCandlesEnabled;
            ChandelierEnabled = previousConfig.ChandelierEnabled;
            ChestLabeledEnabled = previousConfig.ChestLabeledEnabled;
            ChestTrunkEnabled = previousConfig.ChestTrunkEnabled;
            ChestEnabled = previousConfig.ChestEnabled;
            ClutterEnabled = previousConfig.ClutterEnabled;
            CrateLegacyEnabled = previousConfig.CrateLegacyEnabled;
            CrateEnabled = previousConfig.CrateEnabled;
            DisplayCaseEnabled = previousConfig.DisplayCaseEnabled;
            FlowerpotEnabled = previousConfig.FlowerpotEnabled;
            ForgeEnabled = previousConfig.ForgeEnabled;
            HenboxEnabled = previousConfig.HenboxEnabled;
            LogWithResinEnabled = previousConfig.LogWithResinEnabled;
            LootVesselEnabled = previousConfig.LootVesselEnabled;
            MoldRackEnabled = previousConfig.MoldRackEnabled;
            MoldsEnabled = previousConfig.MoldsEnabled;
            OvenEnabled = previousConfig.OvenEnabled;
            PlanterEnabled = previousConfig.PlanterEnabled;
            QuernEnabled = previousConfig.QuernEnabled;
            ShelfEnabled = previousConfig.ShelfEnabled;
            SignEnabled = previousConfig.SignEnabled;
            ReedBasketEnabled = previousConfig.ReedBasketEnabled;
            StorageVesselEnabled = previousConfig.StorageVesselEnabled;
            ToolRackEnabled = previousConfig.ToolRackEnabled;
            TorchHolderEnabled = previousConfig.TorchHolderEnabled;

            // Interactables
            InteractDoorEnabled = previousConfig.InteractDoorEnabled;
            InteractStorageEnabled = previousConfig.InteractStorageEnabled;

            // Carry Options
            BackSlotEnabled = previousConfig.BackSlotEnabled;
            AllowChestTrunksOnBack = previousConfig.AllowChestTrunksOnBack;
            AllowLargeChestsOnBack = previousConfig.AllowLargeChestsOnBack;
            AllowCratesOnBack = previousConfig.AllowCratesOnBack;
            AllowSprintWhileCarrying = previousConfig.AllowSprintWhileCarrying;
            IgnoreCarrySpeedPenalty = previousConfig.IgnoreCarrySpeedPenalty;
            RemoveInteractDelayWhileCarrying = previousConfig.RemoveInteractDelayWhileCarrying;
            InteractSpeedMultiplier = previousConfig.InteractSpeedMultiplier > 0 ? previousConfig.InteractSpeedMultiplier : 1.0f;

            // Carryables Filters
            AutoMatchIgnoreMods = ModConfig.CloneArray(previousConfig.AutoMatchIgnoreMods);
            AllowedShapeOnlyMatches = ModConfig.CloneArray(previousConfig.AllowedShapeOnlyMatches);
            RemoveBaseCarryableBehaviour = ModConfig.CloneArray(previousConfig.RemoveBaseCarryableBehaviour);
            RemoveCarryableBehaviour = ModConfig.CloneArray(previousConfig.RemoveCarryableBehaviour);

            // Dropped Block Options
            NonGroundBlockClasses = ModConfig.CloneArray(previousConfig.NonGroundBlockClasses);

            // Debugging Options
            LoggingEnabled = previousConfig.LoggingEnabled;
            HarmonyPatchEnabled = previousConfig.HarmonyPatchEnabled;
        }

        public CarryOnConfig Convert()
        {
            var newConfig = new CarryOnConfig();
            // Converting to version 2
            newConfig.ConfigVersion = 2;

            // Carryables
            newConfig.Carryables.Anvil = AnvilEnabled;
            newConfig.Carryables.Barrel = BarrelEnabled;
            newConfig.Carryables.Bookshelf = BookshelfEnabled;
            newConfig.Carryables.BunchOCandles = BunchOCandlesEnabled;
            newConfig.Carryables.Chandelier = ChandelierEnabled;
            newConfig.Carryables.ChestLabeled = ChestLabeledEnabled;
            newConfig.Carryables.ChestTrunk = ChestTrunkEnabled;
            newConfig.Carryables.Chest = ChestEnabled;
            newConfig.Carryables.Clutter = ClutterEnabled;
            newConfig.Carryables.Crate = CrateEnabled;
            newConfig.Carryables.DisplayCase = DisplayCaseEnabled;
            newConfig.Carryables.Flowerpot = FlowerpotEnabled;
            newConfig.Carryables.Forge = ForgeEnabled;
            newConfig.Carryables.LogWithResin = LogWithResinEnabled;
            newConfig.Carryables.MoldRack = MoldRackEnabled;
            newConfig.Carryables.Molds = MoldsEnabled;
            newConfig.Carryables.LootVessel = LootVesselEnabled;
            newConfig.Carryables.Oven = OvenEnabled;
            newConfig.Carryables.Planter = PlanterEnabled;
            newConfig.Carryables.Quern = QuernEnabled;
            newConfig.Carryables.ReedBasket = ReedBasketEnabled;
            newConfig.Carryables.Shelf = ShelfEnabled;
            newConfig.Carryables.Sign = SignEnabled;
            newConfig.Carryables.StorageVessel = StorageVesselEnabled;
            newConfig.Carryables.ToolRack = ToolRackEnabled;
            newConfig.Carryables.TorchHolder = TorchHolderEnabled;
            newConfig.Carryables.Henbox = HenboxEnabled;

            // Interactables
            newConfig.Interactables.Door = InteractDoorEnabled;
            newConfig.Interactables.Storage = InteractStorageEnabled;

            // Carry Options
            newConfig.CarryOptions.AllowChestTrunksOnBack = AllowChestTrunksOnBack;
            newConfig.CarryOptions.AllowLargeChestsOnBack = AllowLargeChestsOnBack;
            newConfig.CarryOptions.AllowCratesOnBack = AllowCratesOnBack;
            newConfig.CarryOptions.AllowSprintWhileCarrying = AllowSprintWhileCarrying;
            newConfig.CarryOptions.IgnoreCarrySpeedPenalty = IgnoreCarrySpeedPenalty;
            newConfig.CarryOptions.RemoveInteractDelayWhileCarrying = RemoveInteractDelayWhileCarrying;
            newConfig.CarryOptions.InteractSpeedMultiplier = InteractSpeedMultiplier;
            newConfig.CarryOptions.BackSlotEnabled = BackSlotEnabled;

            // Debugging Options
            newConfig.DebuggingOptions.DisableHarmonyPatch = !HarmonyPatchEnabled;
            newConfig.DebuggingOptions.LoggingEnabled = LoggingEnabled;

            // Dropped Block Options
            newConfig.DroppedBlockOptions.NonGroundBlockClasses = ModConfig.CloneArray(NonGroundBlockClasses);

            // Carryables filters
            newConfig.CarryablesFilters.AutoMatchIgnoreMods = ModConfig.CloneArray(AutoMatchIgnoreMods);
            newConfig.CarryablesFilters.AllowedShapeOnlyMatches = ModConfig.CloneArray(AllowedShapeOnlyMatches);
            newConfig.CarryablesFilters.RemoveBaseCarryableBehaviour = ModConfig.CloneArray(RemoveBaseCarryableBehaviour);
            newConfig.CarryablesFilters.RemoveCarryableBehaviour = ModConfig.CloneArray(RemoveCarryableBehaviour);

            return newConfig;
        }
    }
}