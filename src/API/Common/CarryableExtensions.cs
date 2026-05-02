
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
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
            return block.GetBehavior<BlockBehaviorCarryable>() != null;
        }

        public static bool IsCarryableInteract(this Block block)
        {
            return block.GetBehavior<BlockBehaviorCarryableInteract>() != null;
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
            if (entity.Api.Side == EnumAppSide.Server)
            {
                var entityName = entity?.GetName() ?? "Unknown Entity";
                entity.World.Logger.Audit($"[{CarrySystem.ModId}] {entityName} picked up block {carried.Block.Code.GetName()} at {pos}");
            }            
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

        /// <summary> Attempts to make this entity drop all of its carried
        ///           blocks around its current position in the specified area. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity is null. </exception>
        /// <exception cref="ArgumentOutOfRangeException"> Thrown if hSize or vSize is negative. </exception>
        public static void DropAllCarried(this Entity entity, int hSize = 4, int vSize = 4)
        {
            var api = entity.Api;
            var carryManager = CarrySystem.GetCarryManager(api);
            carryManager?.DropCarried(entity, Enum.GetValues(typeof(CarrySlot)).Cast<CarrySlot>(), hSize);
        }
                     

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

            // Check if the carried blocks can be swapped in their new slots
            bool canSetFirst = carriedFirst == null || carriedFirst.Block.GetBehavior<BlockBehaviorCarryable>()?.Slots?[second] != null;
            bool canSetSecond = carriedSecond == null || carriedSecond.Block.GetBehavior<BlockBehaviorCarryable>()?.Slots?[first] != null;

            if (!canSetFirst || !canSetSecond)
                return false;

            CarriedBlock.Remove(entity, first);
            CarriedBlock.Remove(entity, second);

            try
            {
                carriedFirst?.Set(entity, second);
                carriedSecond?.Set(entity, first);
            }
            catch (Exception ex)
            {
                // Rollback: restore original state if anything fails
                carriedFirst?.Set(entity, first);
                carriedSecond?.Set(entity, second);

                entity.World.Logger.Error($"[{CarrySystem.ModId}] Failed to swap carried blocks for entity {entity.GetName()}. Rolling back changes. {ex}");
                return false;
            }

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

            if (!player.Entity.World.Claims.TryAccess(
                player, selection.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                return false;
            }

            var carried = CarriedBlock.Get(player.Entity, slot);
            if (carried == null) return false;

            return carried.PlaceDown(ref failureCode, player.Entity.World, selection, player.Entity);
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
