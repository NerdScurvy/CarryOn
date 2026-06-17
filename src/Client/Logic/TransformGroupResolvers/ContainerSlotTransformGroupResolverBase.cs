using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Client.Logic.TransformGroupResolvers
{
    public class ContainerSlotTransformGroupResolverBase : IAttachmentTransformGroupResolver
    {
        public virtual string ResolverCode => "container-slot";

        public virtual bool TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup, out AttachmentResolveResult? result)
        {
            result = null;

            if (api?.World == null || carried?.Block == null || string.IsNullOrEmpty(baseGroup))
                return false;

            var containerSlots = BlockUtils.GetContainerSlots(carried);
            if (containerSlots == null || containerSlots.Count == 0)
                return false;

            var resolveResult = new AttachmentResolveResult();
            AddSlotCandidates(api, containerSlots, baseGroup, resolveResult);

            if (resolveResult.Candidates.Count == 0)
                return false;

            result = resolveResult;
            return true;
        }

        public virtual string? GetCacheSignature(ICoreAPI api, CarriedBlock carried, string baseGroup)
        {
            var slots = BlockUtils.GetContainerSlots(carried);
            if (slots == null || slots.Count == 0)
                return "slots=none";

            var keys = slots.Keys?.ToList() ?? new List<string>();
            keys.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder(128);
            foreach (var key in keys)
            {
                var stack = slots.GetItemstack(key);
                if (stack == null)
                {
                    sb.Append(key).Append(":empty;");
                    continue;
                }

                sb.Append(key).Append(':')
                    .Append((int)stack.Class).Append(':')
                    .Append(stack.Id).Append(':')
                    .Append(stack.StackSize).Append(';');
            }

            return sb.ToString();
        }

        protected virtual void AddSlotCandidates(ICoreAPI api, TreeAttribute containerSlots, string baseGroup, AttachmentResolveResult result)
        {
            foreach (var cSlot in containerSlots)
            {
                var itemStack = containerSlots.GetItemstack(cSlot.Key);
                if (itemStack == null)
                    continue;

                var candidate = new CarriedGroupCandidateSet { SourceSlotKey = cSlot.Key };
                candidate.Groups.Add(baseGroup + "-slot" + cSlot.Key);

                if (AssetResolutionHelper.TryResolveFallbackAsset(api, itemStack, out var assetType, out var assetName))
                {
                    candidate.AssetTypeIfUnset = assetType;
                    candidate.AssetNameIfUnset = assetName;
                }

                result.Candidates.Add(candidate);
            }
        }
    }
}
