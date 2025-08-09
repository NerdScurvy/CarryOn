using System.Runtime.InteropServices;
using ProtoBuf;
using Vintagestory.API.Datastructures;

namespace CarryOn.Common.Network
{
    [ProtoContract]
    public class PlayerAttributeUpdateMessage
    {
        [ProtoMember(1)]
        public string AttributeKey { get; set; }
        [ProtoMember(2)]
        public bool? BoolValue { get; set; }
        [ProtoMember(3)]
        public bool IsWatchedAttribute { get; set; }

        public PlayerAttributeUpdateMessage()
        {
        }

        
        public PlayerAttributeUpdateMessage(string attributeKey, bool value, bool isWatchedAttribute = false)
        {
            AttributeKey = attributeKey;
            BoolValue = value;
            IsWatchedAttribute = isWatchedAttribute;
        }        
    }
}