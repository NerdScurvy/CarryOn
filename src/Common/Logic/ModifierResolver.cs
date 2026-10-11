using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using Vintagestory.API.Common;

namespace CarryOn.Common.Logic
{
    /// <summary>
    /// Shared modifier resolution pipeline for walk speed and hunger rate.
    /// Resolution order: block-code override, block-class override, per-type map,
    /// per-group map, slot default, configured slot default, hardcoded default.
    /// </summary>
    internal abstract class ModifierResolver<TConfig>
        where TConfig : CarryModifierConfigBase
    {
        public float ResolveBase(
            ItemStack? stack,
            BlockBehaviorCarryable? behavior,
            SlotSettings? slotSettings,
            CarrySlot slot,
            TConfig config)
        {
            var overrides = config.ModifierOverrides;
            var block = stack?.Block;
            var carryType = CarryTypeResolver.ResolveCarryType(stack);

            if (overrides != null)
            {
                if (ModifierOverrideResolver.TryResolveByBlockCode(overrides, block, carryType, slot, out var byCode))
                    return byCode;

                if (ModifierOverrideResolver.TryResolveByBlockClass(overrides, block, slot, out var byClass))
                    return byClass;
            }

            if (CarryTypeResolver.TryResolveByTypeOverride(stack, TryResolveSlotMapByType(slotSettings), out var byType))
                return byType;

            if (CarryTypeResolver.TryResolveByGroupOverride(behavior, stack, TryResolveSlotMapByGroup(slotSettings), out var byGroup))
                return byGroup;

            if (TryResolveSlotValue(slotSettings, out var slotValue))
                return slotValue;

            if (overrides != null && ModifierOverrideResolver.TryGetSlotModifier(overrides.SlotDefaults, slot, out var slotDefault))
                return slotDefault;

            return TryResolveHardcodedDefault(slot);
        }

        public float Resolve(
            ItemStack? stack,
            BlockBehaviorCarryable? behavior,
            SlotSettings? slotSettings,
            CarrySlot slot,
            TConfig config)
        {
            var block = stack?.Block;
            var multiplier = ModifierMultiplierResolver.ResolveMultiplier(block, config.Multipliers, slot);
            return ClampResolved(ResolveBase(stack, behavior, slotSettings, slot, config) * multiplier);
        }

        /// <summary>Applies the resolver-specific output range clamp. No-op by default.</summary>
        protected virtual float ClampResolved(float value) => value;

        /// <summary>Reads the slot-level modifier value from the behavior's slot settings.</summary>
        protected abstract bool TryResolveSlotValue(SlotSettings? slotSettings, out float value);

        /// <summary>Gets the per-type modifier map from the behavior's slot settings.</summary>
        protected abstract IDictionary<string, float>? TryResolveSlotMapByType(SlotSettings? slotSettings);

        /// <summary>Gets the per-group modifier map from the behavior's slot settings.</summary>
        protected abstract IDictionary<string, float>? TryResolveSlotMapByGroup(SlotSettings? slotSettings);

        /// <summary>Resolves the built-in default modifier for the slot.</summary>
        protected abstract float TryResolveHardcodedDefault(CarrySlot slot);
    }
}
