using ProtoBuf;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class AttachMessage
    {
        public long TargetEntityId { get; }

        public int SlotIndex { get; }

        private AttachMessage() { }

        public AttachMessage(long targetEntityId, int slotIndex)
        {
            TargetEntityId = targetEntityId;
            SlotIndex = slotIndex;
        }
    }
}
