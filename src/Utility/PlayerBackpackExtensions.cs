using System.Linq;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.Common;

namespace CarryOn.Utility
{
    public static class PlayerBackpackExtensions
    {

        public static ItemSlot? GetRenderedBackpackSlot(this IPlayer player, BackpackSelectionMode mode = BackpackSelectionMode.LastFound)
        {
            var backpackInv = player.InventoryManager.GetOwnInventory("backpack");
            if (backpackInv == null) return null;

            if (mode == BackpackSelectionMode.FirstOnly)
            {
                var slot = backpackInv[0];
                return slot?.Itemstack != null &&
                       slot.Itemstack.ItemAttributes?["attachableToEntity"]?["categoryCode"]?.AsString() == "backpack"
                    ? slot : null;
            }

            var matchingSlots = backpackInv
                 .Take(4)
                 .Where(slot => slot?.Itemstack != null &&
                             slot?.Itemstack?.ItemAttributes?["attachableToEntity"]?["categoryCode"]?.AsString() == "backpack")
                 .ToList();

            return mode switch
            {
                BackpackSelectionMode.FirstFound => matchingSlots.FirstOrDefault(),
                _ => matchingSlots.LastOrDefault()
            };
        }

        public static string? GetRenderedBackpackItemCode(this IPlayer player, BackpackSelectionMode mode = BackpackSelectionMode.LastFound)
        {
            var renderedItemSlot = player.GetRenderedBackpackSlot(mode);
            return renderedItemSlot?.Itemstack?.Item?.Code?.ToString();
        }

        public static string ResolveCarryTransformGroupBase(this EntityPlayer entityPlayer, CarryOnConfig config, CarrySlot carrySlot)
        {
            if (carrySlot == CarrySlot.Hands)
            {
                return "hands";
            }

            var selectionMode = config?.CarryOptions?.BackpackSelectionMode ?? BackpackSelectionMode.LastFound;
            var backpackItemCode = entityPlayer?.Player?.GetRenderedBackpackItemCode(selectionMode);
            if (!string.IsNullOrEmpty(backpackItemCode)
                && (config?.BackpackMapping?.TryGetValue(backpackItemCode, out var backpackType) ?? false)
                && !string.IsNullOrEmpty(backpackType))
            {
                return "backpack-" + backpackType;
            }

            return "backpack-none";
        }

        public static string ResolveCarryTransformGroupBase(this EntityAgent entity, CarryOnConfig config, CarrySlot carrySlot)
        {
            if (entity is EntityPlayer entityPlayer)
            {
                return entityPlayer.ResolveCarryTransformGroupBase(config, carrySlot);
            }

            return CarryCode.DefaultTransformGroup;
        }

    }
}
