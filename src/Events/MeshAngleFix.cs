using CarryOn.API.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CarryOn.Events
{

    /// <summary>
    /// Fixes the mesh angle of certain block entities when they are placed.
    /// </summary>
    public class MeshAngleFix : ICarryEvent
    {
        public void Init(ICarryManager carryManager)
        {
            if (carryManager.Api.Side != EnumAppSide.Server) return;

            carryManager.CarryEvents.OnRestoreEntityBlockData += OnRestoreEntityBlockData;
        }

        public void OnRestoreEntityBlockData(BlockEntity blockEntity, ITreeAttribute blockEntityData, bool dropped)
        {

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