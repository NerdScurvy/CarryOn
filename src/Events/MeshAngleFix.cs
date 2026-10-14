using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CarryOn.Events
{

    /// <summary>
    /// Fixes the mesh angle of certain block entities when they are placed.
    /// </summary>
    public class MeshAngleFix : ICarryEventHandler
    {
        public void Init(ICarryManager carryManager)
        {
            if (carryManager.Api.Side != EnumAppSide.Server) return;

            carryManager.CarryEvents.BeforeRestoreBlockEntityData += OnBeforeRestoreBlockEntityData;
        }

        private void OnBeforeRestoreBlockEntityData(BlockEntity blockEntity, ITreeAttribute blockEntityData, bool dropped)
        {
            if (blockEntity == null || blockEntityData == null) return;

            var blockClass = blockEntity.Block?.Class;
            switch (blockClass)
            {
                case CarryCodes.BlockClasses.Sign:
                    if (blockEntity is BlockEntitySign sign)
                        blockEntityData.SetFloat(CarryCodes.AttributeKeys.BlockEntityData.MeshAngle, sign.MeshAngleRad);
                    return;

                case "BlockToolMold":
                    if (blockEntity is BlockEntityToolMold toolMold)
                        blockEntityData.SetFloat(CarryCodes.AttributeKeys.BlockEntityData.MeshAngle, toolMold.MeshAngle);
                    return;

                case "BlockBookshelf":
                    if (blockEntity is BlockEntityBookshelf bookshelf)
                        blockEntityData.SetFloat(CarryCodes.AttributeKeys.BlockEntityData.MeshAngleRad, bookshelf.MeshAngleRad);
                    return;

                case "BlockClutterBookshelf":
                case "BlockClutterBookshelfWithLore":
                    if (blockEntity is BlockEntityGeneric clutterBookshelf)
                    {
                        var beh = clutterBookshelf.GetBehavior<BEBehaviorClutterBookshelf>();
                        if (beh != null)
                        {
                            blockEntityData.SetFloat(CarryCodes.AttributeKeys.BlockEntityData.MeshAngle, beh.rotateY);
                        }
                    }
                    return;

                case "BlockClutter":
                    if (blockEntity is BlockEntityGeneric clutter)
                    {
                        var beh = clutter.GetBehavior<BEBehaviorShapeFromAttributes>();
                        if (beh != null)
                        {
                            blockEntityData.SetFloat(CarryCodes.AttributeKeys.BlockEntityData.MeshAngle, beh.rotateY);
                        }
                    }
                    return;
            }
        }
    }
}