using CarryOn.API.Common.Models;
using CarryOn.Common.Enums;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Models
{

    public class CarryInteraction
    {
        public CarryAction CarryAction { get; set; }
        // Time in milliseconds the interaction has been held
        public float TimeHeld { get; set; } = 0.0F;

        // Index of slot on target entity
        public int? TargetSlotIndex { get; set; }

        // Carry slot being interacted with (e.g., Hands, Back)
        public CarrySlot? CarrySlot { get; set; }

        // Selected block position for the interaction
        // This is used for interactions like placing down blocks
        public BlockPos TargetBlockPos { get; set; }

        // Selected slot on target entity
        public ItemSlot Slot { get; set; }

        // Entity performing the interaction (might be redundant if this interaction is client-side only)
        public Entity TargetEntity { get; set; }
        public float? TransferDelay { get; internal set; }

        public void Complete()
        {
            Clear();
            CarryAction = CarryAction.Done;
        }

        public void Clear(bool resetTimeHeld = false)
        {
            CarryAction = CarryAction.None;
            Slot = null;
            CarrySlot = null;
            TargetSlotIndex = null;
            TargetEntity = null;
            TargetBlockPos = null;
            TransferDelay = null;

            if (resetTimeHeld)
            {
                TimeHeld = 0.0F;
            }

        }
    }
}