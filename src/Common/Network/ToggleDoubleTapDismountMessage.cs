using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class ToggleDoubleTapDismountMessage
    {
        public bool IsEnabled { get; set; }

        public ToggleDoubleTapDismountMessage() { }

        public ToggleDoubleTapDismountMessage(bool isEnabled)
        {
            IsEnabled = isEnabled;
        }
    }
}