using ProtoBuf;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player detaches a carried block from an entity attachment slot.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record DetachMessage
    {
        /// <summary>The entity ID of the target entity to detach from.</summary>
        public long TargetEntityId { get; init; }

        /// <summary>The attachment slot index to detach from.</summary>
        public int SlotIndex { get; init; }

        private DetachMessage() { }

        public DetachMessage(long targetEntityId, int slotIndex)
        {
            TargetEntityId = targetEntityId;
            SlotIndex = slotIndex;
        }
    }
}
