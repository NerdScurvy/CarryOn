using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record InteractMessage
    {
        public BlockPos Position { get; init; } = null!;

        private InteractMessage() { }

        public InteractMessage(BlockPos position)
        {
            Position = position;
        }
    }
}
