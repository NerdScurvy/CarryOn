using CarryOn.API.Common.Models;
using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record QuickDropMessage
    {
        public CarrySlot[] CarrySlots { get; init; } = null!;

        private QuickDropMessage() { }

        public QuickDropMessage(CarrySlot[] carrySlots)
        {
            CarrySlots = carrySlots;
        }
    }
}