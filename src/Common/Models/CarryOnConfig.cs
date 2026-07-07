using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using CarryOn.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using CarryOn.API.Common.Models;
using static CarryOn.Common.Models.CarryCode;

namespace CarryOn.Common.Models
{
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

    public class InteractablesConfig
    {
        [TreeValue("Door")] public bool Door { get; set; } = true;
        [TreeValue("Barrel")] public bool Barrel { get; set; } = true;
        [TreeValue("Storage")] public bool Storage { get; set; } = true;
    }

    public class CarryWalkSpeedConfig
    {
        [DisplayName("Hands Enabled")]
        [Description("Apply walk speed penalty when carrying in hands")]
        [DefaultValue(true)]
        [TreeValue("HandsEnabled")] public bool HandsEnabled { get; set; } = true;

        [DisplayName("Back Enabled")]
        [Description("Apply walk speed penalty when carrying on back")]
        [DefaultValue(true)]
        [TreeValue("BackEnabled")] public bool BackEnabled { get; set; } = true;

        [DisplayName("Allow Sprint (Hands)")]
        [Description("Allow sprinting while carrying in hands")]
        [DefaultValue(false)]
        [TreeValue("HandsAllowSprint")] public bool HandsAllowSprint { get; set; }

        [DisplayName("Allow Sprint (Back)")]
        [Description("Allow sprinting while carrying on back")]
        [DefaultValue(true)]
        [TreeValue("BackAllowSprint")] public bool BackAllowSprint { get; set; } = true;

        [DisplayName("Walk Speed Overrides")]
        [Description("Per-block speed modifier overrides")]
        public ModifierOverridesConfig ModifierOverrides { get; set; } = new ModifierOverridesConfig();
    }

    public class CarryHungerRateConfig
    {
        [DisplayName("Hands Enabled")]
        [Description("Apply hunger rate modifier when carrying in hands")]
        [DefaultValue(false)]
        [TreeValue("HandsEnabled")] public bool HandsEnabled { get; set; } = false;

        [DisplayName("Back Enabled")]
        [Description("Apply hunger rate modifier when carrying on back")]
        [DefaultValue(true)]
        [TreeValue("BackEnabled")] public bool BackEnabled { get; set; } = true;

        [DisplayName("Min Saturation Threshold")]
        [Description("Minimum saturation before hunger modifier takes effect")]
        [DefaultValue(150.0f)]
        [TreeValue("MinSaturationThreshold")] public float MinSaturationThreshold { get; set; } = Default.MinSaturationThreshold;

        [DisplayName("Hunger Rate Overrides")]
        [Description("Per-block hunger rate modifier overrides")]
        public ModifierOverridesConfig ModifierOverrides { get; set; } = new ModifierOverridesConfig();
    }

    public class CarryablesFiltersConfig
    {
        [TreeValue("AutoMapSimilar")] public bool AutoMapSimilar { get; set; } = true;
        [TreeValue("AutoMatchIgnoreMods")] public string[] AutoMatchIgnoreMods { get; set; } = ["mcrate"];
        [TreeValue("AllowedShapeOnlyMatches")] public string[] AllowedShapeOnlyMatches { get; set; } = ["block/clay/lootvessel", "block/wood/chest/normal", "block/wood/trunk/normal", "block/reed/basket-normal"];
        [TreeValue("RemoveBaseCarryableBehaviour")] public string[] RemoveBaseCarryableBehaviour { get; set; } = ["woodchests:wtrunk"];
        [TreeValue("RemoveCarryableBehaviour")] public string[] RemoveCarryableBehaviour { get; set; } = ["game:banner", "game:clutter-devastation"];
    }

    public class SlotModifierConfig
    {
        [DisplayName("Key")]
        [Description("Block code or class name this entry applies to (e.g. \"game:chest-normal\" or \"BlockChest\")")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Key { get; set; }

        [DisplayName("Hands Modifier")]
        [Description("Modifier value for the hands slot (leave empty for no override)")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public float? Hands { get; set; }

        [DisplayName("Back Modifier")]
        [Description("Modifier value for the back slot (leave empty for no override)")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public float? Back { get; set; }

        [JsonIgnore]
        [Browsable(false)]
        public bool IsEmpty => string.IsNullOrEmpty(Key) && Hands == null && Back == null;
    }

    public class ModifierOverridesConfig
    {
        [DisplayName("By Block Code")]
        [Description("Per-block-code speed/hunger overrides. Add entries with a block code pattern as the Key field (e.g. \"game:chest-normal\", \"game:log*\").")]
        public List<SlotModifierConfig> ByBlockCode { get; set; }
            = new List<SlotModifierConfig>();

        [DisplayName("By Block Class")]
        [Description("Per-block-class speed/hunger overrides. Add entries with a block class name as the Key field (e.g. \"BlockChest\").")]
        public List<SlotModifierConfig> ByBlockClass { get; set; }
            = new List<SlotModifierConfig>();

        [DisplayName("Slot Defaults")]
        [Description("Default speed/hunger modifier for all blocks (fallback when no specific override matches)")]
        public SlotModifierConfig SlotDefaults { get; set; } = new SlotModifierConfig();
    }

    public class DropCarriedOnDamageConfig
    {
        [DisplayName("Drop on Damage (Hands)")]
        [Description("Drop hands-carried block when taking damage")]
        [DefaultValue(true)]
        [TreeValue("HandsEnabled")]
        public bool HandsEnabled { get; set; } = true;

        [DisplayName("Drop on Damage (Back)")]
        [Description("Drop back-carried block when taking damage")]
        [DefaultValue(true)]
        [TreeValue("BackEnabled")]
        public bool BackEnabled { get; set; } = true;

        [DisplayName("Hands Damage Threshold")]
        [Description("Minimum damage to drop hands-carried block")]
        [DefaultValue(1.0f)]
        [TreeValue("HandsDamageThreshold")]
        public float HandsDamageThreshold { get; set; } = 1.0f;

        [DisplayName("Back Damage Threshold")]
        [Description("Minimum damage to drop back-carried block")]
        [DefaultValue(6.0f)]
        [TreeValue("BackDamageThreshold")]
        public float BackDamageThreshold { get; set; } = 6.0f;

        [DisplayName("Drop Range")]
        [Description("Max search range (in blocks) for drop placement")]
        [DefaultValue(2)]
        [TreeValue("DropRange")]
        public int DropRange { get; set; } = 2;
    }

    public class CarriedBlockEntityConfig
    {
        [DisplayName("Drop Mode")]
        [Description("Controls how carried blocks are dropped: Items (place in world or drop as items), EntityOnFailedPlacement (place in world or drop as block entity), EntityAlways (always drop as block entity)")]
        [DefaultValue(DropMode.EntityOnFailedPlacement)]
        [TreeValue("DropMode")]
        [JsonConverter(typeof(StringEnumConverter))]
        public DropMode DropMode { get; set; } = DropMode.EntityOnFailedPlacement;

        [DisplayName("Random Drop Rotation")]
        [Description("When enabled, dropped block entities spawn with a random facing rotation")]
        [DefaultValue(true)]
        [TreeValue("RandomDropRotation")] public bool RandomDropRotation { get; set; } = true;

        [DisplayName("Show Particles")]
        [Description("When enabled, dropped block entities display glowing pickup particles")]
        [DefaultValue(true)]
        [TreeValue("ShowParticles")] public bool ShowParticles { get; set; } = true;

        [DisplayName("Despawn After Days")]
        [Description("In-game days after which a dropped block entity despawns (0 or negative to never despawn)")]
        [DefaultValue(30)]
        [TreeValue("DespawnAfterDays")] public float DespawnAfterDays { get; set; } = 30f;

        [DisplayName("Pickup Access")]
        [Description("Who can pick up the dropped block entity: Anyone (no restrictions), OwnerOnly (only the dropper, forever), or OwnerFirst (only the dropper for GracePeriodSeconds, then anyone)")]
        [DefaultValue(PickupAccess.OwnerFirst)]
        [TreeValue("PickupAccess")]
        [JsonConverter(typeof(StringEnumConverter))]
        public PickupAccess PickupAccess { get; set; } = PickupAccess.OwnerFirst;

        [DisplayName("Grace Period Seconds")]
        [Description("Real-time seconds the owner has exclusive pickup access. Only relevant when PickupAccess is OwnerFirst")]
        [DefaultValue(300f)]
        [TreeValue("GracePeriodSeconds")] public float GracePeriodSeconds { get; set; } = 300f;

        [DisplayName("Entity Visual Scale")]
        [Description("Uniform scale for the dropped block entity visual size and collision hitbox (0.1 to 10.0, default 0.6)")]
        [DefaultValue(0.6f)]
        [TreeValue("Scale")]
        public float Scale { get; set; } = 0.6f;
    }

    public class CarryOptionsConfig
    {
        [Category("Interaction")]
        [DisplayName("Remove Interact Delay")]
        [Description("Remove interaction delay while carrying")]
        [DefaultValue(false)]
        [TreeValue("RemoveInteractDelayWhileCarrying")]    public bool RemoveInteractDelayWhileCarrying { get; set; } = false;

        [Category("Interaction")]
        [DisplayName("Interact Speed Multiplier")]
        [Description("Multiplier for interaction speed while carrying")]
        [DefaultValue(1.0f)]
        [TreeValue("InteractSpeedMultiplier")] public float InteractSpeedMultiplier { get; set; } = 1.0f;

        [Category("Interaction")]
        [DisplayName("Max Interaction Distance")]
        [Description("Max distance for carry-related interactions")]
        [DefaultValue(6)]
        [TreeValue("MaxInteractionDistance")] public int MaxInteractionDistance { get; set; } = Default.MaxInteractionDistance;

        [Category("Back Slot")]
        [DisplayName("Back Slot Enabled")]
        [Description("Allow carrying items on the back slot")]
        [DefaultValue(true)]
        [TreeValue("BackSlotEnabled")] public bool BackSlotEnabled { get; set; } = true;

        [Category("Back Slot")]
        [DisplayName("Allow High Capacity Storage On Back (Requires Restart)")]
        [Description("Allow carrying high capacity storage items on the back slot")]
        [DefaultValue(false)]
        [TreeValue("AllowHighCapacityStorageOnBack")] public bool AllowHighCapacityStorageOnBack { get; set; } = false;

        [Category("Back Slot")]
        [DisplayName("Prevent Swap From Back On Target")]
        [Description("List of targets where swapping from the back slot is prevented")]
        [TreeValue("PreventSwapFromBackOnTarget")] public string[] PreventSwapFromBackOnTarget { get; set; } = ["behavior::Container", "behavior::Door", "class::portals.portal", "code::groundstorage", "class::BlockGroundStorage"];

        [Category("Temperature")]
        [DisplayName("Too Hot To Carry")]
        [Description("Prevent picking up blocks that are too hot")]
        [DefaultValue(true)]
        [TreeValue("TooHotToCarry")] public bool TooHotToCarry { get; set; } = true;

        [Category("Temperature")]
        [DisplayName("Temperature Threshold (Â°C)")]
        [Description("Temperature threshold for too-hot-to-carry check")]
        [DefaultValue(50)]
        [TreeValue("TooHotToCarryTemperature")] public int TooHotToCarryTemperature { get; set; } = 50;

        [DisplayName("Carry Attached Wall Signs")]
        [Description("Also capture attached wall signs when picking up")]
        [DefaultValue(false)]
        [TreeValue("CarryAttachedWallSigns")] public bool CarryAttachedWallSigns { get; set; } = false;

        [DisplayName("Client-Side Permission Check")]
        [Description("Allow client-side permission checks to avoid optimistic pickup attempts on claims (may be inaccurate)")]
        [DefaultValue(true)]
        [TreeValue("ClientSidePermissionCheck")] public bool ClientSidePermissionCheck { get; set; } = true;

        [DisplayName("Track Dropped Blocks (Legacy)")]
        [Description("Track dropped blocks to allow pickup from claimed areas (legacy behavior)")]
        [DefaultValue(false)]
        [TreeValue("LegacyTrackDroppedBlocks")] public bool TrackDroppedBlocks { get; set; } = false;

        [DisplayName("Backpack Selection Mode")]
        [Description("How to select which backpack to render")]
        [DefaultValue(BackpackSelectionMode.LastFound)]
        [TreeValue("BackpackSelectionMode")]
        [JsonProperty("BackpackSelectionMode")]
        [JsonConverter(typeof(StringEnumConverter))]
        public BackpackSelectionMode BackpackSelectionMode { get; set; } = BackpackSelectionMode.LastFound;

        [JsonExtensionData(ReadData = true, WriteData = false)]
        internal Dictionary<string, JToken>? Legacy { get; set; }
    }

    public class DebuggingOptionsConfig
    {
        [DisplayName("Logging Enabled")]
        [Description("Enable debug logging (requires restart)")]
        [DefaultValue(false)]
        [TreeValue("LoggingEnabled")] public bool LoggingEnabled { get; set; } = false;

        [DisplayName("Disable Harmony Patch")]
        [Description("Disable Harmony runtime patching - changes require a server restart")]
        [DefaultValue(false)]
        [TreeValue("DisableHarmonyPatch")] public bool DisableHarmonyPatch { get; set; } = false;

        [DisplayName("Enable Pack Adjustment Tool")]
        [Description("Enable the pack adjustment debug tool (requires restart)")]
        [DefaultValue(false)]
        [TreeValue("EnablePackAdjustmentTool")] public bool EnablePackAdjustmentTool { get; set; } = false;

        [DisplayName("Disable Config Watcher")]
        [Description("Disable the file system watcher that hot-reloads the config on change (requires restart)")]
        [DefaultValue(false)]
        [TreeValue("DisableConfigWatcher")] public bool DisableConfigWatcher { get; set; } = false;
    }

    public class CarryOnConfig
    {
        private IDictionary<string, string>? backpackMapping;

        [Browsable(false)]
        public int? ConfigVersion { get; set; }

        [Category("Carryables (requires restart)")]
        [DisplayName("Carryables")]
        [Description("Which blocks can be carried - changes require a server restart to take effect")]
        public CarryablesConfig Carryables { get; set; } = new CarryablesConfig();

        [Category("Carryables on Back (requires restart)")]
        [DisplayName("Carryables on Back")]
        [Description("Which blocks can be placed on the back slot - changes require a server restart")]
        public CarryablesOnBackConfig CarryablesOnBack { get; set; } = new CarryablesOnBackConfig();

        [Category("Interactables (requires restart)")]
        [DisplayName("Interactables")]
        [Description("Which block interactions are allowed while carrying - changes require a server restart")]
        public InteractablesConfig Interactables { get; set; } = new InteractablesConfig();

        [Category("Hunger Rate")]
        public CarryHungerRateConfig CarryHungerRate { get; set; } = new CarryHungerRateConfig();

        [Category("Walk Speed")]
        public CarryWalkSpeedConfig CarryWalkSpeed { get; set; } = new CarryWalkSpeedConfig();

        [Category("Damage Drop")]
        public DropCarriedOnDamageConfig DropCarriedOnDamage { get; set; } = new DropCarriedOnDamageConfig();

        [Category("Dropped Block Entity")]
        public CarriedBlockEntityConfig CarriedBlockEntity { get; set; } = new CarriedBlockEntityConfig();

        [Category("Carry Options")]
        public CarryOptionsConfig CarryOptions { get; set; } = new CarryOptionsConfig();

        [Category("Carryable Filters (requires restart)")]
        [DisplayName("Carryable Filters")]
        [Description("Advanced carryable filtering rules - changes require a server restart")]
        public CarryablesFiltersConfig CarryablesFilters { get; set; } = new CarryablesFiltersConfig();

        [JsonIgnore]
        public IDictionary<string, string> BackpackMapping
        {
            get
            {
                if (backpackMapping == null)
                {
                    backpackMapping = new Dictionary<string, string>();
                    foreach (var type in BackpackTypes)
                    {
                        foreach (var code in type.Value)
                        {
                            if (!backpackMapping.ContainsKey(code))
                            {
                                backpackMapping[code] = type.Key;
                            }
                        }
                    }

                }
                return backpackMapping;
            }
        }

        public void InvalidateBackpackCache()
        {
            backpackMapping = null;
        }

        public IDictionary<string, string[]> BackpackTypes { get; set; }
            = new Dictionary<string, string[]>()
            {
                ["small"] = ["game:hunterbackpack"],
                ["large"] = ["game:backpack-normal", "game:backpack-sturdy"]
            };

        [Category("Debugging (requires restart)")]
        [DisplayName("Debugging")]
        [Description("Debug and developer options - some changes require a server restart")]
        public DebuggingOptionsConfig DebuggingOptions { get; set; } = new DebuggingOptionsConfig();

        [JsonExtensionData(ReadData = true, WriteData = false)]
        internal Dictionary<string, JToken>? Legacy { get; set; }

        public CarryOnConfig()
        {

        }

        public CarryOnConfig(int version)
        {
            ConfigVersion = version;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            PopulateSlotDefaults(CarryWalkSpeed?.ModifierOverrides?.SlotDefaults, CarryCode.Default.WalkSpeedModifier);
            PopulateSlotDefaults(CarryHungerRate?.ModifierOverrides?.SlotDefaults, CarryCode.Default.HungerRateModifier);
        }

        internal static void PopulateSlotDefaults(SlotModifierConfig? slotDefaults, IReadOnlyDictionary<CarrySlot, float> defaultValue)
        {
            if (slotDefaults == null) return;

            if (slotDefaults.Hands == null && defaultValue.TryGetValue(CarrySlot.Hands, out var hands))
                slotDefaults.Hands = hands;

            if (slotDefaults.Back == null && defaultValue.TryGetValue(CarrySlot.Back, out var back))
                slotDefaults.Back = back;
        }

        public void UpgradeVersion()
        {
            try
            {
                // Upgrade from version 1 to 2
                if (ConfigVersion == null)
                {
                    // Perform upgrade actions
                    ConfigVersion = 2;
                    if (Legacy != null)
                    {
                        // Carryables
                        Carryables.Anvil = Legacy.TryGetBool("AnvilEnabled", Carryables.Anvil);
                        Carryables.Barrel = Legacy.TryGetBool("BarrelEnabled", Carryables.Barrel);
                        Carryables.Bookshelf = Legacy.TryGetBool("BookshelfEnabled", Carryables.Bookshelf);
                        Carryables.BunchOCandles = Legacy.TryGetBool("BunchOCandlesEnabled", Carryables.BunchOCandles);
                        Carryables.Chandelier = Legacy.TryGetBool("ChandelierEnabled", Carryables.Chandelier);
                        Carryables.ChestTrunk = Legacy.TryGetBool("ChestTrunkEnabled", Carryables.ChestTrunk);
                        Carryables.Chest = Legacy.TryGetBool("ChestEnabled", Carryables.Chest);
                        Carryables.Clutter = Legacy.TryGetBool("ClutterEnabled", Carryables.Clutter);
                        Carryables.Crate = Legacy.TryGetBool("CrateEnabled", Carryables.Crate);
                        Carryables.DisplayCase = Legacy.TryGetBool("DisplayCaseEnabled", Carryables.DisplayCase);
                        Carryables.Flowerpot = Legacy.TryGetBool("FlowerpotEnabled", Carryables.Flowerpot);
                        Carryables.Forge = Legacy.TryGetBool("ForgeEnabled", Carryables.Forge);
                        Carryables.Henbox = Legacy.TryGetBool("HenboxEnabled", Carryables.Henbox);
                        Carryables.LogWithResin = Legacy.TryGetBool("LogWithResinEnabled", Carryables.LogWithResin);
                        Carryables.LootVessel = Legacy.TryGetBool("LootVesselEnabled", Carryables.LootVessel);
                        Carryables.MoldRack = Legacy.TryGetBool("MoldRackEnabled", Carryables.MoldRack);
                        Carryables.Mold = Legacy.TryGetBool("MoldsEnabled", Carryables.Mold);
                        Carryables.Oven = Legacy.TryGetBool("OvenEnabled", Carryables.Oven);
                        Carryables.Planter = Legacy.TryGetBool("PlanterEnabled", Carryables.Planter);
                        Carryables.Quern = Legacy.TryGetBool("QuernEnabled", Carryables.Quern);
                        Carryables.ReedChest = Legacy.TryGetBool("ReedBasketEnabled", Carryables.ReedChest);
                        Carryables.Shelf = Legacy.TryGetBool("ShelfEnabled", Carryables.Shelf);
                        Carryables.Sign = Legacy.TryGetBool("SignEnabled", Carryables.Sign);
                        Carryables.StorageVessel = Legacy.TryGetBool("StorageVesselEnabled", Carryables.StorageVessel);
                        Carryables.ToolRack = Legacy.TryGetBool("ToolRackEnabled", Carryables.ToolRack);
                        Carryables.TorchHolder = Legacy.TryGetBool("TorchHolderEnabled", Carryables.TorchHolder);
                        Carryables.Resonator = Legacy.TryGetBool("ResonatorEnabled", Carryables.Resonator);

                        // Interactables
                        Interactables.Door = Legacy.TryGetBool("InteractDoorEnabled", Interactables.Door);
                        Interactables.Storage = Legacy.TryGetBool("InteractStorageEnabled", Interactables.Storage);

                        // CarryOptions
                        CarryOptions.BackSlotEnabled = Legacy.TryGetBool("BackSlotEnabled", CarryOptions.BackSlotEnabled);
                        CarryablesOnBack.ChestTrunk = Legacy.TryGetBool("AllowChestTrunksOnBack", CarryablesOnBack.ChestTrunk);
                        CarryOptions.AllowHighCapacityStorageOnBack = Legacy.TryGetBool("AllowLargeChestsOnBack", CarryOptions.AllowHighCapacityStorageOnBack);
                        CarryablesOnBack.Crate = Legacy.TryGetBool("AllowCratesOnBack", CarryablesOnBack.Crate);
                        CarryOptions.RemoveInteractDelayWhileCarrying = Legacy.TryGetBool("RemoveInteractDelayWhileCarrying", CarryOptions.RemoveInteractDelayWhileCarrying);
                        CarryOptions.InteractSpeedMultiplier = Legacy.TryGetFloat("InteractSpeedMultiplier", CarryOptions.InteractSpeedMultiplier);

                        // Debugging Options
                        DebuggingOptions.LoggingEnabled = Legacy.TryGetBool("LoggingEnabled", DebuggingOptions.LoggingEnabled);
                        DebuggingOptions.DisableHarmonyPatch = !Legacy.TryGetBool("HarmonyPatchEnabled", !DebuggingOptions.DisableHarmonyPatch);

                        // CarryablesFilters
                        CarryablesFilters.AutoMatchIgnoreMods = Legacy.TryGetStringArray("AutoMatchIgnoreMods", CarryablesFilters.AutoMatchIgnoreMods);
                        CarryablesFilters.AllowedShapeOnlyMatches = Legacy.TryGetStringArray("AllowedShapeOnlyMatches", CarryablesFilters.AllowedShapeOnlyMatches);
                        CarryablesFilters.RemoveBaseCarryableBehaviour = Legacy.TryGetStringArray("RemoveBaseCarryableBehaviour", CarryablesFilters.RemoveBaseCarryableBehaviour);
                        CarryablesFilters.RemoveCarryableBehaviour = Legacy.TryGetStringArray("RemoveCarryableBehaviour", CarryablesFilters.RemoveCarryableBehaviour);
                    }
                }
                if (ConfigVersion == 2)
                {
                    ConfigVersion = 3;

                    if (CarryOptions?.Legacy != null)
                    {

                        if (CarryOptions.Legacy.ContainsKey("AllowLargeChestsOnBack"))
                        {
                            CarryOptions.AllowHighCapacityStorageOnBack = CarryOptions.Legacy.TryGetBool("AllowLargeChestsOnBack", CarryOptions.AllowHighCapacityStorageOnBack);
                        }
                        if (CarryOptions.Legacy.ContainsKey("AllowChestTrunksOnBack"))
                        {
                            CarryablesOnBack.ChestTrunk = CarryOptions.Legacy.TryGetBool("AllowChestTrunksOnBack", CarryablesOnBack.ChestTrunk);
                        }
                        if (CarryOptions.Legacy.ContainsKey("AllowCratesOnBack"))
                        {
                            CarryablesOnBack.Crate = CarryOptions.Legacy.TryGetBool("AllowCratesOnBack", CarryablesOnBack.Crate);
                        }
                    }

                }
                if (ConfigVersion == 3)
                {
                    ConfigVersion = 4;

                    if (CarryOptions?.Legacy != null)
                    {

                        CarryWalkSpeed.HandsEnabled = !CarryOptions.Legacy.TryGetBool("IgnoreCarrySpeedPenalty", false);
                        CarryWalkSpeed.BackEnabled = !CarryOptions.Legacy.TryGetBool("IgnoreCarrySpeedPenalty", false);
                        CarryWalkSpeed.HandsAllowSprint = CarryOptions.Legacy.TryGetBool("AllowSprintWhileCarrying", false);
                        CarryWalkSpeed.BackAllowSprint = CarryOptions.Legacy.TryGetBool("AllowSprintWhileCarrying", true);

                        if (CarryOptions.Legacy.TryGetValue("WalkSpeedOverrides", out var overridesToken)
                            && overridesToken is JObject overridesObj)
                        {
                            var overrides = new ModifierOverridesConfig();

                            if (overridesObj["ByBlockCode"] is JObject byBlockCode)
                            {
                                foreach (var entry in byBlockCode.Properties())
                                {
                                    if (entry.Value is JObject slotConfig)
                                    {
                                        overrides.ByBlockCode.Add(new SlotModifierConfig
                                        {
                                            Key = entry.Name,
                                            Hands = slotConfig.Value<float?>("Hands"),
                                            Back = slotConfig.Value<float?>("Back")
                                        });
                                    }
                                    else if (entry.Value is JValue val && val.Type == JTokenType.Float)
                                    {
                                        overrides.ByBlockCode.Add(new SlotModifierConfig
                                        {
                                            Key = entry.Name,
                                            Hands = (float?)val,
                                            Back = (float?)val
                                        });
                                    }
                                }
                            }

                            if (overridesObj["ByBlockClass"] is JObject byBlockClass)
                            {
                                foreach (var entry in byBlockClass.Properties())
                                {
                                    if (entry.Value is JObject slotConfig)
                                    {
                                        overrides.ByBlockClass.Add(new SlotModifierConfig
                                        {
                                            Key = entry.Name,
                                            Hands = slotConfig.Value<float?>("Hands"),
                                            Back = slotConfig.Value<float?>("Back")
                                        });
                                    }
                                    else if (entry.Value is JValue val && val.Type == JTokenType.Float)
                                    {
                                        overrides.ByBlockClass.Add(new SlotModifierConfig
                                        {
                                            Key = entry.Name,
                                            Hands = (float?)val,
                                            Back = (float?)val
                                        });
                                    }
                                }
                            }

                            if (overridesObj["SlotDefaults"] is JObject slotDefaults)
                            {
                                overrides.SlotDefaults = new SlotModifierConfig
                                {
                                    Hands = slotDefaults.Value<float?>("Hands"),
                                    Back = slotDefaults.Value<float?>("Back")
                                };
                            }

                            CarryWalkSpeed.ModifierOverrides = overrides;
                            PopulateSlotDefaults(CarryWalkSpeed.ModifierOverrides?.SlotDefaults, CarryCode.Default.WalkSpeedModifier);
                        }
                    }
                }
            }
            finally
            {
                ConfigVersion = CurrentConfigVersion;
            }
        }


        public ITreeAttribute ToTreeAttribute()
        {
            var tree = new TreeAttribute();
            tree.SetInt("ConfigVersion", ConfigVersion ?? 2);

            tree["Carryables"] = TreeSerializer.ToTree(Carryables);
            tree["CarryablesOnBack"] = TreeSerializer.ToTree(CarryablesOnBack);
            tree["Interactables"] = TreeSerializer.ToTree(Interactables);
            tree["CarryHungerRate"] = ToCarryHungerRateTree(CarryHungerRate);
            tree["CarryWalkSpeed"] = ToCarryWalkSpeedTree(CarryWalkSpeed);
            tree["DropCarriedOnDamage"] = ToDropCarriedOnDamageTree(DropCarriedOnDamage);
            tree["CarryOptions"] = ToCarryOptionsTree();
            tree["CarryableFilters"] = TreeSerializer.ToTree(CarryablesFilters);
            tree["CarriedBlockEntity"] = TreeSerializer.ToTree(CarriedBlockEntity);
            tree["DebuggingOptions"] = TreeSerializer.ToTree(DebuggingOptions);


            return tree;
        }

        public static CarryOnConfig FromTreeAttribute(ITreeAttribute tree)
        {
            var config = new CarryOnConfig();

            if (tree.HasAttribute("ConfigVersion"))
                config.ConfigVersion = tree.GetInt("ConfigVersion");

            TreeSerializer.FromTree(tree["Carryables"] as ITreeAttribute, config.Carryables);
            TreeSerializer.FromTree(tree["CarryablesOnBack"] as ITreeAttribute, config.CarryablesOnBack);
            TreeSerializer.FromTree(tree["Interactables"] as ITreeAttribute, config.Interactables);
            config.CarryHungerRate = FromCarryHungerRateTree(tree["CarryHungerRate"] as ITreeAttribute);
            config.CarryWalkSpeed = FromCarryWalkSpeedTree(tree["CarryWalkSpeed"] as ITreeAttribute);
            config.DropCarriedOnDamage = FromDropCarriedOnDamageTree(tree["DropCarriedOnDamage"] as ITreeAttribute);
            FromCarryOptionsTree(tree["CarryOptions"] as ITreeAttribute, config.CarryOptions);
            TreeSerializer.FromTree(tree["CarryableFilters"] as ITreeAttribute, config.CarryablesFilters);
            TreeSerializer.FromTree(tree["CarriedBlockEntity"] as ITreeAttribute, config.CarriedBlockEntity);
            TreeSerializer.FromTree(tree["DebuggingOptions"] as ITreeAttribute, config.DebuggingOptions);

            return config;
        }

        private ITreeAttribute ToCarryOptionsTree()
        {
            return TreeSerializer.ToTree(CarryOptions);
        }

        private static ITreeAttribute ToCarryHungerRateTree(CarryHungerRateConfig config)
        {
            var tree = (TreeAttribute)TreeSerializer.ToTree(config);
            tree["ModifierOverrides"] = ToModifierOverridesTree(config.ModifierOverrides);
            return tree;
        }

        private static CarryHungerRateConfig FromCarryHungerRateTree(ITreeAttribute? tree)
        {
            var config = new CarryHungerRateConfig();
            if (tree == null) return config;

            TreeSerializer.FromTree(tree, config);
            config.ModifierOverrides = FromModifierOverridesTree(tree["ModifierOverrides"] as ITreeAttribute);
            PopulateSlotDefaults(config.ModifierOverrides?.SlotDefaults, CarryCode.Default.HungerRateModifier);
            return config;
        }

        private static void FromCarryOptionsTree(ITreeAttribute? tree, CarryOptionsConfig carryOptions)
        {
            if (tree == null) return;

            TreeSerializer.FromTree(tree, carryOptions);
        }

        private static ITreeAttribute ToCarryWalkSpeedTree(CarryWalkSpeedConfig config)
        {
            var tree = (TreeAttribute)TreeSerializer.ToTree(config);
            tree["ModifierOverrides"] = ToModifierOverridesTree(config.ModifierOverrides);
            return tree;
        }

        private static CarryWalkSpeedConfig FromCarryWalkSpeedTree(ITreeAttribute? tree)
        {
            var config = new CarryWalkSpeedConfig();
            if (tree == null) return config;

            TreeSerializer.FromTree(tree, config);
            config.ModifierOverrides = FromModifierOverridesTree(tree["ModifierOverrides"] as ITreeAttribute);
            PopulateSlotDefaults(config.ModifierOverrides?.SlotDefaults, CarryCode.Default.WalkSpeedModifier);
            return config;
        }

        private static ITreeAttribute ToModifierOverridesTree(ModifierOverridesConfig overrides)
        {
            var tree = new TreeAttribute();
            overrides ??= new ModifierOverridesConfig();

            tree["ByBlockCode"] = ToSpeedMapTree(overrides.ByBlockCode);
            tree["ByBlockClass"] = ToSpeedMapTree(overrides.ByBlockClass);
            tree["SlotDefaults"] = ToSlotSpeedTree(overrides.SlotDefaults);

            return tree;
        }

        private static ModifierOverridesConfig FromModifierOverridesTree(ITreeAttribute? tree)
        {
            if (tree == null)
            {
                return new ModifierOverridesConfig();
            }

            return new ModifierOverridesConfig
            {
                ByBlockCode = FromSpeedMapTree(tree["ByBlockCode"] as ITreeAttribute),
                ByBlockClass = FromSpeedMapTree(tree["ByBlockClass"] as ITreeAttribute),
                SlotDefaults = FromSlotSpeedTree(tree["SlotDefaults"] as ITreeAttribute)
            };
        }

        private static ITreeAttribute ToSpeedMapTree(List<SlotModifierConfig> list)
        {
            var tree = new TreeAttribute();

            if (list == null)
            {
                return tree;
            }

            foreach (var entry in list)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.IsEmpty)
                {
                    continue;
                }

                tree[entry.Key.Trim()] = ToSlotSpeedTree(entry);
            }

            return tree;
        }

        private static List<SlotModifierConfig> FromSpeedMapTree(ITreeAttribute? tree)
        {
            var list = new List<SlotModifierConfig>();

            if (tree is not TreeAttribute speedTree)
            {
                return list;
            }

            foreach (var key in speedTree.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var value = FromSlotSpeedTree(speedTree[key] as ITreeAttribute);
                if (value.IsEmpty)
                {
                    continue;
                }

                value.Key = key.Trim();
                list.Add(value);
            }

            return list;
        }

        private static ITreeAttribute ToSlotSpeedTree(SlotModifierConfig speed)
        {
            var tree = new TreeAttribute();
            speed ??= new SlotModifierConfig();

            if (speed.Hands.HasValue)
            {
                tree.SetFloat(CarrySlot.Hands.ToString(), speed.Hands.Value);
            }

            if (speed.Back.HasValue)
            {
                tree.SetFloat(CarrySlot.Back.ToString(), speed.Back.Value);
            }

            return tree;
        }

        private static SlotModifierConfig FromSlotSpeedTree(ITreeAttribute? tree)
        {
            var speed = new SlotModifierConfig();

            if (tree == null)
            {
                return speed;
            }

            if (tree.HasAttribute(CarrySlot.Hands.ToString()))
            {
                speed.Hands = tree.GetFloat(CarrySlot.Hands.ToString());
            }

            if (tree.HasAttribute(CarrySlot.Back.ToString()))
            {
                speed.Back = tree.GetFloat(CarrySlot.Back.ToString());
            }

            return speed;
        }

        private static ITreeAttribute ToDropCarriedOnDamageTree(DropCarriedOnDamageConfig config)
        {
            return (TreeAttribute)TreeSerializer.ToTree(config);
        }

        private static DropCarriedOnDamageConfig FromDropCarriedOnDamageTree(ITreeAttribute? tree)
        {
            var config = new DropCarriedOnDamageConfig();
            if (tree != null)
            {
                TreeSerializer.FromTree(tree, config);
            }
            return config;
        }
    }
}
