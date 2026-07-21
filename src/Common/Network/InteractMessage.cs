using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player interacts with a block while carrying.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record InteractMessage
    {
        /// <summary>The position of the block being interacted with.</summary>
        public BlockPos Position { get; init; } = null!;

        private InteractMessage() { }

        public InteractMessage(BlockPos position)
        {
            Position = position;
        }
    }
}
