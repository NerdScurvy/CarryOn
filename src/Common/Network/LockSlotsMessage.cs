using ProtoBuf;
using System.Collections.Generic;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class LockSlotsMessage
    {
        public List<int> HotbarSlots { get; }

        private LockSlotsMessage() { }

        public LockSlotsMessage(List<int> hotbarSlots)
            => HotbarSlots = hotbarSlots;
    }
}
