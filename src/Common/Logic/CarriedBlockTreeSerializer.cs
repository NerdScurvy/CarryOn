using System.Collections.Generic;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Logic
{
    public static class CarriedBlockTreeSerializer
    {
        public static ITreeAttribute? Serialize(CarriedBlock carriedBlock)
        {
            var tree = new TreeAttribute();

            var stack = carriedBlock.ItemStack;
            if (stack == null) return null;

            tree.SetItemstack(AttributeKey.CarriedBlock.Stack, stack);

            if (carriedBlock.BlockEntityData != null)
            {
                tree[AttributeKey.CarriedBlock.Data] = carriedBlock.BlockEntityData;
            }

            var childrenTree = BuildAttachedBlocksTree(carriedBlock.AttachedBlocks);
            if (childrenTree != null)
                tree[AttributeKey.CarriedBlock.Children] = childrenTree;

            if (carriedBlock.OriginalBlockCode != null)
                tree.SetString(AttributeKey.CarriedBlock.OriginalBlockCode, carriedBlock.OriginalBlockCode.ToString());

            if (carriedBlock.OriginalMeshAngle.HasValue)
                tree.SetFloat(AttributeKey.CarriedBlock.OriginalMeshAngle, carriedBlock.OriginalMeshAngle.Value);

            return tree;
        }

        public static CarriedBlock? Deserialize(ITreeAttribute tree, ICoreAPI? api, CarrySlot slot = CarrySlot.Hands)
            => Deserialize(tree, api?.World, slot);

        public static CarriedBlock? Deserialize(ITreeAttribute tree, IWorldAccessor? world, CarrySlot slot = CarrySlot.Hands)
        {
            var stack = tree.GetItemstack(AttributeKey.CarriedBlock.Stack);
            if (stack?.Class != EnumItemClass.Block) return null;
            if (stack.Block == null)
            {
                stack.ResolveBlockOrItem(world);
                if (stack.Block == null) return null;
            }

            var blockEntityData = tree[AttributeKey.CarriedBlock.Data] as ITreeAttribute;
            var attachedBlocks = DeserializeAttachedBlocks(tree, world);

            var originalCodeStr = tree.GetString(AttributeKey.CarriedBlock.OriginalBlockCode, null);
            var originalCode = originalCodeStr != null ? new AssetLocation(originalCodeStr) : null;

            float? originalMeshAngle = null;
            if (tree.HasAttribute(AttributeKey.CarriedBlock.OriginalMeshAngle))
                originalMeshAngle = tree.GetFloat(AttributeKey.CarriedBlock.OriginalMeshAngle);

            return new CarriedBlock(slot, stack, blockEntityData, attachedBlocks, originalCode, originalMeshAngle);
        }

        public static List<AttachedCarriedBlock>? DeserializeAttachedBlocks(ITreeAttribute slotAttribute, ICoreAPI? api)
            => DeserializeAttachedBlocks(slotAttribute, api?.World);

        public static List<AttachedCarriedBlock>? DeserializeAttachedBlocks(ITreeAttribute slotAttribute, IWorldAccessor? world)
        {
            if (slotAttribute[AttributeKey.CarriedBlock.Children] is not TreeAttribute childrenTree || childrenTree.Count == 0)
                return null;

            var attached = new List<AttachedCarriedBlock>();
            foreach (var key in childrenTree.Keys)
            {
                if (childrenTree[key] is not ITreeAttribute childAttr) continue;

                var childStack = childAttr.GetItemstack(AttributeKey.CarriedBlock.Stack);
                if (childStack?.Class != EnumItemClass.Block) continue;
                if (childStack.Block == null)
                {
                    childStack.ResolveBlockOrItem(world);
                    if (childStack.Block == null) continue;
                }

                var childData = childAttr[AttributeKey.CarriedBlock.Data] as ITreeAttribute;

                var offsetX = childAttr.GetInt(AttributeKey.CarriedBlock.OffsetX, 0);
                var offsetY = childAttr.GetInt(AttributeKey.CarriedBlock.OffsetY, 0);
                var offsetZ = childAttr.GetInt(AttributeKey.CarriedBlock.OffsetZ, 0);
                var relativeOffset = new BlockPos(offsetX, offsetY, offsetZ);

                var faceCode = childAttr.GetString(AttributeKey.CarriedBlock.OriginalFace, null);
                var originalFace = faceCode != null ? BlockFacing.FromCode(faceCode) : null;

                var originalCodeStr = childAttr.GetString(AttributeKey.CarriedBlock.OriginalBlockCode, null);
                var originalCode = originalCodeStr != null ? new AssetLocation(originalCodeStr) : null;

                float? originalMeshAngle = null;
                if (childAttr.HasAttribute(AttributeKey.CarriedBlock.OriginalMeshAngle))
                    originalMeshAngle = childAttr.GetFloat(AttributeKey.CarriedBlock.OriginalMeshAngle);

                var carriedBlock = new CarriedBlock(CarrySlot.Attached, childStack, childData, null, originalCode, originalMeshAngle);
                attached.Add(new AttachedCarriedBlock(relativeOffset, carriedBlock, originalFace));
            }

            return attached.Count > 0 ? attached : null;
        }

        public static TreeAttribute? BuildAttachedBlocksTree(IReadOnlyList<AttachedCarriedBlock>? attachedBlocks)
        {
            if (attachedBlocks == null || attachedBlocks.Count == 0) return null;

            var childrenTree = new TreeAttribute();
            for (int i = 0; i < attachedBlocks.Count; i++)
            {
                var child = attachedBlocks[i];
                var childAttr = new TreeAttribute();

                childAttr.SetItemstack(AttributeKey.CarriedBlock.Stack, child.ItemStack);

                if (child.BlockEntityData != null)
                {
                    childAttr[AttributeKey.CarriedBlock.Data] = child.BlockEntityData;
                }

                childAttr.SetInt(AttributeKey.CarriedBlock.OffsetX, child.RelativeOffset.X);
                childAttr.SetInt(AttributeKey.CarriedBlock.OffsetY, child.RelativeOffset.Y);
                childAttr.SetInt(AttributeKey.CarriedBlock.OffsetZ, child.RelativeOffset.Z);

                if (child.OriginalLocalFace != null)
                    childAttr.SetString(AttributeKey.CarriedBlock.OriginalFace, child.OriginalLocalFace.Code);

                if (child.OriginalBlockCode != null)
                    childAttr.SetString(AttributeKey.CarriedBlock.OriginalBlockCode, child.OriginalBlockCode.ToString());

                if (child.OriginalMeshAngle.HasValue)
                    childAttr.SetFloat(AttributeKey.CarriedBlock.OriginalMeshAngle, child.OriginalMeshAngle.Value);

                childrenTree[$"child_{i}"] = childAttr;
            }

            return childrenTree;
        }
    }
}
