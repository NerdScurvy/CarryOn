using CarryOn.API.Common.Models;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record PickUpMessage
    {
        public BlockPos Position { get; init; }
        public CarrySlot Slot { get; init; }

        private PickUpMessage() { }

        public PickUpMessage(BlockPos position, CarrySlot slot)
        {
            Position = position; 
            Slot = slot; }
    }
}
