using CarryOn.Common.Network;
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

            if (entityAgent.Api.Side == EnumAppSide.Client)
            {
                // If the player is seated on multiple seats, then unmount them from the other seats
                foreach (var seat in __instance.MountSupplier.Seats)
                {
                    if (seat != __instance && seat.Passenger == entityAgent)
                    {
                        entityAgent.Api.Logger.Warning("Player found seated on multiple seats, unmounting other seats.");
                        seat.DidUnmount(entityAgent);
                    }
                }
            }

            if (!entityAgent.WatchedAttributes.GetBool(DoubleTapDismountEnabledAttributeKey, false))
            {
                return true; // Skip if double tap dismount is not enabled
            }

            bool doubleTapped = false;


            if (entityAgent.Api.Side == EnumAppSide.Server && entityAgent.Attributes.GetBool(DoubleTappedAttributeKey, false))
            {
                doubleTapped = true;
            }

            if (entityAgent.Api.Side == EnumAppSide.Client)
            {
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
                            doubleTapped = true;
                            var carrySystem = entityAgent.Api.ModLoader.GetModSystem<CarrySystem>();
                            if (carrySystem?.ClientChannel == null)
                            {
                                entityAgent.Api.Logger.Error("CarrySystem ClientChannel is null");
                                return false;
                            }
                            carrySystem.ClientChannel.SendPacket(new PlayerAttributeUpdateMessage(DoubleTappedAttributeKey, true, false));
                            entityAgent.WatchedAttributes.SetBool(DoubleTappedAttributeKey, true);
                        }
                    }

                    entityAgent.Attributes.SetLong(LastSneakTapMsKey, nowMs);
                }
            }

            if (doubleTapped)
            {
                // If double tapped, stop all movement and prevent further processing
                if (entityAgent.Api.Side == EnumAppSide.Server)
                {
                    entityAgent.Attributes.RemoveAttribute(DoubleTappedAttributeKey);
                }

                entityAgent.TryUnmount();
                __instance.controls.StopAllMovement();
            }

            return false; // Skips original method execution
        }
    }
}