using HarmonyLib;
using Vintagestory.API.Common;

namespace CarryOn.Patches
{

    public static class DoubleTapSneakState
    {
        public static readonly string LastSneakTapMsKey = "carryon-lastsneaktapms";
        public static readonly int DoubleTapThresholdMs = 300;
    }

    [HarmonyPatch(typeof(Vintagestory.GameContent.EntitySeat), "onControls")]
    public class Patch_EntitySeat_onControls
    {
        [HarmonyPrefix]
        public static bool Prefix(Vintagestory.GameContent.EntitySeat __instance, EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            var entityAgent = __instance.Passenger as EntityAgent;
            if (entityAgent == null) return true;

            if(entityAgent.Api.Side == EnumAppSide.Client)
            {
                return false; 
            }

            // Only check for Sneak key down
            if (action == EnumEntityAction.Sneak && on)
            {
                long nowMs = entityAgent.World.ElapsedMilliseconds;
                long lastTapMs = entityAgent.Attributes.GetLong(DoubleTapSneakState.LastSneakTapMsKey, 0);

                if (nowMs - lastTapMs < DoubleTapSneakState.DoubleTapThresholdMs)
                {
                    // Double tap detected
                    entityAgent.Attributes.SetLong(DoubleTapSneakState.LastSneakTapMsKey, nowMs); // Reset
                    return true;
                }

                entityAgent.Attributes.SetLong(DoubleTapSneakState.LastSneakTapMsKey, nowMs);
            }
            return false; // Continue with original method
        }
    }
}