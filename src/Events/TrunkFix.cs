using CarryOn.API.Common;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Events
{
    public class TrunkFix : ICarryEvent
    {
        public void Init(CarrySystem carrySystem)
        {
            if (carrySystem.Api.Side != EnumAppSide.Server) return;

            carrySystem.CarryEvents.OnRestoreEntityBlockData += OnRestoreEntityBlockData;
        }

        public void OnRestoreEntityBlockData(BlockEntity blockEntity, ITreeAttribute blockEntityData, bool dropped)
        {
            // Fix trunk dropped angle - Sets to a fixed angle for east facing trunk
            // Updated to support labeled trunks
            // Should not be required in CarryOn v2
            bool isTrunkBlock = blockEntity?.Block?.Shape?.Base?.Path?.StartsWith("block/wood/trunk/") == true;
            if (dropped && isTrunkBlock)
            {
                // Workaround fix dropped trunk angle
                blockEntityData.SetFloat("meshAngle", -90 * GameMath.DEG2RAD);
            }
        }
    }
}