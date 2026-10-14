using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace CarryOn.Scenarios.Wiring
{
    /// <summary>
    /// Plain xUnit wiring pins for lifecycle symmetry: everything a mod system or handler
    /// subscribes to or owns at boot must be released at teardown, and an override must call
    /// its base exactly once per invocation. Each row reads the staged <c>CarryOn.dll</c>
    /// with <c>PEReader</c>/<c>MetadataReader</c> and loads no assembly - loading it poisons
    /// the Atlas host's per-install preflight and breaks the version sweep. See
    /// <c>CarryabilitySyncWiringTests</c>.
    ///
    /// <para>These are wiring pins: they prove a call or a call count, not runtime behavior.
    /// A teardown that runs only on a dedicated server is still observable here because the
    /// pin reads the compiled IL, not a running instance.</para>
    /// </summary>
    [Trait("area", "Lifecycle")]
    [Trait("summary", "Plain xUnit wiring pins: each row reads the staged CarryOn.dll metadata and IL and asserts teardown symmetry - event unsubscription, owned-handler disposal, manager clearing, and a single base call - with no server boot.")]
    public class LifecycleWiringTests
    {
        private const string CarryHandlerTypeName = "CarryOn.Common.Handlers.CarryHandlers.CarryHandler";
        private const string CarrySystemTypeName = "CarryOn.CarrySystem";
        private const string DisposeMethodName = "Dispose";
        private const string AssetsFinalizeMethodName = "AssetsFinalize";

        /// <summary>
        /// Authored red-first. The handler subscribed to three events at boot but stored only
        /// one delegate, so one of the three was detached at teardown and the other two stayed
        /// attached. The row asserts the disposal calls each event's remove accessor; the
        /// premise proves the reader can see the one removal that is always performed.
        /// </summary>
        [Trait("summary", "CarryHandler.Dispose must unsubscribe all three server events it subscribed to at boot (entity spawn, player-now-playing, before-active-slot-changed).")]
        [Trait("pins", "Every server event CarryHandler subscribes to at boot is unsubscribed at teardown.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with the spawn and player-now-playing remove accessors absent from Dispose while before-active-slot-changed was present.")]
        [Trait("caveat", "Wiring only - proves the remove accessors are called, not that the runtime detachment succeeds.")]
        [Fact]
        public void CarryHandler_Dispose_UnsubscribesEveryBootSubscription()
        {
            using var peReader = new PEReader(File.OpenRead(StagedModProbe.ModAssemblyPath()));
            var reader = peReader.GetMetadataReader();

            var calls = StagedModProbe.CallsFrom(reader, peReader, CarryHandlerTypeName, DisposeMethodName);

            // Premise: the reader can see the one removal the handler does perform, so its
            // absence for the other two is meaningful.
            Assert.True(
                CallsEndingWith(calls, ".remove_BeforeActiveSlotChanged").Any(),
                $"Premise: {CarryHandlerTypeName}.{DisposeMethodName}() must remove its "
                + "before-active-slot-changed handler - it is the one unsubscription the "
                + "handler already performs and the control for this row. If the encoding "
                + "changed, re-derive this premise before trusting the assertions below. Calls "
                + $"present: {(calls.Count == 0 ? "(none)" : string.Join(", ", calls))}");

            Assert.True(
                CallsEndingWith(calls, ".remove_OnEntitySpawn").Any(),
                $"{CarryHandlerTypeName} subscribes to the entity-spawn event at boot, so its "
                + "disposal must call the matching remove accessor. Leaving it attached keeps "
                + "the handler alive through the server event table after teardown. Calls "
                + $"present: {(calls.Count == 0 ? "(none)" : string.Join(", ", calls))}");

            Assert.True(
                CallsEndingWith(calls, ".remove_PlayerNowPlaying").Any(),
                $"{CarryHandlerTypeName} subscribes to the player-now-playing event at boot, so "
                + "its disposal must call the matching remove accessor. Leaving it attached "
                + "keeps the handler alive through the server event table after teardown. Calls "
                + $"present: {(calls.Count == 0 ? "(none)" : string.Join(", ", calls))}");
        }

        /// <summary>
        /// Authored red-first. The server death handler owns subscriptions but the mod
        /// system's disposal never released it, so it survived teardown. The row accepts a
        /// direct call or a small helper.
        /// </summary>
        [Trait("summary", "CarrySystem.Dispose must reach DeathHandler.Dispose so the server death handler's subscriptions are released.")]
        [Trait("pins", "The server death handler created at boot is disposed with the mod system.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with DeathHandler.Dispose unreachable from CarrySystem.Dispose by any call path.")]
        [Trait("caveat", "Wiring only - proves reachability in the call graph, not that the disposal runs.")]
        [Fact]
        public void CarrySystem_Dispose_DisposesTheDeathHandler()
        {
            using var peReader = new PEReader(File.OpenRead(StagedModProbe.ModAssemblyPath()));
            var reader = peReader.GetMetadataReader();

            var reaches = StagedModProbe.ReachesMember(
                reader, peReader, CarrySystemTypeName, DisposeMethodName,
                "CarryOn.Common.Handlers.DeathHandler.Dispose");
            Assert.True(
                reaches,
                $"{CarrySystemTypeName}.{DisposeMethodName}() must reach "
                + "CarryOn.Common.Handlers.DeathHandler.Dispose(). The death handler is created "
                + "at server start and holds subscriptions of its own; without a disposal path "
                + "it outlives the mod system and keeps firing against stale state.");
        }

        /// <summary>
        /// Authored red-first. The mod system must clear the shared carry manager it published
        /// so a later world in the same process does not read a stale manager. The row asserts
        /// the setter is reached; the manager property lives on the API mod system, so the
        /// call is a cross-assembly member reference - which the reader resolves by name.
        /// </summary>
        [Trait("summary", "CarrySystem.Dispose must clear the published carry manager so a later world in the same process does not see stale state.")]
        [Trait("pins", "The carry manager published to the API mod system is cleared when the mod system is disposed.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with the CarryManager setter unreachable from CarrySystem.Dispose.")]
        [Trait("caveat", "Wiring only - proves the setter is reached, not that a bare null is the fix's shape.")]
        [Fact]
        public void CarrySystem_Dispose_ClearsThePublishedCarryManager()
        {
            using var peReader = new PEReader(File.OpenRead(StagedModProbe.ModAssemblyPath()));
            var reader = peReader.GetMetadataReader();

            var reaches = StagedModProbe.ReachesMember(
                reader, peReader, CarrySystemTypeName, DisposeMethodName,
                "CarryOn.CarryOnLib.CarryOnLibSystem.set_CarryManager");
            Assert.True(
                reaches,
                $"{CarrySystemTypeName}.{DisposeMethodName}() must reach "
                + "CarryOn.CarryOnLib.CarryOnLibSystem.set_CarryManager. The carry manager is "
                + "published to the shared API mod system and is read by consumers and by the "
                + "next world in the same process; leaving the disposed manager published means "
                + "a later world starts against stale state.");
        }

        /// <summary>
        /// Authored red-first. The asset-finalize override called its base twice in one
        /// invocation, so the base work ran twice per boot. The row counts the calls; the
        /// premise proves the base call is visible at all before the count is trusted.
        /// </summary>
        [Trait("summary", "CarrySystem.AssetsFinalize must call base.AssetsFinalize exactly once.")]
        [Trait("pins", "The asset-finalize override runs its base implementation exactly once per invocation.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head by counting two base.AssetsFinalize calls in the override.")]
        [Trait("caveat", "Wiring only - proves the call count in the override, not the runtime cost of the extra base call.")]
        [Fact]
        public void CarrySystem_AssetsFinalize_CallsBaseExactlyOnce()
        {
            using var peReader = new PEReader(File.OpenRead(StagedModProbe.ModAssemblyPath()));
            var reader = peReader.GetMetadataReader();

            var il = StagedModProbe.IlOf(reader, peReader, CarrySystemTypeName, AssetsFinalizeMethodName);
            var calls = StagedModProbe.CallList(reader, il);

            var baseCalls = calls.Count(c => c.EndsWith(".AssetsFinalize", StringComparison.Ordinal)
                && c.Contains("ModSystem", StringComparison.Ordinal));

            Assert.True(
                baseCalls >= 1,
                $"Premise: no base asset-finalize call was found in "
                + $"{CarrySystemTypeName}.{AssetsFinalizeMethodName}() - the reader could not "
                + "see the engine base call, so the count below would prove nothing. Re-derive "
                + "this premise against the actual base type before trusting it. Calls present: "
                + $"{(calls.Count == 0 ? "(none)" : string.Join(", ", calls))}");

            Assert.True(
                baseCalls == 1,
                $"{CarrySystemTypeName}.{AssetsFinalizeMethodName}() must call its base exactly "
                + $"once; found {baseCalls}. Calling the base twice runs the engine's asset "
                + "finalization twice per boot, which is both wasteful and a reentrancy hazard "
                + "for anything the base finalizes.");
        }

        private static IReadOnlyList<string> CallsEndingWith(IEnumerable<string> calls, string suffix)
            => calls.Where(call => call.EndsWith(suffix, StringComparison.Ordinal)).ToList();
    }
}
