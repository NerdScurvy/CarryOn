using System;
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
                var carryType = ResolveCarryType(stack);

                if (TryResolveByBlockCode(configured, block, carryType, slot, out var byCode))
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
            string? carryType,
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

            CarrySlotSpeedConfig? bestConfig = null;
            var bestBlockScore = -1;
            var bestTypeScore = -1;

            foreach (var entry in map)
            {
                if (entry.Value == null) continue;

                var key = entry.Key?.Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;

                var pipeIndex = key.IndexOf('|');
                string blockPattern;
                string? typePattern;
                if (pipeIndex >= 0)
                {
                    blockPattern = key.Substring(0, pipeIndex);
                    typePattern = key.Substring(pipeIndex + 1);
                }
                else
                {
                    blockPattern = key;
                    typePattern = null;
                }

                if (string.IsNullOrWhiteSpace(blockPattern)) continue;

                int blockScore;
                if (blockPattern.EndsWith("*", StringComparison.Ordinal))
                {
                    var prefix = blockPattern.Substring(0, blockPattern.Length - 1);
                    if (!blockCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    blockScore = prefix.Length;
                }
                else
                {
                    if (!string.Equals(blockCode, blockPattern, StringComparison.OrdinalIgnoreCase))
                        continue;
                    blockScore = int.MaxValue;
                }

                int typeScore;
                if (typePattern != null)
                {
                    if (string.IsNullOrWhiteSpace(carryType))
                        continue;

                    if (typePattern.EndsWith("*", StringComparison.Ordinal))
                    {
                        var typePrefix = typePattern.Substring(0, typePattern.Length - 1);
                        if (!carryType.StartsWith(typePrefix, StringComparison.OrdinalIgnoreCase))
                            continue;
                        typeScore = typePrefix.Length;
                    }
                    else if (string.IsNullOrEmpty(typePattern))
                    {
                        if (!string.IsNullOrEmpty(carryType))
                            continue;
                        typeScore = 0;
                    }
                    else
                    {
                        if (!string.Equals(carryType, typePattern, StringComparison.OrdinalIgnoreCase))
                            continue;
                        typeScore = int.MaxValue;
                    }
                }
                else
                {
                    typeScore = -1;
                }

                if (blockScore > bestBlockScore
                    || (blockScore == bestBlockScore && typeScore > bestTypeScore))
                {
                    bestConfig = entry.Value;
                    bestBlockScore = blockScore;
                    bestTypeScore = typeScore;
                }
            }

            if (bestConfig != null)
            {
                return TryGetSpeedFromSlotConfig(bestConfig, slot, out speed);
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
