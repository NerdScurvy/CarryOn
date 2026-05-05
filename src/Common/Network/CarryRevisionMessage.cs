using ProtoBuf;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent server → client after any forced server-side carry mutation (e.g. damage drop).
    /// The client compares its local carried revision against <see cref="Revision"/> and
    /// cancels any in-progress interaction if the revisions do not match, relying on the
    /// normal watched-attribute replication to restore the authoritative state.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class CarryRevisionMessage
    {
        public int Revision { get; }

        private CarryRevisionMessage() { }

        public CarryRevisionMessage(int revision)
            => Revision = revision;
    }
}
