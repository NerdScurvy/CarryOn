using ProtoBuf;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent client → server when the client detects its local carry revision is behind
    /// the server's. The server responds by re-marking the carry attribute path dirty,
    /// causing the authoritative state to be replicated to the requesting client.
    /// </summary>
    [ProtoContract]
    public class CarryResyncRequestMessage
    {
        private CarryResyncRequestMessage() { }

        public CarryResyncRequestMessage(int localRevision)
            => LocalRevision = localRevision;

        /// <summary> The revision the client currently holds, for server-side logging. </summary>
        [ProtoMember(1)]
        public int LocalRevision { get; private set; }
    }
}
