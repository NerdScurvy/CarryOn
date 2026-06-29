using System.Collections.Generic;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Common;

namespace CarryOn.Common.Logic
{
    internal static class CarryTypeResolver
    {
        public static string? ResolveCarryType(ItemStack? stack)
        {
            var fromAttributes = stack?.Attributes?.GetString("type");
            if (!string.IsNullOrWhiteSpace(fromAttributes))
                return fromAttributes;

            var variant = stack?.Block?.Variant;
            if (variant != null && variant.TryGetValue("type", out var fromVariant) && !string.IsNullOrWhiteSpace(fromVariant))
                return fromVariant;

            return stack?.Block?.Attributes?["type"]?.AsString();
        }

        public static bool TryGetValue(IDictionary<string, float>? map, string key, out float value)
        {
            value = 0f;

            if (map == null || string.IsNullOrWhiteSpace(key))
                return false;

            if (JsonHelper.TryGetValueTrimmedIgnoreCase(map, key, out value))
                return true;

            return JsonHelper.TryGetTrailingWildcardValue(map, key, out value);
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
