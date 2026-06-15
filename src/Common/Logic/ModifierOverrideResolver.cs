using System;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;

namespace CarryOn.Common.Logic
{
    internal static class ModifierOverrideResolver
    {
        public static bool TryResolveByBlockCode(
            ModifierOverridesConfig? configured,
            Block? block,
            string? carryType,
            CarrySlot slot,
            out float value)
        {
            value = 0.0f;

            var map = configured?.ByBlockCode;
            var blockCode = block?.Code?.ToString();
            if (map == null || map.Count == 0 || string.IsNullOrWhiteSpace(blockCode))
                return false;

            SlotModifierConfig? bestConfig = null;
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
                return TryGetSlotModifier(bestConfig, slot, out value);

            return false;
        }

        public static bool TryResolveByBlockClass(
            ModifierOverridesConfig? configured,
            Block? block,
            CarrySlot slot,
            out float value)
        {
            value = 0.0f;

            var map = configured?.ByBlockClass;
            var blockClass = block?.Class;
            if (map == null || map.Count == 0 || string.IsNullOrWhiteSpace(blockClass))
                return false;

            if (!JsonHelper.TryGetValueTrimmedIgnoreCase(map, blockClass, out var slotConfig) || slotConfig == null)
                return false;

            return TryGetSlotModifier(slotConfig, slot, out value);
        }

        public static bool TryGetSlotModifier(SlotModifierConfig? slotConfig, CarrySlot slot, out float value)
        {
            value = 0.0f;

            if (slotConfig == null)
                return false;

            switch (slot)
            {
                case CarrySlot.Hands:
                    if (slotConfig.Hands.HasValue)
                    {
                        value = slotConfig.Hands.Value;
                        return true;
                    }
                    break;

                case CarrySlot.Back:
                    if (slotConfig.Back.HasValue)
                    {
                        value = slotConfig.Back.Value;
                        return true;
                    }
                    break;
            }

            return false;
        }
    }
}
