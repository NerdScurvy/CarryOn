using CarryOn.API.Common;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract]
    public class DismountMessage
    {
        [ProtoMember(1)]
        public long EntityId { get; set; }
        [ProtoMember(2)]
        public string SeatId { get; set; }
    }
}