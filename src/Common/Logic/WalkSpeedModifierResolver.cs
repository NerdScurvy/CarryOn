using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;

namespace CarryOn.Common.Logic
{
    internal sealed class WalkSpeedModifierResolver : ModifierResolver<CarryWalkSpeedConfig>
    {
        public bool IsEnabled(CarrySlot slot, CarryWalkSpeedConfig config)
        {
            return slot switch
            {
                CarrySlot.Hands => config.HandsEnabled,
                CarrySlot.Back => config.BackEnabled,
                _ => false
            };
        }

        protected override bool TryResolveSlotValue(SlotSettings? slotSettings, out float value)
        {
            if (slotSettings == null)
            {
                value = 0.0f;
                return false;
            }

            value = slotSettings.WalkSpeedModifier;
            return true;
        }

        protected override IDictionary<string, float>? TryResolveSlotMapByType(SlotSettings? slotSettings)
            => slotSettings?.WalkSpeedModifierByType;

        protected override IDictionary<string, float>? TryResolveSlotMapByGroup(SlotSettings? slotSettings)
            => slotSettings?.WalkSpeedModifierByGroup;

        protected override float TryResolveHardcodedDefault(CarrySlot slot)
            => CarryCodes.Defaults.WalkSpeedModifier.TryGetValue(slot, out var hardcoded) ? hardcoded : 0.0f;
    }
}
