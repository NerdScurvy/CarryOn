using CarryOn.API.Common;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class PickUpMessage
    {
        public BlockPos Position { get; }
        public CarrySlot Slot { get; }

        private PickUpMessage() { }

        public PickUpMessage(BlockPos position, CarrySlot slot)
        { Position = position; Slot = slot; }
    }
}
