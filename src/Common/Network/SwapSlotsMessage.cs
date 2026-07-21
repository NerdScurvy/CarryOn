using CarryOn.API.Common.Models;
using ProtoBuf;
using System;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server to swap carried blocks between two carry slots (e.g. Hands and Back).
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record SwapSlotsMessage
    {
        /// <summary>The first carry slot to swap.</summary>
        public CarrySlot First { get; init; }

        /// <summary>The second carry slot to swap.</summary>
        public CarrySlot Second { get; init; }

        private SwapSlotsMessage() { }

        public SwapSlotsMessage(CarrySlot first, CarrySlot second)
        {
            if (first == second) throw new ArgumentException("Slots can't be the same");
            First = first;
            Second = second;
        }
    }
}
