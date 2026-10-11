using System;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.API.Event;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

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
        {
            CarrySystem? system = world.GetCarrySystem();
            return system?.CarryEvents
                ?? throw new InvalidOperationException("CarrySystem not loaded");
        }


        /* ------------------------------ */
        /* CarriedBlock Extensions      */
        /* ------------------------------ */

        /// <summary>
        /// Gets the carryable behavior the block currently mounts, or default.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The block's behaviors are remounted as fresh instances by every
        /// conditioning pass, so a behavior resolved earlier can be an instance no
        /// pass mounted any more. This accessor therefore resolves on every call, and
        /// no churn source (a recondition, the client's snapshot reconcile, a future
        /// pass) can leave a stale read behind. Resolving here is the only place that
        /// can be made correct: no invalidation site can reach the live carries,
        /// because <c>CarriedBlockTreeSerializer.DeserializeSafe</c> builds a fresh
        /// <see cref="CarriedBlock"/> on every read, so enumerating carried blocks
        /// hands back objects nobody is holding.
        /// </para>
        /// <para>
        /// This costs nothing measurable. A render-path cache used to sit here
        /// (<c>CarriedBlock.CachedCarryableBehavior</c>, removed in the 2.0 release
        /// candidate), but the read paths that matter - render, interaction - receive
        /// a freshly deserialized <see cref="CarriedBlock"/> per call, so that cache
        /// was never warm and the block's behavior list was already scanned per call.
        /// The only sites that did hold a <see cref="CarriedBlock"/> across calls were
        /// interaction-scoped (a drag's cached carry target, the pack-adjustment
        /// state). Resolving per call also keeps the render caches honest: the
        /// render-variant signature is built from this behavior, so a churned
        /// behavior re-keys the transform plans and render infos by itself.
        /// </para>
        /// </remarks>
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
            return (activeHotbarSlot >= 0) && (activeHotbarSlot < CarryCodes.Defaults.HotbarSize);
        }


        /* ------------------------------ */
        /* ItemSlot Extensions            */
        /* ------------------------------ */

        /// <summary>
        /// Returns whether the slot's item is a bag that <b>renders on the player</b> when
        /// worn - the <c>attachableToEntity.categoryCode == "backpack"</c> attribute. Used
        /// for the worn-bag render/transform scan only: a worn held-only bag (linen sack)
        /// occupies the slot but never renders and is correctly skipped.
        /// </summary>
        public static bool IsBagItem(this ItemSlot? slot)
        {
            return slot?.Itemstack != null && slot.Itemstack.IsWearableBagStack();
        }

        /* ------------------------------ */
        /* ItemStack Extensions           */
        /* ------------------------------ */

        /// <summary>
        /// Returns whether the stack renders on the player when worn - the
        /// <c>attachableToEntity.categoryCode == "backpack"</c> attribute. This is a
        /// <b>render</b> marker, not a slot-fitment one: the linen sack and other held-only
        /// bags occupy vanilla worn-bag slots without ever rendering.
        /// </summary>
        public static bool IsWearableBagStack(this ItemStack? stack)
        {
            return stack != null
                && stack.ItemAttributes?["attachableToEntity"]?["categoryCode"]?.AsString() == "backpack";
        }

        /// <summary>
        /// Returns whether the stack fits the vanilla worn-bag slots - the exact rule
        /// <see cref="ItemSlotBackpack"/> applies:
        /// <c>(Collectible.GetStorageFlags(stack) &amp; EnumItemStorageFlags.Backpack) != 0</c>.
        /// In vanilla this is exclusive to the bag family and covers every bag, wearable or
        /// held-only (the linen sack occupies a worn slot without rendering on the player).
        /// </summary>
        public static bool IsBackpackStowable(this ItemStack? stack)
        {
            return stack?.Collectible != null
                && (stack.Collectible.GetStorageFlags(stack) & EnumItemStorageFlags.Backpack) > 0;
        }

        /// <summary>
        /// Returns whether the stack carries vanilla's
        /// <see cref="CollectibleBehaviorGroundStoredHeldBag"/> behavior - the marker that
        /// makes Ctrl+right-click open a ground-stored bag's contents dialog. Every vanilla
        /// bag, wearable or held-only, has it; non-bag ground-storable items do not.
        /// </summary>
        public static bool IsGroundStoredHeldBag(this ItemStack? stack)
        {
            return stack?.Collectible?.GetBehavior<CollectibleBehaviorGroundStoredHeldBag>() != null;
        }

        /// <summary>
        /// Returns whether the stack is a bag eligible for CarryOn's ground-storage bag
        /// features (pickup into hands): a ground-stored held bag (vanilla's marker) or any
        /// stack the vanilla worn-bag slots accept (the bag-family storage flag).
        /// </summary>
        public static bool IsGroundStorageBagStack(this ItemStack? stack)
        {
            return stack.IsGroundStoredHeldBag() || stack.IsBackpackStowable();
        }


        /* ------------------------------ */
        /* BlockEntityGroundStorage Ext.  */
        /* ------------------------------ */

        /// <summary>
        /// Returns whether the ground storage slot targeted by the given <see cref="BlockSelection"/>
        /// contains a bag eligible for the bag pickup (a ground-stored held bag or a wearable
        /// backpack). Uses <see cref="BlockEntityGroundStorage.GetSlotAt"/> to resolve the slot
        /// from the hit position, so only the targeted slot is checked - not all slots.
        /// </summary>
        public static bool IsGroundStorageBagTargeted(
            this BlockEntityGroundStorage? beg, BlockSelection? selection)
        {
            if (beg == null || selection == null) return false;
            return beg.GetSlotAt(selection)?.Itemstack.IsGroundStorageBagStack() == true;
        }

        /// <summary>
        /// Returns whether the targeted ground storage slot contains a held bag - an item with
        /// vanilla's <see cref="CollectibleBehaviorGroundStoredHeldBag"/> behavior, whose
        /// Ctrl+right-click opens the contents dialog. This is the exact set vanilla opens bag
        /// GUIs for, so the carried-bag GUI-open forwards to vanilla only for these.
        /// </summary>
        public static bool IsGroundStorageHeldBagTargeted(
            this BlockEntityGroundStorage? beg, BlockSelection? selection)
        {
            if (beg == null || selection == null) return false;
            return beg.GetSlotAt(selection)?.Itemstack.IsGroundStoredHeldBag() == true;
        }


    }
}
