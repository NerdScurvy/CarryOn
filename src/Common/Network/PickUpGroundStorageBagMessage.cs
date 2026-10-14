using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player picks a single bag item (backpack, hand basket,
    /// sack, quiver, ...) out of a ground storage slot and carries it in the hands slot. Unlike
    /// <see cref="PickUpMessage"/> this targets the <b>slot's ItemStack</b>, resolved server-side
    /// via <c>BlockEntityGroundStorage.GetSlotAt</c> - the ground storage block itself is never
    /// picked up. The hit position must be carried so both sides resolve the identical slot for
    /// angled / Halves / Quadrants placements.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record PickUpGroundStorageBagMessage
    {
        /// <summary>The position of the ground storage block containing the bag.</summary>
        public BlockPos Position { get; init; } = null!;

        /// <summary>The exact hit position within the targeted block.</summary>
        public Vec3d HitPosition { get; init; } = null!;

        /// <summary>The block face index that was hit.</summary>
        public byte Face { get; init; }

        private PickUpGroundStorageBagMessage() { }

        public PickUpGroundStorageBagMessage(BlockSelection selection)
        {
            Position = selection.Position;
            Face = (byte)selection.Face.Index;
            HitPosition = selection.HitPosition.Clone();
        }

        public BlockSelection? Selection =>
            Face < BlockFacing.ALLFACES.Length && HitPosition != null && Position != null
                ? new BlockSelection
                {
                    Position = Position,
                    Face = BlockFacing.ALLFACES[Face],
                    HitPosition = HitPosition.Clone(),
                }
                : null;
    }
}