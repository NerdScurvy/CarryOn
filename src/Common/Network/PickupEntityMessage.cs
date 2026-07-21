using ProtoBuf;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player picks up a dropped carried block entity.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record PickupEntityMessage
    {
        /// <summary>The entity ID of the dropped item to pick up.</summary>
        public long EntityId { get; init; }

        private PickupEntityMessage() { }

        public PickupEntityMessage(long entityId)
        {
            EntityId = entityId;
        }
    }
}
