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

        public bool CanPutCarryable(IPlayer player, BlockEntity blockEntity, int index, ItemStack itemStack, TreeAttribute blockEntityData, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = null;
            onScreenErrorMessage = null;  

            var moldRack = blockEntity as BlockEntityMoldRack;
            if (moldRack == null )
            {
                failureCode = "invalid-blockentity";
                return false;
            }

            if (index < 0 || index >= moldRack.Inventory.Count)
            {
                failureCode = "invalid-index";
                return false;
            }

            var sinkSlot = moldRack.Inventory[index];
            if (!sinkSlot.Empty)
            {
                failureCode = "slot-not-empty";
                return false;
            }         

            var world = player.Entity.Api.World;

            var blockName = block.GetPlacedBlockName(world, blockEntity.Pos);
            var collAtrib = itemStack?.Collectible?.Attributes;
            var moldRackable = collAtrib?["moldrackable"]?.AsBool() ?? false;
            if(!moldRackable){
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

        public bool CanTakeCarryable(IPlayer player, BlockEntity blockEntity, int index, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = null;
            onScreenErrorMessage = null;

            // Ensure correct type
            var moldRack = blockEntity as BlockEntityMoldRack;
            if (moldRack == null )
            {
                failureCode = "invalid-blockentity";
                return false;
            }

            if (index < 0 || index >= moldRack.Inventory.Count)
            {
                failureCode = "invalid-index";
                return false;
            }

            var sourceSlot = moldRack.Inventory[index];
            if (sourceSlot.Empty)
            {
                failureCode = "slot-not-empty";
                return false;
            }

            if (!HasBehavior(sourceSlot.Itemstack.Block, "BlockBehaviorCarryable"))
            {
                failureCode = "block-not-carryable";
                // Could use localisation or leave null and let CarryOn handle the error message
                onScreenErrorMessage = $"Cannot take {sourceSlot?.Itemstack?.GetName()??"item"} in hands";
                return false;
            }            
            return true;
        }

        public bool TryPutCarryable(IPlayer player, BlockEntity blockEntity, int index, ItemStack itemstack, TreeAttribute blockEntityData, out string failureCode, out string onScreenErrorMessage)
        {

            if (!CanPutCarryable(player, blockEntity, index, itemstack, blockEntityData, out failureCode, out onScreenErrorMessage)) {
                return false;    
            }

            if (player.Entity.Api.Side == EnumAppSide.Client)
            {
                // Prevent transfer on client side but tell to continue server side
                failureCode = "continue";
                return false;
            }

            var world = player.Entity.Api.World;
            var moldRack = blockEntity as BlockEntityMoldRack;

            // Place the itemStack into the slot
            var sinkSlot = moldRack.Inventory[index];
            sinkSlot.Itemstack = itemstack.Clone();

            sinkSlot.MarkDirty();
            moldRack.MarkDirty(true);
            world.PlaySoundAt(new AssetLocation("sounds/player/build"), player, player);
            AssetLocation code = itemstack?.Collectible.Code;
            world.Logger.Audit($"{player.PlayerName} Put 1x{code} into Rack at {blockEntity.Pos}.");
            return true;
        }

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
                failureCode = "continue";
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
            return block?.BlockBehaviors.Any(b => b.GetType().Name == behaviorClassName) ?? false;
        }
    }
}