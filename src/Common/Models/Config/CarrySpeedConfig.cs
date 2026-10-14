using System.ComponentModel;
using CarryOn.API.Common.Models;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Models
{
    /// <summary>Shared modifier settings (per-block overrides + multipliers) for walk speed and hunger rate.</summary>
    public class CarryModifierConfigBase
    {
        [DisplayName("Modifier Overrides")]
        [Description("Per-block modifier overrides")]
        public ModifierOverridesConfig ModifierOverrides { get; set; } = new ModifierOverridesConfig();

        [DisplayName("Multipliers")]
        [Description("Scale the resolved modifier by material or globally")]
        public ModifierMultipliersConfig? Multipliers { get; set; }
            = new ModifierMultipliersConfig();
    }

    /// <summary>Walk speed penalty settings for carried blocks.</summary>
    public class CarryWalkSpeedConfig : CarryModifierConfigBase
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
    }

    /// <summary>Hunger rate modifier settings for carried blocks.</summary>
    public class CarryHungerRateConfig : CarryModifierConfigBase
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
        [TreeValue("MinSaturationThreshold")] public float MinSaturationThreshold { get; set; } = Defaults.MinSaturationThreshold;
    }
}
