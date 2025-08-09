using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using static CarryOn.CarrySystem;

namespace CarryOn.Patches
{

    [HarmonyPatch(typeof(EntitySeat), "onControls")]
    public class Patch_EntitySeat_onControls
    {
        [HarmonyPrefix]
        public static bool Prefix(EntitySeat __instance, EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            var entityAgent = __instance.Passenger as EntityAgent;
            if (entityAgent == null) return true;

            if (!entityAgent.WatchedAttributes.GetBool(DoubleTapDismountEnabledAttributeKey, false))
            {
                return true; // Skip if double tap dismount is not enabled
            }


            // Only check for Sneak key down
            if (action == EnumEntityAction.Sneak && on)
            {
                long nowMs = entityAgent.World.ElapsedMilliseconds;
                long lastTapMs = entityAgent.Attributes.GetLong(LastSneakTapMsKey, 0);

                // Check last tap was in the past. If in the future then the server time has been reset.
                if (lastTapMs < nowMs)
                {
                    if (nowMs - lastTapMs < DoubleTapThresholdMs)
                    {
                        // Double tap detected
                        entityAgent.Attributes.SetLong(LastSneakTapMsKey, nowMs); // Reset
                        entityAgent.TryUnmount();
                        __instance.controls.StopAllMovement();
                    }
                }

                entityAgent.Attributes.SetLong(LastSneakTapMsKey, nowMs);
            }
            return false; // Skips original method execution
        }
    }
}