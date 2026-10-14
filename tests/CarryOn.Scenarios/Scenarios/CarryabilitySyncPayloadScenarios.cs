using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Network;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// W2 and W3: the carryability sync handler must
    /// not wedge on one malformed entry.
    ///
    /// <para><b>Read this before treating these rows as coverage.</b> They cover the
    /// <i>transport</i> of the poisoned payload, nothing more. <c>OnCarryabilityStateSync</c>
    /// is registered on the client channel inside <c>StartClientSide</c>
    /// (<c>CarrySystem.Lifecycle.cs:77</c>), so it runs only in a real client process.
    /// Atlas's per-class world runs a dedicated server plus a <c>DummyTcpNetClient</c>
    /// (<c>VanillaClientWire.cs:172-189</c>) that decodes the received byte stream and
    /// discards it: it never instantiates client mod systems and has no client block
    /// registry. So <c>GetBlock</c>, <c>SyncCarryabilityBehavior</c> and
    /// <c>RebuildClientDerivedState</c> execute in <b>no test in this repository</b>, and an
    /// end-to-end row for M2 is impossible. The rejected alternative is recorded so nobody
    /// re-tries it: invoking the handler reflectively and asserting "it did not throw" wants
    /// client-side APIs, so calling it with the server <c>ICoreServerAPI</c> is a type
    /// mismatch that would not exercise the client path at all - a green row on that basis
    /// would be actively misleading.</para>
    ///
    /// <para>What these rows therefore do: <b>W2</b> proves a snapshot carrying a malformed
    /// <c>PropertiesJson</c> really does reach the client over the mod channel, so the
    /// poisoned entry is a reachable input and not a hypothetical; <b>W3</b> proves a
    /// well-formed multi-entry snapshot still crosses the channel with every entry intact,
    /// so whatever the handler does per entry is being handed a complete list. Both are
    /// premise/control rows and both are green before and after the fix - the fix's structure
    /// is pinned by <c>CarryabilitySyncWiringTests.CarryabilityStateSync_ContainsEveryEntry_AndAlwaysRebuildsDerivedState</c>
    /// (W1), and the behaviour half is a manual real-client gate.</para>
    ///
    /// <para>The rows broadcast on the server channel directly through the public
    /// <c>CarrySystem.ServerChannel</c> seam rather than provoking a real recondition,
    /// because a real snapshot is built by <c>BehavioralConditioning.BuildStateMessage</c>
    /// from well-formed behavior properties and cannot be made to carry a malformed payload.
    /// That is precisely why the row has to inject at the seam - and also precisely why this
    /// stands in for "a mod's bad conditioning output", which is the real-world source.</para>
    /// </summary>
    [Trait("area", "Sync/Network")]
    [Trait("summary", "Carryability sync transport rows: a poisoned PropertiesJson snapshot reaches the client over the mod channel and a well-formed multi-entry snapshot crosses it intact, transport only because the client-side handler cannot run on a headless server.")]
    public class CarryabilitySyncPayloadScenarios : CarryOnScenarioBase
    {
        private const string ChestCode = "game:chest-east";
        private const string BarrelCode = "game:barrel";

        /// <summary>Syntactically broken JSON: <c>JToken.Parse</c> throws on this.</summary>
        private const string PoisonedJson = "{not json";

        private const int SettleTicks = 30; // ~1 s - enough for the broadcast to be drained

        private List<CarryabilityStateMessage.CarryabilityEntry> Entry(
            string code, string kind, string? propertiesJson)
            => new() { new CarryabilityStateMessage.CarryabilityEntry(code, kind, propertiesJson) };

        /// <summary>
        /// W2 - premise. A snapshot whose entry carries a malformed <c>PropertiesJson</c>
        /// crosses the mod channel and arrives at the client verbatim.
        /// <para>
        /// This is the input M2 is about, and it proves nothing about what the client does
        /// with it - the harness has no client-side mod system to do it with. What it rules
        /// out is the alternative explanation that a malformed payload could never arrive,
        /// which would make W1's catch regions unreachable in practice.
        /// </para>
        /// </summary>
        [Trait("summary", "A snapshot whose entry carries malformed PropertiesJson reaches the client over the mod channel verbatim.")]
        [Trait("pins", "A malformed payload is a reachable input, so the handler's catch regions are not protecting against the unreachable.")]
        [Trait("role", "premise")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PoisonedPropertiesJson_ReachesTheClientOverTheModChannel()
        {
            Assert.NotNull(World.Api);
            var player = await JoinAndClearAsync("Tester");

            Assert.True(
                Mod.ServerChannel != null,
                "Row premise: the server-side mod channel must be registered - the snapshot "
                + "has to be broadcast from somewhere");

            Mod.ServerChannel.BroadcastPacket(new CarryabilityStateMessage(
                Entry(ChestCode, BlockBehaviorCarryable.Name, PoisonedJson)));
            await World.Ticks(SettleTicks);

            var messages = player.Client.Packets<CarryabilityStateMessage>(Channel);
            Assert.True(
                messages.Count > 0,
                "Row premise: a carryability snapshot carrying a malformed PropertiesJson must "
                + "reach the client over the mod channel. If none arrived the payload never gets "
                + "there, and M2's hardening would be protecting against an unreachable input");

            var poisoned = messages
                .SelectMany(m => m.Entries ?? new List<CarryabilityStateMessage.CarryabilityEntry>())
                .Where(e => e.BlockCode == ChestCode && e.Kind == BlockBehaviorCarryable.Name)
                .ToArray();

            Assert.True(
                poisoned.Length > 0,
                "Row premise: the chest carryable entry must survive the mod channel, malformed "
                + $"payload and all. Delivered {messages.Count} snapshot(s), "
                + $"{messages.Sum(m => m.Entries?.Count ?? 0)} entry/entries in total");
            Assert.All(poisoned, e => Assert.Equal(PoisonedJson, e.PropertiesJson));
        }

        /// <summary>
        /// W3 - control. A snapshot whose entries are all well-formed still crosses the
        /// channel with every entry intact, in order, with its properties JSON unaltered.
        /// <para>
        /// This is the guard against an over-broad fix: a <c>catch</c> wide enough to
        /// swallow the whole payload (or a snapshot builder that dropped entries) would
        /// still satisfy W1's structural pin, because W1 only asserts that the per-entry
        /// boundary exists and catches - not that a healthy snapshot still arrives whole.
        /// </para>
        /// </summary>
        [Trait("summary", "A three-entry well-formed snapshot crosses the channel in order with each entry's properties JSON, including a null, unaltered.")]
        [Trait("pins", "An over-broad fix that swallowed the whole payload would not pass, because a healthy snapshot must still arrive whole.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WellFormedSnapshot_ReachesTheClientWithEveryEntryIntact()
        {
            Assert.NotNull(World.Api);
            var player = await JoinAndClearAsync("Tester");

            Assert.True(
                Mod.ServerChannel != null,
                "Row premise: the server-side mod channel must be registered");

            var chestJson = """{"slots":{"Hands":{}},"InteractSpeedMultiplier":1.0}""";
            Mod.ServerChannel.BroadcastPacket(new CarryabilityStateMessage(
                new List<CarryabilityStateMessage.CarryabilityEntry>
                {
                    new(ChestCode, BlockBehaviorCarryable.Name, chestJson),
                    new(ChestCode, BlockBehaviorCarryableInteract.Name, """{"priority":0}"""),
                    new(BarrelCode, BlockBehaviorCarryable.Name, null),
                }));
            await World.Ticks(SettleTicks);

            var messages = player.Client.Packets<CarryabilityStateMessage>(Channel);
            Assert.NotEmpty(messages);

            var delivered = messages[^1].Entries;
            Assert.NotNull(delivered);
            Assert.Equal(3, delivered!.Count);

            Assert.Equal(ChestCode, delivered[0].BlockCode);
            Assert.Equal(BlockBehaviorCarryable.Name, delivered[0].Kind);
            Assert.Equal(chestJson, delivered[0].PropertiesJson);

            Assert.Equal(ChestCode, delivered[1].BlockCode);
            Assert.Equal(BlockBehaviorCarryableInteract.Name, delivered[1].Kind);

            // A null PropertiesJson means "no such behavior on this code" - a distinct wire
            // value from an empty string, and one M2's parse guard must not collapse.
            Assert.Equal(BarrelCode, delivered[2].BlockCode);
            Assert.Null(delivered[2].PropertiesJson);
        }

        /// <summary>
        /// Joins a player and clears packet captures so the join-time snapshot (if the
        /// session booted Mode B) cannot be mistaken for the row's own broadcast.
        /// </summary>
        private async Task<ITestPlayer> JoinAndClearAsync(string name)
        {
            ITestPlayer player = await JoinPlayer(name);
            await World.Ticks(SettleTicks);
            player.Client.Clear();
            return player;
        }
    }
}