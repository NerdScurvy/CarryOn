using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record PickupEntityMessage
    {
        public long EntityId { get; init; }

        private PickupEntityMessage() { }

        public PickupEntityMessage(long entityId)
        {
            EntityId = entityId;
        }
    }
}
