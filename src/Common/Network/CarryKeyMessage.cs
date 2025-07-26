using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class CarryKeyMessage
    {
        public bool IsCarryKeyHeld { get; private set; }

        private CarryKeyMessage() { }

        public CarryKeyMessage(bool isCarryKeyHeld)
        {
            IsCarryKeyHeld = isCarryKeyHeld;
        }   
    }
}
