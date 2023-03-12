using CarryOn.API.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CarryOn.Events
{
    public class SignFix : ICarryEvent
    {
        public void Init(CarrySystem carrySystem)
        {
            if (carrySystem.Api.Side != EnumAppSide.Server) return;

            carrySystem.CarryEvents.OnRestoreEntityBlockData += OnRestoreEntityBlockData;
        }

        public void OnRestoreEntityBlockData(BlockEntity blockEntity, ITreeAttribute blockEntityData)
        {
            if (blockEntity is BlockEntitySign blockEntitySign)
            {
                // Fix sign rotation
                blockEntityData.SetFloat("meshAngle", blockEntitySign.MeshAngleRad);
            }
        }
    }
}