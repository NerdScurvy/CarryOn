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
using Vintagestory.GameContent;

namespace CarryOn.API.Common
{
    public class CarryManager : ICarryManager
    {

        public static string CarryAttributeKey { get; } = $"{CarrySystem.ModId}:Carried";

        public ICoreAPI Api => CarrySystem?.Api;

        public CarrySystem CarrySystem { get; private set; }

        public CarryEvents CarryEvents => CarrySystem?.CarryEvents;

        public CarryManager(CarrySystem carrySystem)
        {
            CarrySystem = carrySystem ?? throw new ArgumentNullException(nameof(carrySystem));
        }

        /// <summary>
        /// Checks if entity can begin interaction with carryable item that is in the world or carried in hands slot
        /// </summary>
        /// <param name="entityAgent"></param>
        /// <param name="requireEmptyHanded">if true, requires the entity agent to have both left and right hands empty</param>
        /// <returns></returns>
        public bool CanDoCarryAction(EntityAgent entityAgent, bool requireEmptyHanded)
        {
            var isEmptyHanded = entityAgent.RightHandItemSlot.Empty && entityAgent.LeftHandItemSlot.Empty;
            if (!isEmptyHanded && requireEmptyHanded) return false;

            if (entityAgent is not EntityPlayer entityPlayer) return true;

            // Active slot must be main hotbar (This excludes the backpack slots)
            var activeHotbarSlot = entityPlayer.Player.InventoryManager.ActiveHotbarSlotNumber;
            return (activeHotbarSlot >= 0) && (activeHotbarSlot < 10);
        }

        /// <summary>
        /// Gets all carried blocks for the specified entity.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public IEnumerable<CarriedBlock> GetAllCarried(Entity entity)
        {
            foreach (var slot in Enum.GetValues(typeof(CarrySlot)).Cast<CarrySlot>())
            {
                var carried = GetCarried(entity, slot);
                if (carried != null) yield return carried;
            }
        }

        /// <summary>
        /// Gets the CarriedBlock carried by the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="slot">CarrySlot</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public CarriedBlock GetCarried(Entity entity, CarrySlot slot)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var slotAttribute = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(CarryAttributeKey, slot.ToString());
            if (slotAttribute == null) return null;

            var stack = slotAttribute.GetItemstack("Stack");
            if (stack?.Class != EnumItemClass.Block) return null;
            // The ItemStack returned by TreeAttribute.GetItemstack
            // may not have Block set, so we have to resolve it.
            if (stack.Block == null)
            {
                stack.ResolveBlockOrItem(entity.World);
                if (stack.Block == null) return null; // Can't resolve block?
            }

            var blockEntityData = (entity.World.Side == EnumAppSide.Server)
                ? entity.Attributes.TryGet<ITreeAttribute>(CarryAttributeKey, slot.ToString(), "Data")
                : null;

            return new CarriedBlock(slot, stack, blockEntityData);
        }        


        /// <summary>
        /// Sets the CarriedBlock for the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="carriedBlock"></param>
        public void SetCarried(Entity entity, CarriedBlock carriedBlock)
        {
            SetCarried(entity, carriedBlock.Slot, carriedBlock.ItemStack, carriedBlock.BlockEntityData);
        }

        /// <summary>
        /// Sets the CarriedBlock for the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="slot"></param>
        /// <param name="stack"></param>
        /// <param name="blockEntityData"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void SetCarried(Entity entity, CarrySlot slot, ItemStack stack, ITreeAttribute blockEntityData)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            entity.WatchedAttributes.Set(stack, CarryAttributeKey, slot.ToString(), "Stack");
            ((SyncedTreeAttribute)entity.WatchedAttributes).MarkPathDirty(CarryAttributeKey);

            if ((entity.World.Side == EnumAppSide.Server) && (blockEntityData != null))
                entity.Attributes.Set(blockEntityData, CarryAttributeKey, slot.ToString(), "Data");

            var behavior = stack.Block.GetBehaviorOrDefault(BlockBehaviorCarryable.Default);
            var slotSettings = behavior.Slots[slot];

            if (slotSettings?.Animation != null)
                entity.StartAnimation(slotSettings.Animation);

            if (entity is EntityAgent agent)
            {
                var speed = slotSettings?.WalkSpeedModifier ?? 0.0F;
                if (speed != 0.0F && !ModConfig.AllowSprintWhileCarrying)
                {
                    agent.Stats.Set("walkspeed",
                    $"{CarrySystem.ModId}:{slot}", speed, false);
                }

                if (slot == CarrySlot.Hands) LockedItemSlot.Lock(agent.RightHandItemSlot);
                if (slot != CarrySlot.Back) LockedItemSlot.Lock(agent.LeftHandItemSlot);
                CarryHandler.SendLockSlotsMessage(agent as EntityPlayer);
            }
        }

        /// <summary>
        /// Removes the CarriedBlock from the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="slot"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void RemoveCarried(Entity entity, CarrySlot slot)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var animation = entity.GetCarried(slot)?.GetCarryableBehavior()?.Slots?[slot]?.Animation;
            if (animation != null) entity.StopAnimation(animation);

            if (entity is EntityAgent agent)
            {
                agent.Stats.Remove("walkspeed", $"{CarrySystem.ModId}:{slot}");

                if (slot == CarrySlot.Hands) LockedItemSlot.Restore(agent.RightHandItemSlot);
                if (slot != CarrySlot.Back) LockedItemSlot.Restore(agent.LeftHandItemSlot);
                CarryHandler.SendLockSlotsMessage(agent as EntityPlayer);
            }

            entity.WatchedAttributes.Remove(CarryAttributeKey, slot.ToString());
            ((SyncedTreeAttribute)entity.WatchedAttributes).MarkPathDirty(CarryAttributeKey);
            entity.Attributes.Remove(CarryAttributeKey, slot.ToString());
        }

        /// <summary>
        ///   Attempts to swap the <see cref="CarriedBlock"/>s currently carried in the
        ///   entity's <paramref name="first"/> and <paramref name="second"/> slots.
        /// </summary>
        /// <param name="entity">The entity whose carried blocks are being swapped.</param>
        /// <param name="first">The first carry slot.</param>
        /// <param name="second">The second carry slot.</param>
        /// <exception cref="ArgumentNullException"> Thrown if entity is null. </exception>
        public bool SwapCarried(Entity entity, CarrySlot first, CarrySlot second)
        {
            if (first == second) throw new ArgumentException("Slots can't be the same");

            var carriedFirst = GetCarried(entity, first);
            var carriedSecond = GetCarried(entity, second);
            if ((carriedFirst == null) && (carriedSecond == null)) return false;

            RemoveCarried(entity, first);
            RemoveCarried(entity, second);

            carriedFirst?.Set(entity, second);
            carriedSecond?.Set(entity, first);

            return true;
        }        

        /// <summary>
        /// Gets a CarriedBlock from the world at the specified position and slot.
        /// The block is removed from the world.
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="slot"></param>
        /// <param name="checkIsCarryable">If <c>true</c>, checks if the block is carryable in a particular slot.</param>
        /// <returns></returns>
        public CarriedBlock GetCarriedFromWorld(BlockPos pos, CarrySlot slot, bool checkIsCarryable = false)
        {
            var carried = CreateCarriedFromBlockPos(pos, slot);
            if (carried == null) return null;

            if (checkIsCarryable && !IsCarryable(carried.Block, slot)) return null;

            var world = Api.World;
            world.BlockAccessor.SetBlock(0, pos);
            world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.ClearReinforcement(pos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
            return carried;
        }

        /// <summary>
        /// Checks if the block is carryable in the specified slot.
        /// </summary>
        /// <param name="block"></param>
        /// <param name="slot"></param>
        /// <returns></returns>
        public bool IsCarryable(Block block, CarrySlot slot)
        {
            return block.GetBehavior<BlockBehaviorCarryable>()?.Slots?[slot] != null;
        }

        /// <summary>
        /// Tries to carry a block from the specified position.
        /// Will check carry permissions and if the block is carryable.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="pos"></param>
        /// <param name="slot"></param>
        /// <param name="checkIsCarryable"></param>
        /// <param name="playSound"></param>
        /// <returns></returns>
        public bool TryPickUp(Entity entity, BlockPos pos,
                                 CarrySlot slot, bool checkIsCarryable = true, bool playSound = true)
        {
            if (!HasPermissionToCarry(entity, pos)) return false;
            if (GetCarried(entity, slot) != null) return false;
            var carried = GetCarriedFromWorld(pos, slot, checkIsCarryable);
            if (carried == null) return false;

            SetCarried(entity, carried);
            if (playSound) PlaySound(carried.Block, pos, entity as EntityPlayer);
            return true;
        }

        /// <summary>
        /// Tries to place the carriedBlock in the world, removing from entity if successful
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="carriedBlock"></param>
        /// <param name="selection"></param>
        /// <param name="dropped">If <c>true</c>, the block is set in world instead of placed, bypassing some checks.</param>
        /// <param name="playSound"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, bool dropped = false, bool playSound = true)
        {
            string failureCode = "__ignore__";
            return TryPlaceDown(entity, carriedBlock, selection, ref failureCode, dropped, playSound);
        }

        /// <summary>
        /// Tries to place the carriedBlock in the world, removing from entity if successful
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="carriedBlock"></param>
        /// <param name="selection"></param>
        /// <param name="dropped">If <c>true</c>, the block is set in world instead of placed, bypassing some checks.</param>
        /// <param name="playSound"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, ref string failureCode, bool dropped = false, bool playSound = true)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var world = Api.World;

            if (!world.BlockAccessor.IsValidPos(selection.Position)) return false;

            if (entity is EntityPlayer playerEntity && !dropped)
            {
                failureCode ??= "__ignore__";

                var player = world.PlayerByUid(playerEntity.PlayerUID);
                try
                {
                    // Add phantom Item to player's active slot so any related block placement code can fire. (Workaround for creature container)
                    player.InventoryManager.ActiveHotbarSlot.Itemstack = carriedBlock.ItemStack;

                    if (!carriedBlock.Block.TryPlaceBlock(world, player, carriedBlock.ItemStack, selection, ref failureCode))
                    {
                        // Remove phantom item from active slot if failed to place
                        player.InventoryManager.ActiveHotbarSlot.Itemstack = null;
                        return false;
                    }
                }
                catch (NullReferenceException ex)
                {
                    world.Logger.Error("Error occured while trying to place a carried block: " + ex.Message);
                    // Workaround was for null ref with reed chest - Leaving here in case of other issues
                    world.BlockAccessor.SetBlock(carriedBlock.Block.Id, selection.Position, carriedBlock.ItemStack);
                }
            }
            else
            {

                Block block = carriedBlock.Block;
                var meshFacing = selection.Clone().Face;

                // TODO: See if this can be derived from muliblock behavior
                if (carriedBlock.Block.HasBehavior<BlockBehaviorMultiblock>())
                {
                    var assetLocation = carriedBlock.Block.Code.Clone();
                    var baseCode = assetLocation.FirstCodePart();
                    assetLocation.Path = $"{baseCode}-{selection.Face.Code}";

                    if (meshFacing == BlockFacing.EAST || meshFacing == BlockFacing.WEST)
                    {
                        meshFacing = meshFacing.Opposite;
                    }
                    block = world.GetBlock(assetLocation);
                }

                world.BlockAccessor.SetBlock(block.Id, selection.Position, carriedBlock.ItemStack);

                // Set mesh angle to match the block facing
                // TODO: add fix for multiblock support
                carriedBlock.BlockEntityData.SetFloat("meshAngle", GetMeshAngle(meshFacing));

            }

            RestoreBlockEntityData(carriedBlock, selection.Position, dropped);
            world.BlockAccessor.MarkBlockDirty(selection.Position);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(selection.Position);

            RemoveCarried(entity, carriedBlock.Slot);
            if (playSound) PlaySound(carriedBlock.Block, selection.Position, dropped ? null : entity as EntityPlayer);

            if (dropped)
            {
                CarryEvents?.TriggerBlockDropped(world, selection.Position, entity, carriedBlock);
            }

            return true;
        }

        /// <summary>
        /// Tries to place the carried block at the specified position.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="carried"></param>
        /// <param name="selection"></param>
        /// <param name="placedAt">Position of where the block was placed. It may have replaced the selected block.</param>
        /// <param name="failureCode"></param>
        /// <returns></returns>
        public bool TryPlaceDownAt(IPlayer player, CarriedBlock carried,
                                     BlockSelection selection, out BlockPos placedAt, ref string failureCode)
        {
            var blockSelection = selection.Clone();
            var selectedBlock = player.Entity?.World?.BlockAccessor?.GetBlock(blockSelection.Position);

            if (selectedBlock == null)
            {
                placedAt = null;
                return false;
            }

            if (selectedBlock.IsReplacableBy(carried.Block))
            {
                blockSelection.Face = BlockFacing.UP;
                blockSelection.HitPosition.Y = 0.5;
            }
            else
            {
                blockSelection.Position.Offset(blockSelection.Face);
                blockSelection.DidOffset = true;
            }

            placedAt = blockSelection.Position;
            return TryPlaceDown(player.Entity, carried, blockSelection, ref failureCode);
        }


        /// <summary>
        /// Gets the mesh angle for the specified block facing.
        /// </summary>
        /// <param name="facing"></param>
        /// <returns></returns>
        private float GetMeshAngle(BlockFacing facing)
        {
            switch (facing.Code)
            {
                case "north": return 0f;
                case "east": return (float)(Math.PI / 2);
                case "south": return (float)Math.PI;
                case "west": return (float)(3 * Math.PI / 2);
                default: return 0f;
            }
        }

        /// <summary>
        /// Restores the block at a specified position with the entity data from the carried block.
        /// </summary>
        /// <param name="carriedBlock"></param>
        /// <param name="pos"></param>
        /// <param name="dropped"></param>
        private void RestoreBlockEntityData(CarriedBlock carriedBlock, BlockPos pos, bool dropped = false)
        {
            var world = Api.World;
            if ((world.Side != EnumAppSide.Server) || (carriedBlock?.BlockEntityData == null)) return;

            var blockEntityData = carriedBlock.BlockEntityData;
            // Set the block entity's position to the new position.
            // Without this, we get some funny behavior.
            blockEntityData.SetInt("posx", pos.X);
            blockEntityData.SetInt("posy", pos.Y);
            blockEntityData.SetInt("posz", pos.Z);

            var blockEntity = world.BlockAccessor.GetBlockEntity(pos);

            var delegates = CarryEvents?.OnRestoreEntityBlockData?.GetInvocationList();

            // Handle OnRestoreBlockEntityData events
            if (delegates != null)
            {
                foreach (var blockEntityDataDelegate in delegates.Cast<BlockEntityDataDelegate>())
                {
                    try
                    {
                        blockEntityDataDelegate(blockEntity, blockEntityData, dropped);
                    }
                    catch (Exception e)
                    {
                        world.Logger.Error(e.Message);
                    }
                }
            }

            blockEntity?.FromTreeAttributes(blockEntityData, world);
        } 

        /// <summary>
        /// Creates a CarriedBlock from the specified position in the world.
        /// Does not remove the block from the world or assign to an entity.
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="slot"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        private CarriedBlock CreateCarriedFromBlockPos(BlockPos pos, CarrySlot slot)
        {
            var world = Api.World;

            if (world == null) throw new ArgumentNullException(nameof(world));
            if (pos == null) throw new ArgumentNullException(nameof(pos));

            var block = world.BlockAccessor.GetBlock(pos);
            if (block.Id == 0) return null; // Can't pick up air.
            var stack = block.OnPickBlock(world, pos) ?? new ItemStack(block);

            ITreeAttribute blockEntityData = null;
            if (world.Side == EnumAppSide.Server)
            {
                var blockEntity = world.BlockAccessor.GetBlockEntity(pos);
                if (blockEntity != null)
                {
                    blockEntityData = new TreeAttribute();
                    blockEntity.ToTreeAttributes(blockEntityData);
                    blockEntityData = blockEntityData.Clone();
                    // We don't need to keep the position.
                    blockEntityData.RemoveAttribute("posx");
                    blockEntityData.RemoveAttribute("posy");
                    blockEntityData.RemoveAttribute("posz");
                    // And angle needs to be removed, or else it will
                    // override the angle set from block placement.
                    blockEntityData.RemoveAttribute("meshAngle");
                }
            }

            return new CarriedBlock(slot, stack, blockEntityData);
        }

        /// <summary>
        /// Checks if the entity has permission to carry the block at the specified position.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="pos"></param>
        /// <returns></returns>
        public bool HasPermissionToCarry(Entity entity, BlockPos pos)
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

        internal void PlaySound(Block block, BlockPos pos, 
                        EntityPlayer entityPlayer = null)
        {
            const float SOUND_RANGE = 16.0F;
            const float SOUND_VOLUME = 1.0F;

            if (block.Sounds?.Place == null) return;

            var world = Api.World;
            var player = (entityPlayer != null) && (world.Side == EnumAppSide.Server)
                ? entityPlayer?.Player : null;

            world.PlaySoundAt(block.Sounds.Place,
                pos.X + 0.5, pos.Y + 0.25, pos.Z + 0.5, player,
                range: SOUND_RANGE, volume: SOUND_VOLUME);
        }               
    }
}