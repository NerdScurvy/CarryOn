using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using Vintagestory.API.Common;

namespace CarryOn.Common.Logic
{
    internal sealed class WalkSpeedModifierResolver
    {
        public bool IsEnabled(CarrySlot slot, WalkSpeedModifierConfig config)
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
            ModifierOverridesConfig? configured)
        {
            var block = stack?.Block;
            var carryType = CarryTypeResolver.ResolveCarryType(stack);

            if (configured != null)
            {
                if (ModifierOverrideResolver.TryResolveByBlockCode(configured, block, carryType, slot, out var byCode))
                    return byCode;

                if (ModifierOverrideResolver.TryResolveByBlockClass(configured, block, slot, out var byClass))
                    return byClass;
            }

            if (CarryTypeResolver.TryResolveByTypeOverride(stack, slotSettings?.WalkSpeedModifierByType, out var byType))
                return byType;

            if (CarryTypeResolver.TryResolveByGroupOverride(behavior, stack, slotSettings?.WalkSpeedModifierByGroup, out var byGroup))
                return byGroup;

            if (slotSettings != null)
                return slotSettings.WalkSpeedModifier;

            if (ModifierOverrideResolver.TryGetSlotModifier(configured?.SlotDefaults, slot, out var slotDefault))
                return slotDefault;

            return CarryCode.Default.WalkSpeedModifier.TryGetValue(slot, out var hardcoded)
                ? hardcoded
                : 0.0f;
        }
    }
}
