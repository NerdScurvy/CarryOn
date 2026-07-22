using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player takes a carried block from a block entity via carry transfer.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record TakeMessage
    {
        /// <summary>The position of the block entity to take from.</summary>
        public BlockPos BlockPos { get; init; } = null!;

        /// <summary>The slot index within the block entity to take from.</summary>
        public int Index { get; init; }

        private TakeMessage() { }
        
        public TakeMessage(BlockPos blockPos, int index)
        {
            BlockPos = blockPos;
            Index = index;
        }
    }
}