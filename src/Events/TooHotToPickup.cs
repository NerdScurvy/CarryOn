using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
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
        private CarryOnConfig config;
        public void Init(ICarryManager carryManager)
        {
            // Only enable if the config option is enabled
            config = carryManager?.Api?.World.GetCarrySystem()?.Config;
            if (config == null || !config.CarryOptions.TooHotToCarry) return;

            carryManager.CarryEvents.BeforePickUpBlock += OnBeforePickUpBlock;
        }

        private void OnBeforePickUpBlock(Entity entity, BlockPos pos, CarrySlot slot, CarriedBlock carried, out bool? canPickUp, out string failureCode)
        {
            canPickUp = null;
            failureCode = null;

            var blockEntity = entity.World.BlockAccessor.GetBlockEntity(pos);
            if (blockEntity == null) return;

            // Direct block heat checks
            if (blockEntity is ITemperatureSensitive tempSensitive && tempSensitive.IsHot)
            {
                canPickUp = false;
                failureCode = "too-hot";
                return;
            }

            if (blockEntity is IHeatSource heatSource)
            {
                var heatStrength = heatSource.GetHeatStrength(entity.World, pos, pos);
                if (heatStrength > 0)
                {
                    canPickUp = false;
                    failureCode = "too-hot";
                    return;
                }

                // Extra heuristic: a heat source containing very hot items is still too hot to carry
                if (HasTooHotInventoryItems(blockEntity, entity.World))
                {
                    canPickUp = false;
                    failureCode = "too-hot";
                    return;
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
                if (temp >= config.CarryOptions.TooHotToCarryTemperature) return true;
            }

            return false;
        }
    }
}