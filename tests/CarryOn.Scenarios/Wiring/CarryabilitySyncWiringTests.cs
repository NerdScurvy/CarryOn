using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace CarryOn.Scenarios.Wiring
{
    /// <summary>
    /// Wiring pin for the recondition render-cache invalidation.
    ///
    /// <para><b>What this proves.</b> <c>CarrySystem.RebuildClientDerivedState</c> - the
    /// method that swaps the <c>TransformTemplateManager</c> after a carryability snapshot
    /// - calls <c>EntityCarryRenderer.InvalidateRenderCaches()</c>. The test reads the
    /// staged <c>CarryOn.dll</c> file with <c>PEReader</c>/<c>MetadataReader</c>, decodes
    /// the method's IL, and resolves every <c>call</c>/<c>callvirt</c> operand token to a
    /// member name by reading metadata - comparing declaring type and name, not matching a
    /// byte pattern, so it survives recompilation and is not tied to one opcode encoding.
    /// A call is always present in IL: the JIT inlines across method calls at run time,
    /// not the compiler, so no <c>NoInlining</c> production attribute is needed and none is
    /// added for this test's benefit.</para>
    ///
    /// <para><b>Why it deliberately loads no assembly at all.</b> The first version of this
    /// test loaded <c>CarryOn.dll</c> and resolved tokens through
    /// <c>Module.ResolveMethod</c> - which loads every type the method touches, and with
    /// them the test-output copy of <c>VintagestoryAPI.dll</c>. The Atlas host stages each
    /// server install's own engine assemblies into the test output and verifies nothing
    /// engine-side was already loaded; a plain xUnit test that runs before (or alongside)
    /// Atlas classes therefore poisons the process, and the cross-version sweep
    /// (<c>run-versions.ps1</c>) fails on every install but the one the binaries were built
    /// against - exactly what happened on the first sweep (1.22.7 passed, 1.22.6 and below
    /// failed Atlas's preflight with "VintagestoryAPI.dll was already loaded"). Reading
    /// metadata from the file has no such footprint: the sweep runs clean on every install,
    /// and the pin still fails loudly if the method or the renderer moves.</para>
    ///
    /// <para><b>What this does not prove.</b> It pins wiring, not behavior. The render
    /// caches are client-only and the Atlas harness is a headless dedicated server, where
    /// <c>Mod.ClientApi</c> is never assigned and the renderer is never constructed - the
    /// method under test returns at its first line in every scenario in this suite (pinned
    /// in-band by
    /// <c>ReconditionRenderCacheScenarios.DedicatedServer_NeverBuildsTheRenderPipeline</c>).
    /// The behavioral half is a manual real-client gate, and the
    /// <b>input</b> to the defect is pinned by that class's premise row. Do not read a
    /// green row here as "the render cache is covered".</para>
    ///
    /// <para>Deliberately a plain xUnit class, not an <c>AtlasScenario</c>: it needs no
    /// server boot, so it runs in the same <c>dotnet test</c> invocation at no cost. It
    /// lives in the scenario project anyway so one <c>run-tests.ps1</c> covers it - a
    /// known, recorded exception, not an accident.</para>
    ///
    /// <para><b>Why a full invalidation and not a cache-key change.</b> The conditioning
    /// signature is incomplete in a way a key change cannot repair: the per-slot sidecar
    /// memo in <c>CarryRenderCacheManager.GetRenderInfoCached</c> only recomputes when one
    /// of its own dirty inputs changed, and a behavior remount changes none of them, so the
    /// sidecar hands back the pre-recondition <c>CachedTransformPlan</c> without ever
    /// reaching the plan cache. A manager generation would have to be taught to both layers.
    /// A full flush is one call and clears both.</para>
    /// </summary>
    [Trait("area", "Sync/Network")]
    [Trait("summary", "Plain xUnit wiring pins: each row reads the staged CarryOn.dll metadata and IL and asserts a specific call or exception region is present in the carryability-sync and config-reload paths, with no server boot.")]
    public class CarryabilitySyncWiringTests
    {
        private const string SyncTypeName = "CarryOn.CarrySystem";
        private const string SyncMethodName = "RebuildClientDerivedState";
        private const string InvalidationMethodName = "InvalidateRenderCaches";
        private const string RendererTypeName = "CarryOn.Client.Logic.CarryRenderer.EntityCarryRenderer";
        private const string ExpectedCall = RendererTypeName + "." + InvalidationMethodName;

        /// <summary>
        /// The carryability sync must invalidate the client's render caches after it
        /// swaps the transform template manager. RED before the fix, green after; it is the
        /// one genuinely red-first artifact this suite can honestly ship. Everything the fix
        /// depends on beyond the call itself - that the input is reachable, and that the
        /// render path is client-only - is pinned by
        /// <c>ReconditionRenderCacheScenarios</c>.
        /// </summary>
        [Trait("summary", "RebuildClientDerivedState must call EntityCarryRenderer.InvalidateRenderCaches after swapping the transform template manager.")]
        [Trait("pins", "The carryability sync invalidates the client's render caches, the wiring half of the stale-pose fix.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with the render-cache invalidation call absent from the method's enumerated calls.")]
        [Trait("decision", "25")]
        [Fact]
        public void CarryabilitySync_InvalidatesClientRenderCaches()
        {
            var modAssemblyPath = ProbeModAssemblyPath();

            using var peReader = new PEReader(File.OpenRead(modAssemblyPath));
            var reader = peReader.GetMetadataReader();
            var sync = FindMethod(reader, SyncTypeName, SyncMethodName);
            Assert.True(
                sync.HasValue,
                $"{SyncTypeName}.{SyncMethodName}() not found in the staged mod assembly - "
                + "if the method was renamed or inlined away, this pin must be re-derived "
                + "against the new call site rather than deleted, and H3's fix re-checked at "
                + "whatever replaced it");

            var body = peReader.GetMethodBody(sync!.Value.RelativeVirtualAddress);
            Assert.True(
                body != null,
                $"{SyncTypeName}.{SyncMethodName}() has no IL body - an abstract or extern "
                + "method cannot carry the call this pin is about");

            var il = body!.GetILBytes();
            Assert.True(
                il != null && il.Length > 0,
                $"{SyncTypeName}.{SyncMethodName}() has no IL bytes - an empty body cannot "
                + "carry the call this pin is about");

            var calls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var call in ResolveCalledMembers(reader, il!))
            {
                calls.Add(call);
            }

            Assert.True(
                calls.Contains(ExpectedCall),
                $"{SyncMethodName}() must call {ExpectedCall}() after swapping the transform "
                + "template manager. Without it the client's cached transform plans and render "
                + "infos survive a behavior remount: the plan-cache key excludes every "
                + "behavior property and the manager identity, and the per-slot sidecar memo "
                + "bypasses the plan cache entirely, so a carried block keeps its "
                + "pre-recondition pose until it is dropped and re-picked. Calls actually "
                + $"present: {(calls.Count == 0 ? "(none)" : string.Join(", ", calls))}");
        }

        // ---- Config-reload latch pins ----

        private const string ConfigServiceTypeName = "CarryOn.Common.Logic.CarryOnConfigService";
        private const string ReloadMethodName = "ReloadFromFile";
        private const string DebuggingOptionsTypeName = "CarryOn.Common.Models.DebuggingOptionsConfig";
        private const string LatchProperty = "ReconditionOnConfigChangeEnabled";
        private const string LiveFlagProperty = "ReconditionOnConfigChange";
        private const string LatchRead = ConfigServiceTypeName + ".get_" + LatchProperty;
        private const string LiveFlagRead = DebuggingOptionsTypeName + ".get_" + LiveFlagProperty;

        /// <summary>
        /// W1 - RED before the fix. The reload gate must read the session's
        /// boot-time latch, never the reloaded file's own flag.
        /// <para>
        /// <b>This one pin covers both entry points.</b> The two paths were confirmed by hand
        /// that the file watcher and the <c>/carryon-reload</c> command both funnel into
        /// <c>CarryOnConfigService.ReloadFromFile</c>; neither can bypass it. The
        /// behavioral rows therefore only have to split the two entry points - G1/G3 on the
        /// watchdog, D4 on the command - while this pin has to cover the gate itself, once.
        /// </para>
        /// <para>
        /// It is an <i>absence</i> pin, which is unusual and deliberate: it fails on the
        /// pre-fix head by naming the offending getter rather than by missing a call, so a
        /// regression back to the live read is impossible without somebody deliberately
        /// editing this pin. That matters because the pre-fix form looked perfectly
        /// reasonable at the call site - it was the flag the tooltip, the user docs and the
        /// technical doc all describe as "requires restart".
        /// </para>
        /// <para>
        /// <b>One legitimate read of the live flag remains</b>, and deliberately so: it lives
        /// in <c>WarnOnModeMismatch</c>, to report a file that disagrees with the latch. So
        /// the live getter is permitted anywhere in the assembly except in
        /// <c>ReloadFromFile</c> itself. If a future change needs the file's value at the
        /// gate again, it must either go back through the warning helper or re-derive this
        /// pin - not quietly reintroduce the read here. The alternative, normalising the
        /// config object to agree with the latch, is banned outright: it would push a boot
        /// value into the user's file through the non-reload writeback path, which is the
        /// data-clobber class of bug this MR already fixed once.
        /// </para>
        /// </summary>
        [Trait("summary", "ReloadFromFile must read the boot-time recondition latch and must not read the reloaded file's own flag.")]
        [Trait("pins", "The reload gate is the boot latch, so a mid-session file edit cannot switch the session's mode.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head by reading the live flag getter at the reload gate instead of the boot latch.")]
        [Fact]
        public void ReloadFromFile_GatesOnTheBootLatch_NotTheLiveFlag()
        {
            var modAssemblyPath = ProbeModAssemblyPath();

            using var peReader = new PEReader(File.OpenRead(modAssemblyPath));
            var reader = peReader.GetMetadataReader();
            var calls = CallsFrom(reader, peReader, ConfigServiceTypeName, ReloadMethodName);

            Assert.True(
                calls.Contains(LatchRead),
                $"{ReloadMethodName}() must read the boot latch {ConfigServiceTypeName}."
                + $"{LatchProperty}, seeded once by CarrySystem at StartPre. The file's own "
                + $"{LiveFlagProperty} describes the *next* server start - reading it here "
                + "lets a mid-session file edit switch a running server's mode, including "
                + $"into live reconditioning. Calls actually present: "
                + $"{(calls.Count == 0 ? "(none)" : string.Join(", ", calls))}");

            Assert.True(
                !calls.Contains(LiveFlagRead),
                $"{ReloadMethodName}() must NOT read the live flag {LiveFlagRead}. The gate "
                + "is the boot latch - the flag's tooltip, the user docs and the technical doc "
                + "all say it requires a restart, so a session must honour what it booted with. "
                + "Reading it here is the M1 defect, and it silently re-arms reconditioning "
                + "for any operator who edits the file mid-session. Reporting a mismatch is "
                + "still wanted, but it belongs in WarnOnModeMismatch, not at the gate - if "
                + "you genuinely need the file's value here again, re-derive this pin and "
                + "justify it; do not delete this assertion. Also do not 'fix' the mismatch "
                + "by assigning Config.DebuggingOptions.ReconditionOnConfigChange, which would "
                + "persist a boot value into the user's file.");
        }

        private const string ConditioningBaselineTypeName =
            "CarryOn.Server.Logic.BehavioralConditions.ConditioningBaseline";
        private const string AssetsFinalizeMethodName = "AssetsFinalize";
        private const string SetCaptureEnabledCall = ConditioningBaselineTypeName + ".SetCaptureEnabled";

        /// <summary>
        /// W2 - the gate that makes W1's change safe to combine.
        /// <para>
        /// The fix ships two edits that must not be separated: the reload gate stops reading the
        /// live flag (W1), and Mode A stops capturing a baseline. Ship the second without
        /// the first and a session that booted Mode A, had the file turn the flag on, and
        /// then reloaded would enter a conditioning pass with an <i>empty</i> baseline -
        /// so its first <c>RestoreBaseline</c> would capture post-mutation state as
        /// "pristine" and every later run would converge to it (the hazard the baseline
        /// design exists to prevent).
        /// </para>
        /// <para>
        /// The structural statement is that both decisions are seeded from the same latch,
        /// at the same boot site: <c>CarrySystem.AssetsFinalize</c> must call
        /// <c>ConditioningBaseline.SetCaptureEnabled</c>. If it does, the capture gate
        /// cannot drift from the reload gate, and no reachable state lets Mode A reach
        /// <c>RestoreBaseline</c> with an empty baseline.
        /// </para>
        /// <para>
        /// The companion assertion matters as much as the first: <c>ReloadFromFile</c> must
        /// <b>not</b> call <c>SetCaptureEnabled</c>. A reload that could re-enable capture
        /// would reintroduce the mid-session mode switch that W1 exists to close, wearing the
        /// baseline's clothes this time.
        /// </para>
        /// </summary>
        [Trait("summary", "CarrySystem.AssetsFinalize must seed the conditioning capture gate from the boot latch, and ReloadFromFile must not re-enable it.")]
        [Trait("pins", "The capture gate cannot drift from the reload gate, so a Mode A session can never reach a restore with an empty baseline.")]
        [Trait("role", "contract")]
        [Fact]
        public void CaptureGate_IsSeededFromTheSameBootLatchAsTheReloadGate()
        {
            var modAssemblyPath = ProbeModAssemblyPath();

            using var peReader = new PEReader(File.OpenRead(modAssemblyPath));
            var reader = peReader.GetMetadataReader();

            var bootCalls = CallsFrom(reader, peReader, SyncTypeName, AssetsFinalizeMethodName);
            Assert.True(
                bootCalls.Contains(SetCaptureEnabledCall),
                $"{SyncTypeName}.{AssetsFinalizeMethodName}() must call "
                + $"{SetCaptureEnabledCall}, seeded from the boot-time mode latch. Mode A runs "
                + "conditioning exactly once per world, so it captures no baseline; if that "
                + "decision were left to default (or driven by the reloaded config) it could "
                + "disagree with the reload gate, and a session that booted Mode A, had the "
                + "file enable the flag, then reloaded would run its first conditioning pass "
                + "with an empty baseline and capture post-mutation state as pristine. Calls "
                + $"actually present: {(bootCalls.Count == 0 ? "(none)" : string.Join(", ", bootCalls))}");

            var reloadCalls = CallsFrom(reader, peReader, ConfigServiceTypeName, ReloadMethodName);
            Assert.True(
                !reloadCalls.Contains(SetCaptureEnabledCall),
                $"{ReloadMethodName}() must not call {SetCaptureEnabledCall}. Capture is a "
                + "boot-time decision: letting a reload re-enable it would let a mid-session "
                + "config edit change the session's mode, which is the same defect W1 pins "
                + "for the reload gate.");
        }

        /// <summary>
        /// Every member one named parameterless method calls, resolved from metadata with
        /// no assembly loaded - see the type summary for why nothing is loaded here.
        /// </summary>
        private static HashSet<string> CallsFrom(
            MetadataReader reader, PEReader peReader, string typeFullName, string methodName)
        {
            var method = FindMethod(reader, typeFullName, methodName);
            Assert.True(
                method.HasValue,
                $"{typeFullName}.{methodName}() not found in the staged mod assembly - if it "
                + "was renamed or inlined away, this pin must be re-derived against the new "
                + "call site rather than deleted");

            var body = peReader.GetMethodBody(method!.Value.RelativeVirtualAddress);
            Assert.True(
                body != null,
                $"{typeFullName}.{methodName}() has no IL body - an abstract or extern method "
                + "cannot carry the call this pin is about");

            var il = body!.GetILBytes();
            Assert.True(
                il != null && il.Length > 0,
                $"{typeFullName}.{methodName}() has no IL bytes - an empty body cannot carry "
                + "the call this pin is about");

            var calls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var call in ResolveCalledMembers(reader, il!))
            {
                calls.Add(call);
            }

            return calls;
        }

        // ---- Carryability-sync hardening pins ----

        private const string StateSyncMethodName = "OnCarryabilityStateSync";
        private const string TryApplyEntryMethodName = "TryApplyEntry";
        private const string RebuildCall = SyncTypeName + ".RebuildClientDerivedState";

        /// <summary>
        /// W1 - RED before the fix. <c>CarrySystem.OnCarryabilityStateSync</c>
        /// must not be able to wedge the client on one malformed entry.
        /// <para>
        /// <b>What is asserted, and why this is structural.</b> The handler is registered on
        /// the <i>client</i> channel inside <c>StartClientSide</c>, so it only ever runs in a
        /// real client process. Atlas's per-class world runs a dedicated server plus a
        /// <c>DummyTcpNetClient</c> that only decodes the byte stream - it never instantiates
        /// client mod systems and has no client block registry, so <c>GetBlock</c>,
        /// <c>SyncCarryabilityBehavior</c> and <c>RebuildClientDerivedState</c> execute in no
        /// test. An end-to-end row is therefore impossible, and invoking the handler
        /// reflectively with the server <c>ICoreServerAPI</c> would be a type mismatch that
        /// exercises no client path at all - a green row on that basis would be actively
        /// misleading. The fix's <i>structure</i> is the strongest thing assertable, and the
        /// structure is the whole fix: the per-entry body must sit inside a handler that
        /// continues, and the derived-state rebuild must be reached from a path that runs
        /// unconditionally.
        /// </para>
        /// <para>
        /// Three separate requirements, because this is three defects, not one:
        /// <list type="number">
        /// <item>the sync method carries a <c>finally</c> region whose handler reaches
        /// <c>RebuildClientDerivedState</c> - the render caches and transform template manager
        /// are rebuilt even when an entry threw, and no exception escapes to skip them;</item>
        /// <item>it calls <c>TryApplyEntry</c>, the per-entry boundary that catches and logs
        /// (BlockCode, kind) instead of letting a throw escape the loop;</item>
        /// <item><c>TryApplyEntry</c> itself carries at least one <c>catch</c> region, so the
        /// two vectors a caller can still reach - <c>new AssetLocation</c> on an invalid code and
        /// <c>JToken.Parse</c> on a malformed payload - are both contained at the one place
        /// that knows the entry's identity.</item>
        /// </list>
        /// The split matters because each is separately regressable: a future change could
        /// keep the per-entry catch but move the rebuild back out of the <c>finally</c>, which
        /// would restore the silent half of the wedge (a stale transform template manager, no
        /// exception anywhere) with every catch still in place.
        /// </para>
        /// </summary>
        [Trait("summary", "OnCarryabilityStateSync must wrap its loop in a finally reaching the derived-state rebuild, apply entries through TryApplyEntry, and catch and warn inside it.")]
        [Trait("pins", "One malformed entry cannot wedge the client: the per-entry boundary catches and the derived state always rebuilds.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with no finally region reaching the derived-state rebuild and entries applied inline.")]
        [Fact]
        public void CarryabilityStateSync_ContainsEveryEntry_AndAlwaysRebuildsDerivedState()
        {
            var modAssemblyPath = ProbeModAssemblyPath();

            using var peReader = new PEReader(File.OpenRead(modAssemblyPath));
            var reader = peReader.GetMetadataReader();

            var sync = FindMethod(reader, SyncTypeName, StateSyncMethodName);
            Assert.True(
                sync.HasValue,
                $"{SyncTypeName}.{StateSyncMethodName}() not found in the staged mod assembly - "
                + "if it was renamed, this pin must be re-derived against the new call site "
                + "rather than deleted, and M2's fix re-checked at whatever replaced it");

            var syncBody = peReader.GetMethodBody(sync!.Value.RelativeVirtualAddress);
            var syncIl = syncBody!.GetILBytes()!;
            var syncCalls = CallsFrom(reader, peReader, SyncTypeName, StateSyncMethodName);

            // (1) The rebuild is reached from a finally, not merely called somewhere.
            var finallyRegions = syncBody.ExceptionRegions
                .Where(r => r.Kind == ExceptionRegionKind.Finally)
                .ToArray();
            Assert.True(
                finallyRegions.Length > 0,
                $"{SyncTypeName}.{StateSyncMethodName}() must wrap its body in try/finally so "
                + $"{RebuildCall}() runs even when an entry throws. Without it a single "
                + "malformed entry leaves the client holding a transform template manager and "
                + "render caches built from the pre-sync block registry, and nothing in the log "
                + "says so - the engine catches handler exceptions, so the failure is silent "
                + "by omission. If the rebuild genuinely must stay conditional, re-derive this "
                + "pin and justify it; do not delete it.");

            var rebuildInFinally = finallyRegions.Any(r =>
                CallsWithinRegion(reader, syncIl, r.HandlerOffset, r.HandlerLength)
                    .Contains(RebuildCall));
            Assert.True(
                rebuildInFinally,
                $"{RebuildCall}() must be called from the finally handler of "
                + $"{StateSyncMethodName}(), not merely somewhere in the method. A finally that "
                + "does not reach it leaves the derived client state stale after a throw. "
                + $"Finally regions: {finallyRegions.Length}; calls present in the method: "
                + $"{(syncCalls.Count == 0 ? "(none)" : string.Join(", ", syncCalls))}");

            // (2) The per-entry boundary exists and is actually used.
            Assert.True(
                syncCalls.Contains(SyncTypeName + "." + TryApplyEntryMethodName),
                $"{StateSyncMethodName}() must apply entries through {TryApplyEntryMethodName}(), "
                + "which catches per entry and logs the offending (BlockCode, kind) before "
                + "continuing. Applying the body inline is the M2 defect: a throw from "
                + "new AssetLocation or JToken.Parse escapes the loop, so the remaining entries "
                + "never apply, syncedCarryabilityCodes is never updated, and the derived-state "
                + "rebuild never runs. Calls actually present: "
                + $"{(syncCalls.Count == 0 ? "(none)" : string.Join(", ", syncCalls))}");

            // (3) The boundary actually catches.
            var applyCalls = CallsFrom(reader, peReader, SyncTypeName, TryApplyEntryMethodName);
            Assert.True(
                applyCalls.Contains(SyncTypeName + "." + SyncCarryabilityBehaviorMethodName),
                $"{TryApplyEntryMethodName}() must call {SyncCarryabilityBehaviorMethodName}() - "
                + "otherwise the pin above is satisfied by a method that applies nothing. Calls "
                + $"actually present: {(applyCalls.Count == 0 ? "(none)" : string.Join(", ", applyCalls))}");

            var applyMethod = FindMethod(reader, SyncTypeName, TryApplyEntryMethodName);
            var applyBody = peReader.GetMethodBody(applyMethod!.Value.RelativeVirtualAddress);
            var catchRegions = applyBody.ExceptionRegions
                .Where(r => r.Kind == ExceptionRegionKind.Catch)
                .ToArray();
            Assert.True(
                catchRegions.Length > 0,
                $"{TryApplyEntryMethodName}() must contain a try/catch. It is the single place "
                + "that knows an entry's BlockCode and kind, so it is the only place that can "
                + "both contain a throw and report which entry caused it. This is where M2's "
                + "two unlisted vectors are absorbed: new AssetLocation(code) throws on a "
                + "syntactically invalid code, and JToken.Parse(propertiesJson) throws on a "
                + "malformed payload (that one is deliberately NOT caught inside "
                + $"{SyncCarryabilityBehaviorMethodName}(), so it carries this context).");

            var catchLogs = catchRegions
                .SelectMany(r => CallsWithinRegion(reader, applyBody.GetILBytes()!, r.HandlerOffset, r.HandlerLength))
                .ToArray();
            Assert.True(
                catchLogs.Any(c => c.EndsWith(".Warning", StringComparison.Ordinal)),
                $"Every catch in {TryApplyEntryMethodName}() must log at Warning. M2's whole "
                + "point is that the failure stops being silent: a server operator has to be "
                + "able to see that a mod's conditioning output is malformed. Calls in the "
                + $"catch handlers: {(catchLogs.Length == 0 ? "(none)" : string.Join(", ", catchLogs))}");
        }

        private const string SyncCarryabilityBehaviorMethodName = "SyncCarryabilityBehavior";

        private static MethodDefinition FindRequiredMethod(
            MetadataReader reader, string typeFullName, string methodName)
        {
            var method = FindMethod(reader, typeFullName, methodName);
            Assert.True(
                method.HasValue,
                $"{typeFullName}.{methodName}() not found in the staged mod assembly - if it "
                + "was renamed or inlined away, this pin must be re-derived against the new "
                + "call site rather than deleted");
            return method!.Value;
        }

        /// <summary>
        /// The members called from the IL window [offset, offset+length), resolved from
        /// metadata. Exception-region offsets are file-relative and the IL byte array is
        /// method-relative, so the two line up for a method body - which is exactly the
        /// granularity an exception handler is expressed in.
        /// </summary>
        private static HashSet<string> CallsWithinRegion(
            MetadataReader reader, byte[] il, int offset, int length)
        {
            if (offset <= 0 || length <= 0 || offset >= il.Length) return new HashSet<string>(StringComparer.Ordinal);

            var end = Math.Min(il.Length, offset + length);
            var calls = new HashSet<string>(StringComparer.Ordinal);

            // Resolve over the whole body but keep only the ones landing in the window. The
            // decoder walks instructions rather than raw bytes, so an operand straddling the
            // window edge cannot produce a phantom member name.
            var decoder = new InstructionScanner(reader, il);
            while (decoder.MoveNext())
            {
                if (decoder.Offset < offset || decoder.Offset >= end) continue;
                if (decoder.Member != null) calls.Add(decoder.Member);
            }

            return calls;
        }

        /// <summary>
        /// Walks IL instructions, exposing each one's offset and, for call/callvirt, the
        /// resolved member name. Extracted so both the whole-body and windowed reads share
        /// one decoder - the class summary records why metadata is read instead of loading
        /// the assembly.
        /// </summary>
        private sealed class InstructionScanner
        {
            private readonly MetadataReader reader;
            private readonly byte[] il;
            private readonly OpCode[] oneByte = new OpCode[0x100];
            private readonly OpCode[] twoByte = new OpCode[0x100];
            private int position;

            public int Offset { get; private set; }
            public string? Member { get; private set; }

            public InstructionScanner(MetadataReader reader, byte[] il)
            {
                this.reader = reader;
                this.il = il;

                foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (field.GetValue(null) is not OpCode opcode) continue;
                    if (opcode.Size == 1) oneByte[opcode.Value & 0xFF] = opcode;
                    else twoByte[opcode.Value & 0xFF] = opcode;
                }
            }

            public bool MoveNext()
            {
                Member = null;
                if (position >= il.Length) return false;

                Offset = position;
                var first = il[position++];
                var opcode = first == 0xFE
                    ? (position < il.Length ? twoByte[il[position++]] : default)
                    : oneByte[first];
                if (opcode.Size == 0 && first != 0xFE) return true; // unknown opcode: skip the byte

                if (opcode.OperandType == OperandType.InlineMethod)
                {
                    if (position + sizeof(int) > il.Length) return false;
                    Member = ResolveMemberToken(reader, BitConverter.ToInt32(il, position));
                    position += sizeof(int);
                    return true;
                }

                position += OperandSize(il, position, opcode.OperandType);
                return position <= il.Length;
            }
        }

        /// <summary>
        /// Finds a parameterless method by declaring type full name and method name.
        /// Namespace and type name are matched separately so a nested or renamed type fails
        /// loudly instead of silently matching nothing.
        /// </summary>
        private static MethodDefinition? FindMethod(
            MetadataReader reader, string typeFullName, string methodName)
        {
            var dot = typeFullName.LastIndexOf('.');
            var typeNamespace = dot < 0 ? "" : typeFullName[..dot];
            var typeName = typeFullName[(dot + 1)..];

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                if (!string.Equals(reader.GetString(type.Namespace), typeNamespace, StringComparison.Ordinal)
                    || !string.Equals(reader.GetString(type.Name), typeName, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var methodHandle in type.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if (string.Equals(reader.GetString(method.Name), methodName, StringComparison.Ordinal))
                    {
                        return method;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Every member the IL calls, as <c>TypeName.MemberName</c>, resolved from metadata -
        /// no assembly is loaded. Tokens that do not resolve to a plain member name (generic
        /// instantiations on a <c>TypeSpec</c>) are recorded as such: the assertion is about
        /// the presence of a known call, not about naming every one of them.
        /// </summary>
        private static IEnumerable<string> ResolveCalledMembers(MetadataReader reader, byte[] il)
        {
            var oneByteOpcodes = new OpCode[0x100];
            var twoByteOpcodes = new OpCode[0x100];
            foreach (var field in typeof(OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (field.GetValue(null) is not OpCode opcode) continue;
                if (opcode.Size == 1) oneByteOpcodes[opcode.Value & 0xFF] = opcode;
                else twoByteOpcodes[opcode.Value & 0xFF] = opcode;
            }

            for (var i = 0; i < il.Length;)
            {
                var first = il[i++];
                var opcode = first == 0xFE
                    ? twoByteOpcodes[il[i++]]
                    : oneByteOpcodes[first];

                if (opcode.OperandType == OperandType.InlineMethod)
                {
                    var token = BitConverter.ToInt32(il, i);
                    i += sizeof(int);
                    var member = ResolveMemberToken(reader, token);
                    if (member != null) yield return member;
                    continue;
                }

                i += OperandSize(il, i, opcode.OperandType);
            }
        }

        /// <summary>
        /// Resolves a metadata token to a member name. A <c>call</c>/<c>callvirt</c> operand
        /// is always a <c>MethodDef</c> (same assembly) or a <c>MemberRef</c> (other
        /// assembly) row; anything else is a malformed stream and is skipped.
        /// </summary>
        private static string? ResolveMemberToken(MetadataReader reader, int token)
        {
            var row = token & 0x00FFFFFF;
            switch (token >> 24)
            {
                case 0x06: // MethodDef
                {
                    var method = reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(row));
                    var declaring = TypeName(reader, method.GetDeclaringType());
                    return declaring == null ? null : $"{declaring}.{reader.GetString(method.Name)}";
                }
                case 0x0A: // MemberRef
                {
                    var member = reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(row));
                    var declaring = TypeName(reader, member.Parent);
                    return declaring == null ? null : $"{declaring}.{reader.GetString(member.Name)}";
                }
                default:
                    return null;
            }
        }

        /// <summary>The full name of a type handle, or null when it is not a named type
        /// (a generic <c>TypeSpec</c> - the caller reports those as unresolvable rather
        /// than guessing at a signature).</summary>
        private static string? TypeName(MetadataReader reader, EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                {
                    var type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                    var ns = reader.GetString(type.Namespace);
                    var name = reader.GetString(type.Name);
                    return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                }
                case HandleKind.TypeReference:
                {
                    var type = reader.GetTypeReference((TypeReferenceHandle)handle);
                    var ns = reader.GetString(type.Namespace);
                    var name = reader.GetString(type.Name);
                    return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                }
                default:
                    return null;
            }
        }

        private static int OperandSize(byte[] il, int offset, OperandType operandType)
            => operandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget
                    or OperandType.ShortInlineI
                    or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8
                    or OperandType.InlineR => 8,
                OperandType.InlineSwitch => sizeof(int) * BitConverter.ToInt32(il, offset),
                _ => sizeof(int), // InlineBrTarget, InlineField, InlineI, InlineSig,
                                  // InlineString, InlineTok, InlineType, ShortInlineR
            };

        private static string ProbeModAssemblyPath()
        {
            foreach (var directory in ProbeDirectories())
            {
                var candidate = Path.Combine(directory, "CarryOn.dll");
                if (File.Exists(candidate)) return candidate;
            }

            throw new FileNotFoundException(
                "CarryOn.dll not found. Build the mod in Release (run-tests.ps1 does this "
                + $"first) and re-run. Probed: {string.Join("; ", ProbeDirectories())}.");
        }

        /// <summary>
        /// Every directory that could hold the staged mod assembly, found by walking up from
        /// this test assembly's own location - <b>not</b> from <c>AppContext.BaseDirectory</c>,
        /// which the Atlas host points at the VintageStory install when it boots servers
        /// (verified on the full-suite run: BaseDirectory was the install root while the
        /// test assembly still lived in the repo's test output), so a base-directory walk
        /// silently finds no repo ancestor in a full-suite run while finding all of them in
        /// a filtered one. The assembly location is stable in both.
        /// </summary>
        private static IEnumerable<string> ProbeDirectories()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var anchor in new[]
            {
                typeof(CarryabilitySyncWiringTests).Assembly.Location,
                AppContext.BaseDirectory,
            })
            {
                if (string.IsNullOrEmpty(anchor)) continue;

                for (var dir = new DirectoryInfo(Path.GetDirectoryName(anchor)!); dir != null; dir = dir.Parent)
                {
                    foreach (var relative in new[]
                    {
                        Path.Combine("bin", "Release", "net10.0"),
                        Path.Combine("CarryOn", "bin", "Release", "net10.0"),
                        Path.Combine("CarryOnLib", "bin", "Release", "net10.0"),
                    })
                    {
                        var candidate = Path.GetFullPath(Path.Combine(dir.FullName, relative));
                        if (seen.Add(candidate) && Directory.Exists(candidate)) yield return candidate;
                    }
                }
            }
        }
    }
}
