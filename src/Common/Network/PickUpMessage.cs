using CarryOn.API.Common.Models;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player picks up a block into a carry slot.
    /// </summary>
    [ProtoContract]
    public record PickUpMessage
    {
        /// <summary>
        /// Whether to also capture wall signs attached to the picked-up block. Required on
        /// the wire: a value that is merely optional is omitted when it equals the CLR
        /// default, so an explicit <c>false</c> would be lost and the receiver's initializer
        /// default would silently reassert itself.
        /// </summary>
        [ProtoMember(1, IsRequired = true)]
        public bool CaptureAttachedWallSigns { get; init; } = true;

        /// <summary>The position of the block to pick up.</summary>
        [ProtoMember(2)]
        public BlockPos Position { get; init; } = null!;

        /// <summary>The carry slot to place the picked-up block into.</summary>
        [ProtoMember(3)]
        public CarrySlot Slot { get; init; }

        private PickUpMessage() { }

        public PickUpMessage(BlockPos position, CarrySlot slot)
        {
            Position = position;
            Slot = slot;
        }
    }
}
