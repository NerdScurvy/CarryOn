using System;
using System.Linq;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CarryOn.Common.Logic
{
/// <summary>
        /// Open-inventory guard shared by the carry pickup paths: a container must be
        /// rejected while <b>another</b> player has its inventory open, while the
        /// acting player's own open GUI must never block the action.
        /// <para>
        /// <b>Caller invariant.</b> The container a caller passes in must always be
        /// the one whose GUI the player could have open - which, for a multiblock, is
        /// the <b>controller's</b> container, not the clicked cell's. A
        /// <c>BlockMultiblock</c> dummy is not a <c>BlockEntityContainer</c> at all, so
        /// resolving the raw clicked position yields null and the guard can only fail
        /// open. <c>CarryPickupService.TryPickUp</c> resolves the controller through
        /// <c>CarryFootprint.ResolveController</c> - the same anchor the pickup write set
        /// and the placement footprint use - before calling in here.
        /// </para>
        /// </summary>
    internal static class CarryInventoryGuard
    {
        /// <summary>
        /// The <see cref="IInventory.InventoryID"/> that vanilla uses for the
        /// bag-content GUI of <paramref name="bagSlot"/> in a ground-storage block.
        /// <para>
        /// <b>Vanilla citation (VSSurvivalMod, 1.22.7, read from the shipped assembly).</b>
        /// <c>BlockEntityContainedBagWorkspace.TryLoadBagInv</c> ends with
        /// <c>wrapperInv.Init(bagInv.Count, "blockcontainedbaginv",
        /// "blockcontainedbag-slot" + slotId + "-" + be.Pos, ...)</c>, and
        /// <c>InventoryGeneric.Init(int, string className, string instanceID, NewSlotDelegate)</c>
        /// assigns those two arguments to the <c>className</c> and <c>instanceID</c>
        /// fields, which <c>InventoryBase.InventoryID</c> concatenates as
        /// <c>className + "-" + instanceID</c>. So the id is
        /// <c>blockcontainedbaginv-blockcontainedbag-slot{slotId}-{pos}</c>.
        /// </para>
        /// <para>
        /// <b>slotId</b> is <c>beg.Inventory.GetSlotId(bagSlot)</c> because
        /// <c>BEBehaviorContainedBagInventory.Initialize</c> builds one workspace per
        /// inventory index (<c>for (int i = 0; i &lt; containerEntity.Inventory.Count; i++)
        /// new BlockEntityContainedBagWorkspace(containerEntity, i)</c>) and the ctor
        /// stores that <c>i</c> as its <c>slotId</c> field - the very number
        /// <c>TryLoadBagInv</c> interpolates. <b>pos</b> is
        /// <c>BlockEntity.Pos.ToString()</c>, read off the container block entity, not
        /// the clicked cell.
        /// </para>
        /// <para>
        /// This string is vanilla's, not ours, so a VS release that renames
        /// <c>blockcontainedbaginv</c> / <c>blockcontainedbag-slot</c> would make the
        /// guard's prefix arm silently match nothing - a fail-open, invisible to any test
        /// that rebuilt the same literals. That is what the
        /// <c>PickupOpenGuardScenarios</c> premise row exists to catch: it drives the
        /// real workspace and compares against <b>this</b> helper, so the literals live
        /// in exactly one place and a rename turns that row red.
        /// </para>
        /// </summary>
        internal static string BagContentInventoryId(BlockEntityGroundStorage beg, ItemSlot bagSlot)
        {
            return CarryCodes.BagContentInventoryPrefix
                + "-blockcontainedbag-slot"
                + beg.Inventory.GetSlotId(bagSlot)
                + "-"
                + beg.Pos;
        }

        /// <summary>
        /// True when any online player other than <paramref name="acting"/> has an
        /// opened inventory whose <see cref="IInventory.InventoryID"/> satisfies
        /// <paramref name="match"/>. Returns false for a null acting player
        /// (non-player entity paths simply have no owner to shield).
        /// </summary>
        internal static bool HasOpenInventory(ICoreAPI api, IServerPlayer? acting, System.Func<string, bool> match)
        {
            if (acting == null)
            {
                return false;
            }

            return api.World.AllOnlinePlayers
                .OfType<IServerPlayer>()
                .Where(serverPlayer => serverPlayer.PlayerUID != acting.PlayerUID)
                .SelectMany(serverPlayer => serverPlayer.InventoryManager.OpenedInventories)
                .Any(inv => match(inv.InventoryID ?? string.Empty));
        }
    }
}