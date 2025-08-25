using CarryOn.API.Common;
using CarryOn.API.Event;
using CarryOn.Common;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Common;

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
        public static bool IsCarryable(this Block block, CarrySlot slot)
            => block.GetBehavior<BlockBehaviorCarryable>()?.Slots?[slot] != null;


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


    }
}
