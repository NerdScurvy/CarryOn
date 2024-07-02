using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.API.Event
{
    public delegate void CheckPermissionToCarryDelegate(EntityPlayer playerEntity, BlockPos pos, bool isReinforced, out bool? hasPermission);
}