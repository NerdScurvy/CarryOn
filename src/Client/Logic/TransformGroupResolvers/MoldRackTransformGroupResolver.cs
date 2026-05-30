using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Client.Logic.TransformGroupResolvers
{
    public class MoldRackTransformGroupResolver : ContainerSlotTransformGroupResolverBase
    {
        public override string ResolverCode => "moldrack";

        public override bool TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup, out CarriedGroupResolution resolution)
        {
            resolution = null;

            if (api?.World == null || carried?.Block == null) return false;
            if (carried.Block.Class is not "BlockMoldRack") return false;

            var containerSlots = TransformGroupResolverHelper.GetContainerSlots(carried);
            if (containerSlots == null || containerSlots.Count == 0) return false;

            var result = new CarriedGroupResolution();
            AddSlotCandidates(api, containerSlots, "moldrack", result);

            if (result.AdditionalGroupCandidates.Count == 0) return false;

            resolution = result;
            return true;
        }

        protected override void AddSlotCandidates(ICoreAPI api, TreeAttribute containerSlots, string baseGroup, CarriedGroupResolution resolution)
        {
            foreach (var cSlot in containerSlots)
            {
                var itemStack = containerSlots.GetItemstack(cSlot.Key);
                if (itemStack == null) continue;

                var slotBase = baseGroup + "-slot" + cSlot.Key;
                var candidate = new CarriedGroupCandidateSet { SourceSlotKey = cSlot.Key };

                // Highest priority: specific shield construction group (e.g. moldrack-slot0-shield-crude)
                if (TryGetShieldConstruction(api, itemStack, out var shieldConstruction))
                {
                    candidate.Groups.Add(slotBase + "-" + shieldConstruction); // moldrack-slotN-shield-*
                    candidate.Groups.Add(slotBase + "-shield");               // moldrack-slotN-shield
                }

                // Fallback for all mold-rack items
                candidate.Groups.Add(slotBase); // moldrack-slotN

                if (TransformGroupResolverHelper.TryResolveFallbackAsset(api, itemStack, out var assetType, out var assetName))
                {
                    candidate.AssetTypeIfUnset = assetType;
                    candidate.AssetNameIfUnset = assetName;
                }

                resolution.AdditionalGroupCandidates.Add(candidate);
            }
        }

        private bool TryGetShieldConstruction(ICoreAPI api, ItemStack itemStack, out string shieldConstruction)
        {
            shieldConstruction = null;
            if (api?.World == null || itemStack?.Class != EnumItemClass.Item) return false;

            var item = api.World.GetItem(itemStack.Id);
            var path = item?.Code?.Path;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("shield-")) return false;

            // Expected examples:
            // shield-crude (this is the only case where the shield transform is different)
            // shield-blackguard
            // shield-woodmetal-...
            // shield-woodmetalleather-...
            // shield-metal-...
            var parts = path.Split('-');
            if (parts.Length < 2) return false;

            shieldConstruction = "shield-" + parts[1];
            return true;
        }

        public override string GetCacheSignature(ICoreAPI api, CarriedBlock carried, string baseGroup, CarriedGroupResolution resolution)
        {
            var slots = TransformGroupResolverHelper.GetContainerSlots(carried);
            if (slots == null || slots.Count == 0)
            {
                return "slots=none";
            }

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
                    .Append(stack.StackSize);

                if (TryGetShieldConstruction(api, stack, out var shieldConstruction))
                {
                    sb.Append(":").Append(shieldConstruction);

                    // If shield appearance is attribute-driven, include those too.
                    var attrs = stack.Attributes;
                    if (attrs != null)
                    {
                        sb.Append(':').Append(attrs.GetString("wood", string.Empty));
                        sb.Append(':').Append(attrs.GetString("metal", string.Empty));
                        sb.Append(':').Append(attrs.GetString("color", string.Empty));
                        sb.Append(':').Append(attrs.GetString("deco", string.Empty));
                    }
                }

                sb.Append(';');
            }

            return sb.ToString();
        }
    }
}