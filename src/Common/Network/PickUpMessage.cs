using CarryOn.API.Common.Models;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player picks up a block into a carry slot.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record PickUpMessage
    {
        /// <summary>The position of the block to pick up.</summary>
        public BlockPos Position { get; init; } = null!;

        /// <summary>The carry slot to place the picked-up block into.</summary>
        public CarrySlot Slot { get; init; }

        /// <summary>Whether to also capture wall signs attached to the picked-up block.</summary>
        public bool CaptureAttachedWallSigns { get; init; } = true;

        private PickUpMessage() { }

        public PickUpMessage(BlockPos position, CarrySlot slot)
        {
            Position = position;
            Slot = slot;
        }
    }
}
