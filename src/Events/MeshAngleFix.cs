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

            carrySystem.CarryEvents.OnRestoreEntityBlockData += OnRestoreEntityBlockData;
        }

        public void OnRestoreEntityBlockData(BlockEntity blockEntity, ITreeAttribute blockEntityData, bool dropped)
        {
            if (blockEntity is BlockEntitySign blockEntitySign)
            {
                blockEntityData.SetFloat("meshAngle", blockEntitySign.MeshAngleRad);
            }
            else if (blockEntity is BlockEntityBookshelf blockEntityBookshelf)
            {
                blockEntityData.SetFloat("meshAngleRad", blockEntityBookshelf.MeshAngleRad);
            }
            else if (blockEntity is BlockEntityGeneric blockEntityGeneric)
            {
                if(blockEntity?.Block?.Class == "BlockClutterBookshelf"){
                    var behavior = blockEntityGeneric.GetBehavior<BEBehaviorClutterBookshelf>();
                    blockEntityData.SetFloat("meshAngle", behavior.rotateY);
                }else if(blockEntity?.Block?.Class == "BlockClutter"){
                    var behavior = blockEntityGeneric.GetBehavior<BEBehaviorShapeFromAttributes>();
                    blockEntityData.SetFloat("meshAngle", behavior.rotateY);
                }
            }
        }
    }
}