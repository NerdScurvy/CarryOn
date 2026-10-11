using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CarryOn.Utility
{
    /// <summary>
    /// Shared pre-change guards for carry interactions that run identically on both sides.
    /// </summary>
    public static class CarrySlotGuard
    {
        /// <summary>
        /// Prevents the active slot from changing while the player is carrying an item in their hands.
        /// Uses slot occupancy so a present-but-unreadable carry (e.g. block from a removed mod)
        /// keeps the guard active.
        /// </summary>
        public static EnumHandling GuardHandsCarried(ICarryManager carryManager, EntityAgent entity)
        {
            return carryManager.IsCarrySlotOccupied(entity, CarrySlot.Hands)
                ? EnumHandling.PreventDefault
                : EnumHandling.PassThrough;
        }
    }
}
