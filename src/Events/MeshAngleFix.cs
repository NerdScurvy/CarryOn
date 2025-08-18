using CarryOn.API.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CarryOn.Events
{
    public class MeshAngleFix : ICarryEvent
    {
        public void Init(ModSystem modSystem)
        {
            if (modSystem is not CarrySystem carrySystem) return;

            if (carrySystem.Api.Side != EnumAppSide.Server) return;

            carrySystem.CarryEvents.OnRestoreEntityBlockData += OnRestoreEntityBlockData;
        }

        public void OnRestoreEntityBlockData(BlockEntity blockEntity, ITreeAttribute blockEntityData, bool dropped)
        {
            switch (blockEntity?.Block?.Class)
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
                        blockEntityData.SetFloat("meshAngle", clutterBookshelf.GetBehavior<BEBehaviorClutterBookshelf>().rotateY);
                    return;

                case "BlockClutter":
                    if (blockEntity is BlockEntityGeneric clutter)
                        blockEntityData.SetFloat("meshAngle", clutter.GetBehavior<BEBehaviorShapeFromAttributes>().rotateY);
                    return;
            }
        }
    }
}