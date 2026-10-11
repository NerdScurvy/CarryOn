using CarryOn.Utility;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace CarryOn.Patches
{
    /// <summary>
    /// Blocks the vanilla entity-attachment detach path from yanking a CarryOn-attached stack off a
    /// vehicle/entity. Vanilla <see cref="EntityBehaviorAttachable.OnInteract"/> calls
    /// <c>TryRemoveAttachment</c> when a player interacts with an occupied attachment slot (holding
    /// the attach/detach key). That path moves the whole slot stack into the acting player's
    /// inventory via <c>TryGiveItemStack</c>, which bypasses <see cref="InventoryBase.ActivateSlot"/>
    /// entirely - so it needs its own guard here. CarryOn's own detach does NOT use this method (it
    /// flows through <c>ICarryableEntitySlotHandler.TryDetach</c> from the DetachMessage handler),
    /// so rejecting marked stacks here only suppresses vanilla extraction, never CarryOn's own
    /// carry/detach. Runs on both client and server, keeping the two sides consistent (no desync).
    /// </summary>
    [HarmonyPatch(typeof(EntityBehaviorAttachable), "TryRemoveAttachment")]
    public static class EntityBehaviorAttachable_TryRemoveAttachment_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(EntityBehaviorAttachable __instance, int selectionBoxIndex, EntityAgent byEntity)
        {
            var slotIndex = __instance.GetSlotIndexFromSelectionBoxIndex(selectionBoxIndex);
            if (slotIndex < 0) return true;

            var slot = __instance.Inventory[slotIndex];
            if (slot == null || slot.Empty) return true;

            // A CarryOn-attached stack may only leave an attachable slot through CarryOn's own
            // detach interaction (convert to carried block, then clear the slot). Reject the
            // vanilla detach that would hand the whole block (with its reserved block-entity
            // payload) straight to the player.
            if (AttachableCarryConversion.IsCarryAttached(slot.Itemstack))
            {
                // Only the client should surface the message (it's the side the player sees);
                // the server side still skips the original so the block is never removed.
                if (byEntity?.Api is ICoreClientAPI capi)
                {
                    CarryErrorHelper.ShowError(capi, "vanilla-detach-blocked", "vanilla-detach-blocked");
                }
                return false;
            }

            return true;
        }
    }
}
