using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// The defect 2 half of the overlay merge-loss issue:
    /// <c>"priority"</c> is not a recognized spelling of <c>patchPriority</c>
    /// (<c>BlockBehaviorCarryable.ParseBaseSettings</c> reads exactly one key), so an
    /// overlay that omits <c>patchPriority</c> resolves silently at effective
    /// priority 0 - the merge still happens (<c>0 &gt;= 0</c>) but the documented
    /// ascending-priority overlay ordering never engages, and the author gets no
    /// signal at all. Decision: no alias - one spelling only -
    /// plus a conditioning lint that warns once per offending behavior per run
    /// when <c>overrideExistingProperties: true</c> is declared without an explicit
    /// <c>patchPriority</c> key.
    /// <para>
    /// The lint runs at the resolver's pass-3 start - before the merge retires the
    /// overlay, before the mount the merge creates is <c>Initialize</c>d from
    /// merged properties (which inherit the overlay's
    /// <c>overrideExistingProperties</c> while possibly carrying no
    /// <c>patchPriority</c>), and before the automap clones that carry the same
    /// merged shape - so only pristine patch-loaded overlays are ever judged. The
    /// boot lint fires before a row can subscribe, so the row observes the warning
    /// on a Mode-B recondition: <c>ILogger.EntryAdded</c>
    /// (<c>LogEntryDelegate(EnumLogType, string, object[])</c>, verified by
    /// reflection against 1.22.7) fires for every log line, and one signature-dirty
    /// churn re-runs the whole pipeline.
    /// </para>
    /// <para>
    /// The boundary is <b>missing vs declared</b>, and the CarryEntityDemo fixture
    /// pair pins both sides: <c>overlaymissingpriority-block</c>'s overlay omits
    /// <c>patchPriority</c> (the lint's target - its <c>forcePickupOnSwapBack</c>
    /// premise also pins that a missing <c>patchPriority</c> still merges at
    /// effective 0, so the warning is advisory, not a behavior change), and
    /// <c>overlaybase-block</c> declares an explicit <c>patchPriority: 0</c> and must
    /// stay silent - a declared 0 is a value, not an omission. The exactly-one count
    /// also covers the non-overlay base side of the merged pair: the block's base
    /// never warns, or the count would be two.
    /// </para>
    /// <para>
    /// The row never asserts the mold rack: RackEmUp's patch corrects its own
    /// spelling (<c>priority</c> → <c>patchPriority</c>), and a row coupled to the
    /// misspelling would flip with it.
    /// </para>
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "An overlay that omits patchPriority must warn exactly once per recondition, while an overlay declaring an explicit zero stays silent, and the warning never changes resolution.")]
    [AtlasDataFiles("fixtures/OverlayReconditionConfig", TargetPath = "ModConfig")]
    public class OverlayPatchPriorityWarningScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch

        private const string MissingOverlayBlockCode = "carrieddemo:overlaymissingpriority-block";

        /// <summary>The negative control: its overlay declares an explicit 0.</summary>
        private const string ControlBlockCode = "carrieddemo:overlaybase-block";

        private static readonly string[] DefaultAllowedShapes =
            ["block/clay/lootvessel", "block/wood/chest/normal", "block/wood/trunk/normal", "block/reed/basket-normal"];

        /// <summary>
        /// An unrelated allowlist shape no staged blocktype uses - the churn edit's
        /// signature-dirty but behavior-inert delta (the same inert-churn pattern the
        /// overlay recondition rows use).
        /// </summary>
        private const string InertChurnShape = "block/overlaylintprobe/none";

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
                "InteractSpeedMultiplier": 2.0
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": true
              }
            }
            """;

        /// <summary>Polls <see cref="Mod.Config"/> once per tick until <paramref name="predicate"/> holds.</summary>
        private async Task WaitForConfigAsync(System.Func<CarryOnConfig, bool> predicate, string because, int timeoutTicks = WaitTimeoutTicks)
        {
            try
            {
                await World.Until(() => predicate(Mod.Config), timeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    $"Timed out after {timeoutTicks} ticks waiting for config state - {because}. "
                    + $"Now: ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>
        /// An overlay that omits <c>patchPriority</c> must warn exactly once per
        /// recondition, while an overlay that declares an explicit
        /// <c>patchPriority: 0</c> stays silent. The premise reads the boot merge off
        /// the untouched class boot (no reset: every config write re-runs Mode B, and
        /// the recondition is the observation window itself), then one churn edit
        /// re-runs the pipeline with the EntryAdded capture subscribed.
        /// </summary>
        [Trait("summary", "A recondition must warn exactly once for an overlay that omits patchPriority, naming the key, never for the explicit-zero control, and must not change resolution.")]
        [Trait("pins", "Missing versus declared patchPriority is the lint boundary; the warning is advisory and never changes which behavior wins.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the pre-fix head, where the overlay resolved silently at effective priority zero with no warning.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task OverlayWithoutPatchPriority_WarnsOnce_ControlWithExplicitZero_Silent()
        {
            Assert.NotNull(World.Api);

            // Premise: the fixture block is registered and booted merged - the
            // merge gate is >=, so the overlay with no patchPriority still merges at
            // effective 0 (0 >= 0). The warning must therefore be advisory: it never
            // changes resolution.
            var missing = World.Api.World.GetBlock(new AssetLocation(MissingOverlayBlockCode));
            Assert.True(
                missing != null,
                $"Row premise: the registry must hold '{MissingOverlayBlockCode}' (CarryEntityDemo staged?)");
            var merged = missing!.GetBehavior<BlockBehaviorCarryable>();
            Assert.True(
                merged != null,
                "Row premise: the block's carryable must be readable through GetBehavior");
            Assert.True(
                merged!.ForcePickupOnSwapBack,
                "Row premise: the overlay omits patchPriority but the merge gate is >= "
                + "(0 >= 0), so the resolver must still have merged its forcePickupOnSwapBack "
                + "into the live survivor at boot - a missing patchPriority warns without "
                + "changing behavior. Actual: false - the merge never ran and the premise "
                + "is wrong");

            // Premise: the explicit-zero control is registered and resolved too.
            var control = World.Api.World.GetBlock(new AssetLocation(ControlBlockCode));
            Assert.True(
                control != null,
                $"Row premise: the registry must hold '{ControlBlockCode}' (CarryEntityDemo staged?)");
            Assert.True(
                control!.GetBehavior<BlockBehaviorCarryable>() != null,
                "Row premise: the control block's carryable must be readable through "
                + "GetBehavior (its overlay merges at 0 >= 0)");

            // Capture warning-class log lines from here on. The lint fires at boot
            // before a row can subscribe, so the row reads it off the recondition the
            // churn triggers: EntryAdded fires for every log line, so only Warning
            // entries are kept, and the add is lock-guarded because the entry may be
            // logged from the server thread.
            var warnings = new List<string>();
            var gate = new object();
            World.Api.Logger.EntryAdded += OnWarning;
            try
            {
                File.WriteAllText(ConfigPath, FullConfig(allowedShapes: [.. DefaultAllowedShapes, InertChurnShape]));
                await WaitForConfigAsync(
                    c => c.CarryablesFilters.AllowedShapeOnlyMatches.Contains(InertChurnShape),
                    "the edit appends an unrelated AllowedShapeOnlyMatches entry");
                await World.Ticks(SettleTicks); // recondition (and its lint) complete inside the reload
            }
            finally
            {
                World.Api.Logger.EntryAdded -= OnWarning;
            }

            // RED: exactly one warning per offending behavior per run.
            var forMissing = warnings
                .Where(w => w.Contains(MissingOverlayBlockCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(
                forMissing.Count == 1,
                "a carryable with overrideExistingProperties: true but no patchPriority "
                + "key must warn exactly once per recondition (the defect 2 lint). "
                + $"Actual count: {forMissing.Count} - zero means the lint did not fire; "
                + "two means either a double reload or the block's non-overlay base warned "
                + $"too (the base never qualifies). Captured warnings: "
                + $"{string.Join(" | ", warnings)}");

            // The warning must name the missing key so the author knows what to declare.
            Assert.True(
                forMissing[0].Contains("patchPriority", StringComparison.OrdinalIgnoreCase),
                "the lint must name the missing patchPriority key - an authoring warning "
                + $"that does not say which key to declare cannot be acted on. Actual: {forMissing[0]}");

            // Control: a declared patchPriority 0 is a value, not an omission - silent.
            Assert.True(
                !warnings.Any(w => w.Contains(ControlBlockCode, StringComparison.OrdinalIgnoreCase)),
                "an overlay that declares an explicit patchPriority: 0 must not warn - the "
                + "boundary is missing vs declared. Captured warnings: "
                + string.Join(" | ", warnings));

            // Post-control: the advisory lint changed nothing - the merged survivor
            // still carries the overlay's key after the recondition that warned.
            Assert.True(
                missing.GetBehavior<BlockBehaviorCarryable>()!.ForcePickupOnSwapBack,
                "the warned recondition must not change resolution - the overlay still "
                + "merges at effective 0 on every run");

            await Task.CompletedTask;

            void OnWarning(EnumLogType type, string message, object[] args)
            {
                if (type != EnumLogType.Warning) return;
                lock (gate) warnings.Add(message);
            }
        }
    }
}
