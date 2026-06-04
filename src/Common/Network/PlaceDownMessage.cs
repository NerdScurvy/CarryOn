using CarryOn.API.Common.Models;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record PlaceDownMessage
    {
        public CarrySlot Slot { get; init; }

        // These fields are needed for reconstructing BlockSelection
        public BlockPos Position { get; init; } = null!;

        public Vec3d HitPosition { get; init; } = null!;
        public byte Face { get; init; }

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