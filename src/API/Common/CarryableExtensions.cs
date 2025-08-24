
using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Event;
using CarryOn.Common;
using CarryOn.Config;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CarryOn.API.Common
{
    public static class CarryableExtensions
    {
        /* ------------------------------ */
        /* Block extensions               */
        /* ------------------------------ */

        private static ICarryManager clientCarryManager = null;
        private static ICarryManager serverCarryManager = null;

        // This needs to be called when disposing the CarrySystem to avoid stale references
        public static void ClearCachedCarryManager()
        {
            clientCarryManager = null;
            serverCarryManager = null;
        }

        public static ICarryManager GetCarryManager(ICoreAPI api)
        {
            if (api.Side == EnumAppSide.Server)
            {
                serverCarryManager ??= api.ModLoader.GetModSystem<CarrySystem>()?.CarryManager;
                return serverCarryManager;
            }
            clientCarryManager ??= api.ModLoader.GetModSystem<CarrySystem>()?.CarryManager;
            return clientCarryManager;
        }

        /// <summary> Returns whether the specified block can be carried.
        ///           Checks if <see cref="BlockBehaviorCarryable"/> is present.</summary>
        public static bool IsCarryable(this Block block)
            => block.HasBehavior<BlockBehaviorCarryable>();

        public static bool IsCarryableInteract(this Block block)
            => block.HasBehavior<BlockBehaviorCarryableInteract>();

        /// <summary> Returns whether the specified block can be carried in the specified slot.
        ///           Checks if <see cref="BlockBehaviorCarryable"/> is present and has slot enabled. </summary>
        public static bool IsCarryable(this Block block, CarrySlot slot)
            => block.GetBehavior<BlockBehaviorCarryable>()?.Slots?[slot] != null;


        /* ------------------------------ */
        /* Entity extensions              */
        /* ------------------------------ */

        public static bool HasPermissionToCarry(this Entity entity, BlockPos pos)
            => GetCarryManager(entity.Api)?.HasPermissionToCarry(entity, pos) ?? false;

        /// <summary> Returns the <see cref="CarriedBlock"/> this entity
        ///           is carrying in the specified slot, or null of none. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity or pos is null. </exception>
        public static CarriedBlock GetCarried(this Entity entity, CarrySlot slot)
            => GetCarryManager(entity.Api)?.GetCarried(entity, slot);

        /// <summary> Returns all the <see cref="CarriedBlock"/>s this entity is carrying. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity or pos is null. </exception>
        public static IEnumerable<CarriedBlock> GetCarried(this Entity entity)
            => GetCarryManager(entity.Api)?.GetAllCarried(entity);

        /// <summary> Attempts to make this entity drop its carried blocks from the
        ///           specified slots around its current position in the specified area. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity or slots is null. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> Thrown if hSize or vSize is negative. </exception>
        public static void DropCarried(this Entity entity, IEnumerable<CarrySlot> slots,
                                       int hSize = 4, int vSize = 4)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            if (hSize < 0) throw new ArgumentOutOfRangeException(nameof(hSize));
            if (vSize < 0) throw new ArgumentOutOfRangeException(nameof(vSize));


            IServerPlayer player = (entity is EntityPlayer entityPlayer) ? (IServerPlayer)entityPlayer.Player : null;
            var api = entity.Api;
            var world = entity.World;
            var blockAccessor = world.BlockAccessor;
            var nonGroundBlockClasses = ModConfig.ServerConfig?.DroppedBlockOptions?.NonGroundBlockClasses ?? [];

            var remaining = new HashSet<CarriedBlock>(
                slots.Select(s => entity.GetCarried(s))
                     .Where(c => c != null).OrderBy(t => t?.GetCarryableBehavior()?.MultiblockOffset));
            if (remaining.Count == 0) return;



            // TODO: Avoid potential infinite loop if there is no bedrock for some reason.
            //var centerBlock = FindGround(entity.Pos.AsBlockPos, blockAccessor, nonGroundBlockClasses);
            BlockPos centerBlock = entity.Pos.AsBlockPos.UpCopy();

            var blockPlacer = new BlockPlacer(entity.Api);
            var blockSelection = blockPlacer.FindBlockPlacement(remaining.First().Block, centerBlock, 3);

            if (blockSelection == null)
            {
                // No valid placement found, drop all blocks as items
                foreach (var carriedBlock in remaining)
                {
                    DropBlockAsItem(world, carriedBlock, centerBlock, player, entity);
                }
                return;
            }

            var carryManager = GetCarryManager(api);
            if (carryManager != null && carryManager.TryPlaceDown(player?.Entity, remaining.First(), blockSelection, true))
            {
                carryManager.RemoveCarried(player?.Entity, remaining.First().Slot);
                //remaining.Remove(block);
                // TODO: Implement logic to handle remaining blocks and remove inaccessible code below
                return;
            }


            var nearbyBlocks = GetNearbyBlocks(centerBlock, hSize);
            var airBlocks = new HashSet<BlockPos>();

            // Record air blocks
            foreach (var pos in nearbyBlocks)
            {
                var testBlock = blockAccessor.GetBlock(pos);
                if (testBlock.BlockId == 0 || nonGroundBlockClasses.Contains(testBlock.Class))
                    airBlocks.Add(pos.Copy());
            }

            // Try to place each carried block
            foreach (var block in remaining.ToList())
            {
                bool placed = false;
                foreach (var pos in nearbyBlocks)
                {
                    if (CanPlaceMultiblock(pos, block, blockAccessor) && blockAccessor.GetBlock(pos).IsReplacableBy(block.Block))
                    {
                        if (carryManager != null && carryManager.TryPlaceDown(player?.Entity, block, new BlockSelection { Position = pos }, true))
                        {
                            carryManager.RemoveCarried(player?.Entity, block.Slot);
                            remaining.Remove(block);
                            placed = true;
                            break;
                        }
                    }
                }
                if (!placed)
                {
                    DropBlockAsItem(world, block, centerBlock, player, entity);
                    remaining.Remove(block);
                }
            }
        }

        // Helper: Find the ground position below a block
        private static BlockPos FindGround(BlockPos start, IBlockAccessor blockAccessor, string[] nonGroundBlockClasses)
        {
            var pos = start.Copy();
            var blockBelow = pos.DownCopy();
            while (true)
            {
                var testBlock = blockAccessor.GetBlock(blockBelow);
                if (testBlock.BlockId == 0 || (nonGroundBlockClasses?.Contains(testBlock.Class) ?? false))
                {
                    pos = blockBelow;
                    blockBelow = blockBelow.DownCopy();
                }
                else
                {
                    return pos;
                }
            }
        }

        // Helper: Get all nearby blocks in a square area
        private static List<BlockPos> GetNearbyBlocks(BlockPos center, int hSize)
        {
            var blocks = new List<BlockPos>();
            for (int x = -hSize; x <= hSize; x++)
            {
                for (int z = -hSize; z <= hSize; z++)
                {
                    blocks.Add(center.AddCopy(x, 0, z));
                }
            }
            blocks.Sort((a, b) => a.DistanceTo(center).CompareTo(b.DistanceTo(center)));
            return blocks;
        }

        // Helper: Can place multiblock
        private static bool CanPlaceMultiblock(BlockPos position, CarriedBlock carriedBlock, IBlockAccessor blockAccessor)
        {
            if (carriedBlock?.GetCarryableBehavior()?.MultiblockOffset != null)
            {
                var multiPos = position.AddCopy(carriedBlock.GetCarryableBehavior().MultiblockOffset);
                var testBlock = blockAccessor.GetBlock(multiPos);
                if (!testBlock.IsReplacableBy(carriedBlock.Block))
                {
                    return false;
                }
            }
            return true;
        }

        // Helper: Drop block as item
        private static void DropBlockAsItem(IWorldAccessor world, CarriedBlock carriedBlock, BlockPos centerBlock, IServerPlayer player, Entity entity)
        {
            var api = world.Api;
            var blockDestroyed = false;
            var hadContents = false;
            var dropVec3d = new Vec3d(centerBlock.X + 0.5, centerBlock.Y + 0.5, centerBlock.Z + 0.5);

            if (carriedBlock.BlockEntityData?["inventory"] is TreeAttribute inventory && inventory["slots"] is TreeAttribute invSlots)
            {
                foreach (var item in invSlots.Values.Cast<ItemstackAttribute>())
                {
                    var itemStack = (ItemStack)item.GetValue();
                    world.SpawnItemEntity(itemStack, dropVec3d);
                    hadContents = true;
                }
                var carriedItemStack = carriedBlock.ItemStack.Clone();
                carriedItemStack.Attributes.Remove("contents");
                world.SpawnItemEntity(carriedItemStack, dropVec3d);
            }
            else
            {
                var itemStacks = carriedBlock.Block.GetDrops(world, centerBlock, player);
                if (itemStacks.Length == 1 && itemStacks[0].Id == carriedBlock.ItemStack.Id)
                {
                    world.SpawnItemEntity(carriedBlock.ItemStack, dropVec3d);
                }
                else
                {
                    blockDestroyed = true;
                    foreach (var itemStack in itemStacks)
                    {
                        world.SpawnItemEntity(itemStack, dropVec3d);
                        hadContents = true;
                    }
                }
            }

            var breakSound = carriedBlock.Block.Sounds.GetBreakSound(player) ?? new AssetLocation("game:sounds/block/planks");
            world.PlaySoundAt(breakSound, (double)centerBlock.X, (double)centerBlock.Y, (double)centerBlock.Z);
            GetCarryManager(api).RemoveCarried(entity, carriedBlock.Slot);
            world.GetCarryEvents()?.TriggerBlockDropped(world, centerBlock, entity, carriedBlock, blockDestroyed, hadContents);
        }

        /// <summary> Attempts to make this entity drop all of its carried
        ///           blocks around its current position in the specified area. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity is null. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> Thrown if hSize or vSize is negative. </exception>
        public static void DropAllCarried(this Entity entity, int hSize = 4, int vSize = 4)
            => DropCarried(entity, Enum.GetValues(typeof(CarrySlot)).Cast<CarrySlot>(), hSize, vSize);

        /// <summary>
        ///   Attempts to swap the <see cref="CarriedBlock"/>s currently carried in the
        ///   entity's <paramref name="first"/> and <paramref name="second"/> slots.
        /// </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity is null. </exception>
        public static bool SwapCarried(this Entity entity, CarrySlot first, CarrySlot second)
            => GetCarryManager(entity.Api).SwapCarried(entity, first, second);

        /* ------------------------------ */
        /* IWorldAccessor Extensions      */
        /* ------------------------------ */

        public static CarrySystem GetCarrySystem(this IWorldAccessor world)
            => world.Api.ModLoader.GetModSystem<CarrySystem>();

        public static CarryEvents GetCarryEvents(this IWorldAccessor world)
            => world.GetCarrySystem().CarryEvents;

        /// <summary>
        /// Gets the carryable behavior of the block or default.
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public static BlockBehaviorCarryable GetCarryableBehavior(this CarriedBlock carriedBlock)
            => carriedBlock.Block.GetBehaviorOrDefault(BlockBehaviorCarryable.Default);

        public static void Set(this CarriedBlock carriedBlock, Entity entity, CarrySlot slot)
            => GetCarryManager(entity.Api).SetCarried(entity, slot, carriedBlock.ItemStack, carriedBlock.BlockEntityData);
    }
}
