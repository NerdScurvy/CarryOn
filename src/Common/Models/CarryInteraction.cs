using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Models
{

    public class CarryInteraction
    {
        public CarryAction CarryAction { get; set; }

        /// <summary> Time in milliseconds the interaction has been held. </summary>
        public float TimeHeld { get; set; } = 0.0f;

        /// <summary> Index of slot on target entity. </summary>
        public int? TargetSlotIndex { get; set; }

        /// <summary> Carry slot being interacted with (e.g., Hands, Back). </summary>
        public CarrySlot? CarrySlot { get; set; }

        /// <summary> Selected block position for the interaction. </summary>
        public BlockPos? TargetBlockPos { get; set; }

        /// <summary> Selected slot on target entity. </summary>
        public ItemSlot? Slot { get; set; }

        /// <summary> Entity performing the interaction. </summary>
        public Entity? TargetEntity { get; set; }
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
                TimeHeld = 0.0f;
            }

        }
    }
}
