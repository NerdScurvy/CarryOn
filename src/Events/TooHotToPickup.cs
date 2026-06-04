using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using static CarryOn.API.Common.Models.CarryCode;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Events
{
    /// <summary>
    /// Prevents picking up blocks that are too hot to carry, such as ovens and forges.
    /// </summary>
    public class TooHotToPickup : ICarryEvent
    {
        private CarryOnConfig? config;
        public void Init(ICarryManager carryManager)
        {
            config = carryManager.Api.World?.GetCarrySystem()?.Config;
            if (config?.CarryOptions?.TooHotToCarry != true) return;

            var carryEvents = carryManager.CarryEvents;
            if (carryEvents == null) return;

            carryEvents.BeforePickUpBlock += OnBeforePickUpBlock;
        }

        private void OnBeforePickUpBlock(Entity entity, BlockPos pos, CarrySlot slot, CarriedBlock carried, out bool? canPickUp, out string failureCode)
        {
            canPickUp = null;
            failureCode = string.Empty;

            var world = entity?.World;
            if (world == null) return;

            var blockEntity = world.BlockAccessor.GetBlockEntity(pos);
            if (blockEntity == null) return;

            // Direct block heat checks
            if (blockEntity is ITemperatureSensitive tempSensitive && tempSensitive.IsHot)
            {
                canPickUp = false;
                failureCode = FailureCode.TooHot;
                return;
            }

            // Check if block is an oven or forge and too hot
            if (blockEntity is IHeatSource heatSource)
            {
                var carryOptions = config?.CarryOptions;
                if (carryOptions != null && ((heatSource is BlockEntityOven oven && oven.ovenTemperature > carryOptions.TooHotToCarryTemperature)
                    || (heatSource is BlockEntityForge forge && forge.IsBurning)))
                {
                    canPickUp = false;
                    failureCode = FailureCode.TooHot;
                    return;
                }

                // Check if any inventory items are too hot
                if (carryOptions != null && HasTooHotInventoryItems(blockEntity, world))
                {
                    canPickUp = false;
                    failureCode = FailureCode.TooHot;
                }
            }
        }

        private bool HasTooHotInventoryItems(BlockEntity blockEntity, IWorldAccessor world)
        {
            var tree = new TreeAttribute();
            blockEntity.ToTreeAttributes(tree);

            if (tree["inventory"] is not TreeAttribute inventory) return false;
            if (inventory["slots"] is not TreeAttribute slots) return false;

            foreach (var value in slots.Values)
            {
                if (value is not ItemstackAttribute itemAttr) continue;

                var stack = itemAttr.GetValue() as ItemStack;
                if (stack?.Collectible == null) continue;

                var temp = stack.Collectible.GetTemperature(world, stack);
                if (config != null && temp >= config.CarryOptions.TooHotToCarryTemperature) return true;
            }

            return false;
        }
    }
}