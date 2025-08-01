using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CarryOn.Patches
{
    [HarmonyPatch(typeof(EntityAgent), "doMount")]
    public class Patch_EntityAgent_doMount
    {
        [HarmonyPrefix]
        public static bool Prefix(EntityAgent __instance, IMountableSeat mountable)
        {
            // If player is already mounted on this seat, skip the mount logic
            // This is to prevent the player's view snapping forward when sneak is held
            if (mountable?.Passenger == __instance)
            {
                return false; // Skip original method if already mounted
            }

            return true;
        }
    }
}