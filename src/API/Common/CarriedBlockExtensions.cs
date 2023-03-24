
using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Event;
using CarryOn.Common;
using CarryOn.Server;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CarryOn.API.Common
{
    public static class CarriedBlockExtensions
    {
        /// <summary> Returns whether the specified block can be carried in the specified slot.
        ///           Checks if <see cref="BlockBehaviorCarryable"/> is present and has slot enabled. </summary>
        public static bool IsCarryable(this Block block, CarrySlot slot)
        {
            return block.GetBehavior<BlockBehaviorCarryable>()?.Slots?[slot] != null;
        }

        /// <summary> Returns the <see cref="CarriedBlock"/> this entity
        ///           is carrying in the specified slot, or null of none. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity or pos is null. </exception>
        public static CarriedBlock GetCarried(this Entity entity, CarrySlot slot)
            => CarriedBlock.Get(entity, slot);

        /// <summary> Returns all the <see cref="CarriedBlock"/>s this entity is carrying. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity or pos is null. </exception>
        public static IEnumerable<CarriedBlock> GetCarried(this Entity entity)
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
            if (CarriedBlock.Get(entity, slot) != null) return false;
            var carried = CarriedBlock.PickUp(entity.World, pos, slot, checkIsCarryable);
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
                            checkPermissionToCarryDelegate(playerEntity, pos, out bool? hasPermission);

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

        /// <summary>
        ///   Attempts to get this player to place down its
        ///   <see cref="CarriedBlock"/> (if any) at the specified
        ///   selection, returning whether it was successful.
        /// </summary>
        /// <exception cref="ArgumentNullException"> Thrown if player or selection is null. </exception>
        public static bool PlaceCarried(this IPlayer player, BlockSelection selection, CarrySlot slot)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (selection == null) throw new ArgumentNullException(nameof(selection));

            if (!player.Entity.World.Claims.TryAccess(
                player, selection.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                return false;
            }

            var carried = CarriedBlock.Get(player.Entity, slot);
            if (carried == null) return false;

            return carried.PlaceDown(player.Entity.World, selection, player.Entity);
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
            IServerPlayer player = null;

            if (entity is EntityPlayer entityPlayer)
            {
                player = (IServerPlayer)entityPlayer.Player;
            }

            var remaining = new HashSet<CarriedBlock>(
                slots.Select(s => entity.GetCarried(s))
                     .Where(c => c != null));
            if (remaining.Count == 0) return;

            bool Drop(BlockPos pos, CarriedBlock block)
            {
                if (!block.PlaceDown(entity.World, new BlockSelection { Position = pos }, player.Entity, true)) return false;
                CarriedBlock.Remove(entity, block.Slot);
                return true;
            }

            var centerBlock = entity.Pos.AsBlockPos;
            var accessor = entity.World.BlockAccessor;

            // Look for ground 
            var blockBelow = centerBlock.DownCopy();
            bool foundGround = false;
            while (!foundGround)
            {
                var testBlock = accessor.GetBlock(blockBelow);
                // Check if block is air or defined set of non-ground blocks
                if (testBlock.BlockId == 0 || ModConfig.ServerConfig.NonGroundBlockClasses.Contains(testBlock.Class))
                {
                    centerBlock = blockBelow;
                    blockBelow = blockBelow.DownCopy();
                }
                else
                {
                    foundGround = true;
                }
            }

            var nearbyBlocks = new List<BlockPos>(((hSize * 2) + 1) * ((hSize * 2) + 1));
            for (int x = -hSize; x <= hSize; x++)
            {
                for (int z = -hSize; z <= hSize; z++)
                    nearbyBlocks.Add(centerBlock.AddCopy(x, 0, z));
            }

            var airBlocks = new List<BlockPos>();

            nearbyBlocks = nearbyBlocks.OrderBy(b => b.DistanceTo(centerBlock)).ToList();

            var blockIndex = 0;
            var distance = 0;
            while (remaining.Count > 0)
            {
                if (blockIndex >= nearbyBlocks.Count)
                {
                    while (remaining.Count > 0)
                    {
                        // Try to place blocks in known air
                        var placeable = remaining.FirstOrDefault();
                        var airPos = airBlocks.FirstOrDefault();

                        if (airPos == null) break;

                        if ((placeable != null) && Drop(airPos, placeable))
                        {
                            remaining.Remove(placeable);
                            airBlocks.Remove(airPos);
                        }
                    }
                    if (remaining.Count > 0)
                    {
                        entity.Api.Logger.Warning($"Entity {entity.GetName()} could not drop carryable on or near {centerBlock}");
                    }
                    break;
                }
                var pos = nearbyBlocks[blockIndex];
                if (Math.Abs(pos.Y - centerBlock.Y) <= vSize)
                {
                    var sign = Math.Sign(pos.Y - centerBlock.Y);
                    var testBlock = accessor.GetBlock(pos);
                    // Record known air blocks and non ground blocks
                    if (testBlock.BlockId == 0 || ModConfig.ServerConfig.NonGroundBlockClasses.Contains(testBlock.Class))
                    {
                        airBlocks.Add(pos.Copy());
                    }
                    var placeable = remaining.FirstOrDefault(c => testBlock.IsReplacableBy(c.Block));
                    if (sign == 0)
                    {
                        sign = (placeable != null) ? -1 : 1;
                    }
                    else if (sign > 0)
                    {
                        if ((placeable != null) && Drop(pos, placeable))
                        {
                            remaining.Remove(placeable);
                            airBlocks.Remove(pos);
                        }
                    }
                    else if (placeable == null)
                    {
                        BlockPos multiPos = null;
                        var above = pos.UpCopy();

                        testBlock = accessor.GetBlock(above);
                        placeable = remaining.FirstOrDefault(c => testBlock.IsReplacableBy(c.Block));

                        // Dirty fix to test second block of multiblock. e.g. trunk
                        if(placeable?.Behavior?.MultiblockOffset != null){
                            multiPos = above.AddCopy(placeable.Behavior.MultiblockOffset);
                            testBlock = accessor.GetBlock(multiPos);
                            if(!testBlock.IsReplacableBy(placeable.Block)){
                                placeable = null;
                            }
                        }

                        if ((placeable != null) && Drop(above, placeable))
                        {
                            remaining.Remove(placeable);
                            airBlocks.Remove(above);
                            airBlocks.Remove(multiPos);
                        }
                    }
                    pos.Add(0, sign, 0);
                }

                if (++distance > 3)
                {
                    distance = 0;
                    blockIndex++;
                    if (blockIndex % 4 == 4)
                    {
                        if (++blockIndex >= nearbyBlocks.Count)
                            blockIndex = 0;
                    }
                }
            }
            // FIXME: Drop container contents if blocks could not be placed.
            //        Right now, the player just gets to keep them equipped.
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

            var carriedFirst = CarriedBlock.Get(entity, first);
            var carriedSecond = CarriedBlock.Get(entity, second);
            if ((carriedFirst == null) && (carriedSecond == null)) return false;

            CarriedBlock.Remove(entity, first);
            CarriedBlock.Remove(entity, second);

            carriedFirst?.Set(entity, second);
            carriedSecond?.Set(entity, first);

            return true;
        }

        public static CarryEvents GetCarryEvents(this IWorldAccessor world)
        {
            var carrySystem = world.Api.ModLoader.GetModSystem<CarrySystem>();
            return carrySystem.CarryEvents;
        }
    }
}
