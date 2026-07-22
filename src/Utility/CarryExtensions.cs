using System;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.API.Event;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Utility
{
    public static class CarryExtensions
    {

        /* ------------------------------ */
        /* Block extensions               */
        /* ------------------------------ */

        /// <summary> Returns whether the specified block can be carried. </summary>
        public static bool IsCarryable(this Block block)
            => block.HasBehavior<BlockBehaviorCarryable>();

        /// <summary> Returns whether the specified block can be interacted with while being carried. </summary>
        public static bool IsCarryableInteract(this Block block)
            => block.HasBehavior<BlockBehaviorCarryableInteract>();

        /// <summary> Returns whether the specified block can be carried in the specified slot.
        /// Checks if <see cref="BlockBehaviorCarryable"/> is present and has slot enabled. 
        /// </summary>
        public static bool CanCarryInSlot(this Block block, CarrySlot slot, ItemStack? itemStack)
        {
            if (block == null) return false;
            var behavior = block.GetBehavior<BlockBehaviorCarryable>();
            return behavior?.CanCarryInSlot(slot, itemStack) == true;
        }

        /// <summary>
        /// Safely calls <see cref="Block.OnPickBlock"/> and returns null if the block throws.
        /// Some modded blocks (e.g. multiblock pulverizers) throw exceptions from OnPickBlock
        /// when the block entity is in an unexpected state or the multiblock structure is partially
        /// loaded. Since this is called from interaction help builders and carry validators on the
        /// client tick hot path, an unhandled exception here crashes the game. Returning null
        /// causes the caller to treat the block as non-pickable, which is correct behavior for
        /// a block that cannot be identified.
        /// </summary>
        public static ItemStack? SafeOnPickBlock(this Block? block, IWorldAccessor world, BlockPos pos)
        {
            if (block == null) return null;
            try
            {
                return block.OnPickBlock(world, pos);
            }
            catch (Exception)
            {
                return null;
            }
        }


        /* ------------------------------ */
        /* IWorldAccessor Extensions      */
        /* ------------------------------ */

        /// <summary>
        /// Gets the carry system instance
        /// </summary>
        /// <param name="world"></param>
        /// <returns></returns>
        public static CarrySystem GetCarrySystem(this IWorldAccessor world)
            => world.Api.ModLoader.GetModSystem<CarrySystem>();

        /// <summary>
        /// Gets the carry events instance
        /// </summary>
        /// <param name="world"></param>
        /// <returns></returns>
        public static CarryEvents GetCarryEvents(this IWorldAccessor world)
            => world.GetCarrySystem().CarryEvents;


        /* ------------------------------ */
        /* CarriedBlock Extensions      */
        /* ------------------------------ */

        /// <summary>
        /// Gets the carryable behavior of the block or default.
        /// Uses the <see cref="CarriedBlock.CachedCarryableBehavior"/> to avoid
        /// repeated linear scans of the block's behavior list on the render hot path.
        /// </summary>
        public static BlockBehaviorCarryable GetCarryableBehavior(this CarriedBlock carriedBlock)
        {
            if (carriedBlock.CachedCarryableBehavior is BlockBehaviorCarryable cached)
                return cached;

            var behavior = carriedBlock.Block.GetBehaviorOrDefault(BlockBehaviorCarryable.Default);
            carriedBlock.CachedCarryableBehavior = behavior;
            return behavior;
        }


        /// <summary>
        /// Checks if the entity can interact with a carryable item.
        /// </summary>
        /// <param name="entityAgent"></param>
        /// <param name="requireEmptyHanded"></param>
        /// <returns></returns>
        public static bool CanInteract(this EntityAgent entityAgent, bool requireEmptyHanded)
        {
            if (entityAgent.Api is ICoreClientAPI api)
            {
                if (!api.Input.IsCarryKeyPressed(true))
                {
                    return false;
                }
            }
            return entityAgent.CanDoCarryAction(requireEmptyHanded);
        }


        /// <summary>
        /// Checks if entity can begin interaction with carryable item that is in the world or carried in hands slot
        /// </summary>
        /// <param name="entityAgent"></param>
        /// <param name="requireEmptyHanded">if true, requires the entity agent to have both left and right hands empty</param>
        /// <returns></returns>
        public static bool CanDoCarryAction(this EntityAgent entityAgent, bool requireEmptyHanded)
        {
            var isEmptyHanded = entityAgent.RightHandItemSlot.Empty && entityAgent.LeftHandItemSlot.Empty;
            if (!isEmptyHanded && requireEmptyHanded) return false;

            if (entityAgent is not EntityPlayer entityPlayer) return true;

            // Active slot must be main hotbar (This excludes the backpack slots)
            var activeHotbarSlot = entityPlayer.Player.InventoryManager.ActiveHotbarSlotNumber;
            return (activeHotbarSlot >= 0) && (activeHotbarSlot < CarryCodes.Defaults.HotbarSize);
        }


    }
}
