using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Vintagestory.API.Common;

namespace CarryOn.Common.Models
{
    /// <summary>Per-slot modifier value pair (hands/back).</summary>
    public class SlotValueConfig
    {
        [DisplayName("Hands Modifier")]
        [Description("Modifier value for the hands slot (leave empty for no override)")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public float? Hands { get; set; }

        [DisplayName("Back Modifier")]
        [Description("Modifier value for the back slot (leave empty for no override)")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public float? Back { get; set; }
    }

    /// <summary>A per-block-code or per-block-class slot modifier override entry.</summary>
    public class SlotModifierConfig : SlotValueConfig
    {
        [DisplayName("Key")]
        [Description("Block code or class name this entry applies to (e.g. \"game:chest-normal\" or \"BlockChest\")")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Key { get; set; }

        [JsonIgnore]
        [Browsable(false)]
        public bool IsEmpty => string.IsNullOrEmpty(Key) && Hands == null && Back == null;
    }

    /// <summary>Per-block-code and per-block-class modifier overrides with slot defaults.</summary>
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
        public SlotValueConfig SlotDefaults { get; set; } = new SlotValueConfig();
    }

    /// <summary>A per-material slot multiplier entry.</summary>
    public class SlotMaterialMultiplierConfig
    {
        [DisplayName("Material")]
        [Description("Block material this entry applies to")]
        [DefaultValue(EnumBlockMaterial.Wood)]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public EnumBlockMaterial? Material { get; set; }

        [DisplayName("Hands Multiplier")]
        [Description("Multiplier for the hands slot (leave empty for no override)")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public float? Hands { get; set; }

        [DisplayName("Back Multiplier")]
        [Description("Multiplier for the back slot (leave empty for no override)")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public float? Back { get; set; }
    }

    /// <summary>Global and per-material multiplier configuration for walk speed or hunger rate.</summary>
    public class ModifierMultipliersConfig
    {
        [DisplayName("Global Multiplier")]
        [Description("Global multiplier applied to all carried blocks for each slot (default 1.0 = no change)")]
        public SlotValueConfig Global { get; set; } = new SlotValueConfig
        {
            Hands = 1.0f,
            Back = 1.0f
        };

        [DisplayName("By Block Material")]
        [Description("Per-material multiplier entries. Each entry has a Material (e.g. Wood, Stone, Ceramic), Hands and Back multiplier values.")]
        public List<SlotMaterialMultiplierConfig> ByBlockMaterial { get; set; }
            = new List<SlotMaterialMultiplierConfig>();
    }
}
