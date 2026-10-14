using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Resolves cargo-boat fixtures for the vehicle-attach scenarios from loaded content
    /// (never invented codes): the sailed boat is vanilla's only chest-capable boat - its
    /// *Store* cargo slots accept the "chest" category, while the raft's slots take baskets
    /// and oars only. Slot and selection-box indices are resolved at runtime through the
    /// vanilla mappings (ItemSlotWearable.CanTakeFrom / EntityBehaviorAttachable.
    /// GetSlotIndexFromSelectionBoxIndex), mirroring how the interaction reaches them.
    /// </summary>
    public static class BoatFixture
    {
        public const string CargoBoatCodePart = "boat-sailed";

        /// <summary>
        /// The concrete entity code of the sailed cargo boat (boat-sailed-aged - the base
        /// code "boat" resolves its type/material variants), resolved from the loaded
        /// entity registry.
        /// </summary>
        public static string ResolveCargoBoatCode(IWorldAccessor world)
        {
            var match = world.EntityTypes
                .Where(type => type != null && type.Code != null
                    && type.Code.Domain == "game"
                    && type.Code.Path.StartsWith(CargoBoatCodePart + "-", StringComparison.Ordinal))
                .OrderBy(type => type.Code!.ToString(), StringComparer.Ordinal)
                .FirstOrDefault();

            return match?.Code?.ToString()
                ?? throw new InvalidOperationException(
                    $"No game entity with code part '{CargoBoatCodePart}-*' loaded in this world");
        }

        /// <summary>
        /// The first cargo slot whose wearable categories accept a chest stack (the Left
        /// Storage slots) - the slot a chest attach targets, resolved through the same
        /// ItemSlotWearable.CanTakeFrom gate vanilla applies. Returns -1 when none accepts.
        /// </summary>
        public static int FindChestCargoSlot(Entity boat, ItemStack chestStack)
        {
            var attachable = boat.GetBehavior<EntityBehaviorAttachable>();
            if (attachable == null) return -1;

            for (var i = 0; i < attachable.Inventory.Count; i++)
            {
                if (attachable.Inventory[i].CanTakeFrom(new DummySlot(chestStack)))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// The 0-based storage selection-box index that maps to the given inventory slot
        /// through vanilla's box→slot mapping - the index vanilla
        /// TryRemoveAttachment(selectionBoxIndex) expects. Returns -1 when no box maps.
        /// </summary>
        public static int FindBoxIndexForSlot(Entity boat, int slotIndex)
        {
            var attachable = boat.GetBehavior<EntityBehaviorAttachable>();
            var boxes = boat.GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes;
            if (attachable == null || boxes == null) return -1;

            for (var i = 0; i < boxes.Length; i++)
            {
                if (attachable.GetSlotIndexFromSelectionBoxIndex(i) == slotIndex)
                    return i;
            }

            return -1;
        }
    }
}
