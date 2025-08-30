using CarryOn.API.Common.Models;
using CarryOn.API.Event;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using static CarryOn.API.Common.Models.CarryCode;

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

        /// <summary> 
        /// Returns whether the specified block can be carried in the specified slot.
        /// Checks if <see cref="BlockBehaviorCarryable"/> is present and has slot enabled. 
        /// </summary>
        {
            if (block == null) return false;
            var behavior = block.GetBehavior<BlockBehaviorCarryable>();
            if (behavior == null) return false;
            if (behavior.Slots == null) return false;
            return behavior.Slots[slot] != null;
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
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public static BlockBehaviorCarryable GetCarryableBehavior(this CarriedBlock carriedBlock)
            => carriedBlock.Block.GetBehaviorOrDefault(BlockBehaviorCarryable.Default);


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
            return (activeHotbarSlot >= 0) && (activeHotbarSlot < 10);
        }


        /// <summary>
        /// Checks if the carry key is currently pressed.
        /// Always returns false on server.
        /// </summary>
        /// <param name="checkMouse"></param>
        /// <returns></returns>
        public static bool IsCarryKeyPressed(this IInputAPI input, bool checkMouse = false)
        {
            if (checkMouse && !input.InWorldMouseButton.Right) return false;

            return input.KeyboardKeyState[input.HotKeys.Get(HotKeyCode.Pickup).CurrentMapping.KeyCode];
        }

        /// <summary>
        /// Checks if the carry swap key is currently pressed.
        /// Always returns false on server.
        /// </summary>
        /// <returns></returns>
        public static bool IsCarrySwapBackKeyPressed(this IInputAPI input)
        {
            return input.KeyboardKeyState[input.HotKeys.Get(HotKeyCode.SwapBackModifier).CurrentMapping.KeyCode];
        }

    }
}
