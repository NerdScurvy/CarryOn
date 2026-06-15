using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;

namespace CarryOn.Common.Logic
{
    internal sealed class HungerDrainRateResolver
    {
        public float Resolve(
            CarrySlot slot,
            CarryHungerRateConfig config,
            SlotSettings? slotSettings = null,
            ItemStack? stack = null,
            BlockBehaviorCarryable? behavior = null)
        {
            // 1) Per-type override from JSON (e.g. "hungerModifierByBlockType": { "aged": 0.3 })
            if (TryResolveByType(stack, slotSettings, out var byType))
                return Math.Clamp(byType, 0.0f, 9.0f);

            // 2) Per-group override from JSON (e.g. "hungerModifierByGroup": { "compact": 0.1 })
            if (TryResolveByGroup(behavior, stack, slotSettings, out var byGroup))
                return Math.Clamp(byGroup, 0.0f, 9.0f);

            // 3) Plain per-block override from JSON (e.g. "hungerModifier": 0.1)
            if (slotSettings?.HungerModifier.HasValue == true)
                return Math.Clamp(slotSettings.HungerModifier.Value, 0.0f, 9.0f);

            // 4) Fall back to config defaults
            var modifier = slot switch
            {
                CarrySlot.Hands => config.DefaultHandsModifier,
                CarrySlot.Back => config.DefaultBackModifier,
                _ => 0.0f
            };

            return Math.Clamp(modifier, 0.0f, 9.0f);
        }

        public bool IsEnabled(CarrySlot slot, CarryHungerRateConfig config)
        {
            return slot switch
            {
                CarrySlot.Hands => config.HandsEnabled,
                CarrySlot.Back => config.BackEnabled,
                _ => false
            };
        }

        private static bool TryResolveByType(
            ItemStack? stack,
            SlotSettings? slotSettings,
            out float modifier)
        {
            modifier = 0f;

            var typeMap = slotSettings?.HungerModifierByType;
            if (typeMap == null || typeMap.Count == 0)
                return false;

            var type = ResolveCarryType(stack);
            if (string.IsNullOrWhiteSpace(type))
                return false;

            return TryGetModifier(typeMap, type, out modifier);
        }

        private static bool TryResolveByGroup(
            BlockBehaviorCarryable? behavior,
            ItemStack? stack,
            SlotSettings? slotSettings,
            out float modifier)
        {
            modifier = 0f;

            var groupMap = slotSettings?.HungerModifierByGroup;
            if (groupMap == null || groupMap.Count == 0)
                return false;

            var type = ResolveCarryType(stack);
            if (string.IsNullOrWhiteSpace(type))
                return false;

            if (behavior?.TypeGroup == null || behavior.TypeGroup.Count == 0)
                return false;

            if (!behavior.TypeGroup.TryGetValue(type, out var group) || string.IsNullOrWhiteSpace(group))
                return false;

            return TryGetModifier(groupMap, group, out modifier);
        }

        private static string? ResolveCarryType(ItemStack? stack)
        {
            var fromAttributes = stack?.Attributes?.GetString("type");
            if (!string.IsNullOrWhiteSpace(fromAttributes))
                return fromAttributes;

            var variant = stack?.Block?.Variant;
            if (variant != null && variant.TryGetValue("type", out var fromVariant) && !string.IsNullOrWhiteSpace(fromVariant))
                return fromVariant;

            return stack?.Block?.Attributes?["type"]?.AsString();
        }

        private static bool TryGetModifier(IDictionary<string, float> map, string key, out float modifier)
        {
            modifier = 0f;

            if (map == null || string.IsNullOrWhiteSpace(key))
                return false;

            if (JsonHelper.TryGetValueTrimmedIgnoreCase(map, key, out modifier))
                return true;

            // Support trailing-* prefix wildcards (for example: "owl*") with longest-prefix wins.
            return JsonHelper.TryGetTrailingWildcardValue(map, key, out modifier);
        }
    }
}
