using System;
using CarryOn.API.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Utility
{
    public class BlockUtils
    {

        /// <summary>
        /// Creates a CarriedBlock from the specified position in the world.
        /// Does not remove the block from the world or assign to an entity.
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="slot"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static CarriedBlock CreateCarriedFromBlockPos(IWorldAccessor world, BlockPos pos, CarrySlot slot)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (pos == null) throw new ArgumentNullException(nameof(pos));

            var block = world.BlockAccessor.GetBlock(pos);
            if (block.Id == 0) return null; // Can't pick up air.
            var stack = block.OnPickBlock(world, pos) ?? new ItemStack(block);

            ITreeAttribute blockEntityData = null;
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

            return new CarriedBlock(slot, stack, blockEntityData);
        }

        /// <summary> 
        /// Returns the position that the specified block would
        /// be placed at for the specified block selection.
        /// </summary>
        public static BlockPos GetPlacedPosition(IBlockAccessor blockAccessor, BlockSelection selection, Block block)
        {
            if (selection == null) return null;
            var position = selection.Position.Copy();
            var clickedBlock = blockAccessor.GetBlock(position);
            if (!clickedBlock.IsReplacableBy(block))
            {
                position.Offset(selection.Face);
                var replacedBlock = blockAccessor.GetBlock(position);
                if (!replacedBlock.IsReplacableBy(block)) return null;
            }
            return position;
        }

        /// <summary>
        /// Gets the origin block of a multiblock structure if the specified block is part of one.
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="position"></param>
        /// <param name="block"></param>
        /// <returns>will return the origin block if it exists, otherwise the original block</returns>
        public static Block GetOriginBlockIfMultiBlock(IBlockAccessor blockAccessor, BlockPos position, Block block)
        {
            if (position == null || block == null || blockAccessor == null) return block;

            if (block is BlockMultiblock multiblock)
            {
                var multiPosition = position.Copy();
                multiPosition.Add(multiblock.OffsetInv);
                return blockAccessor.GetBlock(multiPosition);
            }
            return block;
        }

        /// <summary>
        /// Get the block position for the main block within for a multiblock structure
        /// </summary>
        public static BlockPos GetMultiblockOrigin(BlockPos position, BlockMultiblock multiblock)
        {
            if (position == null) return null;

            if (multiblock != null)
            {
                var multiPosition = position.Copy();
                multiPosition.Add(multiblock.OffsetInv);
                return multiPosition;
            }
            return position;
        }

        /// <summary>
        /// Create a new block selection pointing to the main block within a multiblock structure
        /// </summary>
        public static BlockSelection GetMultiblockOriginSelection(IBlockAccessor blockAccessor, BlockSelection blockSelection)
        {
            if (blockSelection?.Block is BlockMultiblock multiblock)
            {
                var position = GetMultiblockOrigin(blockSelection.Position, multiblock);
                var block = blockAccessor.GetBlock(position);
                var selection = blockSelection.Clone();
                selection.Position = position;
                selection.Block = block;

                return selection;
            }
            return blockSelection;
        }

        /// <summary>
        /// Converts a block inventory attribute to a backpack attribute.
        /// </summary>
        /// <param name="blockInventory"></param>
        /// <returns></returns>
        public static ITreeAttribute ConvertBlockInventoryToBackpack(ITreeAttribute blockInventory)
        {
            var backpack = new TreeAttribute();
            if (blockInventory == null) return backpack; // graceful fallback

            var slotCount = blockInventory.GetInt("qslots");
            var slots = blockInventory.GetTreeAttribute("slots");

            // create backpack slots and copy items
            var backpackSlots = new TreeAttribute();
            for (int i = 0; i < slotCount; i++)
            {
                var slotKey = $"slot-{i}";

                // This will populate the slot even if null
                var itemstack = slots?.GetItemstack(i.ToString())?.Clone();
                backpackSlots.SetItemstack(slotKey, itemstack);
            }

            // Store backpack slots
            // Note: backpack does not have "qslots"
            backpack.SetAttribute("slots", backpackSlots);
            return backpack;
        }

        /// <summary>
        /// Converts a backpack attribute to a block inventory attribute.
        /// </summary>
        /// <param name="backpack"></param>
        /// <returns></returns>
        public static ITreeAttribute ConvertBackpackToBlockInventory(ITreeAttribute backpack)
        {
            var blockInventory = new TreeAttribute();
            if (backpack == null || backpack.Count == 0) return blockInventory; // graceful fallback

            var backpackSlots = backpack.GetTreeAttribute("slots");
            var count = backpackSlots.Count;

            blockInventory.SetInt("qslots", count);
            var slotsAttribute = new TreeAttribute();

            for (var i = 0; i < count; i++)
            {
                var value = backpackSlots.Values[i];
                if (value?.GetValue() == null) continue;
                slotsAttribute.SetAttribute(i.ToString(), value.Clone());
            }

            blockInventory.SetAttribute("slots", slotsAttribute);
            return blockInventory;
        }
    }
}