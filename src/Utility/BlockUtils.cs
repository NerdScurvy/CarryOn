using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Utility
{
    public class BlockUtils
    {

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

    }
}