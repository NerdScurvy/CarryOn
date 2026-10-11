using System.Collections.Generic;
using ProtoBuf;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from server to client to reconcile the client's local block registry with the
    /// server's post-conditioning carryability state. A pure snapshot: every receipt
    /// fully reconciles, no diff/version bookkeeping.
    /// </summary>
    [ProtoContract]
    public sealed class CarryabilityStateMessage
    {
        /// <summary>
        /// Every managed block code, each contributing both a carryable and a
        /// carryable-interact entry so the client can resolve presence/absence for both
        /// kinds without separate bookkeeping.
        /// </summary>
        [ProtoMember(1)]
        public List<CarryabilityEntry> Entries { get; set; } = new();

        private CarryabilityStateMessage() { }

        public CarryabilityStateMessage(List<CarryabilityEntry> entries)
        {
            Entries = entries ?? new List<CarryabilityEntry>();
        }

        /// <summary>
        /// Carryability of one block code for one behavior kind. Null/empty
        /// <see cref="PropertiesJson"/> means "block currently has no such behavior".
        /// </summary>
        [ProtoContract]
        public sealed class CarryabilityEntry
        {
            /// <summary>Block code the entry applies to.</summary>
            [ProtoMember(1)]
            public string BlockCode { get; set; } = string.Empty;

            /// <summary>Behavior kind key: <c>BlockBehaviorCarryable.Name</c> or <c>BlockBehaviorCarryableInteract.Name</c>.</summary>
            [ProtoMember(2)]
            public string Kind { get; set; } = string.Empty;

            /// <summary>
            /// Post-<c>ProcessConditions</c> behavior properties JSON (slot-level condition
            /// removals already applied); null/empty when the behavior is absent.
            /// </summary>
            [ProtoMember(3)]
            public string? PropertiesJson { get; set; }

            private CarryabilityEntry() { }

            public CarryabilityEntry(string blockCode, string kind, string? propertiesJson)
            {
                BlockCode = blockCode;
                Kind = kind;
                PropertiesJson = propertiesJson;
            }
        }
    }
}
