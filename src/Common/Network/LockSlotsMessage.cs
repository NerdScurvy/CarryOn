using ProtoBuf;
using System.Collections.Generic;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from server to client to lock or unlock hotbar slots during carry operations.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record LockSlotsMessage
    {
        /// <summary>The hotbar slot indices to lock, or an empty list to unlock all.</summary>
        public IReadOnlyList<int> HotbarSlots { get; init; } = [];

        private LockSlotsMessage() { }

        public LockSlotsMessage(IReadOnlyList<int> hotbarSlots)
            => HotbarSlots = hotbarSlots ?? [];
    }
}
