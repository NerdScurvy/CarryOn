using ProtoBuf;
using System.Collections.Generic;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record LockSlotsMessage
    {
        public IReadOnlyList<int> HotbarSlots { get; init; }

        private LockSlotsMessage() { }

        public LockSlotsMessage(IReadOnlyList<int> hotbarSlots)
            => HotbarSlots = hotbarSlots;
    }
}
