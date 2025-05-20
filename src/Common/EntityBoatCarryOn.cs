using CarryOn.API.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Common
{

    public class EntityBoatCarryOn : EntityBoat
    {


public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
{
    Api.Logger.Debug($"EntityBoatCarryOn.OnInteract {mode}");
    EnumHandling handled = EnumHandling.PassThrough;

            var carryBehavior = GetBehavior<EntityBehaviorAttachableCarryable>();

            if (carryBehavior != null)
            {
                carryBehavior.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
                if (handled == EnumHandling.PreventSubsequent)
                    return;
            }

            if (byEntity.IsCarryKeyHeld() && byEntity?.MountedOn?.Entity == this)
            {
                return;
            }

            if (mode == EnumInteractMode.Interact && AllowPickup())
            {
                if (IsEmpty() && tryPickup(byEntity, mode))
                {
                    Api.Logger.Log(EnumLogType.Debug, "EntityBoatCarryOn.OnInteract PICKUP");
                    return;
                }
            }

            foreach (EntityBehavior behavior in SidedProperties.Behaviors)
            {
                if (behavior == carryBehavior) continue;

                Api.Logger.Log(EnumLogType.Debug, $"EntityBoatCarryOn.OnInteract Behavior {behavior.ToString()}");
                behavior.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);

                if (behavior is EntityBehaviorRideableAccessories)
                {
                    var bra = behavior as EntityBehaviorRideableAccessories;
                    int i = 0;
                    foreach (var slot in bra.Inventory)
                    {
                        Api.Logger.Log(EnumLogType.Debug, $"slot-{i++} Empty = {slot.Empty}");
                    }
                }

                if (behavior is EntityBehaviorCreatureCarrier)
                {
                    var bcc = behavior as EntityBehaviorCreatureCarrier;
                    foreach (var seat in bcc.Seats)
                    {

                        Api.Logger.Log(EnumLogType.Debug, $"{seat.SeatId} {seat?.Passenger?.GetName()}");
                    }

                }

                if (handled == EnumHandling.PreventSubsequent)
                    break;
            }
        }

        private bool AllowPickup()
        {
            JsonObject attributes = Properties.Attributes;
            if (attributes == null)
                return false;
            return attributes["rightClickPickup"]?.AsBool() ?? false;
        }

        private bool IsEmpty()
        {
            EntityBehaviorSeatable seatableBehavior = GetBehavior<EntityBehaviorSeatable>();
            EntityBehaviorRideableAccessories rideableAccBehavior = GetBehavior<EntityBehaviorRideableAccessories>();
            if (seatableBehavior != null && seatableBehavior.AnyMounted())
                return false;
            return rideableAccBehavior == null || rideableAccBehavior.Inventory.Empty;
        }

        private bool tryPickup(EntityAgent byEntity, EnumInteractMode mode)
        {
            if (!byEntity.Controls.ShiftKey)
                return false;
            ItemStack itemstack = new ItemStack(World.GetItem(Code), 1);
            if (!byEntity.TryGiveItemStack(itemstack))
                World.SpawnItemEntity(itemstack, ServerPos.XYZ);
            Api.World.Logger.Audit($"{byEntity.GetName()} Picked up boat 1x{itemstack.Collectible.Code} at {Pos}");
            Die();
            return true;
        }

    }




}