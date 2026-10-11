using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Network;
using Vintagestory.API.Common;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// L3 premise row, payload half: the carryability snapshot must describe an
    /// automapped interact twin with its donor's properties JSON, not collapse it to
    /// the protocol's "absent" state. On the pre-fix head the interact automapper
    /// appended the twin without ever calling <c>Initialize</c> and without reading
    /// the donor's properties at all (it collected only <c>EntityClass</c> keys), so
    /// <c>CurrentPropertiesJson</c> stayed blank, <c>Normalize</c> folded it to
    /// <c>null</c> (<c>BehavioralConditioning.cs</c>), and the client mounted
    /// <b>no</b> interact behavior for a block the server has one on
    /// (<c>CarrySystem.Configuration.cs</c>) - every interact patch CarryOn ships is
    /// Server-side, so the snapshot is the client's only route to one. Red by
    /// construction on the pre-fix head; green since the fix copied the donor's
    /// properties onto each twin (<c>BehaviorAutoMapper</c>'s interact pass).
    /// <para>
    /// <c>game:storagevessel-blue-fired</c> is the measured twin, not a guess:
    /// <c>clay/fired/storagevessel.json</c> carries
    /// <c>entityclass: "GenericTypedContainer"</c> but is <b>not</b> matched by the
    /// shipped <c>clay/storagevessel*</c> interact patch glob (that glob only matches
    /// <c>clay/storagevessel-fancy.json</c>), so the interact automapper twin-creates
    /// it from the chest/reedchest/storagevessel-fancy/stationarybasket donor keys.
    /// A suite probe measured exactly 10 such twins - the 10 fired vessel colors -
    /// against 56 GenericTypedContainer blocks; every other one is patch-covered.
    /// This corrects the B10 waiver in <c>ConvergenceStructureScenarios</c>, which
    /// claimed no interact clone exists on either default surface.
    /// </para>
    /// <para>
    /// Fixture: <c>ConvergenceConfig</c> boots Mode B
    /// (<c>ReconditionOnConfigChange = true</c>), which is what wires the join-time
    /// snapshot send (<c>CarrySystem.Lifecycle.cs:127-134</c>) and therefore makes the
    /// payload observable on the wire; <c>AutoMapSimilar = true</c> is pinned, also
    /// its default.
    /// </para>
    /// </summary>
    [Trait("area", "Handler path")]
    [Trait("summary", "On a Mode B join, an automapped interact twin must be described by the carryability snapshot with its donor's properties, not reported absent.")]
    [AtlasDataFiles("fixtures/ConvergenceConfig", TargetPath = "ModConfig")]
    public class InteractTwinPayloadScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - join send lands within this

        /// <summary>The measured automapped interact twin (see the class summary).</summary>
        private const string TwinCode = "game:storagevessel-blue-fired";

        /// <summary>
        /// The factory-made marker the baseline's un-create sweep and the automapper's
        /// appends set - internal on the behavior class, so reflection is the only
        /// read the suite has (no InternalsVisibleTo; same shape as
        /// <c>ConvergenceStructureScenarios.MarkerOf</c>).
        /// </summary>
        private static FieldInfo? MarkerField()
            => typeof(BlockBehaviorCarryableInteract)
                .GetField("CreatedByConditioning", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// The block's factory-made interact twin, read from the raw arrays rather
        /// than <c>GetBehavior&lt;T&gt;()</c>: conditioning reassigns the arrays after
        /// registration, so a behavior cache can go stale (the caveat
        /// <c>CarryOnParityAssert.MaxPriority</c> documents).
        /// </summary>
        private static BlockBehaviorCarryableInteract? AutomappedTwin(Block block, FieldInfo marker)
            => block.BlockBehaviors
                .OfType<BlockBehaviorCarryableInteract>()
                .FirstOrDefault(b => (bool)marker.GetValue(b)!);

        /// <summary>
        /// The twin's interact entry of the latest captured snapshot, if any.
        /// </summary>
        private CarryabilityStateMessage.CarryabilityEntry? LatestTwinEntry(ITestPlayer player)
        {
            var messages = player.Client.Packets<CarryabilityStateMessage>(Channel);
            if (messages.Count == 0) return null;
            var latest = messages[messages.Count - 1];
            return latest.Entries?.FirstOrDefault(e =>
                e.BlockCode == TwinCode && e.Kind == BlockBehaviorCarryableInteract.Name);
        }

        /// <summary>
        /// L3 - a player joining a Mode B world receives the boot snapshot, and the
        /// automapped interact twin's entry must carry the donor's properties JSON.
        /// On the pre-fix head the twin was broadcast as
        /// <c>(code, carryableinteract, null)</c> - the protocol's "block has no such
        /// behavior" answer - so the client reconciled the twin away while the server
        /// kept one.
        /// </summary>
        [Trait("summary", "On a Mode B join, the automapped interact twin on a fired storage vessel carries a non-blank properties payload in the carryability snapshot.")]
        [Trait("pins", "The snapshot describes automapped interact twins with their donor's properties.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the twin was appended without initialization, so its payload normalized to null.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task AutomappedInteractTwin_SnapshotCarriesPropertiesPayload()
        {
            Assert.NotNull(World.Api);

            // Seed premises - a missed fixture flag degrades to Mode A, where no join
            // send exists and this row would fail on the wrong assertion.
            Assert.True(Mod.Config.CarryablesFilters.AutoMapSimilar,
                "Seed did not keep Interactables/CarryablesFilters.AutoMapSimilar on - "
                + "without it the interact automapper never creates the twin this row measures");
            Assert.True(Mod.Config.DebuggingOptions.ReconditionOnConfigChange,
                "Seed did not boot Mode B - the join-time snapshot send is wired only under "
                + "Mode B (CarrySystem.Lifecycle.cs:127-134), so the payload would be unobservable");

            // Premise: the automapper actually twin-created the measured block.
            var marker = MarkerField();
            Assert.True(marker != null,
                "structural premise: BlockBehaviorCarryableInteract needs the internal "
                + "'CreatedByConditioning' instance field the factory sets (no InternalsVisibleTo, "
                + "so the suite reads it by reflection)");

            var twin = World.Api!.World.GetBlock(new AssetLocation(TwinCode));
            Assert.True(twin != null && twin.Id != 0,
                $"Row premise: the registry must hold '{TwinCode}' (the fired storage vessel - "
                + "clay/fired/storagevessel.json is not matched by the clay/storagevessel* interact "
                + "patch glob, so it can only hold an automapper twin)");
            var behavior = AutomappedTwin(twin!, marker!);
            Assert.True(behavior != null,
                $"Row premise: '{TwinCode}' must hold a factory-made interact twin - without one "
                + "there is no payload to measure (was it patched after all, or AutoMapSimilar off?)");

            // The join-time snapshot send lands within the settle window (R7's pattern).
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await World.Ticks(SettleTicks);

            var joinSnapshots = player.Client.Packets<CarryabilityStateMessage>(Channel);
            Assert.True(joinSnapshots.Count > 0,
                "Row premise: Mode B must send the boot snapshot to joiners - no "
                + $"CarryabilityStateMessage arrived on channel '{Channel}'");

            var entry = LatestTwinEntry(player);
            Assert.True(entry != null,
                $"Row premise: the snapshot must carry a {BlockBehaviorCarryableInteract.Name} "
                + $"entry for '{TwinCode}' (the code is in the managed set - every block code is, "
                + "under a Mode B baseline capture)");

            Assert.False(string.IsNullOrWhiteSpace(entry!.PropertiesJson),
                "L3: the automapped interact twin's snapshot payload must be non-null - a blank "
                + "payload means the twin was appended without Initialize, CurrentPropertiesJson "
                + "stayed blank, and Normalize folded blank to null (BehavioralConditioning.cs). "
                + "The client then mounts no interact behavior for a block the server has one on "
                + $"(CarrySystem.Configuration.cs). code={entry.BlockCode}, "
                + $"kind={entry.Kind}, PropertiesJson={(entry.PropertiesJson == null ? "<null>" : $"'{entry.PropertiesJson}'")}");

            Assert.NotNull(World.Api);
        }
    }
}
