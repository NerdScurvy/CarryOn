using System.Linq;
using Vintagestory.API.Datastructures;

namespace CarryOn.Utility
{
    public static class CarryInventoryUtils
    {
        /// <summary>
        /// Converts a block inventory attribute to a backpack attribute.
        /// </summary>
        /// <param name="blockInventory"></param>
        /// <returns></returns>
        public static ITreeAttribute ConvertBlockInventoryToBackpack(ITreeAttribute blockInventory)
        {
            var backpack = new TreeAttribute();
            if (blockInventory == null) return backpack; // graceful fallback
            
            var slotCount = blockInventory.GetAsInt("qslots");
            var slots = blockInventory.GetTreeAttribute("slots");

            // create backpack slots and copy items
            var backpackSlots = new TreeAttribute();
            for (int i = 0; i < slotCount; i++)
            {
                var slotKey = $"slot-{i}";

                var itemstack = slots?.GetItemstack(i.ToString())?.Clone();
                backpackSlots.SetItemstack(slotKey, itemstack);
            }

            backpack.SetAttribute("slots", backpackSlots);
            return backpack;
        }
        
        /// <summary>
        /// Converts a backpack attribute to a block inventory attribute.
        /// </summary>
        /// <param name="backpack"></param>
        /// <returns></returns>
        public static ITreeAttribute ConvertBackpackToBlockInventory(ITreeAttribute backpack)
        {
            var blockInventory = new TreeAttribute();
            if (backpack == null || backpack.Count == 0) return blockInventory; // graceful fallback

            var backpackSlots = backpack.GetTreeAttribute("slots");
            var count = backpackSlots.Count;

            blockInventory.SetInt("qslots", count);
            var slotsAttribute = new TreeAttribute();

            for (var i = 0; i < count; i++)
            {
                var value = backpackSlots.Values[i];
                if (value?.GetValue() == null) continue;
                slotsAttribute.SetAttribute(i.ToString(), value.Clone());
            }

            blockInventory.SetAttribute("slots", slotsAttribute);
        

            return blockInventory;
        }
    }
}