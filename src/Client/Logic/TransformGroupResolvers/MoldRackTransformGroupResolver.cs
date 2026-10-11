using System;
using System.Collections.Generic;
using System.Text;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Client.Logic.TransformGroupResolvers
{
    public class MoldRackTransformGroupResolver : ContainerSlotTransformGroupResolverBase
    {
        private const string GroupBase = "moldrack";

        public override string ResolverCode => "moldrack";

        protected override bool CanResolve(CarriedBlock carried)
        {
            return carried.Block?.Class is "BlockMoldRack";
        }

        protected override void AddSlotCandidates(ICoreAPI api, CarriedBlock carried, TreeAttribute containerSlots, string baseGroup, List<CarriedGroupCandidateSet> candidates)
        {
            foreach (var cSlot in containerSlots)
            {
                var itemStack = containerSlots.GetItemstack(cSlot.Key);
                if (itemStack == null) continue;

                var slotBase = GroupBase + "-slot" + cSlot.Key;
                var groups = new List<string>();

                if (TryGetShieldConstruction(api, itemStack, out var shieldConstruction))
                {
                    groups.Add(slotBase + "-" + shieldConstruction);
                    groups.Add(slotBase + "-shield");
                }

                if (IsHoop(api, itemStack))
                {
                    groups.Add(slotBase + "-hoop");
                }

                groups.Add(slotBase);

                var candidate = new CarriedGroupCandidateSet(groups) { SourceSlotKey = cSlot.Key };

                if (AssetResolutionHelper.TryResolveFallbackAsset(api, itemStack, out var assetType, out var assetName))
                {
                    candidate.AssetTypeIfUnset = assetType;
                    candidate.AssetNameIfUnset = assetName;
                }

                candidates.Add(candidate);
            }
        }

        private bool TryGetShieldConstruction(ICoreAPI api, ItemStack itemStack, out string shieldConstruction)
        {
            shieldConstruction = string.Empty;
            if (api?.World == null || itemStack?.Class != EnumItemClass.Item) return false;

            var item = api.World.GetItem(itemStack.Id);
            var path = item?.Code?.Path;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("shield-")) return false;

            var parts = path.Split('-');
            if (parts.Length < 2) return false;

            shieldConstruction = "shield-" + parts[1];
            return true;
        }

        private bool IsHoop(ICoreAPI api, ItemStack itemStack)
        {
            if (api?.World == null || itemStack?.Class != EnumItemClass.Item) return false;

            var item = api.World.GetItem(itemStack.Id);
            var path = item?.Code?.Path;
            return !string.IsNullOrEmpty(path) && path.StartsWith("hoop-", StringComparison.Ordinal);
        }

        protected override void AppendSlotSignatureDetail(ICoreAPI api, ItemStack stack, StringBuilder sb)
        {
            if (!TryGetShieldConstruction(api, stack, out var shieldConstruction)) return;

            sb.Append(":").Append(shieldConstruction);

            var attrs = stack.Attributes;
            if (attrs != null)
            {
                sb.Append(':').Append(attrs.GetString("wood", string.Empty));
                sb.Append(':').Append(attrs.GetString("metal", string.Empty));
                sb.Append(':').Append(attrs.GetString("color", string.Empty));
                sb.Append(':').Append(attrs.GetString("deco", string.Empty));
            }
        }
    }
}
