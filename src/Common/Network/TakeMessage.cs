using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract]
    public record TakeMessage
    {
        [ProtoMember(1)]
        public BlockPos BlockPos { get; init; }

        [ProtoMember(2)]
        public int Index { get; init; }
        private TakeMessage() { }
        public TakeMessage(BlockPos blockPos, int index)
        {
            BlockPos = blockPos;
            Index = index;
        }
    }
}