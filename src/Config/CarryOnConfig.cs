using System.Collections.Generic;
using CarryOn.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

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
            tree.SetInt("ConfigVersion", ConfigVersion ?? 2);

            // Carryables
            var carryables = new TreeAttribute();
            carryables.SetBool("Anvil", Carryables.Anvil);
            carryables.SetBool("Barrel", Carryables.Barrel);
            carryables.SetBool("Bookshelf", Carryables.Bookshelf);
            carryables.SetBool("BunchOCandles", Carryables.BunchOCandles);
            carryables.SetBool("Chandelier", Carryables.Chandelier);
            carryables.SetBool("ChestLabeled", Carryables.ChestLabeled);
            carryables.SetBool("ChestTrunk", Carryables.ChestTrunk);
            carryables.SetBool("Chest", Carryables.Chest);
            carryables.SetBool("Clutter", Carryables.Clutter);
            carryables.SetBool("Crate", Carryables.Crate);
            carryables.SetBool("DisplayCase", Carryables.DisplayCase);
            carryables.SetBool("Flowerpot", Carryables.Flowerpot);
            carryables.SetBool("Forge", Carryables.Forge);
            carryables.SetBool("Henbox", Carryables.Henbox);
            carryables.SetBool("LogWithResin", Carryables.LogWithResin);
            carryables.SetBool("LootVessel", Carryables.LootVessel);
            carryables.SetBool("MoldRack", Carryables.MoldRack);
            carryables.SetBool("Mold", Carryables.Mold);
            carryables.SetBool("Oven", Carryables.Oven);
            carryables.SetBool("Planter", Carryables.Planter);
            carryables.SetBool("Quern", Carryables.Quern);
            carryables.SetBool("ReedChest", Carryables.ReedChest);
            carryables.SetBool("Resonator", Carryables.Resonator);
            carryables.SetBool("Shelf", Carryables.Shelf);
            carryables.SetBool("Sign", Carryables.Sign);
            carryables.SetBool("StorageVessel", Carryables.StorageVessel);
            carryables.SetBool("ToolRack", Carryables.ToolRack);
            carryables.SetBool("TorchHolder", Carryables.TorchHolder);
            tree["Carryables"] = carryables;

            // Interactables
            var interactables = new TreeAttribute();
            interactables.SetBool("Door", Interactables.Door);
            interactables.SetBool("Barrel", Interactables.Barrel);
            interactables.SetBool("Storage", Interactables.Storage);
            tree["Interactables"] = interactables;

            // CarryOptions
            var carryOptions = new TreeAttribute();
            carryOptions.SetBool("AllowSprintWhileCarrying", CarryOptions.AllowSprintWhileCarrying);
            carryOptions.SetBool("IgnoreCarrySpeedPenalty", CarryOptions.IgnoreCarrySpeedPenalty);
            carryOptions.SetBool("RemoveInteractDelayWhileCarrying", CarryOptions.RemoveInteractDelayWhileCarrying);
            carryOptions.SetFloat("InteractSpeedMultiplier", CarryOptions.InteractSpeedMultiplier);
            carryOptions.SetBool("BackSlotEnabled", CarryOptions.BackSlotEnabled);
            carryOptions.SetBool("AllowChestTrunksOnBack", CarryOptions.AllowChestTrunksOnBack);
            carryOptions.SetBool("AllowLargeChestsOnBack", CarryOptions.AllowLargeChestsOnBack);
            carryOptions.SetBool("AllowCratesOnBack", CarryOptions.AllowCratesOnBack);
            tree["CarryOptions"] = carryOptions;

            // CarryablesFilters
            var filters = new TreeAttribute();
            filters.SetBool("AutoMapSimilar", CarryablesFilters.AutoMapSimilar);
            filters.SetStringArray("AutoMatchIgnoreMods", CarryablesFilters.AutoMatchIgnoreMods);
            filters.SetStringArray("AllowedShapeOnlyMatches", CarryablesFilters.AllowedShapeOnlyMatches);
            filters.SetStringArray("RemoveBaseCarryableBehaviour", CarryablesFilters.RemoveBaseCarryableBehaviour);
            filters.SetStringArray("RemoveCarryableBehaviour", CarryablesFilters.RemoveCarryableBehaviour);
            tree["CarryablesFilters"] = filters;

            // DebuggingOptions
            var debug = new TreeAttribute();
            debug.SetBool("LoggingEnabled", DebuggingOptions.LoggingEnabled);
            debug.SetBool("DisableHarmonyPatch", DebuggingOptions.DisableHarmonyPatch);
            tree["DebuggingOptions"] = debug;

            return tree;
        }
    }
}