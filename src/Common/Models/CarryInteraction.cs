using CarryOn.API.Common.Models;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Models
{

    public class CarryInteraction
    {
        public CarryAction Action { get; set; }

        /// <summary> Time in milliseconds the interaction has been held. </summary>
        public float TimeHeld { get; set; } = 0.0f;

        /// <summary> Index of slot on target entity. </summary>
        public int? TargetSlotIndex { get; set; }

        /// <summary> Carry slot being interacted with (e.g., Hands, Back). </summary>
        public CarrySlot? Slot { get; set; }

        /// <summary> Selected block position for the interaction. </summary>
        public BlockPos? TargetBlockPos { get; set; }

        /// <summary> Entity performing the interaction. </summary>
        public Entity? TargetEntity { get; set; }
        public float TransferDelay { get; internal set; } = float.NaN;

        public void Complete()
        {
            Clear();
            Action = CarryAction.Done;
        }

        public void Clear(bool resetTimeHeld = false)
        {
            Action = CarryAction.None;
            Slot = null;
            TargetSlotIndex = null;
            TargetEntity = null;
            TargetBlockPos = null;
            TransferDelay = float.NaN;

            if (resetTimeHeld)
            {
                TimeHeld = 0.0f;
            }

        }
    }
}
