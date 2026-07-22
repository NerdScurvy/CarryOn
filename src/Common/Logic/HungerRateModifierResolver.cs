using System;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Common;

namespace CarryOn.Common.Logic
{
    internal sealed class HungerRateModifierResolver
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

        public float Resolve(
            ItemStack? stack,
            BlockBehaviorCarryable? behavior,
            SlotSettings? slotSettings,
            CarrySlot slot,
            CarryHungerRateConfig config)
        {
            var overrides = config.ModifierOverrides;
            var block = stack?.Block;
            var carryType = CarryTypeResolver.ResolveCarryType(stack);

            float modifier;

            if (overrides != null && ModifierOverrideResolver.TryResolveByBlockCode(overrides, block, carryType, slot, out var byCode))
                modifier = byCode;
            else if (overrides != null && ModifierOverrideResolver.TryResolveByBlockClass(overrides, block, slot, out var byClass))
                modifier = byClass;
            else if (CarryTypeResolver.TryResolveByTypeOverride(stack, slotSettings?.HungerModifierByType, out var byType))
                modifier = byType;
            else if (CarryTypeResolver.TryResolveByGroupOverride(behavior, stack, slotSettings?.HungerModifierByGroup, out var byGroup))
                modifier = byGroup;
            else if (slotSettings?.HungerModifier.HasValue == true)
                modifier = slotSettings.HungerModifier.Value;
            else if (overrides != null && ModifierOverrideResolver.TryGetSlotModifier(overrides.SlotDefaults, slot, out var slotDefault))
                modifier = slotDefault;
            else
                modifier = CarryCodes.Defaults.HungerRateModifier.TryGetValue(slot, out var hardcoded) ? hardcoded : 0.0f;

            var multiplier = ModifierMultiplierResolver.ResolveMultiplier(block, config.Multipliers, slot);
            return Math.Clamp(modifier * multiplier, MinModifier, MaxModifier);
        }

    }
}
