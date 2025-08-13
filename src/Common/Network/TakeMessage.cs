using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract]
    public class TakeMessage
    {
        [ProtoMember(1)]
        public BlockPos BlockPos { get; set; }

        // Optional, not required on the wire; omit when null
        [ProtoMember(2, IsRequired = false)]
        public int? Index { get; set; }
    }    
}