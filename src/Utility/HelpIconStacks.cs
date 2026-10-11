using CarryOn.Common.Models;
using Vintagestory.API.Common;

namespace CarryOn.Utility
{
    /// <summary>
    /// Help-icon <see cref="ItemStack"/> resolution for interaction help, used by the block and
    /// entity help builders.
    ///
    /// Deliberately does not cache: item ids and atlas positions are per world connection - the
    /// game builds a fresh <c>ClientMain</c>, item registry and atlases for every world run - so
    /// an icon stack captured in one world is an orphan in the next. A stale stack either
    /// renders fragments of other assets (stale atlas coordinates) or, if its id is past the new
    /// registry's size, crashes the vanilla renderer in
    /// <c>InventoryItemRenderer.GetItemStackRenderInfo</c> under the conditions that the help-icon
    /// resolution contract guards against.
    ///
    /// A missing item degrades to help text without an icon instead of throwing.
    /// </summary>
    public static class HelpIconStacks
    {
        /// <summary>
        /// Returns a fresh one-element icon stack array for <paramref name="code"/> (resolved via
        /// <see cref="CarryCodes.GetCarryCode"/>) - e.g. <c>"icon-handsfree"</c> - backed by
        /// <paramref name="world"/>'s current item registry, or null when the icon item does not
        /// exist. Every call returns a new array.
        /// </summary>
        public static ItemStack[]? Get(IWorldAccessor world, string code)
        {
            var item = world.GetItem(new AssetLocation(CarryCodes.GetCarryCode(code)));
            return item != null ? [new ItemStack(item)] : null;
        }
    }
}
