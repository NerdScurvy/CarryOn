using System;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;

namespace CarryOn.Common.Logic
{
    public static class CarriedBlockAccessPolicy
    {
        public static bool CanPickup(
            EnumGameMode gameMode,
            string? playerUid,
            string? ownerUid,
            PickupAccess pickupAccess,
            float gracePeriodSeconds,
            long dropTimeRealTicks)
        {
            if (gameMode == EnumGameMode.Creative)
                return true;

            if (pickupAccess == PickupAccess.Anyone)
                return true;

            if (ownerUid != null && playerUid == ownerUid)
                return true;

            if (pickupAccess == PickupAccess.OwnerFirst)
            {
                var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - dropTimeRealTicks).TotalSeconds;
                if (elapsed >= gracePeriodSeconds)
                    return true;
            }

            return false;
        }
    }
}
