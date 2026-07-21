using CarryOn.API.Common.Models;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player places a carried block in the world.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record PlaceDownMessage
    {
        /// <summary>The carry slot containing the block to place.</summary>
        public CarrySlot Slot { get; init; }

        // These fields are needed for reconstructing BlockSelection
        /// <summary>The target block position for the block selection.</summary>
        public BlockPos Position { get; init; } = null!;

        /// <summary>The exact hit position within the targeted block.</summary>
        public Vec3d HitPosition { get; init; } = null!;

        /// <summary>The block face index that was hit.</summary>
        public byte Face { get; init; }

        /// <summary>The position where the block was actually placed.</summary>
        public BlockPos PlacedAt { get; init; } = null!;

        private PlaceDownMessage() { }

        public PlaceDownMessage(CarrySlot slot, BlockSelection selection, BlockPos placedAt)
        {
            Slot = slot;
            Position = selection.Position;
            Face = (byte)selection.Face.Index;
            HitPosition = selection.HitPosition.Clone();
            PlacedAt = placedAt;
        }

        public BlockSelection Selection => new()
        {
            Position = Position,
            Face = BlockFacing.ALLFACES[Face],
            HitPosition = HitPosition.Clone(),
        };
    }
}