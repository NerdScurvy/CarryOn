using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract]
    public sealed class ToggleDoubleTapDismountMessage
    {
        [ProtoMember(1)]
        public bool IsEnabled { get; set; }

        public ToggleDoubleTapDismountMessage() { }

        public ToggleDoubleTapDismountMessage(bool isEnabled)
        {
            IsEnabled = isEnabled;
        }
    }
}