using System.Collections.Generic;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace CarryOn.Common.Logic
{
    internal static class CarryTypeResolver
    {
        public static string? ResolveCarryType(ItemStack? stack)
        {
            var fromAttributes = stack?.Attributes?.GetString(CarryCodes.AttributeKeys.Type);
            if (!string.IsNullOrWhiteSpace(fromAttributes))
                return fromAttributes;

            var variant = stack?.Block?.Variant;
            if (variant != null && variant.TryGetValue("type", out var fromVariant) && !string.IsNullOrWhiteSpace(fromVariant))
                return fromVariant;

            return stack?.Block?.Attributes?["type"]?.AsString();
        }

        public static List<string>? EnumerateFallbackCarryTypes(ItemStack? stack, BlockBehaviorCarryable? behavior)
        {
            if (behavior?.TypeGroup?.Count > 0)
            {
                var results = new List<string>();
                foreach (var type in behavior.TypeGroup.Keys)
                {
                    if (string.IsNullOrWhiteSpace(type)) continue;
                    results.Add(type);
                }
                return results;
            }

            var typesArray = stack?.Block?.Attributes?["types"]?.AsArray();
            if (typesArray?.Length > 0)
            {
                var results = new List<string>();
                foreach (var entry in typesArray)
                {
                    var tokenType = entry.Token?.Type;
                    string? type = tokenType switch
                    {
                        JTokenType.String => entry.AsString(),
                        JTokenType.Object => entry["code"]?.AsString(),
                        _ => null
                    };
                    if (string.IsNullOrWhiteSpace(type)) continue;
                    results.Add(type);
                }
                return results;
            }

            return null;
        }

        public static bool TryGetValue(IDictionary<string, float>? map, string key, out float value)
        {
            value = 0f;

            if (map == null || string.IsNullOrWhiteSpace(key))
                return false;

            if (WildcardMatcher.TryGetValueTrimmedIgnoreCase(map, key, out value))
                return true;

            return WildcardMatcher.TryGetTrailingWildcardValue(map, key, out value);
        }

        public static bool TryResolveByTypeOverride(
            ItemStack? stack,
            IDictionary<string, float>? typeMap,
            out float modifier)
        {
            modifier = 0f;

            if (typeMap == null || typeMap.Count == 0)
                return false;

            var type = ResolveCarryType(stack);
            if (string.IsNullOrWhiteSpace(type))
                return false;

            return TryGetValue(typeMap, type, out modifier);
        }

        public static bool TryResolveByGroupOverride(
            BlockBehaviorCarryable? behavior,
            ItemStack? stack,
            IDictionary<string, float>? groupMap,
            out float modifier)
        {
            modifier = 0f;

            if (groupMap == null || groupMap.Count == 0)
                return false;

            var type = ResolveCarryType(stack);
            if (string.IsNullOrWhiteSpace(type))
                return false;

            if (behavior?.TypeGroup == null || behavior.TypeGroup.Count == 0)
                return false;

            if (!behavior.TypeGroup.TryGetValue(type, out var group) || string.IsNullOrWhiteSpace(group))
                return false;

            return TryGetValue(groupMap, group, out modifier);
        }
    }
}
