using System;
using System.Linq;
using CarryOn.API.Event;
using CarryOn.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.API.Common
{
    public class CarryManager : ICarryManager
    {

        public ICoreAPI Api => CarrySystem?.Api;

        public CarrySystem CarrySystem { get; private set; }

        public CarryEvents CarryEvents => CarrySystem?.CarryEvents;

        public CarryManager(CarrySystem carrySystem)
        {
            CarrySystem = carrySystem ?? throw new ArgumentNullException(nameof(carrySystem));
        }

        public CarriedBlock GetCarriedBlock(Entity entity, CarrySlot slot)
        {
            CarriedBlock carriedBlock = entity?.GetCarried(slot);
            return carriedBlock;
        }

        public void SetCarriedBlock(Entity entity, CarriedBlock carriedBlock)
        {
            CarriedBlockExtended.Set(entity, carriedBlock.Slot, carriedBlock.ItemStack, carriedBlock.BlockEntityData);
        }

        public void SetCarriedBlock(Entity entity, CarrySlot slot, ItemStack itemStack, ITreeAttribute blockEntityData)
        {
            CarriedBlockExtended.Set(entity, slot, itemStack, blockEntityData);
        }

        public void RemoveCarriedBlock(Entity entity, CarrySlot slot)
        {
            CarriedBlockExtended.Remove(entity, slot);
        }

        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, bool dropped = false, bool playSound = true)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var world = Api.World;

            if (!world.BlockAccessor.IsValidPos(selection.Position)) return false;

            if (entity is EntityPlayer playerEntity && !dropped)
            {
                var failureCode = "__ignore__";

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
            if (entity != null) RemoveCarriedBlock(entity, carriedBlock.Slot);
            if (playSound) PlaySound(carriedBlock.Block, selection.Position, world, dropped ? null : entity as EntityPlayer);

            if (dropped)
            {
                CarryEvents?.TriggerBlockDropped(world, selection.Position, entity, carriedBlock);
            }

            return true;
        }

        private float GetMeshAngle(BlockFacing facing)
        {
            switch (facing.Code)
            {
                case "north": return 0f;
                case "east":  return (float)(Math.PI / 2);
                case "south": return (float)Math.PI;
                case "west":  return (float)(3 * Math.PI / 2);
                default:      return 0f;
            }
        }

        public void RestoreBlockEntityData(CarriedBlock carriedBlock, BlockPos pos, bool dropped = false)
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

        internal void PlaySound(Block block, BlockPos pos, IWorldAccessor world,
                        EntityPlayer entityPlayer = null)
        {
            const float SOUND_RANGE = 16.0F;
            const float SOUND_VOLUME = 1.0F;

            if (block.Sounds?.Place == null) return;

            var player = (entityPlayer != null) && (world.Side == EnumAppSide.Server)
                ? entityPlayer?.Player : null;

            world.PlaySoundAt(block.Sounds.Place,
                pos.X + 0.5, pos.Y + 0.25, pos.Z + 0.5, player,
                range: SOUND_RANGE, volume: SOUND_VOLUME);
        }               
    }
}