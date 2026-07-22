using ProtoBuf;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player dismounts from an entity.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record DismountMessage
    {
        /// <summary>The entity ID of the entity to dismount from.</summary>
        public long EntityId { get; init; }

        /// <summary>The seat identifier to dismount from.</summary>
        public string SeatId { get; init; } = null!;

        private DismountMessage() { }

        public DismountMessage(long entityId, string seatId)
        {
            EntityId = entityId;
            SeatId = seatId;
        }
    }
}