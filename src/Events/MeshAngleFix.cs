using CarryOn.API.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CarryOn.Events
{
    public class MeshAngleFix : ICarryEvent
    {
        public void Init(CarrySystem carrySystem)
        {
            if (carrySystem.Api.Side != EnumAppSide.Server) return;

            carrySystem.CarryEvents.BeforeRestoreBlockEntityData += OnBeforeRestoreBlockEntityData;
        }

        private void OnBeforeRestoreBlockEntityData(BlockEntity blockEntity, ITreeAttribute blockEntityData, bool dropped)
        {
            if (blockEntity == null || blockEntityData == null) return;

            var blockClass = blockEntity?.Block?.Class;
            switch (blockClass)
            {
                case "BlockSign":
                    if (blockEntity is BlockEntitySign sign)
                        blockEntityData.SetFloat("meshAngle", sign.MeshAngleRad);
                    return;

                case "BlockToolMold":
                    if (blockEntity is BlockEntityToolMold toolMold)
                        blockEntityData.SetFloat("meshAngle", toolMold.MeshAngle);
                    return;

                case "BlockBookshelf":
                    if (blockEntity is BlockEntityBookshelf bookshelf)
                        blockEntityData.SetFloat("meshAngleRad", bookshelf.MeshAngleRad);
                    return;

                case "BlockClutterBookshelf":
                    if (blockEntity is BlockEntityGeneric clutterBookshelf)
                    {
                        var beh = clutterBookshelf.GetBehavior<BEBehaviorClutterBookshelf>();
                        if (beh != null)
                        {
                            blockEntityData.SetFloat("meshAngle", beh.rotateY);
                        }
                    }
                    return;

                case "BlockClutter":
                    if (blockEntity is BlockEntityGeneric clutter)
                    {
                        var beh = clutter.GetBehavior<BEBehaviorShapeFromAttributes>();
                        if (beh != null)
                        {
                            blockEntityData.SetFloat("meshAngle", beh.rotateY);
                        }
                    }
                    return;
            }
        }
    }
}