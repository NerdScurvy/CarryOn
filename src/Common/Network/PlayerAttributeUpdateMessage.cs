using ProtoBuf;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from server to clients to synchronize a player's carry-related attribute.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record PlayerAttributeUpdateMessage
    {
        /// <summary>The attribute key identifying the carry-related attribute.</summary>
        public string AttributeKey { get; init; } = null!;

        /// <summary>The boolean value to assign, or null to clear the attribute.</summary>
        public bool? BoolValue { get; init; }

        /// <summary>Whether this attribute is watched and should trigger client-side events.</summary>
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