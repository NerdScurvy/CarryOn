using ProtoBuf;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player attaches a carried block to an entity.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record AttachMessage
    {
        /// <summary>The entity ID of the target entity to attach to.</summary>
        public long TargetEntityId { get; init; }

        /// <summary>The attachment slot index to attach to.</summary>
        public int SlotIndex { get; init; }

        private AttachMessage() { }

        public AttachMessage(long targetEntityId, int slotIndex)
        {
            TargetEntityId = targetEntityId;
            SlotIndex = slotIndex;
        }
    }
}
