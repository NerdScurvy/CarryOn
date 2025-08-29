using CarryOn.Common.Network;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using static CarryOn.API.Common.CarryCode;
using static CarryOn.CarrySystem;

namespace CarryOn.Patches
{

    [HarmonyPatch(typeof(EntitySeat), "onControls")]
    public class EntitySeat_onControls_Patch
    {

        [HarmonyPrefix]
        public static bool Prefix(EntitySeat __instance, EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            var entityAgent = __instance.Passenger as EntityAgent;
            if (entityAgent == null) return true;

            if (!entityAgent.WatchedAttributes.GetBool(AttributeKey.Watched.EntityDoubleTapDismountEnabled, false))
            {
                return true; // return to normal behavior
            }

            if (action != EnumEntityAction.Sneak) return true; 

            if (entityAgent.Api.Side == EnumAppSide.Client)
            {
                if (action == EnumEntityAction.Sneak && on)
                {
                    long nowMs = entityAgent.World.ElapsedMilliseconds;
                    long lastTapMs = entityAgent.Attributes.GetLong(AttributeKey.EntityLastSneakTap, 0);

                    // Check last tap was in the past. If in the future then the server time has been reset.
                    if (lastTapMs < nowMs)
                    {
                        if (nowMs - lastTapMs < Default.DoubleTapThresholdMs && nowMs - lastTapMs > 50)
                        {
                            // Double tap detected
                            var carrySystem = entityAgent.Api.ModLoader.GetModSystem<CarrySystem>();
                            if (carrySystem?.ClientChannel == null)
                            {
                                entityAgent.Api.Logger.Error("CarrySystem ClientChannel is null");
                                return false;
                            }
                            var entityId = __instance.Entity.EntityId;
                            var seatId = __instance.SeatId;

                            entityAgent.TryUnmount();
                            __instance.controls.StopAllMovement();

                            carrySystem.ClientChannel.SendPacket(new DismountMessage() { EntityId = entityId, SeatId = seatId });

                            // Log the dismount action
                            entityAgent.Api.Logger.Debug($"Entity {entityAgent.GetName()} double-tapped to dismount from seat {seatId} on entity {entityId}.");
                        }
                        else
                        {
                            // Single tap, just update the last tap time
                            entityAgent.Attributes.SetLong(AttributeKey.EntityLastSneakTap, nowMs);

                        }
                    }

                    entityAgent.Attributes.SetLong(AttributeKey.EntityLastSneakTap, nowMs);
                }
            }

            return false; // Skips original method execution
        }
    }
}