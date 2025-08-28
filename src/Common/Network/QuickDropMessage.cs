using CarryOn.API.Common;
using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class QuickDropMessage
    {
        public CarrySlot[] CarrySlots { get; set; }
    }
}