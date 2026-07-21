using CarryOn.API.Common.Models;

namespace CarryOn.Common.Models
{
    /// <summary>Which block types can be carried in the hands slot.</summary>
    public class CarryablesConfig
    {
        [TreeValue("Anvil")] public bool Anvil { get; set; } = true;
        [TreeValue("Barrel")] public bool Barrel { get; set; } = true;
        [TreeValue("Bookshelf")] public bool Bookshelf { get; set; }
        [TreeValue("BunchOCandles")] public bool BunchOCandles { get; set; }
        [TreeValue("Cabinet")] public bool Cabinet { get; set; } = true;
        [TreeValue("Chandelier")] public bool Chandelier { get; set; }
        [TreeValue("ChestTrunk")] public bool ChestTrunk { get; set; }
        [TreeValue("Clutter")] public bool Clutter { get; set; }
        [TreeValue("Chest")] public bool Chest { get; set; } = true;
        [TreeValue("Crate")] public bool Crate { get; set; } = true;
        [TreeValue("DisplayCase")] public bool DisplayCase { get; set; }
        [TreeValue("Flowerpot")] public bool Flowerpot { get; set; } = true;
        [TreeValue("Forge")] public bool Forge { get; set; }
        [TreeValue("Henbox")] public bool Henbox { get; set; }
        [TreeValue("LogWithResin")] public bool LogWithResin { get; set; }
        [TreeValue("LootVessel")] public bool LootVessel { get; set; } = true;
        [TreeValue("MoldRack")] public bool MoldRack { get; set; }
        [TreeValue("Mold")] public bool Mold { get; set; }
        [TreeValue("Oven")] public bool Oven { get; set; }
        [TreeValue("Planter")] public bool Planter { get; set; } = true;
        [TreeValue("Quern")] public bool Quern { get; set; } = true;
        [TreeValue("ReedChest")] public bool ReedChest { get; set; } = true;
        [TreeValue("Resonator")] public bool Resonator { get; set; } = true;
        [TreeValue("Shelf")] public bool Shelf { get; set; }
        [TreeValue("Sign")] public bool Sign { get; set; }
        [TreeValue("StorageVessel")] public bool StorageVessel { get; set; } = true;
        [TreeValue("ToolRack")] public bool ToolRack { get; set; }
        [TreeValue("TorchHolder")] public bool TorchHolder { get; set; }
    }

    /// <summary>Which block types can be carried on the back slot.</summary>
    public class CarryablesOnBackConfig
    {
        [TreeValue("Barrel")] public bool Barrel { get; set; } = true;
        [TreeValue("ChestTrunk")] public bool ChestTrunk { get; set; }
        [TreeValue("Chest")] public bool Chest { get; set; } = true;
        [TreeValue("Crate")] public bool Crate { get; set; }
        [TreeValue("Flowerpot")] public bool Flowerpot { get; set; } = true;
        [TreeValue("LogWithResin")] public bool LogWithResin { get; set; }
        [TreeValue("LootVessel")] public bool LootVessel { get; set; } = true;
        [TreeValue("Planter")] public bool Planter { get; set; } = true;
        [TreeValue("ReedChest")] public bool ReedChest { get; set; } = true;
        [TreeValue("Resonator")] public bool Resonator { get; set; } = true;
        [TreeValue("StorageVessel")] public bool StorageVessel { get; set; } = true;
    }

    /// <summary>Which block interactions are allowed while carrying.</summary>
    public class InteractablesConfig
    {
        [TreeValue("Door")] public bool Door { get; set; } = true;
        [TreeValue("Barrel")] public bool Barrel { get; set; } = true;
        [TreeValue("Storage")] public bool Storage { get; set; } = true;
    }

    /// <summary>Advanced filtering rules for determining which blocks are carryable.</summary>
    public class CarryablesFiltersConfig
    {
        [TreeValue("AutoMapSimilar")] public bool AutoMapSimilar { get; set; } = true;
        [TreeValue("AutoMatchIgnoreMods")] public string[] AutoMatchIgnoreMods { get; set; } = ["mcrate"];
        [TreeValue("AllowedShapeOnlyMatches")] public string[] AllowedShapeOnlyMatches { get; set; } = ["block/clay/lootvessel", "block/wood/chest/normal", "block/wood/trunk/normal", "block/reed/basket-normal"];
        [TreeValue("RemoveBaseCarryableBehaviour")] public string[] RemoveBaseCarryableBehaviour { get; set; } = ["woodchests:wtrunk"];
        [TreeValue("RemoveCarryableBehaviour")] public string[] RemoveCarryableBehaviour { get; set; } = ["game:banner", "game:clutter-devastation"];
    }
}
