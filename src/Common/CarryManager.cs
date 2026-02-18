using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Event;
using CarryOn.Server.Logic;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;


namespace CarryOn.API.Common
{
    public class CarryManager
    {
        
        public ICoreAPI Api { get; private set; }

        public CarrySystem CarrySystem { get; private set; }

        public CarryEvents CarryEvents => CarrySystem?.CarryEvents;


        public CarryManager(ICoreAPI api, CarrySystem carrySystem)
        {
            CarrySystem = carrySystem ?? throw new ArgumentNullException(nameof(carrySystem));
            Api = api ?? throw new ArgumentNullException(nameof(api));
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


        internal void PlaySound(Block block, BlockPos pos,
                        EntityPlayer entityPlayer = null)
        {
            const float SOUND_RANGE = 16.0F;
            const float SOUND_VOLUME = 1.0F;

            var sound = block.Sounds?.Place.Location ?? new AssetLocation("sounds/player/build");

            if (sound == null) return;

            var world = Api.World;
            var player = (entityPlayer != null) && (world.Side == EnumAppSide.Server)
                ? entityPlayer?.Player : null;

            world.PlaySoundAt(sound,
                pos.X + 0.5, pos.Y + 0.25, pos.Z + 0.5, player,
                range: SOUND_RANGE, volume: SOUND_VOLUME);
        }



        /// <summary>
        /// Restores the block at a specified position with the entity data from the carried block.
        /// </summary>
        /// <param name="carriedBlock"></param>
        /// <param name="pos"></param>
        /// <param name="dropped">Signal block was dropped to any delegates</param>
        public void RestoreBlockEntityData(IWorldAccessor world, CarriedBlock carriedBlock, BlockPos pos, bool dropped = false)
        {
            if ((world.Side != EnumAppSide.Server) || (carriedBlock?.BlockEntityData == null)) return;

            var delegates = CarryEvents?.BeforeRestoreBlockEntityData?.GetInvocationList();
            RestoreBlockEntityData(world, carriedBlock, pos, delegates: delegates, dropped: dropped);            
        }


        /// <summary>
        /// Restores the block at a specified position with the entity data from the carried block.
        /// </summary>
        /// <param name="carriedBlock"></param>
        /// <param name="pos"></param>
        /// <param name="delegates">Optional delegates to call when restoring block entity data</param>
        /// <param name="dropped">Signal block was dropped to any delegates</param>
        public void RestoreBlockEntityData(IWorldAccessor world, CarriedBlock carriedBlock, BlockPos pos, Delegate[] delegates = null, bool dropped = false)
        {
            if (carriedBlock?.BlockEntityData == null) return;

            var blockEntityData = carriedBlock.BlockEntityData;
            // Set the block entity's position to the new position.
            // Without this, we get some funny behavior.
            blockEntityData.SetInt("posx", pos.X);
            blockEntityData.SetInt("posy", pos.Y);
            blockEntityData.SetInt("posz", pos.Z);

            // Get the block entity at the position (Likely default from block just placed)
            var blockEntity = world.BlockAccessor.GetBlockEntity(pos);

            // Handle BeforeRestoreBlockEntityData events
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
            blockEntity?.MarkDirty(true);
        }

        /// <summary>
        /// Removes the CarriedBlock from the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="slot"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void RemoveCarried(Entity entity, CarrySlot slot)
            => CarriedBlock.Remove(entity, slot);

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

            BlockEntity droppedBlockEntity = null;
            var placementSucceeded = false;

            if (entity is EntityPlayer playerEntity && !dropped)
            {
                failureCode ??= "__ignore__";

                var player = world.PlayerByUid(playerEntity.PlayerUID);
                // Defensive null checks
                if (player == null || playerEntity == null || carriedBlock == null || carriedBlock.Block == null || selection == null)
                {
                    world.Logger.Error($"Error: Null reference detected while trying to place a carried block at {selection?.Position}. player: {player}, playerEntity: {playerEntity}, carriedBlock: {carriedBlock}, block: {carriedBlock?.Block}, selection: {selection}");
                    // Fallback logic for known issue (e.g., reed chest)
                    if (carriedBlock != null && carriedBlock.Block != null && selection != null && carriedBlock.ItemStack != null)
                    {
                        world.BlockAccessor.SetBlock(carriedBlock.Block.Id, selection.Position, carriedBlock.ItemStack);
                        droppedBlockEntity = world.BlockAccessor.GetBlockEntity(selection.Position);
                        placementSucceeded = true;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (!placementSucceeded)
                {

                    var shift = playerEntity.Controls.ShiftKey;
                    var ctrl = playerEntity.Controls.CtrlKey;

                    try
                    {

                        // Add phantom Item to player's active slot so any related block placement code can fire. (Workaround for creature container)
                        player.InventoryManager.ActiveHotbarSlot.Itemstack = carriedBlock.ItemStack;

                        // Force sneak mode for placing blocks (in case carry keybinds are different)
                        // This is a workaround for some blocks like Molds which require sneak to be placed
                        playerEntity.Controls.ShiftKey = true;

                        // Force ctrl key to be off - workaround for More Piles mod
                        playerEntity.Controls.CtrlKey = false;

                        if (!carriedBlock.Block.TryPlaceBlock(world, player, carriedBlock.ItemStack, selection, ref failureCode))
                        {
                            return false;
                        }
                        placementSucceeded = true;
                    }
                    finally
                    {
                        // Restore player controls
                        playerEntity.Controls.ShiftKey = shift;
                        playerEntity.Controls.CtrlKey = ctrl;

                        // Remove phantom item from active slot
                        player.InventoryManager.ActiveHotbarSlot.Itemstack = null;
                    }
                }
            }
            else
            {
                var meshFacing = selection.Clone().Face;
                var assetLocation = carriedBlock.Block.Code.Clone();
                var baseCode = assetLocation.FirstCodePart();
                assetLocation.Path = $"{baseCode}-{selection.Face.Code}";

                // Check for cardinal version of block
                var droppedBlock = world.GetBlock(assetLocation) ?? carriedBlock.Block;

                world.BlockAccessor.ExchangeBlock(droppedBlock.Id, selection.Position);
                world.BlockAccessor.SpawnBlockEntity(droppedBlock.EntityClass, selection.Position, carriedBlock.ItemStack);

                // Will trigger placement of multiblock sections
                droppedBlock?.OnBlockPlaced(world, selection.Position, carriedBlock.ItemStack);

                droppedBlockEntity = world.BlockAccessor.GetBlockEntity(selection.Position);
                placementSucceeded = true;

                // Set mesh angle opposite to block facing
                carriedBlock.BlockEntityData?.SetFloat("meshAngle", -GetMeshAngle(meshFacing));

            }
            
            if (!placementSucceeded) return false;
            RestoreBlockEntityData(world, carriedBlock, selection.Position, dropped: dropped);

            // Notify the dropped block entity that it has been placed
            droppedBlockEntity?.OnBlockPlaced(carriedBlock.ItemStack);

            world.BlockAccessor.TriggerNeighbourBlockUpdate(selection.Position);

            RemoveCarried(entity, carriedBlock.Slot);
            if (playSound) PlaySound(carriedBlock.Block, selection.Position, dropped ? null : entity as EntityPlayer);

            if (dropped)
            {
                CarryEvents?.TriggerBlockDropped(world, selection.Position, entity, carriedBlock);
            }

            CarrySystem.Api.World.Logger.Audit($"[{CarrySystem.ModId}] Player {entity?.GetName()} dropped carried block {carriedBlock.Block.Code} at {selection.Position}");
            return true;
        }



        /// <summary>
        /// Drops the carried blocks from specified slots on the entity.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="slots">Carried slots to drop</param>
        /// <param name="range">Radius to check for placement</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void DropCarried(Entity entity, IEnumerable<CarrySlot> slots, int range = 4)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));


            IServerPlayer player = (entity is EntityPlayer entityPlayer) ? (IServerPlayer)entityPlayer.Player : null;

            var remaining = slots
                .Select(s => entity.GetCarried(s))
                .Where(c => c != null)
                .OrderBy(c => c?.Block.GetBehavior<BlockBehaviorMultiblock>() != null)
                .ToList();
            if (remaining.Count == 0) return;

            BlockPos centerBlock = entity.Pos.AsBlockPos.UpCopy();
            var blockPlacer = new BlockPlacer(entity.Api);

            foreach (var carriedBlock in remaining)
            {
                var blockSelection = blockPlacer.FindBlockPlacement(carriedBlock.Block, centerBlock, range);

                if (blockSelection == null)
                {
                    DropBlockAsItem(carriedBlock, centerBlock, player, entity);
                    continue;
                }

                if (TryPlaceDown(entity, carriedBlock, blockSelection, dropped: true))
                {
                    continue;
                }
                DropBlockAsItem(carriedBlock, centerBlock, player, entity);
            }
        }

        /// <summary>
        /// Drops a carried block as an item.
        /// All items in the carried block's inventory will be dropped if applicable.
        /// </summary>
        /// <param name="carriedBlock"></param>
        /// <param name="centerBlock"></param>
        /// <param name="player"></param>
        /// <param name="entity"></param>
        public void DropBlockAsItem(CarriedBlock carriedBlock, BlockPos centerBlock, IServerPlayer player, Entity entity)
        {
            var world = Api.World;
            var blockDestroyed = false;
            var hadContents = false;
            var dropCount = 1;
            var dropVec3d = new Vec3d(centerBlock.X + 0.5, centerBlock.Y + 0.5, centerBlock.Z + 0.5);

            if (carriedBlock.BlockEntityData?["inventory"] is TreeAttribute inventory && inventory["slots"] is TreeAttribute invSlots)
            {
                foreach (var item in invSlots.Values.Cast<ItemstackAttribute>())
                {
                    var itemStack = (ItemStack)item.GetValue();
                    world.SpawnItemEntity(itemStack, dropVec3d);
                    hadContents = true;
                    dropCount++;
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
                        dropCount++;
                    }
                }
            }

            var breakSound = carriedBlock.Block.Sounds.GetBreakSound(player).Location ?? new AssetLocation("game:sounds/block/planks");
            world.PlaySoundAt(breakSound, (double)centerBlock.X, (double)centerBlock.Y, (double)centerBlock.Z);
            RemoveCarried(entity, carriedBlock.Slot);

            if (blockDestroyed)
                world.Logger.Audit($"[{CarrySystem.ModId}] Player {player?.PlayerName} dropped carried block {carriedBlock.Block.Code} at {centerBlock} and it was destroyed dropping {dropCount} items.");
            else
                world.Logger.Audit($"[{CarrySystem.ModId}] Player {player?.PlayerName} dropped carried block {carriedBlock.Block.Code} as item at {centerBlock} spilling {dropCount} items from its contents.");

            CarryEvents?.TriggerBlockDropped(world, centerBlock, entity, carriedBlock, blockDestroyed, hadContents, blockPlaced: false);

        }
    }
}