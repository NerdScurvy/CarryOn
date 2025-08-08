using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class TakeMessage
    {
        public BlockPos BlockPos { get; set; }
        public int? Index { get; set; }
    }
}