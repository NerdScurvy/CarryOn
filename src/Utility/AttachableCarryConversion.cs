using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CarryOn.Utility
{
    /// <summary>
    /// Conversions between a slot <see cref="ItemStack"/> holding an attached carryable block and the
    /// <see cref="CarriedBlock"/> carried in hands (or spawned as an entity). Both directions are used by
    /// the attach/detach behavior and the server-side recovery sweep, which must agree on the
    /// <c>backpack</c>/<c>carryonbackup</c>/<c>type</c> attribute layout.
    /// </summary>
    public static class AttachableCarryConversion
    {
        /// <summary>
        /// Returns true when the stack is a CarryOn-attached block (one that was placed onto an
        /// attachable slot by <see cref="ToAttachItemSlot"/> and therefore carries the
        /// <see cref="CarryCodes.AttributeKeys.CarryOnBackup"/> marker). Such stacks are reserved to
        /// CarryOn and must never be transferred out of an attachable slot through vanilla inventory
        /// interactions.
        /// </summary>
        public static bool IsCarryAttached(ItemStack? stack)
            => stack?.Attributes?.HasAttribute(CarryCodes.AttributeKeys.CarryOnBackup) == true;

        /// <summary>
        /// Rebuilds a <see cref="CarriedBlock"/> from a slot itemstack, converting the carried backpack
        /// back into block-entity inventory data. Returns null when the stack has no block, or when the
        /// block resolves to the missing-block placeholder (<see cref="CollectibleObject.IsMissing"/>,
        /// e.g. a stale id after the block's mod was removed) - such a stack can only be recovered as a
        /// raw unreadable carry via <see cref="ItemStackToCarriedTree"/>.
        /// Both <c>backpack</c> and <c>carryonbackup</c> are removed from the carried stack: the
        /// block-entity data they encode lives in the carried <see cref="CarriedBlock.BlockEntityData"/>,
        /// and <c>carryonbackup</c> is re-created on re-attach by <see cref="ToAttachItemSlot"/>.
        /// </summary>
        public static CarriedBlock? ItemStackToCarriedBlock(ItemStack stack)
        {
            var block = stack.Block;
            if (block == null || block.IsMissing) return null;

            var itemstackCopy = stack.Clone();
            var blockEntityData = BuildBlockEntityData(stack, block.Code.ToShortString());

            itemstackCopy.Attributes.Remove("backpack");
            itemstackCopy.Attributes.Remove(CarryCodes.AttributeKeys.CarryOnBackup);

            return new CarriedBlock(CarrySlot.Hands, itemstackCopy, blockEntityData);
        }

        /// <summary>
        /// Builds a carried slot tree for an attached stack whose block can no longer resolve
        /// (removed/disabled mod), so the stack can be adopted as a raw unreadable carry via
        /// <see cref="CarryOn.API.Common.Interfaces.ICarryManager.SetCarriedRaw"/>. The stack id
        /// and its block-entity data (rebuilt from <c>backpack</c>/<c>carryonbackup</c>) are
        /// preserved verbatim; contents survive and re-resolve once the source mod is re-enabled.
        /// </summary>
        public static TreeAttribute ItemStackToCarriedTree(ItemStack stack)
        {
            var itemstackCopy = stack.Clone();
            itemstackCopy.Attributes.Remove("backpack");
            itemstackCopy.Attributes.Remove(CarryCodes.AttributeKeys.CarryOnBackup);

            var tree = new TreeAttribute();
            tree.SetItemstack(CarryCodes.AttributeKeys.CarriedBlockData.Stack, itemstackCopy);
            tree[CarryCodes.AttributeKeys.CarriedBlockData.Data] = BuildBlockEntityData(stack, blockCode: null);
            return tree;
        }

        private static TreeAttribute BuildBlockEntityData(ItemStack stack, string? blockCode)
        {
            // carryonbackup holds the original block-entity data minus contents; backpack holds
            // the converted contents. Recombine into the carried Data tree the serializer reads.
            var sourceBackpack = stack.Attributes?["backpack"] as ITreeAttribute;
            var backupAttributes = stack.Attributes?[CarryCodes.AttributeKeys.CarryOnBackup] as TreeAttribute;
            var blockEntityData = backupAttributes ?? new TreeAttribute();

            var destInventory = InventoryConverter.ConvertBackpackToBlockInventory(sourceBackpack);
            if (destInventory != null)
                blockEntityData.SetAttribute("inventory", destInventory);
            else
                blockEntityData.RemoveAttribute("inventory");

            if (blockCode != null)
            {
                blockEntityData.SetString("blockCode", blockCode);
                blockEntityData.SetString("forBlockCode", blockCode);
            }

            blockEntityData.SetString(CarryCodes.AttributeKeys.Type,
                stack.Attributes?.GetString(CarryCodes.AttributeKeys.Type) ?? string.Empty);

            return blockEntityData;
        }

        /// <summary>
        /// Converts a <see cref="CarriedBlock"/> back into an itemstack suitable for an attachable slot,
        /// re-encoding the block-entity data as <c>backpack</c> + <c>carryonbackup</c> attributes.
        /// Returns null when the carried block carries no block-entity data.
        /// </summary>
        public static ItemSlot? ToAttachItemSlot(CarriedBlock carriedBlock)
        {
            var blockEntityData = carriedBlock.BlockEntityData;
            if (blockEntityData == null) return null;

            var sourceItemSlot = new DummySlot(null)
            {
                Itemstack = carriedBlock.ItemStack.Clone()
            };
            if (sourceItemSlot.Itemstack?.Attributes is not TreeAttribute attr)
            {
                return null;
            }

            var backupAttributes = blockEntityData.Clone();
            backupAttributes.RemoveAttribute("inventory");

            attr.SetString(CarryCodes.AttributeKeys.Type, blockEntityData.GetString(CarryCodes.AttributeKeys.Type));
            var backpack = InventoryConverter.ConvertBlockInventoryToBackpack(blockEntityData.GetTreeAttribute("inventory"));
            if (backpack != null)
                attr.SetAttribute("backpack", backpack);
            else
                attr.RemoveAttribute("backpack");
            attr.SetAttribute(CarryCodes.AttributeKeys.CarryOnBackup, backupAttributes);

            return sourceItemSlot;
        }
    }
}
