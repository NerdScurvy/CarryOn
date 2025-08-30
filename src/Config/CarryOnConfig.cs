using System.Collections.Generic;
using CarryOn.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Config
{
    public class CarryablesConfig
    {
        public bool Anvil { get; set; } = true;
        public bool Barrel { get; set; } = true;
        public bool Bookshelf { get; set; }
        public bool BunchOCandles { get; set; }
        public bool Chandelier { get; set; }
        public bool ChestLabeled { get; set; } = true;
        public bool ChestTrunk { get; set; }
        public bool Chest { get; set; } = true;
        public bool Clutter { get; set; }
        public bool Crate { get; set; } = true;
        public bool DisplayCase { get; set; }
        public bool Flowerpot { get; set; }
        public bool Forge { get; set; }
        public bool Henbox { get; set; }
        public bool LogWithResin { get; set; }
        public bool LootVessel { get; set; } = true;
        public bool MoldRack { get; set; }
        public bool Mold { get; set; }
        public bool Oven { get; set; }
        public bool Planter { get; set; } = true;
        public bool Quern { get; set; } = true;
        public bool ReedChest { get; set; } = true;
        public bool Resonator { get; set; } = true;
        public bool Shelf { get; set; }
        public bool Sign { get; set; }
        public bool StorageVessel { get; set; } = true;
        public bool ToolRack { get; set; }
        public bool TorchHolder { get; set; }
    }

    public class InteractablesConfig
    {
        public bool Door { get; set; } = true;
        public bool Barrel { get; set; } = true;
        public bool Storage { get; set; } = true;
    }

    public class CarryablesFiltersConfig
    {
        public bool AutoMapSimilar { get; set; } = true;

        public string[] AutoMatchIgnoreMods { get; set; } = ["mcrate"];

        public string[] AllowedShapeOnlyMatches { get; set; } = ["block/clay/lootvessel", "block/wood/chest/normal", "block/wood/trunk/normal", "block/reed/basket-normal"];

        public string[] RemoveBaseCarryableBehaviour { get; set; } = ["woodchests:wtrunk"];

        public string[] RemoveCarryableBehaviour { get; set; } = ["game:banner"];
    }

    public class CarryOptionsConfig
    {
        public bool AllowSprintWhileCarrying { get; set; } = false;
        public bool IgnoreCarrySpeedPenalty { get; set; } = false;
        public bool RemoveInteractDelayWhileCarrying { get; set; } = false;
        public float InteractSpeedMultiplier { get; set; } = 1.0f;

        public bool BackSlotEnabled { get; set; } = true;
        public bool AllowChestTrunksOnBack { get; set; } = false;
        public bool AllowLargeChestsOnBack { get; set; } = false;
        public bool AllowCratesOnBack { get; set; } = false;

    }

    public class DebuggingOptionsConfig
    {
        public bool LoggingEnabled { get; set; } = false;
        public bool DisableHarmonyPatch { get; set; } = false;
    }


    public class CarryOnConfig
    {
        public int? ConfigVersion { get; set; }
        public CarryablesConfig Carryables { get; set; } = new CarryablesConfig();

        public InteractablesConfig Interactables { get; set; } = new InteractablesConfig();

        public CarryOptionsConfig CarryOptions { get; set; } = new CarryOptionsConfig();

        public CarryablesFiltersConfig CarryablesFilters { get; set; } = new CarryablesFiltersConfig();

        public DebuggingOptionsConfig DebuggingOptions { get; set; } = new DebuggingOptionsConfig();

        [JsonExtensionData(ReadData = true, WriteData = false)]
        private Dictionary<string, JToken> LegacyData { get; set; }

        public CarryOnConfig()
        {

        }

        public CarryOnConfig(int version)
        {
            ConfigVersion = version;
        }

        public void UpgradeVersion()
        {
            // Upgrade from version 1 to 2
            if (ConfigVersion == null)
            {
                // Perform upgrade actions
                ConfigVersion = 2;
                if (LegacyData == null) return;

                // Carryables
                Carryables.Anvil = LegacyData.TryGetBool("AnvilEnabled", Carryables.Anvil);
                Carryables.Barrel = LegacyData.TryGetBool("BarrelEnabled", Carryables.Barrel);
                Carryables.Bookshelf = LegacyData.TryGetBool("BookshelfEnabled", Carryables.Bookshelf);
                Carryables.BunchOCandles = LegacyData.TryGetBool("BunchOCandlesEnabled", Carryables.BunchOCandles);
                Carryables.Chandelier = LegacyData.TryGetBool("ChandelierEnabled", Carryables.Chandelier);
                Carryables.ChestLabeled = LegacyData.TryGetBool("ChestLabeledEnabled", Carryables.ChestLabeled);
                Carryables.ChestTrunk = LegacyData.TryGetBool("ChestTrunkEnabled", Carryables.ChestTrunk);
                Carryables.Chest = LegacyData.TryGetBool("ChestEnabled", Carryables.Chest);
                Carryables.Clutter = LegacyData.TryGetBool("ClutterEnabled", Carryables.Clutter);
                Carryables.Crate = LegacyData.TryGetBool("CrateEnabled", Carryables.Crate);
                Carryables.DisplayCase = LegacyData.TryGetBool("DisplayCaseEnabled", Carryables.DisplayCase);
                Carryables.Flowerpot = LegacyData.TryGetBool("FlowerpotEnabled", Carryables.Flowerpot);
                Carryables.Forge = LegacyData.TryGetBool("ForgeEnabled", Carryables.Forge);
                Carryables.Henbox = LegacyData.TryGetBool("HenboxEnabled", Carryables.Henbox);
                Carryables.LogWithResin = LegacyData.TryGetBool("LogWithResinEnabled", Carryables.LogWithResin);
                Carryables.LootVessel = LegacyData.TryGetBool("LootVesselEnabled", Carryables.LootVessel);
                Carryables.MoldRack = LegacyData.TryGetBool("MoldRackEnabled", Carryables.MoldRack);
                Carryables.Mold = LegacyData.TryGetBool("MoldsEnabled", Carryables.Mold);
                Carryables.Oven = LegacyData.TryGetBool("OvenEnabled", Carryables.Oven);
                Carryables.Planter = LegacyData.TryGetBool("PlanterEnabled", Carryables.Planter);
                Carryables.Quern = LegacyData.TryGetBool("QuernEnabled", Carryables.Quern);
                Carryables.ReedChest = LegacyData.TryGetBool("ReedBasketEnabled", Carryables.ReedChest);
                Carryables.Shelf = LegacyData.TryGetBool("ShelfEnabled", Carryables.Shelf);
                Carryables.Sign = LegacyData.TryGetBool("SignEnabled", Carryables.Sign);
                Carryables.StorageVessel = LegacyData.TryGetBool("StorageVesselEnabled", Carryables.StorageVessel);
                Carryables.ToolRack = LegacyData.TryGetBool("ToolRackEnabled", Carryables.ToolRack);
                Carryables.TorchHolder = LegacyData.TryGetBool("TorchHolderEnabled", Carryables.TorchHolder);
                Carryables.Resonator = LegacyData.TryGetBool("ResonatorEnabled", Carryables.Resonator);

                // Interactables
                Interactables.Door = LegacyData.TryGetBool("InteractDoorEnabled", Interactables.Door);
                Interactables.Storage = LegacyData.TryGetBool("InteractStorageEnabled", Interactables.Storage);

                // CarryOptions
                CarryOptions.BackSlotEnabled = LegacyData.TryGetBool("BackSlotEnabled", CarryOptions.BackSlotEnabled);
                CarryOptions.AllowChestTrunksOnBack = LegacyData.TryGetBool("AllowChestTrunksOnBack", CarryOptions.AllowChestTrunksOnBack);
                CarryOptions.AllowLargeChestsOnBack = LegacyData.TryGetBool("AllowLargeChestsOnBack", CarryOptions.AllowLargeChestsOnBack);
                CarryOptions.AllowCratesOnBack = LegacyData.TryGetBool("AllowCratesOnBack", CarryOptions.AllowCratesOnBack);
                CarryOptions.AllowSprintWhileCarrying = LegacyData.TryGetBool("AllowSprintWhileCarrying", CarryOptions.AllowSprintWhileCarrying);
                CarryOptions.IgnoreCarrySpeedPenalty = LegacyData.TryGetBool("IgnoreCarrySpeedPenalty", CarryOptions.IgnoreCarrySpeedPenalty);
                CarryOptions.RemoveInteractDelayWhileCarrying = LegacyData.TryGetBool("RemoveInteractDelayWhileCarrying", CarryOptions.RemoveInteractDelayWhileCarrying);
                CarryOptions.InteractSpeedMultiplier = LegacyData.TryGetFloat("InteractSpeedMultiplier", CarryOptions.InteractSpeedMultiplier);

                // Debugging Options
                DebuggingOptions.LoggingEnabled = LegacyData.TryGetBool("LoggingEnabled", DebuggingOptions.LoggingEnabled);
                DebuggingOptions.DisableHarmonyPatch = !LegacyData.TryGetBool("HarmonyPatchEnabled", !DebuggingOptions.DisableHarmonyPatch);

                // CarryablesFilters
                CarryablesFilters.AutoMatchIgnoreMods = LegacyData.TryGetStringArray("AutoMatchIgnoreMods", CarryablesFilters.AutoMatchIgnoreMods);
                CarryablesFilters.AllowedShapeOnlyMatches = LegacyData.TryGetStringArray("AllowedShapeOnlyMatches", CarryablesFilters.AllowedShapeOnlyMatches);
                CarryablesFilters.RemoveBaseCarryableBehaviour = LegacyData.TryGetStringArray("RemoveBaseCarryableBehaviour", CarryablesFilters.RemoveBaseCarryableBehaviour);
                CarryablesFilters.RemoveCarryableBehaviour = LegacyData.TryGetStringArray("RemoveCarryableBehaviour", CarryablesFilters.RemoveCarryableBehaviour);
            }
        }


        public ITreeAttribute ToTreeAttribute()
        {
            var tree = new TreeAttribute();
            tree.SetInt(ConfigKey.ConfigVersionKey, ConfigVersion ?? 2);

            // Carryables
            var carryables = new TreeAttribute();
            carryables.SetBool(ConfigKey.Carryables.AnvilKey, Carryables.Anvil);
            carryables.SetBool(ConfigKey.Carryables.BarrelKey, Carryables.Barrel);
            carryables.SetBool(ConfigKey.Carryables.BookshelfKey, Carryables.Bookshelf);
            carryables.SetBool(ConfigKey.Carryables.BunchOCandlesKey, Carryables.BunchOCandles);
            carryables.SetBool(ConfigKey.Carryables.ChandelierKey, Carryables.Chandelier);
            carryables.SetBool(ConfigKey.Carryables.ChestLabeledKey, Carryables.ChestLabeled);
            carryables.SetBool(ConfigKey.Carryables.ChestTrunkKey, Carryables.ChestTrunk);
            carryables.SetBool(ConfigKey.Carryables.ChestKey, Carryables.Chest);
            carryables.SetBool(ConfigKey.Carryables.ClutterKey, Carryables.Clutter);
            carryables.SetBool(ConfigKey.Carryables.CrateKey, Carryables.Crate);
            carryables.SetBool(ConfigKey.Carryables.DisplayCaseKey, Carryables.DisplayCase);
            carryables.SetBool(ConfigKey.Carryables.FlowerpotKey, Carryables.Flowerpot);
            carryables.SetBool(ConfigKey.Carryables.ForgeKey, Carryables.Forge);
            carryables.SetBool(ConfigKey.Carryables.HenboxKey, Carryables.Henbox);
            carryables.SetBool(ConfigKey.Carryables.LogWithResinKey, Carryables.LogWithResin);
            carryables.SetBool(ConfigKey.Carryables.LootVesselKey, Carryables.LootVessel);
            carryables.SetBool(ConfigKey.Carryables.MoldRackKey, Carryables.MoldRack);
            carryables.SetBool(ConfigKey.Carryables.MoldKey, Carryables.Mold);
            carryables.SetBool(ConfigKey.Carryables.OvenKey, Carryables.Oven);
            carryables.SetBool(ConfigKey.Carryables.PlanterKey, Carryables.Planter);
            carryables.SetBool(ConfigKey.Carryables.QuernKey, Carryables.Quern);
            carryables.SetBool(ConfigKey.Carryables.ReedChestKey, Carryables.ReedChest);
            carryables.SetBool(ConfigKey.Carryables.ResonatorKey, Carryables.Resonator);
            carryables.SetBool(ConfigKey.Carryables.ShelfKey, Carryables.Shelf);
            carryables.SetBool(ConfigKey.Carryables.SignKey, Carryables.Sign);
            carryables.SetBool(ConfigKey.Carryables.StorageVesselKey, Carryables.StorageVessel);
            carryables.SetBool(ConfigKey.Carryables.ToolRackKey, Carryables.ToolRack);
            carryables.SetBool(ConfigKey.Carryables.TorchHolderKey, Carryables.TorchHolder);
            tree[ConfigKey.CarryablesKey] = carryables;

            // Interactables
            var interactables = new TreeAttribute();
            interactables.SetBool(ConfigKey.Interactables.DoorKey, Interactables.Door);
            interactables.SetBool(ConfigKey.Interactables.BarrelKey, Interactables.Barrel);
            interactables.SetBool(ConfigKey.Interactables.StorageKey, Interactables.Storage);
            tree[ConfigKey.InteractablesKey] = interactables;

            // CarryOptions
            var carryOptions = new TreeAttribute();
            carryOptions.SetBool(ConfigKey.CarryOptions.AllowSprintWhileCarryingKey, CarryOptions.AllowSprintWhileCarrying);
            carryOptions.SetBool(ConfigKey.CarryOptions.IgnoreCarrySpeedPenaltyKey, CarryOptions.IgnoreCarrySpeedPenalty);
            carryOptions.SetBool(ConfigKey.CarryOptions.RemoveInteractDelayWhileCarryingKey, CarryOptions.RemoveInteractDelayWhileCarrying);
            carryOptions.SetFloat(ConfigKey.CarryOptions.InteractSpeedMultiplierKey, CarryOptions.InteractSpeedMultiplier);
            carryOptions.SetBool(ConfigKey.CarryOptions.BackSlotEnabledKey, CarryOptions.BackSlotEnabled);
            carryOptions.SetBool(ConfigKey.CarryOptions.AllowChestTrunksOnBackKey, CarryOptions.AllowChestTrunksOnBack);
            carryOptions.SetBool(ConfigKey.CarryOptions.AllowLargeChestsOnBackKey, CarryOptions.AllowLargeChestsOnBack);
            carryOptions.SetBool(ConfigKey.CarryOptions.AllowCratesOnBackKey, CarryOptions.AllowCratesOnBack);
            tree[ConfigKey.CarryOptionsKey] = carryOptions;

            // CarryablesFilters
            var filters = new TreeAttribute();
            filters.SetBool(ConfigKey.CarryableFilters.AutoMapSimilarKey, CarryablesFilters.AutoMapSimilar);
            filters.SetStringArray(ConfigKey.CarryableFilters.AutoMatchIgnoreModsKey, CarryablesFilters.AutoMatchIgnoreMods);
            filters.SetStringArray(ConfigKey.CarryableFilters.AllowedShapeOnlyMatchesKey, CarryablesFilters.AllowedShapeOnlyMatches);
            filters.SetStringArray(ConfigKey.CarryableFilters.RemoveBaseCarryableBehaviourKey, CarryablesFilters.RemoveBaseCarryableBehaviour);
            filters.SetStringArray(ConfigKey.CarryableFilters.RemoveCarryableBehaviourKey, CarryablesFilters.RemoveCarryableBehaviour);
            tree[ConfigKey.CarryableFiltersKey] = filters;

            // DebuggingOptions
            var debug = new TreeAttribute();
            debug.SetBool(ConfigKey.DebuggingOptions.LoggingEnabledKey, DebuggingOptions.LoggingEnabled);
            debug.SetBool(ConfigKey.DebuggingOptions.DisableHarmonyPatchKey, DebuggingOptions.DisableHarmonyPatch);
            tree[ConfigKey.DebuggingOptionsKey] = debug;

            return tree;
        }

        public static CarryOnConfig FromTreeAttribute(ITreeAttribute tree)
        {
            var config = new CarryOnConfig();

            // ConfigVersion
            if (tree.HasAttribute(ConfigKey.ConfigVersionKey))
                config.ConfigVersion = tree.GetInt(ConfigKey.ConfigVersionKey);

            // Carryables
            var carryables = tree[ConfigKey.CarryablesKey] as ITreeAttribute;
            if (carryables != null)
            {
                config.Carryables.Anvil = carryables.GetBool(ConfigKey.Carryables.AnvilKey);
                config.Carryables.Barrel = carryables.GetBool(ConfigKey.Carryables.BarrelKey);
                config.Carryables.Bookshelf = carryables.GetBool(ConfigKey.Carryables.BookshelfKey);
                config.Carryables.BunchOCandles = carryables.GetBool(ConfigKey.Carryables.BunchOCandlesKey);
                config.Carryables.Chandelier = carryables.GetBool(ConfigKey.Carryables.ChandelierKey);
                config.Carryables.ChestLabeled = carryables.GetBool(ConfigKey.Carryables.ChestLabeledKey);
                config.Carryables.ChestTrunk = carryables.GetBool(ConfigKey.Carryables.ChestTrunkKey);
                config.Carryables.Chest = carryables.GetBool(ConfigKey.Carryables.ChestKey);
                config.Carryables.Clutter = carryables.GetBool(ConfigKey.Carryables.ClutterKey);
                config.Carryables.Crate = carryables.GetBool(ConfigKey.Carryables.CrateKey);
                config.Carryables.DisplayCase = carryables.GetBool(ConfigKey.Carryables.DisplayCaseKey);
                config.Carryables.Flowerpot = carryables.GetBool(ConfigKey.Carryables.FlowerpotKey);
                config.Carryables.Forge = carryables.GetBool(ConfigKey.Carryables.ForgeKey);
                config.Carryables.Henbox = carryables.GetBool(ConfigKey.Carryables.HenboxKey);
                config.Carryables.LogWithResin = carryables.GetBool(ConfigKey.Carryables.LogWithResinKey);
                config.Carryables.LootVessel = carryables.GetBool(ConfigKey.Carryables.LootVesselKey);
                config.Carryables.MoldRack = carryables.GetBool(ConfigKey.Carryables.MoldRackKey);
                config.Carryables.Mold = carryables.GetBool(ConfigKey.Carryables.MoldKey);
                config.Carryables.Oven = carryables.GetBool(ConfigKey.Carryables.OvenKey);
                config.Carryables.Planter = carryables.GetBool(ConfigKey.Carryables.PlanterKey);
                config.Carryables.Quern = carryables.GetBool(ConfigKey.Carryables.QuernKey);
                config.Carryables.ReedChest = carryables.GetBool(ConfigKey.Carryables.ReedChestKey);
                config.Carryables.Resonator = carryables.GetBool(ConfigKey.Carryables.ResonatorKey);
                config.Carryables.Shelf = carryables.GetBool(ConfigKey.Carryables.ShelfKey);
                config.Carryables.Sign = carryables.GetBool(ConfigKey.Carryables.SignKey);
                config.Carryables.StorageVessel = carryables.GetBool(ConfigKey.Carryables.StorageVesselKey);
                config.Carryables.ToolRack = carryables.GetBool(ConfigKey.Carryables.ToolRackKey);
                config.Carryables.TorchHolder = carryables.GetBool(ConfigKey.Carryables.TorchHolderKey);
            }

            // Interactables
            var interactables = tree[ConfigKey.InteractablesKey] as ITreeAttribute;
            if (interactables != null)
            {
                config.Interactables.Door = interactables.GetBool(ConfigKey.Interactables.DoorKey);
                config.Interactables.Barrel = interactables.GetBool(ConfigKey.Interactables.BarrelKey);
                config.Interactables.Storage = interactables.GetBool(ConfigKey.Interactables.StorageKey);
            }

            // CarryOptions
            var carryOptions = tree[ConfigKey.CarryOptionsKey] as ITreeAttribute;
            if (carryOptions != null)
            {
                config.CarryOptions.AllowSprintWhileCarrying = carryOptions.GetBool(ConfigKey.CarryOptions.AllowSprintWhileCarryingKey);
                config.CarryOptions.IgnoreCarrySpeedPenalty = carryOptions.GetBool(ConfigKey.CarryOptions.IgnoreCarrySpeedPenaltyKey);
                config.CarryOptions.RemoveInteractDelayWhileCarrying = carryOptions.GetBool(ConfigKey.CarryOptions.RemoveInteractDelayWhileCarryingKey);
                config.CarryOptions.InteractSpeedMultiplier = carryOptions.GetFloat(ConfigKey.CarryOptions.InteractSpeedMultiplierKey);
                config.CarryOptions.BackSlotEnabled = carryOptions.GetBool(ConfigKey.CarryOptions.BackSlotEnabledKey);
                config.CarryOptions.AllowChestTrunksOnBack = carryOptions.GetBool(ConfigKey.CarryOptions.AllowChestTrunksOnBackKey);
                config.CarryOptions.AllowLargeChestsOnBack = carryOptions.GetBool(ConfigKey.CarryOptions.AllowLargeChestsOnBackKey);
                config.CarryOptions.AllowCratesOnBack = carryOptions.GetBool(ConfigKey.CarryOptions.AllowCratesOnBackKey);
            }

            // CarryablesFilters
            var filters = tree[ConfigKey.CarryableFiltersKey] as TreeAttribute;
            if (filters != null)
            {
                config.CarryablesFilters.AutoMapSimilar = filters.GetBool(ConfigKey.CarryableFilters.AutoMapSimilarKey);
                config.CarryablesFilters.AutoMatchIgnoreMods = filters.GetStringArray(ConfigKey.CarryableFilters.AutoMatchIgnoreModsKey);
                config.CarryablesFilters.AllowedShapeOnlyMatches = filters.GetStringArray(ConfigKey.CarryableFilters.AllowedShapeOnlyMatchesKey);
                config.CarryablesFilters.RemoveBaseCarryableBehaviour = filters.GetStringArray(ConfigKey.CarryableFilters.RemoveBaseCarryableBehaviourKey);
                config.CarryablesFilters.RemoveCarryableBehaviour = (filters[ConfigKey.CarryableFilters.RemoveCarryableBehaviourKey] as StringArrayAttribute)?.value ?? [];
            }

            // DebuggingOptions
            var debug = tree[ConfigKey.DebuggingOptionsKey] as ITreeAttribute;
            if (debug != null)
            {
                config.DebuggingOptions.LoggingEnabled = debug.GetBool(ConfigKey.DebuggingOptions.LoggingEnabledKey);
                config.DebuggingOptions.DisableHarmonyPatch = debug.GetBool(ConfigKey.DebuggingOptions.DisableHarmonyPatchKey);
            }

            return config;
        }
    }
}