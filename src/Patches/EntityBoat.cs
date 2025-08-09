using CarryOn.Common.Network;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using static CarryOn.CarrySystem;

namespace CarryOn.Patches
{

    [HarmonyPatch(typeof(EntityBoat), "OnGameTick")]
    public class Patch_EntityBoat_OnGameTick
    {

        [HarmonyPrefix]
        public static bool Prefix(EntityBoat __instance, float dt)
        {
            // Run client side to detect and cleanup seat mismatches caused by a desync
            if (__instance.Api.Side == EnumAppSide.Client)
            {
                EntityBehaviorSeatable behavior = __instance.GetBehavior<EntityBehaviorSeatable>();

                if (behavior?.Seats == null) return true;

                foreach (IMountableSeat seat in behavior.Seats)
                {
                    if (seat.Passenger is EntityAgent entityAgent)
                    {
                        var playerMountedOn = entityAgent.WatchedAttributes?.GetAttribute("mountedOn") as TreeAttribute;
                        if (playerMountedOn != null)
                        {
                            var playerMountedOnSeatId = playerMountedOn.GetString("seatId");
                            var playerMountedOnEntityId = playerMountedOn.GetLong("entityIdMount");
                            if (playerMountedOnSeatId != seat.SeatId || playerMountedOnEntityId != __instance.EntityId)
                            {
                                __instance.Api.Logger.Warning($"Player {entityAgent.GetName()} found in wrong seat of {__instance.GetName()} ({__instance.EntityId}).");
                                seat.DidUnmount(entityAgent);
                            }
                        }
                    }

                }
            }
            return true;
        }
    }

}
