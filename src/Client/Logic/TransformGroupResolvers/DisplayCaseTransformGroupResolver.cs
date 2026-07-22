using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.TransformGroupResolvers
{
    public class DisplayCaseTransformGroupResolver : ContainerSlotTransformGroupResolverBase
    {
        public override string ResolverCode => "displaycase";

        public override bool TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup, out AttachmentResolveResult? result)
        {
            result = null;

            if (api?.World == null || carried?.Block == null || string.IsNullOrEmpty(baseGroup)) return false;

            var containerSlots = BlockUtils.GetContainerSlots(carried);
            if (containerSlots == null || containerSlots.Count == 0) return false;

            bool haveCenterPlacement = carried.BlockEntityData?.GetBool("haveCenterPlacement", false) == true;

            var carryBehavior = carried.GetCarryableBehavior();
            var candidates = new List<CarriedGroupCandidateSet>();

            foreach (var cSlot in containerSlots)
            {
                var itemStack = containerSlots.GetItemstack(cSlot.Key);
                if (itemStack == null) continue;

                if (itemStack.Collectible == null)
                    itemStack.ResolveBlockOrItem(api.World);

                var slotKey = haveCenterPlacement ? "-center" : cSlot.Key;
                var groups = new List<string>();

                if (IsCrystal(itemStack))
                {
                    var crystalGroup = "displaycase-slot" + slotKey + "-crystal";
                    if (carryBehavior?.TransformGroupExists(carried, crystalGroup) == true)
                        groups.Add(crystalGroup);
                }

                groups.Add("displaycase-slot" + slotKey);

                var candidate = new CarriedGroupCandidateSet(groups)
                {
                    SourceSlotKey = cSlot.Key,
                    ApplyDisplaySlotYaw = true,
                    ApplyDisplayCaseYawOffset = true,
                    ApplyOnDisplayTransform = true
                };

                if (AssetResolutionHelper.TryResolveFallbackAsset(api, itemStack, out var assetType, out var assetName))
                {
                    candidate.AssetTypeIfUnset = assetType;
                    candidate.AssetNameIfUnset = assetName;
                }

                candidates.Add(candidate);

                if (haveCenterPlacement) break;
            }

            if (candidates.Count == 0) return false;

            result = new AttachmentResolveResult(candidates);
            return true;
        }

        private static bool IsCrystal(ItemStack stack)
        {
            var path = stack?.Collectible?.Code?.Path;
            if (string.IsNullOrEmpty(path)) return false;

            return path.Contains("crystal", StringComparison.OrdinalIgnoreCase);
        }

        public override string? GetCacheSignature(ICoreAPI api, CarriedBlock carried, string baseGroup)
        {
            var haveCenterPlacement = carried?.BlockEntityData?.GetBool("haveCenterPlacement", false) == true ? "1" : "0";
            var containerSlots = BlockUtils.GetContainerSlots(carried);

            if (containerSlots == null || containerSlots.Count == 0)
                return "center=" + haveCenterPlacement + "|slots=none";

            var keys = containerSlots.Keys?.ToList() ?? new List<string>();
            keys.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder(192);
            sb.Append("center=").Append(haveCenterPlacement).Append("|slots=");

            foreach (var key in keys)
            {
                var stack = containerSlots.GetItemstack(key);
                if (stack == null)
                {
                    sb.Append(key).Append(":empty;");
                    continue;
                }

                if (stack.Collectible == null)
                    stack.ResolveBlockOrItem(api?.World);

                var code = stack.Collectible?.Code?.ToString() ?? "unresolved";

                sb.Append(key).Append(':')
                    .Append((int)stack.Class).Append(':')
                    .Append(stack.Id).Append(':')
                    .Append(stack.StackSize).Append(':')
                    .Append(code).Append(';');
            }

            return sb.ToString();
        }
    }
}
