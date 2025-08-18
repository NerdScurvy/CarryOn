using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract]
    public class PutMessage
    {
        [ProtoMember(1)]
        public BlockPos BlockPos { get; set; }

        [ProtoMember(2)]
        public int Index { get; set; }
    }
}