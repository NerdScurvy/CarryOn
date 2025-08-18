using CarryOn.API.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Events
{
    public class TrunkFix : ICarryEvent
    {
        public void Init(ModSystem modSystem)
        {
            if (modSystem is not CarrySystem carrySystem) return;
            if (carrySystem.Api.Side != EnumAppSide.Server) return;

            carrySystem.CarryEvents.OnRestoreEntityBlockData += OnRestoreEntityBlockData;
        }

        public void OnRestoreEntityBlockData(BlockEntity blockEntity, ITreeAttribute blockEntityData, bool dropped)
        {
            if (dropped && blockEntity.Block.Shape.Base.Path == "block/wood/trunk/normal")
            {
                // Workaround fix dropped trunk angle
                blockEntityData.SetFloat("meshAngle", -90 * GameMath.DEG2RAD);
            }
        }
    }
}