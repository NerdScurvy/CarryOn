using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CarryOn.Common
{
    public class BlockBehaviorMoldRackTransfer : BlockBehavior
    {

        public static string Name { get; } = "MoldRackTransfer";

        public BlockBehaviorMoldRackTransfer(Block block) : base(block)
        {
        }

        public bool IsTransferEnabled(ICoreAPI api)
        {
            return api?.World?.Config?.GetBool("carryon:TransferMoldRackEnabled") ?? false;
        }

        /// <summary>
        /// Checks if an item can be put into the mold rack.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="blockEntity"></param>
        /// <param name="index">Index of the slot in the rack</param>
        /// <param name="itemStack">Item stack to put into the mold rack</param>
        /// <param name="blockEntityData">Block Entity Data for the carried itemStack</param>
        /// <param name="failureCode">
        ///     Used to define error codes or control codes for the interaction.
        ///         __stop__ - Stop all further CarryOn interactions and default handling
        ///         __default__ - Stop all further CarryOn interactions and continue with default handling
        /// </param>
        /// <param name="onScreenErrorMessage"></param>
        /// <returns>
        ///     True if the item can be put into the mold rack. Interaction Spinner will start and default handling will be prevented.
        ///     False if not. CarryOn will display an error message if OnScreenErrorMessage is set. 
        ///         Error message will stop further processing similar to the __stop__ code.
        ///         CarryOn can be directed to stop all further interactions if failureCode is set to "__stop__". 
        ///         Otherwise will continue checking CarryOn interactions.
        /// </returns>
        public bool CanPutCarryable(IPlayer player, BlockEntity blockEntity, int index, ItemStack itemStack, TreeAttribute blockEntityData, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = null;
            onScreenErrorMessage = null;

            var moldRack = blockEntity as BlockEntityMoldRack;
            if (moldRack == null || index < 0 || index >= moldRack.Inventory.Count)
            {
                // Invalid slot - tell caller to continue to next interaction
                return false;
            }

            if(moldRack.Inventory[index]?.Empty == false)
            {
                failureCode = "__stop__";
                onScreenErrorMessage = $"Target slot is occupied";
                return false;
            }

            var world = player.Entity.Api.World;

            var blockName = block.GetPlacedBlockName(world, blockEntity.Pos);

            var moldRackable = itemStack?.Collectible?.Attributes?["moldrackable"]?.AsBool() ?? false;

            if (!moldRackable)
            {
                failureCode = "put-block-incompatible";
                onScreenErrorMessage = $"Cannot put carried block in {blockName}";
                return false;
            }

            // Only server has blockEntityData
            if (world.Side == EnumAppSide.Server && blockEntityData != null)
            {

                if (blockEntityData.GetAsBool("shattered", false))
                {
                    failureCode = "put-block-state-incompatible";
                    onScreenErrorMessage = $"Cannot put shattered mold in {blockName}";
                    return false;
                }


                if (blockEntityData.GetAsInt("fillLevel", -1) > 0)
                {
                    failureCode = "put-block-state-incompatible";
                    onScreenErrorMessage = $"Cannot put non-empty mold in {blockName}";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks if an item can be taken from the mold rack.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="blockEntity"></param>
        /// <param name="index"></param>
        /// <param name="failureCode"></param>
        /// <param name="onScreenErrorMessage"></param>
        /// <returns>
        ///     True if the item can be taken from the mold rack. Interaction Spinner will start and default handling will be prevented.
        ///     False if not. CarryOn will display an error message if OnScreenErrorMessage is set.
        ///         Error message will stop further processing similar to the __stop__ code.
        ///         CarryOn can be directed to stop all further interactions if failureCode is set to "__stop__". 
        ///         Otherwise will continue checking CarryOn interactions.
        /// </returns>
        public bool CanTakeCarryable(IPlayer player, BlockEntity blockEntity, int index, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = null;
            onScreenErrorMessage = null;

            // Ensure correct type
            var moldRack = blockEntity as BlockEntityMoldRack;
            var sourceSlot = moldRack?.Inventory?[index];

            // Check if the moldRack is valid and the slot is not empty
            if (moldRack == null || index < 0 || index >= moldRack.Inventory.Count || sourceSlot?.Empty == true)
            {
                // Nothing to take - tell the caller to continue to the next interaction (pickup the rack if carryable)
                return false;
            }

            if (!HasBehavior(sourceSlot.Itemstack.Block, "BlockBehaviorCarryable"))
            {
                // Item in slot is not carryable - skip further CarryOn interactions and allow default handling
                // If the item in the slot is a shield then pick it up normally
                failureCode = "__default__";

                return false;
            }
            return true;
        }


        /// <summary>
        /// Try to put carried item into the mold rack.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="blockEntity"></param>
        /// <param name="index"></param>
        /// <param name="itemstack"></param>
        /// <param name="blockEntityData"></param>
        /// <param name="failureCode"></param>
        /// <param name="onScreenErrorMessage"></param>
        /// <returns></returns>
        public bool TryPutCarryable(IPlayer player, BlockEntity blockEntity, int index, ItemStack itemstack, TreeAttribute blockEntityData, out string failureCode, out string onScreenErrorMessage)
        {

            if (!CanPutCarryable(player, blockEntity, index, itemstack, blockEntityData, out failureCode, out onScreenErrorMessage))
            {
                return false;
            }

            if (player.Entity.Api.Side == EnumAppSide.Client)
            {
                // Prevent transfer on client side but tell to continue server side
                failureCode = "__continue__";
                return false;
            }

            var world = player.Entity.Api.World;
            var moldRack = blockEntity as BlockEntityMoldRack;

            // Place the itemStack into the slot
            var sinkSlot = moldRack.Inventory[index];
            sinkSlot.Itemstack = itemstack.Clone();

            sinkSlot.MarkDirty();
            moldRack.MarkDirty(true);
            world.PlaySoundAt(new AssetLocation("sounds/player/build"), player);
            AssetLocation code = itemstack?.Collectible.Code;
            world.Logger.Audit($"{player.PlayerName} Put 1x{code} into Rack at {blockEntity.Pos}.");
            return true;
        }

        /// <summary>
        /// Try to take item from the mold rack to be carried.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="blockEntity"></param>
        /// <param name="index"></param>
        /// <param name="itemstack"></param>
        /// <param name="blockEntityData"></param>
        /// <param name="failureCode"></param>
        /// <param name="onScreenErrorMessage"></param>
        /// <returns></returns>
        public bool TryTakeCarryable(IPlayer player, BlockEntity blockEntity, int index, out ItemStack itemstack, out TreeAttribute blockEntityData, out string failureCode, out string onScreenErrorMessage)
        {
            itemstack = null;
            blockEntityData = null;

            if (!CanTakeCarryable(player, blockEntity, index, out failureCode, out onScreenErrorMessage))
            {
                return false;
            }

            if (player.Entity.Api.Side == EnumAppSide.Client)
            {
                // Prevent transfer on client side but tell to continue server side
                failureCode = "__continue__";
                return false;
            }

            var world = player.Entity.Api.World;
            var moldRack = blockEntity as BlockEntityMoldRack;
            var sourceSlot = moldRack.Inventory[index];
            // Clone the itemStack to return
            itemstack = sourceSlot.Itemstack.Clone();

            // Remove the item from the inventory (replicating TryTake core logic)
            sourceSlot.Itemstack = null;
            sourceSlot.MarkDirty();
            moldRack.MarkDirty(true);
            world.PlaySoundAt(new AssetLocation("sounds/player/build"), player);
            AssetLocation code = itemstack?.Collectible.Code;
            world.Logger.Audit($"{player.PlayerName} Took 1x{code} into Rack at {blockEntity.Pos}.");
            return true;
        }

        private bool HasBehavior(Block block, string behaviorClassName)
        {
            return block?.BlockBehaviors?.Any(b => b.GetType().Name == behaviorClassName) ?? false;
        }
    }
}