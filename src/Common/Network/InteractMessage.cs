using ProtoBuf;
using Vintagestory.API.Common;
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

        // These fields are needed for reconstructing BlockSelection. The hit position is
        // required so the server resolves the same ground-storage slot (and block face) as the
        // client; without it the server would use its own independently-computed raycast, which
        // can target a different bag/quiver for angled placements.
        /// <summary>The exact hit position within the targeted block.</summary>
        public Vec3d HitPosition { get; init; } = null!;

        /// <summary>The block face index that was hit.</summary>
        public byte Face { get; init; }

        private InteractMessage() { }

        public InteractMessage(BlockSelection selection)
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
