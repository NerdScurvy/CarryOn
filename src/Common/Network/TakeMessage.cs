using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record TakeMessage
    {
        public BlockPos BlockPos { get; init; } = null!;

        public int Index { get; init; }

        private TakeMessage() { }
        
        public TakeMessage(BlockPos blockPos, int index)
        {
            BlockPos = blockPos;
            Index = index;
        }
    }
}