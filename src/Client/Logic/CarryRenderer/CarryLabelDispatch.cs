using System;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class CarryLabelDispatch
    {
        private readonly CarryRenderDispatcher dispatcher;
        private readonly ICoreClientAPI api;
        private readonly CarryLabelRenderer labelRenderer;

        public CarryLabelDispatch(ICoreClientAPI api, CarryRenderDispatcher dispatcher, CarryLabelRenderer labelRenderer)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.labelRenderer = labelRenderer ?? throw new ArgumentNullException(nameof(labelRenderer));
        }

        public void Render(CarriedBlock carried, float[] initialMatrix, float[] viewMat, IStandardShaderProgram prog, EntityAgent entity)
        {
            if (carried.AttachedBlocks == null) return;

            var world = api.World;
            if (world == null) return;

            var defaultFacing = carried.GetCarryableBehavior()?.RootRenderFacing;
            int offsetSteps = CarryRotationHelper.GetOriginalToModelDefaultSteps(carried, defaultFacing);

            foreach (var attached in carried.AttachedBlocks)
            {
                if (attached == null) continue;

                var offset = CarryRotationHelper.RotateOffset(attached.RelativeOffset, offsetSteps);

                var rotatedFace = attached.OriginalLocalFace != null
                    ? CarryRotationHelper.RotateFacing(attached.OriginalLocalFace, offsetSteps)
                    : null;

                var childMatrix = dispatcher.RentMatrix();
                Array.Copy(initialMatrix, childMatrix, 16);

                var offsetTransform = new ModelTransform();
                offsetTransform.EnsureDefaultValues();
                offsetTransform.Translation.Set(offset.X, offset.Y, offset.Z);
                if (rotatedFace != null)
                {
                    var facingDegrees = -CarryRotationHelper.FacingToYRotationDegrees(rotatedFace);
                    offsetTransform.Origin.Set(0.5f, 0.5f, 0.5f);
                    offsetTransform.Rotation.Y = facingDegrees;
                }
                CarryTransformResolver.ApplyTransformInPlace(offsetTransform, childMatrix);

                CarriedBlock labelCarriedBlock = attached.CarriedBlock;
                var originalCode = attached.OriginalBlockCode;
                if (originalCode != null)
                {
                    Block? variantBlock = rotatedFace != null
                        ? CarryRotationHelper.GetRotatedVariantBlock(world, originalCode, rotatedFace)
                        : null;
                    variantBlock ??= world.GetBlock(originalCode);

                    if (variantBlock != null)
                    {
                        var variantStack = new ItemStack(variantBlock);
                        variantStack.ResolveBlockOrItem(world);
                        labelCarriedBlock = new CarriedBlock(
                            CarrySlot.Attached,
                            variantStack,
                            attached.BlockEntityData,
                            null,
                            variantBlock.Code,
                            attached.OriginalMeshAngle
                        );
                    }
                }

                var behavior = labelCarriedBlock.GetCarryableBehavior();
                var attachedTransform = behavior?.LabelRenderSettings?.AttachedTransform;
                if (attachedTransform != null)
                {
                    CarryTransformResolver.ApplyTransformInPlace(attachedTransform, childMatrix);
                }

                labelRenderer.TryRender(labelCarriedBlock, childMatrix, viewMat, prog, entity.Pos.AsBlockPos);
                dispatcher.ReturnMatrix(childMatrix);
            }
        }
    }
}
