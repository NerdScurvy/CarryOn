using System;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.TransformGroupResolvers.HiddenBackpack
{
    /// <summary>
    /// Reads Hide Gear's per-wearer hidden state from the entity's synced WatchedAttributes:
    /// key <c>hidegear:hidden</c> holds JSON <c>{ "HideBackpack": bool, ... }</c>, and a
    /// server-set <c>hidegear:forcevisible</c> bool force-shows hidden gear (PvP) - mirroring
    /// Hide Gear's own render decision, which reports no settings under force-visible. The
    /// JSON DTO is local; the mod assembly is never referenced.
    /// </summary>
    public sealed class HideGearHiddenBackpackSignal : IHiddenBackpackSignal
    {
        public const string SignalCodeName = "hidegear";
        public const string ModId = SignalCodeName;
        public const string HiddenSettingsKey = "hidegear:hidden";
        public const string ForceVisibleKey = "hidegear:forcevisible";

        public string SignalCode => SignalCodeName;

        public bool IsBackpackHidden(EntityPlayer entity)
        {
            var attributes = entity?.WatchedAttributes;
            if (attributes == null) return false;
            if (attributes.GetBool(ForceVisibleKey, false)) return false;

            var json = attributes.GetString(HiddenSettingsKey, null);
            if (string.IsNullOrEmpty(json)) return false;

            try
            {
                return JsonConvert.DeserializeObject<HideGearSettingsDto>(json)?.HideBackpack == true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public string? GetCacheSignature(EntityPlayer entity)
        {
            var attributes = entity?.WatchedAttributes;
            if (attributes == null) return null;
            return (attributes.GetBool(ForceVisibleKey, false) ? "1" : "0")
                + "|" + attributes.GetString(HiddenSettingsKey, null);
        }

        private sealed class HideGearSettingsDto
        {
            public bool HideBackpack { get; set; }
        }
    }
}
