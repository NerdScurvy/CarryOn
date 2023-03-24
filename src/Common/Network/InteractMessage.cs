using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class InteractMessage
    {
        public BlockPos Position { get; }

        private InteractMessage() { }

        public InteractMessage(BlockPos position)
        { Position = position; }
    }
}
