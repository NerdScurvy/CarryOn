using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CarryOn.API.Common.Models;
using CarryOn.Common.Logic;
using Vintagestory.API.Datastructures;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Models
{
    /// <summary>Root configuration model for the CarryOn mod.</summary>
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
            PopulateSlotDefaults(CarryWalkSpeed?.ModifierOverrides?.SlotDefaults, Defaults.WalkSpeedModifier);
            PopulateSlotDefaults(CarryHungerRate?.ModifierOverrides?.SlotDefaults, Defaults.HungerRateModifier);
        }

        internal static void PopulateSlotDefaults(SlotValueConfig? slotDefaults, IReadOnlyDictionary<CarrySlot, float> defaultValue)
        {
            if (slotDefaults == null) return;

            if (slotDefaults.Hands == null && defaultValue.TryGetValue(CarrySlot.Hands, out var hands))
                slotDefaults.Hands = hands;

            if (slotDefaults.Back == null && defaultValue.TryGetValue(CarrySlot.Back, out var back))
                slotDefaults.Back = back;
        }

        public void UpgradeVersion()
        {
            CarryOnConfigMigrations.Upgrade(this);
        }

        public ITreeAttribute ToTreeAttribute()
        {
            return CarryOnConfigTreeSerializer.ToTree(this);
        }

        public static CarryOnConfig FromTreeAttribute(ITreeAttribute tree)
        {
            return CarryOnConfigTreeSerializer.FromTree(tree);
        }
    }
}
