using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record PutMessage
    {
        public BlockPos BlockPos { get; init; } = null!;

        public int Index { get; init; }

        private PutMessage() { }

        public PutMessage(BlockPos blockPos, int index)
        {
            BlockPos = blockPos;
            Index = index;
        }

    }
}