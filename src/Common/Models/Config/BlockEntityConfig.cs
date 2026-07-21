using System.ComponentModel;
using CarryOn.API.Common.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CarryOn.Common.Models
{
    /// <summary>Settings for dropping carried blocks when the player takes damage.</summary>
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

    /// <summary>Settings for dropped block entities (visuals, pickup access, despawn).</summary>
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
}
