using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Common;

namespace CarryOn.Common.Logic
{
    internal sealed class WalkSpeedModifierResolver
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

        public float Resolve(
            ItemStack? stack,
            BlockBehaviorCarryable? behavior,
            SlotSettings? slotSettings,
            CarrySlot slot,
            CarryWalkSpeedConfig? config)
        {
            var block = stack?.Block;
            var carryType = CarryTypeResolver.ResolveCarryType(stack);
            var overrides = config?.ModifierOverrides;

            if (overrides != null)
            {
                if (ModifierOverrideResolver.TryResolveByBlockCode(overrides, block, carryType, slot, out var byCode))
                    return ApplyMultiplier(byCode, block, config, slot);

                if (ModifierOverrideResolver.TryResolveByBlockClass(overrides, block, slot, out var byClass))
                    return ApplyMultiplier(byClass, block, config, slot);
            }

            if (CarryTypeResolver.TryResolveByTypeOverride(stack, slotSettings?.WalkSpeedModifierByType, out var byType))
                return ApplyMultiplier(byType, block, config, slot);

            if (CarryTypeResolver.TryResolveByGroupOverride(behavior, stack, slotSettings?.WalkSpeedModifierByGroup, out var byGroup))
                return ApplyMultiplier(byGroup, block, config, slot);

            if (slotSettings != null)
                return ApplyMultiplier(slotSettings.WalkSpeedModifier, block, config, slot);

            if (ModifierOverrideResolver.TryGetSlotModifier(overrides?.SlotDefaults, slot, out var slotDefault))
                return ApplyMultiplier(slotDefault, block, config, slot);

            var hardcoded = CarryCodes.Defaults.WalkSpeedModifier.TryGetValue(slot, out var val)
                ? val
                : 0.0f;

            return ApplyMultiplier(hardcoded, block, config, slot);
        }

        private static float ApplyMultiplier(float modifier, Block? block, CarryWalkSpeedConfig? config, CarrySlot slot)
        {
            var multiplier = ModifierMultiplierResolver.ResolveMultiplier(block, config?.Multipliers, slot);
            return modifier * multiplier;
        }
    }
}
