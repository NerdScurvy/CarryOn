using ProtoBuf;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from server to client to synchronize the CarryOn configuration.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record ConfigSyncMessage
    {
        /// <summary>
        /// Serialized CarryOnConfig JSON. Null when sent as a client→server trigger;
        /// populated when the server broadcasts to clients.
        /// </summary>
        public string? ConfigJson { get; init; }

        private ConfigSyncMessage() { }

        public ConfigSyncMessage(string? configJson)
        {
            ConfigJson = configJson;
        }
    }
}
