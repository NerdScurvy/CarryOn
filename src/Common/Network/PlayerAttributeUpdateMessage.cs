using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record PlayerAttributeUpdateMessage
    {
        public string AttributeKey { get; init; } = null!;
        public bool? BoolValue { get; init; }
        public bool IsWatchedAttribute { get; init; }

        private PlayerAttributeUpdateMessage() { }

        public PlayerAttributeUpdateMessage(string attributeKey, bool value, bool isWatchedAttribute = false)
        {
            AttributeKey = attributeKey;
            BoolValue = value;
            IsWatchedAttribute = isWatchedAttribute;
        }        
    }
}