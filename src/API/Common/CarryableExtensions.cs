
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
using Vintagestory.GameContent;
using Vintagestory.ServerMods.NoObf;

namespace CarryOn.API.Common
{
    public static class CarryableExtensions
    {
        /* ------------------------------ */
        /* Block extensions               */
        /* ------------------------------ */

        /// <summary> Returns whether the specified block can be carried.
        ///           Checks if <see cref="BlockBehaviorCarryable"/> is present.</summary>
        public static bool IsCarryable(this Block block)
        {
            return block.HasBehavior<BlockBehaviorCarryable>();
        }

        public static bool IsCarryableInteract(this Block block)
        {
            return block.HasBehavior<BlockBehaviorCarryableInteract>();
        }

        /// <summary> Returns whether the specified block can be carried in the specified slot.
        ///           Checks if <see cref="BlockBehaviorCarryable"/> is present and has slot enabled. </summary>
        public static bool IsCarryable(this Block block, CarrySlot slot)
        {
            return block.GetBehavior<BlockBehaviorCarryable>()?.Slots?[slot] != null;
        }

        /* ------------------------------ */
        /* Entity extensions              */
        /* ------------------------------ */

        /// <summary> Returns the <see cref="CarriedBlock"/> this entity
        ///           is carrying in the specified slot, or null of none. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity or pos is null. </exception>
        public static CarriedBlockExtended GetCarried(this Entity entity, CarrySlot slot)
            => CarriedBlockExtended.Get(entity, slot);

        /// <summary> Returns all the <see cref="CarriedBlock"/>s this entity is carrying. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity or pos is null. </exception>
        public static IEnumerable<CarriedBlockExtended> GetCarried(this Entity entity)
        {
            foreach (var slot in Enum.GetValues(typeof(CarrySlot)).Cast<CarrySlot>())
            {
                var carried = entity.GetCarried(slot);
                if (carried != null) yield return carried;
            }
        }

        /// <summary>
        ///   Attempts to get this entity to pick up the block the
        ///   specified position as a <see cref="CarriedBlock"/>,
        ///   returning whether it was successful.
        /// </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity or pos is null. </exception>
        public static bool Carry(this Entity entity, BlockPos pos,
                                 CarrySlot slot, bool checkIsCarryable = true, bool playSound = true)
        {
            if (!HasPermissionToCarry(entity, pos)) return false;
            if (CarriedBlockExtended.Get(entity, slot) != null) return false;
            var carried = CarriedBlockExtended.PickUp(entity.World, pos, slot, checkIsCarryable);
            if (carried == null) return false;

            carried.Set(entity, slot);
            if (playSound) carried.PlaySound(pos, entity.World, entity as EntityPlayer);
            return true;
        }

        private static bool HasPermissionToCarry(Entity entity, BlockPos pos)
        {
            var isReinforced = entity.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.IsReinforced(pos) ?? false;
            if (entity is EntityPlayer playerEntity)
            {
                var delegates = entity.World.GetCarryEvents()?.OnCheckPermissionToCarry?.GetInvocationList();

                // Handle OnRestoreBlockEntityData events
                if (delegates != null)
                {
                    foreach (var checkPermissionToCarryDelegate in delegates.Cast<CheckPermissionToCarryDelegate>())
                    {
                        try
                        {
                            checkPermissionToCarryDelegate(playerEntity, pos, isReinforced, out bool? hasPermission);

                            if (hasPermission != null)
                            {
                                return hasPermission.Value;
                            }
                        }
                        catch (Exception e)
                        {
                            entity.World.Logger.Error(e.Message);
                        }
                    }
                }

                var isCreative = playerEntity.Player.WorldData.CurrentGameMode == EnumGameMode.Creative;

                if (!isCreative && isReinforced) return false; // Can't pick up when reinforced unless in creative mode.
                // Can pick up if has access to any claims that might be present.
                return entity.World.Claims.TryAccess(playerEntity.Player, pos, EnumBlockAccessFlags.BuildOrBreak);
            }
            else
            {
                return !isReinforced; // If not a player entity, can pick up if not reinforced.
            }
        }


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

            var remaining = new HashSet<CarriedBlockExtended>(
                slots.Select(s => entity.GetCarried(s))
                     .Where(c => c != null).OrderBy(t => t?.Behavior?.MultiblockOffset));
            if (remaining.Count == 0) return;



            // TODO: Avoid potential infinite loop if there is no bedrock for some reason.
            var centerBlock = FindGround(entity.Pos.AsBlockPos, blockAccessor, nonGroundBlockClasses);

            var blockPlacer = new BlockPlacer(entity.Api);
            var blockSelection = blockPlacer.FindBlockPlacement(remaining.First().Block, centerBlock, 2, 3);

            if (blockSelection == null)
            {
                // No valid placement found, drop all blocks as items
                foreach (var carriedBlock in remaining)
                {
                    DropBlockAsItem(world, carriedBlock, centerBlock, player, entity);
                }
                return;
            }

            var carryManager = world.GetCarrySystem()?.CarryManager;
            if (carryManager != null && carryManager.TryPlaceDown(player?.Entity, remaining.First(), blockSelection, true))
            {
                carryManager.RemoveCarriedBlock(player?.Entity, remaining.First().Slot);
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
                        carryManager = world.GetCarrySystem()?.CarryManager;
                        if (carryManager != null && carryManager.TryPlaceDown(player?.Entity, block, new BlockSelection { Position = pos }, true))
                        {
                            carryManager.RemoveCarriedBlock(player?.Entity, block.Slot);
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
        private static bool CanPlaceMultiblock(BlockPos position, CarriedBlockExtended carriedBlock, IBlockAccessor blockAccessor)
        {
            if (carriedBlock?.Behavior?.MultiblockOffset != null)
            {
                var multiPos = position.AddCopy(carriedBlock.Behavior.MultiblockOffset);
                var testBlock = blockAccessor.GetBlock(multiPos);
                if (!testBlock.IsReplacableBy(carriedBlock.Block))
                {
                    return false;
                }
            }
            return true;
        }

        // Helper: Drop block as item
        private static void DropBlockAsItem(IWorldAccessor world, CarriedBlockExtended carriedBlock, BlockPos centerBlock, IServerPlayer player, Entity entity)
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
            CarriedBlockExtended.Remove(entity, carriedBlock.Slot);
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
        public static bool Swap(this Entity entity, CarrySlot first, CarrySlot second)
        {
            if (first == second) throw new ArgumentException("Slots can't be the same");

            var carriedFirst = CarriedBlockExtended.Get(entity, first);
            var carriedSecond = CarriedBlockExtended.Get(entity, second);
            if ((carriedFirst == null) && (carriedSecond == null)) return false;

            CarriedBlockExtended.Remove(entity, first);
            CarriedBlockExtended.Remove(entity, second);

            carriedFirst?.Set(entity, second);
            carriedSecond?.Set(entity, first);

            return true;
        }

        public static bool IsCarryKeyHeld(this Entity entity)
        {
            return entity.Attributes.GetBool("carryKeyHeld");
        }

        public static void SetCarryKeyHeld(this Entity entity, bool isHeld)
        {
            if (entity.IsCarryKeyHeld() != isHeld)
            {
                entity.Attributes.SetBool("carryKeyHeld", isHeld);
            }
        }

        /* ------------------------------ */
        /* EntityAgent Extensions         */
        /* ------------------------------ */


        /// <summary>
        /// Checks if entity can begin interaction with carryable item that is in the world or in hand slot
        /// Their left and right hands be empty.
        /// </summary>
        /// <param name="entityAgent"></param>
        /// <param name="requireEmptyHanded"></param>
        /// <returns></returns>
        public static bool CanDoCarryAction(this EntityAgent entityAgent, bool requireEmptyHanded)
        {
            var system = entityAgent.World.GetCarrySystem();
            return system.CarryHandler.CanDoCarryAction(entityAgent, requireEmptyHanded);
        }

        /* ------------------------------ */
        /* IPlayer extensions             */
        /* ------------------------------ */

        /// <summary>
        ///   Attempts to get this player to place down its
        ///   <see cref="CarriedBlock"/> (if any) at the specified
        ///   selection, returning whether it was successful.
        /// </summary>
        /// <exception cref="ArgumentNullException"> Thrown if player or selection is null. </exception>
        public static bool PlaceCarried(this IPlayer player, BlockSelection selection, CarrySlot slot, ref string failureCode)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (selection == null) throw new ArgumentNullException(nameof(selection));

            var world = player?.Entity?.World;

            if (!world.Claims.TryAccess(
                player, selection.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                return false;
            }

            var carryManager = world.GetCarrySystem()?.CarryManager;

            var carried = CarriedBlockExtended.Get(player.Entity, slot);
            if (carried == null) return false;

            return carryManager.TryPlaceDown(player.Entity, carried, selection);

        }

        /* ------------------------------ */
        /* IWorldAccessor Extensions      */
        /* ------------------------------ */

        public static CarrySystem GetCarrySystem(this IWorldAccessor world)
            => world.Api.ModLoader.GetModSystem<CarrySystem>();

        public static CarryEvents GetCarryEvents(this IWorldAccessor world)
            => world.GetCarrySystem().CarryEvents;

    }
}
