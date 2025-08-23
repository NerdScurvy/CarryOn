using CarryOn.API.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Events
{
    /// <summary>
    /// Fixes mesh angle of dropped trunk blocks.
    /// </summary>
    public class TrunkFix : ICarryEvent
    {
        public void Init(ICarryManager carryManager)
        {
            if (carryManager.Api.Side != EnumAppSide.Server) return;

            carryManager.CarryEvents.OnRestoreEntityBlockData += OnRestoreEntityBlockData;
        }

        public void OnRestoreEntityBlockData(BlockEntity blockEntity, ITreeAttribute blockEntityData, bool dropped)
        {
            if (dropped && blockEntity.Block.Shape.Base.Path == "block/wood/trunk/normal")
            {
                // Workaround fix dropped trunk angle
                //blockEntityData.SetFloat("meshAngle", -90 * GameMath.DEG2RAD);
            }
        }
    }
}