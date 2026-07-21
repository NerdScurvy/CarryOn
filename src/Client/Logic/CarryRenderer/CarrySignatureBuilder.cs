using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal static class CarrySignatureBuilder
    {
        internal static string BuildRenderInfoVariantSignature(CarriedBlock carried, TreeAttribute? containerSlots, IReadOnlyList<EffectiveTransformSetting> effectiveSettings, IWorldAccessor world, string? rootRenderVariant = null)
        {
            var sb = new StringBuilder(160);
            var be = carried?.BlockEntityData;

            var center = be?.GetBool("haveCenterPlacement", false) == true ? "1" : "0";
            sb.Append("center=").Append(center);

            for (int i = 0; i < 4; i++)
            {
                var key = "rotation" + i;
                sb.Append("|r").Append(i).Append('=');

                if (be?[key] == null)
                {
                    sb.Append("na");
                }
                else
                {
                    sb.Append(be.GetFloat(key).ToString("R", CultureInfo.InvariantCulture));
                }
            }

            if (containerSlots != null)
            {
                sb.Append("|slots=");
                var keys = containerSlots.Keys.OrderBy(k => k, StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    var stack = containerSlots.GetItemstack(key);
                    var code = stack?.Collectible?.Code?.ToString() ?? "null";
                    sb.Append(key).Append(':').Append(code).Append(',');
                }
            }

            if (effectiveSettings != null && effectiveSettings.Count > 0)
            {
                sb.Append("|bepaths=");
                for (var i = 0; i < effectiveSettings.Count; i++)
                {
                    AppendBeStackPathSignature(sb, be, world, effectiveSettings[i]?.Setting?.DisableIfItemStackPath, "disable");
                    AppendBeStackPathSignature(sb, be, world, effectiveSettings[i]?.Setting?.BlockEntityDataItemStackPath, "render");
                }
            }

            if (carried?.OriginalBlockCode != null)
            {
                sb.Append("|origCode=").Append(carried.OriginalBlockCode.ToString());
            }

            if (carried?.OriginalMeshAngle.HasValue == true)
            {
                sb.Append("|origMeshAngle=").Append(carried.OriginalMeshAngle.Value.ToString("R", CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(rootRenderVariant))
            {
                sb.Append("|renderVariant=").Append(rootRenderVariant);
            }

            if (carried?.HasAttachedBlocks == true && carried.AttachedBlocks != null)
            {
                sb.Append("|attached=").Append(carried.AttachedBlocks.Count);
                foreach (var attached in carried.AttachedBlocks)
                {
                    if (attached == null) continue;
                    sb.Append('|').Append(attached.ItemStack?.Collectible?.Code?.ToString() ?? "null");
                    sb.Append(',').Append(attached.RelativeOffset.X).Append(',').Append(attached.RelativeOffset.Y).Append(',').Append(attached.RelativeOffset.Z);
                    sb.Append(',').Append(attached.OriginalLocalFace?.Code ?? "null");
                }
            }

            return sb.ToString();
        }

        internal static string BuildTransformPlanSignature(
            CarriedBlock? carried,
            string transformsGroupBase,
            string? matchedResolverCode,
            string? resolverCacheSignature)
        {
            var sb = new StringBuilder(192);
            sb.Append(carried?.Block?.Code?.ToString() ?? "noblock").Append('|');

            var variantType = carried?.BlockEntityData?.GetString("type", "none") ?? "none";
            sb.Append(variantType).Append('|');
            sb.Append(transformsGroupBase ?? CarryCodes.DefaultTransformGroup).Append('|');
            sb.Append((int?)carried?.Slot ?? 0).Append('|');
            sb.Append(carried?.ItemStack?.Collectible?.Code?.ToString() ?? "nostack").Append('|');

            if (!string.IsNullOrEmpty(matchedResolverCode))
            {
                sb.Append("resolver=").Append(matchedResolverCode).Append('|');
            }

            if (!string.IsNullOrEmpty(resolverCacheSignature))
            {
                sb.Append("resolversig=").Append(resolverCacheSignature);
            }
            else
            {
                sb.Append("resolversig=none");
            }

            return sb.ToString();
        }

        private static void AppendBeStackPathSignature(StringBuilder sb, ITreeAttribute? be, IWorldAccessor world, string? path, string kind)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var stack = CarryRenderHelpers.TryGetItemStackByPath(be, path, world);
            sb.Append(kind).Append(':').Append(path).Append(':').Append(BuildItemStackContentSignature(stack)).Append(',');
        }

        private static string BuildItemStackContentSignature(ItemStack? stack)
        {
            if (stack == null)
            {
                return "null";
            }

            var raw = new StringBuilder(256)
                .Append((int)stack.Class).Append('|')
                .Append(stack.Id).Append('|')
                .Append(stack.StackSize).Append('|')
                .Append(stack.Collectible?.Code?.ToString() ?? "nocode");

            if (stack.Attributes != null)
            {
                if (stack.Attributes is TreeAttribute treeAttributes)
                {
                    raw.Append("|a:").Append(SerializeTreeAttribute(treeAttributes));
                }
 
            }

            return ComputeFnv1a64Hex(raw.ToString());
        }

        private static string SerializeTreeAttribute(TreeAttribute tree, int depth = 0)
        {
            if (tree == null) return "null";
            if (depth > 16) return "<max-depth>";

            var keys = tree.Keys?.OrderBy(k => k, StringComparer.Ordinal) ?? Enumerable.Empty<string>();
            var sb = new StringBuilder(256);
            sb.Append('{');

            foreach (var key in keys)
            {
                sb.Append(key).Append('=');
                sb.Append(SerializeAttribute(tree[key], depth + 1));
                sb.Append(';');
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static string SerializeAttribute(IAttribute attribute, int depth)
        {
            if (attribute == null) return "null";
            if (depth > 16) return "<max-depth>";

            if (attribute is TreeAttribute nestedTree)
            {
                return SerializeTreeAttribute(nestedTree, depth + 1);
            }

            if (attribute is ItemstackAttribute itemstackAttribute)
            {
                var nestedStack = itemstackAttribute.GetValue() as ItemStack;
                return "itemstack(" + BuildItemStackContentSignature(nestedStack) + ")";
            }

            var value = attribute.GetValue();
            if (value == null)
            {
                return attribute.GetType().Name + "(null)";
            }

            if (value is byte[] bytes)
            {
                return attribute.GetType().Name + "(b64:" + Convert.ToBase64String(bytes) + ")";
            }

            if (value is Array arr && value is not char[])
            {
                var values = new System.Collections.Generic.List<string>(arr.Length);
                foreach (var element in arr)
                {
                    values.Add(element?.ToString() ?? "null");
                }

                return attribute.GetType().Name + "([" + string.Join(",", values) + "])";
            }

            return attribute.GetType().Name + "(" + value + ")";
        }

        private static string ComputeFnv1a64Hex(string text)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offset;
            for (var i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= prime;
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
