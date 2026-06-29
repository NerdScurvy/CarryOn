using System;
using System.Linq;
using System.Text;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Client.Logic.TransformGroupResolvers
{
    public class DataAttributeTransformGroupResolver : IAttachmentTransformGroupResolver
    {
        public string ResolverCode => "data-attributes";

        public bool TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup, out AttachmentResolveResult? result)
        {
            result = null;

            if (api?.World == null || carried?.Block == null || string.IsNullOrEmpty(baseGroup))
                return false;

            var carryBehavior = carried.GetCarryableBehavior();
            if (carryBehavior == null)
                return false;

            var attrNames = carryBehavior.DataAttributes;
            if (attrNames == null || attrNames.Length == 0)
                return false;

            var beData = carried.BlockEntityData;
            if (beData == null)
                return false;

            var groupPrefix = carryBehavior.DataAttributesPrefix ?? baseGroup;
            var resolveResult = new AttachmentResolveResult();

            foreach (var attrName in attrNames)
            {
                if (string.IsNullOrEmpty(attrName))
                    continue;

                var attr = beData[attrName];
                if (attr is not ItemstackAttribute itemAttr)
                    continue;

                var stack = itemAttr.GetValue() as ItemStack;
                if (stack == null)
                    continue;

                var candidate = new CarriedGroupCandidateSet
                {
                    SourceSlotKey = attrName
                };

                candidate.Groups.Add(groupPrefix + "-" + attrName);

                if (AssetResolutionHelper.TryResolveFallbackAsset(api, stack, out var assetType, out var assetName))
                {
                    candidate.AssetTypeIfUnset = assetType;
                    candidate.AssetNameIfUnset = assetName;
                }

                resolveResult.Candidates.Add(candidate);
            }

            if (resolveResult.Candidates.Count == 0)
                return false;

            result = resolveResult;
            return true;
        }

        public string? GetCacheSignature(ICoreAPI api, CarriedBlock carried, string baseGroup)
        {
            var carryBehavior = carried.GetCarryableBehavior();
            var attrNames = carryBehavior?.DataAttributes;
            if (attrNames == null || attrNames.Length == 0)
                return "data=none";

            var beData = carried.BlockEntityData;
            if (beData == null)
                return "data=none";

            var sorted = attrNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var sb = new StringBuilder(128);

            foreach (var attrName in sorted)
            {
                sb.Append(attrName).Append(':');

                var attr = beData[attrName];
                if (attr is not ItemstackAttribute itemAttr)
                {
                    sb.Append("empty;");
                    continue;
                }

                var stack = itemAttr.GetValue() as ItemStack;
                if (stack == null)
                {
                    sb.Append("empty;");
                    continue;
                }

                sb.Append((int)stack.Class).Append(':')
                    .Append(stack.Id).Append(':')
                    .Append(stack.StackSize).Append(';');
            }

            return sb.ToString();
        }
    }
}
