using CarryOn.Common.Network;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using static CarryOn.Common.Models.CarryCodes;

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

            if (!entityAgent.WatchedAttributes.GetBool(AttributeKeys.Watched.EntityDoubleTapDismountEnabled, false))
                return true;

            if (action != EnumEntityAction.Sneak) return true;

            if (entityAgent.Api.Side == EnumAppSide.Client && on)
            {
                long nowMs = entityAgent.World.ElapsedMilliseconds;
                long lastTapMs = entityAgent.Attributes.GetLong(AttributeKeys.EntityLastSneakTap, 0);

                if (lastTapMs < nowMs
                    && nowMs - lastTapMs < Defaults.DoubleTapThresholdMs
                    && nowMs - lastTapMs > 50)
                {
                    var carrySystem = entityAgent.Api.ModLoader.GetModSystem<CarrySystem>();
                    if (carrySystem?.ClientChannel == null)
                    {
                        entityAgent.Api.Logger.Error("CarrySystem ClientChannel is null");
                        return false;
                    }

                    entityAgent.TryUnmount();
                    __instance.controls.StopAllMovement();
                    carrySystem.ClientChannel.SendPacket(new DismountMessage(__instance.Entity.EntityId, __instance.SeatId));

                    entityAgent.Api.Logger.Debug($"Entity {entityAgent.GetName()} double-tapped to dismount from seat {__instance.SeatId} on entity {__instance.Entity.EntityId}.");
                }

                entityAgent.Attributes.SetLong(AttributeKeys.EntityLastSneakTap, nowMs);
            }

            return false; // Suppress original sneak - double-tap dismount fully overrides single sneak
        }
    }
}