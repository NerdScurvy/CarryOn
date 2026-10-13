using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace CarryOn.Scenarios.Wiring
{
    /// <summary>
    /// Plain xUnit wiring pins for network message contracts. Each row reads the staged
    /// <c>CarryOn.dll</c> metadata with <c>PEReader</c>/<c>MetadataReader</c> and asserts an
    /// attribute shape - never a round-trip, because encoding and decoding are engine code
    /// and the client-to-server wire is an explicitly parked gap in this suite (see the
    /// README's known gaps). A real protobuf round-trip in a non-Atlas unit project would
    /// supersede these rows; until that lane exists, the attribute shape is the strongest
    /// honest claim.
    ///
    /// <para>These rows deliberately load no assembly. Loading <c>CarryOn.dll</c> pulls in
    /// <c>VintagestoryAPI.dll</c> and poisons the Atlas host's per-install preflight, which
    /// breaks the cross-version sweep. See <c>CarryabilitySyncWiringTests</c>.</para>
    /// </summary>
    [Trait("area", "Common/Network")]
    [Trait("summary", "Plain xUnit wiring pins: each row reads the staged CarryOn.dll metadata and asserts a network message member's serializer attribute shape, with no server boot and no wire traffic.")]
    public class MessageContractWiringTests
    {
        private const string ControlTypeName = "CarryOn.Common.Network.CarryabilityStateMessage";
        private const string ControlPropertyName = "Entries";

        private const string PickupTypeName = "CarryOn.Common.Network.PickUpMessage";
        private const string PickupFlagPropertyName = "CaptureAttachedWallSigns";

        /// <summary>
        /// Authored red-first. A pickup message used to capture attached wall signs must
        /// carry that intent as a required wire member, so a receiver can never read a
        /// default in place of an explicit value. Before the fix the record opted every
        /// field onto the wire implicitly and the flag carried no explicit member, so an
        /// explicitly false value could arrive as true on the far side.
        /// </summary>
        [Trait("summary", "PickUpMessage.CaptureAttachedWallSigns must carry a required ProtoMember so an explicit false is not lost on the wire.")]
        [Trait("pins", "The attached-sign flag is serialized as a required member rather than inferred from a default.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with no ProtoMember attribute present on the flag property or its backing field.")]
        [Trait("caveat", "Wiring only - proves the attribute shape, not the wire round-trip (encoding is engine code and the client-to-server wire is a parked suite gap).")]
        [Fact]
        public void PickUpMessage_AttachedSignFlag_IsARequiredWireMember()
        {
            using var peReader = new PEReader(File.OpenRead(StagedModProbe.ModAssemblyPath()));
            var reader = peReader.GetMetadataReader();

            // Premise/control: the reader must be able to see a known ProtoMember that is
            // NOT required before its negative result on the flag means anything. If this
            // control fails, the attribute reader is broken and the flag row below proves
            // nothing.
            var controlLocated = StagedModProbe.TryGetProtoMember(
                reader, ControlTypeName, ControlPropertyName, out var controlHasMember, out var controlIsRequired);
            Assert.True(
                controlLocated && controlHasMember,
                $"Premise: the pin reader cannot see the known ProtoMember on "
                + $"{ControlTypeName}.{ControlPropertyName}; if that member was renamed, "
                + "re-derive this premise against another property that carries an explicit "
                + "ProtoMember before trusting the flag assertion.");
            Assert.False(
                controlIsRequired,
                $"Premise: {ControlTypeName}.{ControlPropertyName} must not report IsRequired "
                + "- it is the control that proves the reader distinguishes required from "
                + "optional members.");

            var flagLocated = StagedModProbe.TryGetProtoMember(
                reader, PickupTypeName, PickupFlagPropertyName, out var hasMember, out var isRequired);
            Assert.True(
                flagLocated,
                $"{PickupTypeName} (or its property {PickupFlagPropertyName}) not found in the "
                + "staged mod assembly - if it was renamed, re-derive this pin rather than "
                + "delete it.");

            Assert.True(
                hasMember,
                $"{PickupTypeName}.{PickupFlagPropertyName} must carry an explicit "
                + "ProtoMember-family attribute. The record opts every field onto the wire "
                + "implicitly, so the flag has no explicit member and its value is inferred "
                + "from the initialized default - an explicitly false flag can therefore "
                + "arrive as true. Add the member attribute the contract needs.");

            Assert.True(
                isRequired,
                $"{PickupTypeName}.{PickupFlagPropertyName} must be a *required* wire member "
                + "(IsRequired = true). Optional members can be absent from the wire, and a "
                + "receiver then reads the type default rather than what the sender meant; "
                + "requiring it makes a sender that omits the intent fail loudly instead of "
                + "silently inverting the flag.");
        }
    }
}
