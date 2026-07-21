using System.Collections.Generic;
using System.ComponentModel;
using CarryOn.API.Common.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Models
{
    /// <summary>General carry behavior options (interactions, back slot, temperature, permissions).</summary>
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
        [TreeValue("MaxInteractionDistance")] public int MaxInteractionDistance { get; set; } = Defaults.MaxInteractionDistance;

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
        [DisplayName("Temperature Threshold (°C)")]
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
}
