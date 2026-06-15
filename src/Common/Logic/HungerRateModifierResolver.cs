using System;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
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

            if (overrides != null && ModifierOverrideResolver.TryResolveByBlockCode(overrides, block, carryType, slot, out var byCode))
                return Math.Clamp(byCode, MinModifier, MaxModifier);

            if (overrides != null && ModifierOverrideResolver.TryResolveByBlockClass(overrides, block, slot, out var byClass))
                return Math.Clamp(byClass, MinModifier, MaxModifier);

            if (CarryTypeResolver.TryResolveByTypeOverride(stack, slotSettings?.HungerModifierByType, out var byType))
                return Math.Clamp(byType, MinModifier, MaxModifier);

            if (CarryTypeResolver.TryResolveByGroupOverride(behavior, stack, slotSettings?.HungerModifierByGroup, out var byGroup))
                return Math.Clamp(byGroup, MinModifier, MaxModifier);

            if (slotSettings?.HungerModifier.HasValue == true)
                return Math.Clamp(slotSettings.HungerModifier.Value, MinModifier, MaxModifier);

            if (overrides != null && ModifierOverrideResolver.TryGetSlotModifier(overrides.SlotDefaults, slot, out var slotDefault))
                return Math.Clamp(slotDefault, MinModifier, MaxModifier);

            var modifier = CarryCode.Default.HungerRateModifier.TryGetValue(slot, out var hardcoded)
                ? hardcoded
                : 0.0f;

            return Math.Clamp(modifier, MinModifier, MaxModifier);
        }

    }
}
