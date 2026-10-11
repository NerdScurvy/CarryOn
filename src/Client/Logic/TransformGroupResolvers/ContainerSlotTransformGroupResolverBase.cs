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

            if (!CanResolve(carried))
                return false;

            var containerSlots = BlockUtils.GetContainerSlots(carried);
            if (containerSlots == null || containerSlots.Count == 0)
                return false;

            var candidates = new List<CarriedGroupCandidateSet>();
            AddSlotCandidates(api, carried, containerSlots, baseGroup, candidates);

            if (candidates.Count == 0)
                return false;

            result = new AttachmentResolveResult(candidates);
            return true;
        }

        public virtual string? GetCacheSignature(ICoreAPI api, CarriedBlock carried, string baseGroup)
        {
            var slots = BlockUtils.GetContainerSlots(carried);

            if (slots == null || slots.Count == 0)
                return BuildSignaturePrefix(api, carried, hasSlots: false);

            var sb = new StringBuilder(128);
            sb.Append(BuildSignaturePrefix(api, carried, hasSlots: true));

            var keys = slots.Keys?.ToList() ?? new List<string>();
            keys.Sort(StringComparer.Ordinal);

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
                    .Append(stack.Id);

                AppendSlotSignatureDetail(api, stack, sb);
                sb.Append(';');
            }

            return sb.ToString();
        }

        protected virtual bool CanResolve(CarriedBlock carried)
        {
            return true;
        }

        protected virtual string BuildSignaturePrefix(ICoreAPI api, CarriedBlock carried, bool hasSlots)
        {
            return hasSlots ? string.Empty : "slots=none";
        }

        protected virtual void AppendSlotSignatureDetail(ICoreAPI api, ItemStack stack, StringBuilder sb)
        {
        }

        protected virtual void AddSlotCandidates(ICoreAPI api, CarriedBlock carried, TreeAttribute containerSlots, string baseGroup, List<CarriedGroupCandidateSet> candidates)
        {
            foreach (var cSlot in containerSlots)
            {
                var itemStack = containerSlots.GetItemstack(cSlot.Key);
                if (itemStack == null)
                    continue;

                var groups = new List<string> { baseGroup + "-slot" + cSlot.Key };
                var candidate = new CarriedGroupCandidateSet(groups) { SourceSlotKey = cSlot.Key };

                if (AssetResolutionHelper.TryResolveFallbackAsset(api, itemStack, out var assetType, out var assetName))
                {
                    candidate.AssetTypeIfUnset = assetType;
                    candidate.AssetNameIfUnset = assetName;
                }

                candidates.Add(candidate);
            }
        }
    }
}
