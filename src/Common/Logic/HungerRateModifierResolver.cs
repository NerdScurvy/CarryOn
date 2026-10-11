using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;

namespace CarryOn.Common.Logic
{
    internal sealed class HungerRateModifierResolver : ModifierResolver<CarryHungerRateConfig>
    {
        private const float MinModifier = 0.0f;
        private const float MaxModifier = 9.0f;

        public bool IsEnabled(CarrySlot slot, CarryHungerRateConfig config)
        {
            return slot switch
            {
                CarrySlot.Hands => config.HandsEnabled,
                CarrySlot.Back => config.BackEnabled,
                _ => false
            };
        }

        protected override float ClampResolved(float value)
            => Math.Clamp(value, MinModifier, MaxModifier);

        protected override bool TryResolveSlotValue(SlotSettings? slotSettings, out float value)
        {
            if (slotSettings?.HungerModifier.HasValue == true)
            {
                value = slotSettings.HungerModifier.Value;
                return true;
            }

            value = 0.0f;
            return false;
        }

        protected override IDictionary<string, float>? TryResolveSlotMapByType(SlotSettings? slotSettings)
            => slotSettings?.HungerModifierByType;

        protected override IDictionary<string, float>? TryResolveSlotMapByGroup(SlotSettings? slotSettings)
            => slotSettings?.HungerModifierByGroup;

        protected override float TryResolveHardcodedDefault(CarrySlot slot)
            => CarryCodes.Defaults.HungerRateModifier.TryGetValue(slot, out var hardcoded) ? hardcoded : 0.0f;
    }
}
