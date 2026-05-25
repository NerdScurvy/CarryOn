using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record AttachMessage
    {
        public long TargetEntityId { get; init; }
        public int SlotIndex { get; init; }

        private AttachMessage() { }

        public AttachMessage(long targetEntityId, int slotIndex)
        {
            TargetEntityId = targetEntityId;
            SlotIndex = slotIndex;
        }
    }
}
