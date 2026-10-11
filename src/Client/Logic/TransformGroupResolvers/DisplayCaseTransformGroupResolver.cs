using System;
using System.Collections.Generic;
using System.Text;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Client.Logic.TransformGroupResolvers
{
    public class DisplayCaseTransformGroupResolver : ContainerSlotTransformGroupResolverBase
    {
        private const string GroupBase = "displaycase";

        public override string ResolverCode => "displaycase";

        protected override void AddSlotCandidates(ICoreAPI api, CarriedBlock carried, TreeAttribute containerSlots, string baseGroup, List<CarriedGroupCandidateSet> candidates)
        {
            var haveCenterPlacement = carried.BlockEntityData?.GetBool(CarryCodes.AttributeKeys.BlockEntityData.HaveCenterPlacement, false) == true;
            var carryBehavior = carried.GetCarryableBehavior();

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
                    var crystalGroup = GroupBase + "-slot" + slotKey + "-crystal";
                    groups.Add(crystalGroup);
                }

                groups.Add(GroupBase + "-slot" + slotKey);

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
        }

        private static bool IsCrystal(ItemStack stack)
        {
            var path = stack?.Collectible?.Code?.Path;
            if (string.IsNullOrEmpty(path)) return false;

            return path.Contains("crystal", StringComparison.OrdinalIgnoreCase);
        }

        protected override string BuildSignaturePrefix(ICoreAPI api, CarriedBlock carried, bool hasSlots)
        {
            var haveCenterPlacement = carried?.BlockEntityData?.GetBool(CarryCodes.AttributeKeys.BlockEntityData.HaveCenterPlacement, false) == true ? "1" : "0";
            return "center=" + haveCenterPlacement + (hasSlots ? "|slots=" : "|slots=none");
        }

        protected override void AppendSlotSignatureDetail(ICoreAPI api, ItemStack stack, StringBuilder sb)
        {
            if (stack.Collectible == null)
                stack.ResolveBlockOrItem(api?.World);

            sb.Append(':').Append(stack.Collectible?.Code?.ToString() ?? "unresolved");
        }
    }
}
