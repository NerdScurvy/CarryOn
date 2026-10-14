using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CarryOn.API.Common.Models;
using CarryOn.Common.Logic;
using Vintagestory.API.Common;
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

        [Category("Carryables (requires restart*)")]
        [DisplayName("Carryables")]
        [Description("Which blocks can be carried - changes require a server restart* to take effect")]
        public CarryablesConfig Carryables { get; set; } = new CarryablesConfig();

        [Category("Carryables on Back (requires restart*)")]
        [DisplayName("Carryables on Back")]
        [Description("Which blocks can be placed on the back slot - changes require a server restart*")]
        public CarryablesOnBackConfig CarryablesOnBack { get; set; } = new CarryablesOnBackConfig();

        [Category("Interactables (requires restart*)")]
        [DisplayName("Interactables")]
        [Description("Which block interactions are allowed while carrying - changes require a server restart*")]
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

        [Category("Carryable Filters (requires restart*)")]
        [DisplayName("Carryable Filters")]
        [Description("Advanced carryable filtering rules - changes require a server restart*")]
        public CarryablesFiltersConfig CarryablesFilters { get; set; } = new CarryablesFiltersConfig();

        /// <summary>Backpack mapping dictionary. Main-thread only - populated during config load, read during gameplay.</summary>
        [JsonIgnore]
        [Browsable(false)]
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

        // 2f7 decision (b): keep stringly render-settings keys (group names derive from user config,
        // so a closed enum cannot represent them) but fail loud at load. A BackpackTypes type with no
        // matching "backpack-{type}" body-attachment group would otherwise render nothing on the back
        // (CarryRenderDispatcher's static table miss returns null -> silent no-op). Logging once here
        // turns the silent vanish into a visible startup warning.
        public void ValidateBackpackTransformGroups(ILogger logger)
        {
            if (logger == null || BackpackTypes == null) return;

            var unknownTypes = new List<string>();
            foreach (var type in BackpackTypes.Keys)
            {
                if (string.IsNullOrEmpty(type)) continue;
                if (Array.IndexOf(KnownBackpackTransformGroups, BackpackGroupPrefix + type) < 0)
                {
                    unknownTypes.Add(type);
                }
            }

            if (unknownTypes.Count > 0)
            {
                logger.Warning("[CarryOn] BackpackTypes type(s) have no matching \"backpack-<type>\" body-attachment render group; carried blocks of these types will not be rendered on the back: {0}", string.Join(", ", unknownTypes));
            }
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

        private static void PopulateSlotDefaults(SlotValueConfig? slotDefaults, IReadOnlyDictionary<CarrySlot, float> defaultValue)
        {
            if (slotDefaults == null) return;

            if (slotDefaults.Hands == null && defaultValue.TryGetValue(CarrySlot.Hands, out var hands))
                slotDefaults.Hands = hands;

            if (slotDefaults.Back == null && defaultValue.TryGetValue(CarrySlot.Back, out var back))
                slotDefaults.Back = back;
        }

        public void EnsureSlotDefaults()
        {
            PopulateSlotDefaults(CarryWalkSpeed?.ModifierOverrides?.SlotDefaults, Defaults.WalkSpeedModifier);
            PopulateSlotDefaults(CarryHungerRate?.ModifierOverrides?.SlotDefaults, Defaults.HungerRateModifier);
        }

        public void UpgradeVersion()
        {
            CarryOnConfigMigrations.Upgrade(this);
            EnsureSlotDefaults();
        }

        /// <summary>
        /// Signature over exactly the config surface that shapes behavioral-conditioning
        /// output, used to gate Mode B reconditioning on reload. Whole sections that feed
        /// <c>enabledCondition</c> evaluation and the conditioning passes
        /// (<c>Carryables</c>, <c>CarryablesOnBack</c>, <c>Interactables</c>,
        /// <c>CarryablesFilters</c>) plus the two <c>CarryOptions</c> keys the behavioral
        /// passes consult (<c>PickupGroundStorageBagWhileCarrying</c>,
        /// <c>AllowHighCapacityStorageOnBack</c>). Speed/hunger/distance edits must never
        /// change this signature. Anonymous-type serialization keeps exact, order-stable
        /// key/value coverage of the section models.
        /// </summary>
        public string ComputeBehaviorSurfaceSignature()
        {
            return JsonConvert.SerializeObject(new
            {
                Carryables,
                CarryablesOnBack,
                Interactables,
                CarryablesFilters,
                PickupGroundStorageBagWhileCarrying = CarryOptions?.PickupGroundStorageBagWhileCarrying,
                AllowHighCapacityStorageOnBack = CarryOptions?.AllowHighCapacityStorageOnBack
            });
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
