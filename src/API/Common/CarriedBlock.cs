using System;
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
    public class CarriedBlock
    {
        /// <summary> Root tree attribute on an entity which stores carried data. </summary>
        public static string AttributeId { get; }
            = $"{CarrySystem.ModId}:Carried";

        public CarrySlot Slot { get; }

        public ItemStack ItemStack { get; }
        public Block Block => ItemStack.Block;

        public ITreeAttribute BlockEntityData { get; }

        public CarriedBlock(CarrySlot slot, ItemStack stack, ITreeAttribute blockEntityData)
        {
            Slot = slot;
            ItemStack = stack ?? throw new ArgumentNullException(nameof(stack));
            BlockEntityData = blockEntityData;
        }
 
        public BlockBehaviorCarryable Behavior
            => Block.GetBehaviorOrDefault(BlockBehaviorCarryable.Default);

        /// <summary> Gets the <see cref="CarriedBlock"/> currently
        ///           carried by the specified entity, or null if none. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity is null. </exception>
        public static CarriedBlock Get(Entity entity, CarrySlot slot)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var slotAttribute = entity.WatchedAttributes
                .TryGet<ITreeAttribute>(AttributeId, slot.ToString());
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
                ? entity.Attributes.TryGet<ITreeAttribute>(AttributeId, slot.ToString(), "Data")
                : null;

            return new CarriedBlock(slot, stack, blockEntityData);
        }

        /// <summary> Stores the specified stack and blockEntityData (may be null)
        ///           as the <see cref="CarriedBlock"/> of the entity in that slot. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity is null. </exception>
        public static void Set(Entity entity, CarrySlot slot, ItemStack stack, ITreeAttribute blockEntityData)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            entity.WatchedAttributes.Set(stack, AttributeId, slot.ToString(), "Stack");
            ((SyncedTreeAttribute)entity.WatchedAttributes).MarkPathDirty(AttributeId);

            if ((entity.World.Side == EnumAppSide.Server) && (blockEntityData != null))
                entity.Attributes.Set(blockEntityData, AttributeId, slot.ToString(), "Data");

            var behavior = stack.Block.GetBehaviorOrDefault(BlockBehaviorCarryable.Default);
            var slotSettings = behavior.Slots[slot];

            if (slotSettings?.Animation != null)
                entity.StartAnimation(slotSettings.Animation);

            if (entity is EntityAgent agent)
            {
                var speed = ModConfig.IgnoreCarrySpeedPenalty ? 0.0f : slotSettings?.WalkSpeedModifier ?? 0.0F;
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

        /// <summary> Stores this <see cref="CarriedBlock"/> as the
        ///           specified entity's carried block in that slot. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity is null. </exception>
        public void Set(Entity entity, CarrySlot slot)
            => Set(entity, slot, ItemStack, BlockEntityData);

        /// <summary> Removes the <see cref="CarriedBlock"/>
        ///           carried by the specified entity in that slot. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if entity is null. </exception>
        public static void Remove(Entity entity, CarrySlot slot)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var animation = entity.GetCarried(slot)?.Behavior?.Slots?[slot]?.Animation;
            if (animation != null) entity.StopAnimation(animation);

            if (entity is EntityAgent agent)
            {
                agent.Stats.Remove("walkspeed", $"{CarrySystem.ModId}:{slot}");

                if (slot == CarrySlot.Hands) LockedItemSlot.Restore(agent.RightHandItemSlot);
                if (slot != CarrySlot.Back) LockedItemSlot.Restore(agent.LeftHandItemSlot);
                CarryHandler.SendLockSlotsMessage(agent as EntityPlayer);
            }

            entity.WatchedAttributes.Remove(AttributeId, slot.ToString());
            ((SyncedTreeAttribute)entity.WatchedAttributes).MarkPathDirty(AttributeId);
            entity.Attributes.Remove(AttributeId, slot.ToString());
        }

        /// <summary> Creates a <see cref="CarriedBlock"/> from the specified world
        ///           and position, but doesn't remove it. Returns null if unsuccessful. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if world or pos is null. </exception>
        public static CarriedBlock CreateFromBlockPos(IWorldAccessor world, BlockPos pos, CarrySlot slot)
        {
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

        /// <summary> Attempts to pick up a <see cref="CarriedBlock"/> from the specified
        ///           world and position, removing it. Returns null if unsuccessful. </summary>
        /// <exception cref="ArgumentNullException"> Thrown if world or pos is null. </exception>
        public static CarriedBlock PickUp(IWorldAccessor world, BlockPos pos,
                                          CarrySlot slot, bool checkIsCarryable = false)
        {
            var carried = CreateFromBlockPos(world, pos, slot);
            if (carried == null) return null;

            if (checkIsCarryable && !carried.Block.IsCarryable(slot)) return null;

            world.BlockAccessor.SetBlock(0, pos);
            world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.ClearReinforcement(pos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
            return carried;
        }

        /// <summary> Attempts to place down a <see cref="CarriedBlock"/> at the specified world,
        ///           selection and by the entity (if any), returning whether it was successful.
        ///           </summary>
        /// <exception cref="ArgumentNullException"> Thrown if world or pos is null. </exception>
        public bool PlaceDown(ref string failureCode, IWorldAccessor world, BlockSelection selection, Entity entity, bool dropped = false, bool playSound = true)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (!world.BlockAccessor.IsValidPos(selection.Position)) return false;

            if (entity is EntityPlayer playerEntity && !dropped)
            {
                failureCode ??= "__ignore__";

                var player = world.PlayerByUid(playerEntity.PlayerUID);
                try{
                    // Add phantom Item to player's active slot so any related block placement code can fire. (Workaround for creature container)
                    player.InventoryManager.ActiveHotbarSlot.Itemstack = ItemStack;

                    // Force sneak mode for placing blocks (in case carry keybinds are different)
                    // This is a workaround for some blocks like Molds which require sneak to be placed
                    playerEntity.Controls.ShiftKey = true;
                    // Force ctrl key to be off - workaround for more piles
                    playerEntity.Controls.CtrlKey = false;

                    if (!Block.TryPlaceBlock(world, player, ItemStack, selection, ref failureCode))
                    {
                        // Remove phantom item from active slot if failed to place
                        player.InventoryManager.ActiveHotbarSlot.Itemstack = null;
                        return false;
                    }
                }catch(NullReferenceException ex){
                    world.Logger.Error("Error occured while trying to place a carried block: " + ex.Message);
                    // Workaround was for null ref with reed chest - Leaving here in case of other issues
                    world.BlockAccessor.SetBlock(Block.Id, selection.Position, ItemStack);
                }
            }
            else
            {
                world.BlockAccessor.SetBlock(Block.Id, selection.Position, ItemStack);

                // TODO: Handle type attribute.

            }

            RestoreBlockEntityData(world, selection.Position, dropped);
            world.BlockAccessor.MarkBlockDirty(selection.Position);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(selection.Position);
            if (entity != null) Remove(entity, Slot);
            if (playSound) PlaySound(selection.Position, world, dropped ? null : entity as EntityPlayer);

            if (dropped)
            {
                world.GetCarryEvents()?.TriggerBlockDropped(world, selection.Position, entity, this);
            }

            return true;
        }

        /// <summary>
        /// <para>
        ///   Restores the carriedBlock.BlockEntityData to the
        ///   block entity at the specified world and position.
        /// </para>
        /// <para>
        ///   Does nothing if executed on client side,
        ///   <see cref="carriedBlock"/> is null, or there's
        ///   no entity at the specified location.
        /// </para>
        /// </summary>
        public void RestoreBlockEntityData(IWorldAccessor world, BlockPos pos, bool dropped = false)
        {
            if ((world.Side != EnumAppSide.Server) || (BlockEntityData == null)) return;

            var blockEntityData = BlockEntityData;
            // Set the block entity's position to the new position.
            // Without this, we get some funny behavior.
            blockEntityData.SetInt("posx", pos.X);
            blockEntityData.SetInt("posy", pos.Y);
            blockEntityData.SetInt("posz", pos.Z);

            var blockEntity = world.BlockAccessor.GetBlockEntity(pos);

            var delegates = world.GetCarryEvents()?.OnRestoreEntityBlockData?.GetInvocationList();

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

        internal void PlaySound(BlockPos pos, IWorldAccessor world,
                        EntityPlayer entityPlayer = null)
        {
            const float SOUND_RANGE = 16.0F;
            const float SOUND_VOLUME = 1.0F;

            // TODO: In 1.7.0, Block.Sounds should not be null anymore.
            if (Block.Sounds?.Place == null) return;

            var player = (entityPlayer != null) && (world.Side == EnumAppSide.Server)
                ? entityPlayer?.Player : null;

            world.PlaySoundAt(Block.Sounds.Place,
                pos.X + 0.5, pos.Y + 0.25, pos.Z + 0.5, player,
                range: SOUND_RANGE, volume: SOUND_VOLUME);
        }
    }
}
