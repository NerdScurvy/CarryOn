using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Common;

namespace CarryOn.Common.Logic
{
    /// <summary>
    /// Resolves effective walk speed modifiers for carried blocks.
    /// </summary>
    internal sealed class WalkSpeedModifierResolver
    {
        public float Resolve(
            ItemStack? stack,
            BlockBehaviorCarryable? behavior,
            BlockBehaviorCarryable.SlotSettings? slotSettings,
            CarrySlot slot,
            WalkSpeedOverridesConfig? configured)
        {
            var block = stack?.Block;

            if (configured != null)
            {
                if (TryResolveByBlockCode(configured, block, slot, out var byCode))
                {
                    return byCode;
                }

                if (TryResolveByBlockClass(configured, block, slot, out var byClass))
                {
                    return byClass;
                }
            }

            if (TryResolveBySlotTypeOverride(stack, slotSettings, out var byType))
            {
                return byType;
            }

            if (TryResolveBySlotGroupOverride(behavior, stack, slotSettings, out var byGroup))
            {
                return byGroup;
            }

            if (slotSettings != null)
            {
                return slotSettings.WalkSpeedModifier;
            }

            if (TryGetSpeedFromSlotConfig(configured?.SlotDefaults, slot, out var slotDefault))
            {
                return slotDefault;
            }

            return BlockBehaviorCarryable.DefaultWalkSpeed.TryGetValue(slot, out var hardcoded)
                ? hardcoded
                : 0.0f;
        }

        private static bool TryResolveByBlockCode(
            WalkSpeedOverridesConfig configured,
            Block? block,
            CarrySlot slot,
            out float speed)
        {
            speed = 0.0f;

            var map = configured?.ByBlockCode;
            var blockCode = block?.Code?.ToString();
            if (map == null || map.Count == 0 || string.IsNullOrWhiteSpace(blockCode))
            {
                return false;
            }

            if (JsonHelper.TryGetValueTrimmedIgnoreCase(map, blockCode, out var exact)
                && exact != null
                && TryGetSpeedFromSlotConfig(exact, slot, out speed))
            {
                return true;
            }

            if (JsonHelper.TryGetTrailingWildcardValue(
                    map,
                    blockCode,
                    out CarrySlotSpeedConfig? wildcardConfig,
                    static value => value is not null)
                && wildcardConfig is not null
                && TryGetSpeedFromSlotConfig(wildcardConfig, slot, out speed))
            {
                return true;
            }

            return false;
        }

        private static bool TryResolveByBlockClass(
            WalkSpeedOverridesConfig? configured,
            Block? block,
            CarrySlot slot,
            out float speed)
        {
            speed = 0.0f;

            var map = configured?.ByBlockClass;
            var blockClass = block?.Class;
            if (map == null || map.Count == 0 || string.IsNullOrWhiteSpace(blockClass))
            {
                return false;
            }

            if (!JsonHelper.TryGetValueTrimmedIgnoreCase(map, blockClass, out var slotConfig) || slotConfig == null)
            {
                return false;
            }

            return TryGetSpeedFromSlotConfig(slotConfig, slot, out speed);
        }

        private static bool TryGetSpeedFromSlotConfig(CarrySlotSpeedConfig? slotConfig, CarrySlot slot, out float speed)
        {
            speed = 0.0f;

            if (slotConfig == null)
            {
                return false;
            }

            switch (slot)
            {
                case CarrySlot.Hands:
                    if (slotConfig.Hands.HasValue)
                    {
                        speed = slotConfig.Hands.Value;
                        return true;
                    }
                    break;

                case CarrySlot.Back:
                    if (slotConfig.Back.HasValue)
                    {
                        speed = slotConfig.Back.Value;
                        return true;
                    }
                    break;
            }

            return false;
        }

        private static bool TryResolveBySlotTypeOverride(
            ItemStack? stack,
            BlockBehaviorCarryable.SlotSettings? slotSettings,
            out float speed)
        {
            speed = 0.0f;

            var typeMap = slotSettings?.WalkSpeedModifierByType;
            if (typeMap == null || typeMap.Count == 0)
            {
                return false;
            }

            var type = ResolveCarryType(stack);
            if (string.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            return TryGetSlotSpeed(typeMap, type, out speed);
        }

        private static bool TryResolveBySlotGroupOverride(
            BlockBehaviorCarryable? behavior,
            ItemStack? stack,
            BlockBehaviorCarryable.SlotSettings? slotSettings,
            out float speed)
        {
            speed = 0.0f;

            var groupMap = slotSettings?.WalkSpeedModifierByGroup;
            if (groupMap == null || groupMap.Count == 0)
            {
                return false;
            }

            var type = ResolveCarryType(stack);
            if (string.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            if (behavior?.TypeGroup == null || behavior.TypeGroup.Count == 0)
            {
                return false;
            }

            if (!behavior.TypeGroup.TryGetValue(type, out var group) || string.IsNullOrWhiteSpace(group))
            {
                return false;
            }

            return TryGetSlotSpeed(groupMap, group, out speed);
        }

        private static string? ResolveCarryType(ItemStack? stack)
        {
            var fromAttributes = stack?.Attributes?.GetString("type");
            if (!string.IsNullOrWhiteSpace(fromAttributes))
            {
                return fromAttributes;
            }

            var variant = stack?.Block?.Variant;
            if (variant != null && variant.TryGetValue("type", out var fromVariant) && !string.IsNullOrWhiteSpace(fromVariant))
            {
                return fromVariant;
            }

            return stack?.Block?.Attributes?["type"]?.AsString();
        }

        private static bool TryGetSlotSpeed(IDictionary<string, float> map, string key, out float speed)
        {
            speed = 0.0f;

            if (map == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (JsonHelper.TryGetValueTrimmedIgnoreCase(map, key, out speed))
            {
                return true;
            }

            // Support trailing-* prefix wildcards (for example: "owl*") with longest-prefix wins.
            return JsonHelper.TryGetTrailingWildcardValue(map, key, out speed);
        }
    }
}
