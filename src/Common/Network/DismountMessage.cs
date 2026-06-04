using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record DismountMessage
    {
        public long EntityId { get; init; }
        public string SeatId { get; init; } = null!;

        private DismountMessage() { }

        public DismountMessage(long entityId, string seatId)
        {
            EntityId = entityId;
            SeatId = seatId;
        }
    }
}