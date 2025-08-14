using System.Text.Json.Serialization;
using ProtoBuf;

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
        public bool Molds { get; set; }
        public bool Oven { get; set; }
        public bool Planter { get; set; } = true;
        public bool Quern { get; set; } = true;
        public bool ReedBasket { get; set; } = true;
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

    public class DroppedBlockOptionsConfig
    {
        public string[] NonGroundBlockClasses { get; set; } = ["BlockWater", "BlockLava"];
    }

    public class DebuggingOptionsConfig
    {
        public bool LoggingEnabled { get; set; } = false;
        public bool DisableHarmonyPatch { get; set; } = false;
    }


    public class CarryOnConfig
    {
        public int ConfigVersion { get; set; } = 2;
        public CarryablesConfig Carryables { get; set; } = new CarryablesConfig();

        public InteractablesConfig Interactables { get; set; } = new InteractablesConfig();

        public CarryOptionsConfig CarryOptions { get; set; } = new CarryOptionsConfig();

        public CarryablesFiltersConfig CarryablesFilters { get; set; } = new CarryablesFiltersConfig();

        public DroppedBlockOptionsConfig DroppedBlockOptions { get; set; } = new DroppedBlockOptionsConfig();

        public DebuggingOptionsConfig DebuggingOptions { get; set; } = new DebuggingOptionsConfig();

        public CarryOnConfig()
        {

        }

        public CarryOnConfig(CarryOnConfig previousConfig)
        {
            if (previousConfig is null)
                throw new System.ArgumentNullException(nameof(previousConfig));

            ConfigVersion = previousConfig.ConfigVersion;

            // Carryables
            if (previousConfig.Carryables != null)
            {
                Carryables.Anvil = previousConfig.Carryables.Anvil;
                Carryables.Barrel = previousConfig.Carryables.Barrel;
                Carryables.Bookshelf = previousConfig.Carryables.Bookshelf;
                Carryables.BunchOCandles = previousConfig.Carryables.BunchOCandles;
                Carryables.Chandelier = previousConfig.Carryables.Chandelier;
                Carryables.ChestLabeled = previousConfig.Carryables.ChestLabeled;
                Carryables.ChestTrunk = previousConfig.Carryables.ChestTrunk;
                Carryables.Chest = previousConfig.Carryables.Chest;
                Carryables.Clutter = previousConfig.Carryables.Clutter;
                Carryables.Crate = previousConfig.Carryables.Crate;
                Carryables.DisplayCase = previousConfig.Carryables.DisplayCase;
                Carryables.Flowerpot = previousConfig.Carryables.Flowerpot;
                Carryables.Forge = previousConfig.Carryables.Forge;
                Carryables.Henbox = previousConfig.Carryables.Henbox;
                Carryables.LogWithResin = previousConfig.Carryables.LogWithResin;
                Carryables.MoldRack = previousConfig.Carryables.MoldRack;
                Carryables.Molds = previousConfig.Carryables.Molds;
                Carryables.LootVessel = previousConfig.Carryables.LootVessel;
                Carryables.Oven = previousConfig.Carryables.Oven;
                Carryables.Planter = previousConfig.Carryables.Planter;
                Carryables.Quern = previousConfig.Carryables.Quern;
                Carryables.ReedBasket = previousConfig.Carryables.ReedBasket;
                Carryables.Resonator = previousConfig.Carryables.Resonator;
                Carryables.Shelf = previousConfig.Carryables.Shelf;
                Carryables.Sign = previousConfig.Carryables.Sign;
                Carryables.StorageVessel = previousConfig.Carryables.StorageVessel;
                Carryables.ToolRack = previousConfig.Carryables.ToolRack;
                Carryables.TorchHolder = previousConfig.Carryables.TorchHolder;
            }

            // Interactables
            if (previousConfig.Interactables != null)
            {
                Interactables.Barrel = previousConfig.Interactables.Barrel;
                Interactables.Door = previousConfig.Interactables.Door;
                Interactables.Storage = previousConfig.Interactables.Storage;
            }

            // CarryOptions
            if (previousConfig.CarryOptions != null)
            {
                CarryOptions.AllowSprintWhileCarrying = previousConfig.CarryOptions.AllowSprintWhileCarrying;
                CarryOptions.IgnoreCarrySpeedPenalty = previousConfig.CarryOptions.IgnoreCarrySpeedPenalty;
                CarryOptions.RemoveInteractDelayWhileCarrying = previousConfig.CarryOptions.RemoveInteractDelayWhileCarrying;
                CarryOptions.InteractSpeedMultiplier = previousConfig.CarryOptions.InteractSpeedMultiplier;
                CarryOptions.AllowChestTrunksOnBack = previousConfig.CarryOptions.AllowChestTrunksOnBack;
                CarryOptions.BackSlotEnabled = previousConfig.CarryOptions.BackSlotEnabled;
                CarryOptions.AllowLargeChestsOnBack = previousConfig.CarryOptions.AllowLargeChestsOnBack;
                CarryOptions.AllowCratesOnBack = previousConfig.CarryOptions.AllowCratesOnBack;
            }

            // CarryablesFilters
            if (previousConfig.CarryablesFilters != null)
            {
                CarryablesFilters.AutoMapSimilar = previousConfig.CarryablesFilters.AutoMapSimilar;
                CarryablesFilters.AutoMatchIgnoreMods = previousConfig.CarryablesFilters.AutoMatchIgnoreMods != null
                    ? (string[])previousConfig.CarryablesFilters.AutoMatchIgnoreMods.Clone()
                    : [];
                CarryablesFilters.AllowedShapeOnlyMatches = previousConfig.CarryablesFilters.AllowedShapeOnlyMatches != null
                    ? (string[])previousConfig.CarryablesFilters.AllowedShapeOnlyMatches.Clone()
                    : [];
                CarryablesFilters.RemoveBaseCarryableBehaviour = previousConfig.CarryablesFilters.RemoveBaseCarryableBehaviour != null
                    ? (string[])previousConfig.CarryablesFilters.RemoveBaseCarryableBehaviour.Clone()
                    : [];
                CarryablesFilters.RemoveCarryableBehaviour = previousConfig.CarryablesFilters.RemoveCarryableBehaviour != null
                    ? (string[])previousConfig.CarryablesFilters.RemoveCarryableBehaviour.Clone()
                    : [];
            }

            // Dropped Block Options
            if (previousConfig.DroppedBlockOptions != null)
            {
                DroppedBlockOptions.NonGroundBlockClasses = previousConfig.DroppedBlockOptions.NonGroundBlockClasses != null
                    ? (string[])previousConfig.DroppedBlockOptions.NonGroundBlockClasses.Clone()
                    : [];
            }

            // Debugging Options
            if (previousConfig.DebuggingOptions != null)
            {
                DebuggingOptions.LoggingEnabled = previousConfig.DebuggingOptions.LoggingEnabled;
                DebuggingOptions.DisableHarmonyPatch = previousConfig.DebuggingOptions.DisableHarmonyPatch;
            }
        }
    }
}