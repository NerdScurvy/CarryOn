using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record DetachMessage
    {
        public long TargetEntityId { get; init; }

        public int SlotIndex { get; init; }

        private DetachMessage() { }

        public DetachMessage(long targetEntityId, int slotIndex)
        {
            TargetEntityId = targetEntityId;
            SlotIndex = slotIndex;
        }
    }
}
