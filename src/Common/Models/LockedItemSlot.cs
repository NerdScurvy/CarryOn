using System;
using Vintagestory.API.Common;

namespace CarryOn.Common.Models
{
    /// <summary>Wraps a hotbar slot to prevent it from being modified during carry operations.</summary>
    public class LockedItemSlot : ItemSlot
    {
        /// <summary>Gets the original slot that was locked.</summary>
        public ItemSlot Original { get; }

        /// <summary>Gets the index of this slot within its inventory.</summary>
        public int SlotID { get; }

        /// <summary>Creates a locked copy of the given slot.</summary>
        /// <param name="original">The slot to lock.</param>
        public LockedItemSlot(ItemSlot original)
            : base(original.Inventory)
        {
            Original = original ?? throw new ArgumentNullException(nameof(original));
            Itemstack = original.Itemstack;
            BackgroundIcon = original.BackgroundIcon;
            StorageType = default;

            SlotID = -1;
            for (var i = 0; i < original.Inventory.Count; i++)
                if (original.Inventory[i] == original) { SlotID = i; break; }
            if (SlotID == -1) throw new Exception("Couldn't find original slot in its own inventory!");
        }

        /// <summary>Locks a slot by replacing it with a <see cref="LockedItemSlot"/> in its inventory.</summary>
        /// <param name="slot">The slot to lock.</param>
        /// <returns>The locked slot instance.</returns>
        public static LockedItemSlot Lock(ItemSlot slot)
        {
            if (slot is not LockedItemSlot locked)
            {
                locked = new LockedItemSlot(slot);
                slot.Inventory[locked.SlotID] = locked;
            }
            return locked;
        }

        /// <summary>Restores a locked slot back to its original state in the inventory.</summary>
        /// <param name="slot">The slot to restore.</param>
        public static void Restore(ItemSlot slot)
        {
            if (slot is LockedItemSlot locked)
                slot.Inventory[locked.SlotID] = locked.Original;
        }

        // These should be the only methods we have to override.

        public override bool CanHold(ItemSlot sourceSlot) => false;
        public override bool CanTake() => false;

        // Unfortunately, only some of ItemSlot's method make use of them.

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority) => false;
        public override ItemStack? TakeOutWhole() => null;
        public override ItemStack? TakeOut(int quantity) => null;
        protected override void ActivateSlotLeftClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op) { }
        protected override void ActivateSlotRightClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op) { }
    }
}
