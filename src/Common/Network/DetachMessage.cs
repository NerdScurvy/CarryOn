using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class DetachMessage
    {
        public long TargetEntityId { get; }

        public int SlotIndex { get; }

        private DetachMessage() { }

        public DetachMessage(long targetEntityId, int slotIndex)
        {
            TargetEntityId = targetEntityId;
            SlotIndex = slotIndex;
        }
    }
}
