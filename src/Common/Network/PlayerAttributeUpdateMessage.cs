using ProtoBuf;

namespace CarryOn.Common.Network
{
    [ProtoContract]
    public record PlayerAttributeUpdateMessage
    {
        [ProtoMember(1)]
        public string AttributeKey { get; init; }
        [ProtoMember(2)]
        public bool? BoolValue { get; init; }
        [ProtoMember(3)]
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