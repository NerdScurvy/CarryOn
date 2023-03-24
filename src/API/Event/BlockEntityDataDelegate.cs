using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.API.Event
{
    public delegate void BlockEntityDataDelegate(BlockEntity blockEntity, ITreeAttribute blockEntityData, bool dropped = false);
}