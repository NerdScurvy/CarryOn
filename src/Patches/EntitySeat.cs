using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CarryOn.Patches
{

    public static class DoubleTapSneakState
    {
        public static readonly string LastSneakTapMsKey = "carryon-lastsneaktapms";
        public static readonly int DoubleTapThresholdMs = 300;
    }

    [HarmonyPatch(typeof(EntitySeat), "onControls")]
    public class Patch_EntitySeat_onControls
    {
        [HarmonyPrefix]
        public static bool Prefix(EntitySeat __instance, EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            var entityAgent = __instance.Passenger as EntityAgent;
            if (entityAgent == null) return true;

            if (entityAgent.Api.Side == EnumAppSide.Client)
            {
                return false;
            }

            // Only check for Sneak key down
            if (action == EnumEntityAction.Sneak && on)
            {
                long nowMs = entityAgent.World.ElapsedMilliseconds;
                long lastTapMs = entityAgent.Attributes.GetLong(DoubleTapSneakState.LastSneakTapMsKey, 0);

                // Check last tap was in the past. If in the future then the server time has been reset.
                if (lastTapMs < nowMs)
                {
                    if (nowMs - lastTapMs < DoubleTapSneakState.DoubleTapThresholdMs)
                    {
                        // Double tap detected
                        entityAgent.Api.Logger.Debug($"Double tap detected for seat interaction {nowMs - lastTapMs}");
                        entityAgent.Attributes.SetLong(DoubleTapSneakState.LastSneakTapMsKey, nowMs); // Reset
                        //handled = EnumHandling.PassThrough;
                        entityAgent.TryUnmount();
                        __instance.controls.StopAllMovement();
                    }
                }

                entityAgent.Attributes.SetLong(DoubleTapSneakState.LastSneakTapMsKey, nowMs);
            }
            return false; // Skips original method execution
        }
    }
}