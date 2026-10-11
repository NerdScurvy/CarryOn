using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Server.Logic;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// B9 - the overlay merge loss.
    /// RackEmUp contributes properties to CarryOn's conditional mold-rack base
    /// through <c>overrideExistingProperties</c>: at boot the base and the
    /// overlay both land, the resolver merges the overlay into the base in
    /// place (<c>primaryBehavior.Initialize(merged)</c>,
    /// <c>MultipleBehaviorResolver.cs:110-137</c>) and removes the overlay
    /// instance - so the rack's <b>live</b> carryable carries
    /// <c>forcePickupOnSwapBack: true</c> and <c>transferCode: "auto"</c> in its
    /// parsed fields. On the first signature-dirty recondition, the pre-fix
    /// reconcile remounted the base from its <b>pristine</b> capture (the
    /// pre-merge JSON) and nothing restored the overlay - it was never captured
    /// (unconditional, so capture was not its to own), so the resolver saw one
    /// carryable and merged nothing. The live behavior reverted to the plain
    /// base: <see cref="BlockBehaviorCarryable.ForcePickupOnSwapBack"/> fell
    /// back to <c>false</c> and <see cref="BlockBehaviorCarryable.TransferEnabled"/>
    /// (the public read of the private <c>transferCode</c>) to <c>false</c> -
    /// RackEmUp's rack transfer stopped resolving, the issue's own repro
    /// symptom. Green since the pass-0 baseline: the overlay is a baseline
    /// entry, so pass 0 re-mounts it and the resolver re-merges the pair onto a
    /// fresh mount on every run. The second row walks two signature-dirty
    /// reloads, so the overlay's contribution has to survive repeated
    /// reconditioning rather than one lucky merge.
    /// <para>
    /// The fixture seeds <c>Carryables.MoldRack: true</c> - the family default
    /// is false, and with it off the base is disabled at boot and there is no
    /// merge to lose. The merged <c>CurrentPropertiesJson</c> is
    /// <c>internal</c>, and the client snapshot cannot stand in for it:
    /// <c>CurrentPropertiesJson</c> prefers the engine-set
    /// <c>propertiesAtString</c> (the pristine patch snippet) over the resolver-merged <c>Properties</c>, so the snapshot
    /// reports the pristine base at boot and after a churn alike and cannot
    /// evidence the loss. The live parsed fields are the discriminating public
    /// surface, so the rows assert through them. The overlay's
    /// <c>transferDelay</c> lives on RackEmUp's own
    /// <c>rackemup:moldracktransfer</c> behavior (not a carryable, never
    /// conditioned), so it is not part of the assertion.
    /// </para>
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "Overlay merge across a recondition: RackEmUp's overlay properties merged into the mold rack's base must survive one or more signature-dirty reloads.")]
    [AtlasDataFiles("fixtures/OverlayReconditionConfig", TargetPath = "ModConfig")]
    public class OverlayReconditionScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private const string RackCode = "game:moldrack-normal-north";

        /// <summary>
        /// The shape the automapper treats as "no shape" - a cube registers no shape key, so
        /// the twin scan must not either.
        /// </summary>
        private const string DefaultCubeShape = "block/basic/cube";

        private static readonly string[] DefaultAllowedShapes =
            ["block/clay/lootvessel", "block/wood/chest/normal", "block/wood/trunk/normal", "block/reed/basket-normal"];

        /// <summary>
        /// An unrelated allowlist shape no staged blocktype uses - the churn edit's
        /// signature-dirty but behavior-inert delta (the sibling issue's own example
        /// for a dirtying edit).
        /// </summary>
        private const string InertChurnShape = "block/convergenceprobe/none";

        /// <summary>
        /// A second unrelated allowlist shape, for the second successive recondition: the
        /// merge must re-derive on <b>every</b> run, not only on the first one after boot.
        /// </summary>
        private const string InertChurnShape2 = "block/convergenceprobe/none2";

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>
        /// Full-file Mode B payload: every key pinned to the fixture's boot value
        /// (Chest and MoldRack both on - the fixture seeds them) except the churn's
        /// appended allowlist shape.
        /// </summary>
        private static string FullConfig(string[]? allowedShapes = null)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": true,
                "MoldRack": true
              },
              "CarryablesFilters": {
                "AutoMapSimilar": true,
                "AutoMatchIgnoreMods": ["mcrate"],
                "AllowedShapeOnlyMatches": [{{string.Join(", ", (allowedShapes ?? DefaultAllowedShapes).Select(s => $"\"{s}\""))}}]
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", CultureInfo.InvariantCulture)}},
                "AllowHighCapacityStorageOnBack": false,
                "PickupGroundStorageBagWhileCarrying": false
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": true
              }
            }
            """;

        /// <summary>Polls <see cref="Mod.Config"/> once per tick until <paramref name="predicate"/> holds.</summary>
        private async Task WaitForConfigAsync(System.Func<CarryOn.Common.Models.CarryOnConfig, bool> predicate, string because, int timeoutTicks = WaitTimeoutTicks)
        {
            try
            {
                await World.Until(() => predicate(Mod.Config), timeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    $"Timed out after {timeoutTicks} ticks waiting for config state - {because}. "
                    + $"Now: MoldRack={Mod.Config.Carryables.MoldRack}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>
        /// A signature-dirty recondition must not lose the overlay's merged properties: the
        /// rack's live carryable keeps <c>ForcePickupOnSwapBack</c> and <c>TransferEnabled</c>
        /// exactly as at boot. The premise asserts the boot state (the merged live fields -
        /// the resolver's merge <c>Initialize</c> parses the overlay's keys into the base
        /// instance); the churn is an unrelated, behavior-inert allowlist append; the
        /// control asserts the base itself survives (the red is the merge loss, not a
        /// strip).
        /// <para>
        /// Two clauses extend this row. <b>Two successive
        /// reconditions</b>: a second, different inert delta re-runs the whole pipeline and
        /// the merge must be re-derived again - a surface that only converges "the first
        /// time after boot" is still a convergence bug. <b>Donor/twin determinism</b>: the
        /// automapper clones the donor's <i>live, merged</i> carryable properties onto every
        /// block whose key it matches, so the twin must report exactly the donor's merged
        /// surface - full property JSON plus the two parsed fields. The historical
        /// divergence ran the other way (the donor lost the overlay while its twins, cloned
        /// from a differently-surfaced donor, kept it), and the pass order in
        /// <c>BehavioralConditioning.Init</c> - resolver, then automapper - is what makes
        /// the two agree; swapping the two makes this row red.
        /// </para>
        /// <para>
        /// The twin needs a target that boots carryable-less, and the default surface has
        /// none: every block that shares a class, entity-class or allowlisted shape key with
        /// a donor boots with its own carryable patch, so the automapper - which only
        /// clones onto <c>!IsCarryable()</c> blocks - has nothing to clone onto. RackEmUp
        /// therefore ships <c>rackemup:moldrack-twin-*</c>, a mold rack blocktype with no
        /// <c>carryon:carryable</c> behavior. The scan below finds the twins by the
        /// automapper's own rules rather than by name, and the premise demands at least one
        /// so the clause cannot pass vacuously.
        /// </para>
        /// <para>
        /// No <c>ResetToFixtureAsync</c> before the premise: every config write re-runs
        /// Mode B's <c>Init</c>, and that recondition is itself the defect this row pins -
        /// a reset would un-merge the rack before the premise could read the boot state.
        /// The row is alone in its class, so the fixture the class boots with is the state
        /// the row starts from.
        /// </para>
        /// </summary>
        [Trait("summary", "A signature-dirty recondition must not lose the overlay's merged properties: the rack's live carryable keeps forcePickupOnSwapBack and its transfer code across one and then a second reload.")]
        [Trait("pins", "The overlay merge is re-derived on every run, and automap twins report the donor's merged surface.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the pre-fix head, where reconcile remounted the base from its pristine capture and the overlay was silently lost.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ChurnEdit_OverlayMerge_SurvivesInLiveBehavior()
        {
            Assert.NotNull(World.Api);
            Assert.True(
                Mod.Config.Carryables.MoldRack,
                "fixture must boot with Carryables.MoldRack = true (the family default is false)");

            var rack = World.Api.World.GetBlock(new AssetLocation(RackCode));
            Assert.True(
                rack != null,
                $"Row premise: the registry must hold '{RackCode}' (RackEmUp staged?)");
            CarryOnParityAssert.AssertPresentInBoth(
                rack!,
                "Row premise: the mold rack must hold its merged carryable in both arrays "
                + "(the conditional base is enabled: MoldRack=true)");

            // The boot state: the base with RackEmUp's overlay merged into it by the
            // resolver. ForcePickupOnSwapBack is the overlay's key - the pristine base
            // patch carries none - so a live true proves the merge's Initialize ran
            // (premise, green today).
            var boot = rack!.GetBehavior<BlockBehaviorCarryable>();
            Assert.True(
                boot != null,
                "Row premise: the rack's carryable must be readable through GetBehavior");
            Assert.True(
                boot!.ForcePickupOnSwapBack,
                "Row premise: the boot carryable must carry the overlay's "
                + "forcePickupOnSwapBack - the resolver merges RackEmUp's overlay "
                + "(overrideExistingProperties) into the base at boot and its "
                + "Initialize parses the key into the live instance. Actual: false - "
                + "the pristine base patch carries no such key, so a false here means "
                + "the merge never happened and the row's premise is wrong");

            // The churn: a signature-dirty but behavior-inert filters edit (an unrelated
            // allowlist shape no staged blocktype uses).
            File.WriteAllText(ConfigPath, FullConfig(allowedShapes: [.. DefaultAllowedShapes, InertChurnShape]));
            await WaitForConfigAsync(
                c => c.CarryablesFilters.AllowedShapeOnlyMatches.Contains(InertChurnShape),
                "the edit appends an unrelated AllowedShapeOnlyMatches entry");
            await World.Ticks(SettleTicks); // recondition + broadcast complete inside the reload

            // Control: the rack is still carryable - the base is a baseline entry and
            // enabled, so pass 0 re-mounted it; the red below is the merge loss, not a strip.
            CarryOnParityAssert.AssertPresentInBoth(
                rack!,
                "control: the recondition must keep the rack's carryable mounted (the "
                + "baseline entry is enabled) - the failure below is about the merge, not the strip");

            // RED: the live behavior must still carry the overlay's merged semantics.
            var churned = rack.GetBehavior<BlockBehaviorCarryable>();
            Assert.True(
                churned != null,
                "the recondition must keep a readable carryable mounted (the control "
                + "above just showed the rack still carryable)");
            Assert.True(
                churned!.ForcePickupOnSwapBack,
                "a recondition must not lose the overlay's merged properties - a fresh "
                + "boot re-merges RackEmUp's overlay (overrideExistingProperties) into the "
                + "base every run, so forcePickupOnSwapBack must survive the churn. The "
                + "overlay is never captured (unconditional), so reconcile's pristine "
                + "remount leaves nothing for the resolver to merge, and the live behavior "
                + "reverts to the plain base (the overlay-loss defect). Actual: ForcePickupOnSwapBack=false");
            Assert.True(
                churned.TransferEnabled,
                "transferCode must survive the churn for the same reason - the merged "
                + "transferCode: \"auto\" resolved the rack's transfer handler at boot; the "
                + "pristine remount carries none, so RackEmUp's rack transfer stops "
                + "resolving (the issue's own repro symptom). Actual: TransferEnabled=false");

            // Phase 5: a SECOND successive recondition
            // The merge is re-derived by every run, not restored once. A second
            // signature-dirty run (a different inert delta) must reach the same merged surface.
            File.WriteAllText(
                ConfigPath,
                FullConfig(allowedShapes: [.. DefaultAllowedShapes, InertChurnShape, InertChurnShape2]));
            await WaitForConfigAsync(
                c => c.CarryablesFilters.AllowedShapeOnlyMatches.Contains(InertChurnShape2),
                "the second edit appends a second unrelated AllowedShapeOnlyMatches entry");
            await World.Ticks(SettleTicks);

            CarryOnParityAssert.AssertPresentInBoth(
                rack!,
                "control: the second recondition must keep the rack's carryable mounted");

            var afterSecond = rack.GetBehavior<BlockBehaviorCarryable>();
            Assert.True(
                afterSecond != null,
                "the second recondition must keep a readable carryable mounted");
            Assert.True(
                afterSecond!.ForcePickupOnSwapBack,
                "the overlay's merge must survive a SECOND successive recondition too - the "
                + "restored overlay is a baseline entry, so every run re-merges it into a "
                + "fresh mount; a run that resolved the pristine base instead would leave "
                + "ForcePickupOnSwapBack=false here). Actual: false");
            Assert.True(
                afterSecond.TransferEnabled,
                "transferCode must survive the second recondition for the same reason - a "
                + "run that lost the overlay would leave TransferEnabled=false. Actual: false");

            // Phase 5: donor / automap-twin determinism
            // The automapper clones the donor's (merged) carryable properties onto every
            // block whose donor key it matches, so the twins must report the same merged
            // surface as the donor - after every recondition, not just at boot. The
            // historical divergence ran the other way (the donor lost the overlay while
            // its twins kept it, because the twins were cloned from the merged donor and
            // the donor was remounted pristine).
            var (twins, twinReport) = AutomapTwinsOf(rack!, Mod.Config.CarryablesFilters.AllowedShapeOnlyMatches);
            Assert.True(
                twins.Count > 0,
                "Row premise: the automapper must have cloned the mold rack's carryable onto "
                + "at least one twin in this fixture, or the donor/twin clause below is "
                + "vacuous. " + twinReport);

            foreach (var twin in twins)
            {
                var twinBehavior = twin.GetBehavior<BlockBehaviorCarryable>();
                Assert.True(
                    twinBehavior != null,
                    $"Row premise: automap twin '{twin.Code}' must hold a carryable - it "
                    + "matched the donor's key, booted carryable-less, and the scan only "
                    + "accepts a marked clone");
                var twinProperties = twinBehavior!.Properties?.Token?.ToString();
                Assert.True(
                    twinProperties == afterSecond.Properties?.Token?.ToString(),
                    "donor/twin divergence: the automap twin '" + twin.Code
                    + "' must carry the same merged property JSON as its donor - the "
                    + "automapper clones the donor's live properties onto every matched "
                    + "block, so equal JSON is the 'twins report the same merged properties' "
                    + "contract in full (not only the two keys the parsed fields check). "
                    + "Donor: " + afterSecond.Properties?.Token?.ToString()
                    + " Twin: " + twinProperties);
                Assert.True(
                    twinBehavior.ForcePickupOnSwapBack == afterSecond.ForcePickupOnSwapBack,
                    $"donor/twin divergence: the automap twin '{twin.Code}' reports "
                    + $"ForcePickupOnSwapBack={twinBehavior.ForcePickupOnSwapBack} while the "
                    + $"donor reports {afterSecond.ForcePickupOnSwapBack} - the automapper "
                    + "clones the donor's properties, so a twin that disagrees means the "
                    + "donor and its clones were built from different surfaces (the "
                    + "overlay-loss issue's twin divergence)");
                Assert.True(
                    twinBehavior.TransferEnabled == afterSecond.TransferEnabled,
                    $"donor/twin divergence: the automap twin '{twin.Code}' reports "
                    + $"TransferEnabled={twinBehavior.TransferEnabled} while the donor "
                    + $"reports {afterSecond.TransferEnabled} - the same cloned-properties "
                    + "contract as above");
            }
        }

        /// <summary>
        /// I1 - the resolver's merge target is a mount, never a baseline-owned instance, and
/// a merged run leaves the baseline byte-identical.
            /// <para>
        /// RackEmUp's overlay is a second carryable on the mold rack, so the resolver merges
        /// every run. The merge used to land on the primary in place
        /// (<c>primaryBehavior.Initialize(merged)</c>), and after the baseline restore the
        /// primary is the very object the restore pass mounted from the pristine JSON - so
        /// the merge mutated a baseline-owned object, and a remount could have fed a merge
        /// into itself. Three facts pin the fix:
        /// <list type="number">
        /// <item>the base entry's live instance still carries the pristine base properties
        /// (no overlay key) and is no longer the block's survivor - the merge ran on a
        /// separate mount, not on the entry,</item>
        /// <item>the baseline's captured JSON for the rack is byte-identical before and
        /// after the merged run (I3), and</item>
        /// <item>the live merged survivor still carries the overlay's key, so the mount is
        /// a faithful merge and not a plain base remount.</item>
        /// </list>
        /// The premise is read off the untouched class boot (same no-reset reasoning as
        /// <see cref="ChurnEdit_OverlayMerge_SurvivesInLiveBehavior"/>), and the row is
        /// order-invariant under convergence: every config write here preserves the merge,
        /// so either row may run first.
        /// </para>
        /// </summary>
        [Trait("summary", "A merged run must leave the baseline's captured JSON byte-identical and merge onto a fresh mount rather than a baseline-owned instance, so the overlay is a faithful merge and not a second application.")]
        [Trait("pins", "The resolver's merge target is a mount created for the run, never a baseline-owned instance.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ChurnEdit_MergedRun_LeavesBaselineUnchanged()
        {
            Assert.NotNull(World.Api);
            Assert.True(
                Mod.Config.Carryables.MoldRack,
                "fixture must boot with Carryables.MoldRack = true (the family default is false)");

            var rack = World.Api.World.GetBlock(new AssetLocation(RackCode));
            Assert.True(
                rack != null,
                $"Row premise: the registry must hold '{RackCode}' (RackEmUp staged?)");

            var boot = rack!.GetBehavior<BlockBehaviorCarryable>();
            Assert.True(
                boot != null,
                "Row premise: the rack's carryable must be readable through GetBehavior");
            Assert.True(
                boot!.ForcePickupOnSwapBack,
                "Row premise: the boot carryable must carry the overlay's forcePickupOnSwapBack "
                + "- the merge ran, so the rack is the one block with two carryables to merge");

            // The baseline's captured JSON for the rack, before any run mutates anything.
            var pristineBefore = BaselineCapturesFor(RackCode);
            Assert.True(
                pristineBefore.Any(json => !json.Contains("forcePickupOnSwapBack", StringComparison.OrdinalIgnoreCase)),
                "Row premise: the base's capture must be the pristine base JSON (the overlay's "
                + "key belongs to the overlay's own entry) - a capture set with no pre-merge "
                + "base in it would mean the merge wrote back into the baseline (I1/I3). "
                + "Actual: " + string.Join(" | ", pristineBefore));

            // The churn: the same signature-dirty, behavior-inert filters edit B9 uses.
            File.WriteAllText(ConfigPath, FullConfig(allowedShapes: [.. DefaultAllowedShapes, InertChurnShape]));
            await WaitForConfigAsync(
                c => c.CarryablesFilters.AllowedShapeOnlyMatches.Contains(InertChurnShape),
                "the edit appends an unrelated AllowedShapeOnlyMatches entry");
            await World.Ticks(SettleTicks);

            // (2) I1/I3 - the merged run must not have written the merge into the baseline.
            var pristineAfter = BaselineCapturesFor(RackCode);
            Assert.Equal(
                pristineBefore,
                pristineAfter);

            // (1) I1 - the merge never ran on the baseline-owned instance. The base entry's
            // live representative is the object the restore pass mounted from the pristine
            // JSON; an in-place merge would have Initialize'd the overlay's keys into exactly
            // that object (and left it as the block's live carryable). The merge must land
            // on a separate mount, so the entry's own object still carries the pristine base
            // and is no longer the block's survivor.
            var baseInstance = BaselineInstanceWithoutOverlayKey(RackCode);
            var churned = rack.GetBehavior<BlockBehaviorCarryable>();
            Assert.True(
                churned != null,
                "the recondition must keep a readable carryable mounted");
            Assert.False(
                baseInstance.Properties?.Token?.ToString()
                    .Contains("forcePickupOnSwapBack", StringComparison.OrdinalIgnoreCase) == true,
                "I1: the resolver's merge must never call Initialize on a baseline-owned "
                + "object - the base entry's live instance still carries the overlay's "
                + "forcePickupOnSwapBack, so the merge went into the entry the restore pass "
                + "mounted instead of into a fresh mount.");
            Assert.False(
                ReferenceEquals(baseInstance, churned),
                "I1: the live merged survivor must be a mount created by the resolver, not the "
                + "baseline-owned instance it was merged from");

            // (3) The mount is a faithful merge, not a plain base remount.
            Assert.True(
                churned!.ForcePickupOnSwapBack,
                "the mount must still carry the overlay's merged forcePickupOnSwapBack - a "
                + "merge that lost the overlay's keys would satisfy I1 but break the rack");
        }

        /// <summary>
        /// The <see cref="BlockBehaviorCarryable"/> the baseline's entry for
        /// <paramref name="code"/> whose capture has no overlay key holds - the conditional
        /// base, i.e. the merge primary, read by reflection (the baseline is an internal
        /// type, so a structural row probes it the way the B5c/B10 marker facts do).
        /// </summary>
        private static BlockBehaviorCarryable BaselineInstanceWithoutOverlayKey(string code)
        {
            var list = BaselineEntriesFor(code);
            Assert.True(
                list.Count > 0,
                $"Row premise: the baseline must hold an entry for '{code}' - the resolver "
                + "and the strips call EnsureBaseline before they mutate a block");

            foreach (var entry in list)
            {
                var entryType = entry!.GetType();
                var json = (string)entryType.GetField("PristineJson")!.GetValue(entry)!;
                if (json.Contains("forcePickupOnSwapBack", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var instance = entryType.GetField("Instance")!.GetValue(entry);
                Assert.True(
                    instance is BlockBehaviorCarryable,
                    "Row premise: the base entry's live representative must be a carryable - "
                    + "the restore pass remounts it from the captured JSON");
                return (BlockBehaviorCarryable)instance!;
            }

            Assert.Fail(
                $"Row premise: no baseline entry for '{code}' has a pre-merge capture - the "
                + "base's captured JSON must be the pristine base, without the overlay's key");
            throw new InvalidOperationException("unreachable");
        }

        /// <summary>
        /// The baseline's captured JSON per entry for <paramref name="code"/>, read by
        /// reflection. One string per entry, in list order - the rack holds the conditional
        /// base and RackEmUp's overlay, so the row can assert that a pre-merge base capture
        /// is among them and that the set is byte-identical across a merged run.
        /// </summary>
        private static System.Collections.Generic.List<string> BaselineCapturesFor(string code)
        {
            var captures = new System.Collections.Generic.List<string>();
            foreach (var entry in BaselineEntriesFor(code))
            {
                captures.Add((string)entry.GetType().GetField("PristineJson")!.GetValue(entry)!);
            }
            return captures;
        }

        /// <summary>The baseline's entry objects for <paramref name="code"/>.</summary>
        private static System.Collections.IList BaselineEntriesFor(string code)
        {
            var list = BaselineEntries()[code] as System.Collections.IList;
            Assert.True(
                list != null && list.Count > 0,
                $"Row premise: the baseline must hold an entry for '{code}' - the resolver "
                + "and the strips call EnsureBaseline before they mutate a block");
            return list!;
        }

        /// <summary>
        /// How many boot-time CarryOn-type behaviors the baseline captured for
        /// <paramref name="code"/> - zero for a code that booted carryable-less, including a
        /// code <see cref="ConditioningBaseline"/> only holds an empty list for
        /// (<see cref="System.Collections.IDictionary"/> indexer returns a list the
        /// <c>EnsureBaseline</c> call created but recorded nothing into). The twin
        /// identification reads this instead of <c>IsCarryable()</c>: by assertion time an
        /// automap clone makes a twin carryable, so only the capture can say what it booted
        /// with, and "no capture" is exactly the automapper's own target condition
        /// (<c>!w.IsCarryable()</c> at clone time).
        /// </summary>
        private static int CapturedBaselineCount(string code)
            => BaselineEntries()[code] as System.Collections.IList is { } list ? list.Count : 0;

        /// <summary>
        /// The baseline's private static <c>entries</c> dictionary - the per-world capture
        /// the whole class probes by reflection (the type and the field are internal, and
        /// B5c/B10 pin the same reflective style for the internal marker).
        /// </summary>
        private static System.Collections.IDictionary BaselineEntries()
        {
            var baselineType = typeof(BehavioralConditioning).Assembly
                .GetType("CarryOn.Server.Logic.BehavioralConditions.ConditioningBaseline");
            Assert.True(
                baselineType != null,
                "Row premise: the mod assembly must still declare the internal "
                + "ConditioningBaseline type");

            var entriesField = baselineType!.GetField(
                "entries",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var entries = entriesField?.GetValue(null) as System.Collections.IDictionary;
            Assert.True(
                entries != null,
                "Row premise: ConditioningBaseline must still hold the readable private "
                + "static entries dictionary");
            return entries!;
        }

        /// <summary>
        /// The automap twins of <paramref name="donor"/>: the blocks the automapper cloned
        /// this donor's carryable onto, found by the automapper's own rules
        /// (<c>BehaviorAutoMapper.cs:39-113</c>). A twin must satisfy every one of them, or
        /// the row would compare a block the automapper never cloned:
        /// <list type="bullet">
        /// <item>not the donor, and not in the configured <c>AutoMatchIgnoreMods</c>;</item>
        /// <item>has an entity class (the automapper skips the rest);</item>
        /// <item>one of its potential keys - <c>class|shape</c>, <c>entityclass|shape</c>,
        /// <c>shape</c>, then <c>class</c>, or bare <c>class</c> without a shape - is a key
        /// the donor registered (its entity-class, class and non-cube allowlisted-shape
        /// keys);</item>
        /// <item>booted carryable-less (no baseline capture), which is the automapper's
        /// <c>!IsCarryable()</c> target condition at clone time;</item>
        /// <item>and its live carryable carries the factory marker, so the block really was
        /// cloned rather than patched.</item>
        /// </list>
        /// The report string carries the donor's keys and the whole scan, so a failing
        /// premise says which clause ran out (no candidate, a candidate that booted
        /// carryable, an ignore-mod exclusion) instead of just "no twins".
        /// </summary>
        private (List<Block> Twins, string Report) AutomapTwinsOf(Block donor, IEnumerable<string>? allowedShapes)
        {
            var filters = Mod.Config.CarryablesFilters;
            var shapes = new HashSet<string>(allowedShapes ?? [], StringComparer.OrdinalIgnoreCase);
            var ignoreMods = new HashSet<string>(filters.AutoMatchIgnoreMods ?? [], StringComparer.OrdinalIgnoreCase);

            var donorShape = donor.ShapeInventory?.Base?.Path ?? donor.Shape?.Base?.Path;
            var donorEntityKey = donor.EntityClass is not (null or "Generic" or "Transient")
                ? $"EntityClass:{donor.EntityClass}"
                : null;
            var donorClassKey = donor.Class is not (null or "Block" or "BlockGeneric")
                ? $"Class:{donor.Class}"
                : null;
            var donorShapeKey = donorShape != null && donorShape != DefaultCubeShape
                ? $"Shape:{donorShape}"
                : null;

            var donorKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in new[] { donorEntityKey, donorClassKey }.Where(k => k != null))
                donorKeys.Add(key!);
            if (donorShapeKey != null)
            {
                if (donorEntityKey != null) donorKeys.Add($"{donorEntityKey}|{donorShapeKey}");
                if (donorClassKey != null) donorKeys.Add($"{donorClassKey}|{donorShapeKey}");
                if (shapes.Contains(donorShape!)) donorKeys.Add(donorShapeKey);
            }

            var twins = new List<Block>();
            var scan = new List<string>();
            foreach (var block in World.Api!.World.Blocks)
            {
                if (block == null || block == donor || block.Code == null) continue;
                if (block.EntityClass == null || ignoreMods.Contains(block.Code.Domain)) continue;

                var shape = block.ShapeInventory?.Base?.Path ?? block.Shape?.Base?.Path;
                var classKey = $"Class:{block.Class}";
                var entityKey = $"EntityClass:{block.EntityClass}";
                var shapeKey = shape != null ? $"Shape:{shape}" : null;
                var key = (shapeKey == null ? new[] { classKey } : new[]
                {
                    $"{classKey}|{shapeKey}",
                    $"{entityKey}|{shapeKey}",
                    shapeKey,
                    classKey
                }).FirstOrDefault(donorKeys.Contains);
                if (key == null) continue;

                var captured = CapturedBaselineCount(block.Code.ToString());
                var carryable = block.GetBehavior<BlockBehaviorCarryable>();
                var cloned = carryable != null && IsFactoryClone(carryable);
                if (captured == 0 && cloned) twins.Add(block);
                scan.Add(
                    $"{block.Code} key={key} captured={captured} carryable={carryable != null} "
                    + $"cloned={cloned}");
            }

            var report =
                $"Donor {donor.Code}: class={donor.Class}, entityclass={donor.EntityClass}, "
                + $"shape={donorShape ?? "(none)"}, registered keys=[{string.Join(" | ", donorKeys)}]. "
                + $"Key-matched blocks ({(scan.Count == 0 ? "none" : string.Join("; ", scan))}).";
            return (twins, report);
        }


        /// <summary>
        /// Whether <paramref name="carryable"/> is a CarryOn-factory-constructed instance
        /// (a remount or an automap clone) - the internal marker field, read the way B5c/B10
        /// read it.
        /// </summary>
        private static bool IsFactoryClone(BlockBehaviorCarryable carryable)
            => (bool)(typeof(BlockBehaviorCarryable)
                .GetField("CreatedByConditioning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(carryable) ?? false);
    }
}
