using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract]
    public record DismountMessage
    {
        [ProtoMember(1)]
        public long EntityId { get; init; }
        [ProtoMember(2)]
        public string SeatId { get; init; }

        private DismountMessage() { }

        public DismountMessage(long entityId, string seatId)
        {
            EntityId = entityId;
            SeatId = seatId;
        }
    }
}