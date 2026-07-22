using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player puts a carried item into a block entity via carry transfer.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record PutMessage
    {
        /// <summary>The position of the block entity to put carrird block into.</summary>
        public BlockPos BlockPos { get; init; } = null!;

        /// <summary>The slot index within the block entity to put carried block into.</summary>
        public int Index { get; init; }

        private PutMessage() { }

        public PutMessage(BlockPos blockPos, int index)
        {
            BlockPos = blockPos;
            Index = index;
        }

    }
}