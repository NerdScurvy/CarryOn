using CarryOn.API.Common.Models;
using ProtoBuf;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player quick-drops carried items.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record QuickDropMessage
    {
        /// <summary>The carry slots whose contents should be dropped.</summary>
        public CarrySlot[] CarrySlots { get; init; } = null!;

        private QuickDropMessage() { }

        public QuickDropMessage(CarrySlot[] carrySlots)
        {
            CarrySlots = carrySlots;
        }
    }
}