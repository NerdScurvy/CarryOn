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
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// B4 - the blank-JSON base (C4), the one shape the old base pass's record
    /// could never cover. A carryable appended programmatically <b>without</b>
    /// <c>Initialize</c> has a blank <c>CurrentPropertiesJson</c>, so
    /// <c>RecordRemovedBaseCarryables</c> skipped it: the strip still removed it
    /// from both arrays, but nothing recorded the removal, so clearing the prefix
    /// restored nothing. Red-first against that head; green since the pass-0
    /// baseline, which captures such a behavior as an <c>InstanceReference</c>
    /// entry - the recorded <i>object</i> is the only restore source a blank-JSON
    /// base has, so its restoration is not limited to recorded JSON.
    /// <para>
    /// The target is <c>game:planks-aged-ud</c>: no carryable patch, no
    /// <c>entityclass</c>, default <c>Block</c> class and the <c>block/basic/cube</c>
    /// shape - the automapper ignores all three keys, so the block registers no
    /// donor key and is never a clone target. It is fully inert until the row's
    /// own append, which is what makes the row's before/after comparison clean.
    /// </para>
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Blank-JSON base restore row: a carryable appended without Initialize is stripped by a matched base prefix and must be restored when the prefix is cleared, because the baseline captured it as an instance reference.")]
    [AtlasDataFiles("fixtures/ConvergenceConfig", TargetPath = "ModConfig")]
    public class ConvergenceRestoreScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private const string PlanksCode = "game:planks-aged-ud";
        private static readonly string[] DefaultBaseFilter = ["woodchests:wtrunk"];

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>
        /// Full-file Mode B payload. Only <paramref name="removeBase"/> moves; every
        /// signature-relevant key is pinned to the fixture's boot value or left to
        /// the same default, so the only behavior-surface delta is the base filter.
        /// </summary>
        private static string FullConfig(params string[] removeBase)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": true
              },
              "CarryablesFilters": {
                "AutoMapSimilar": true,
                "AutoMatchIgnoreMods": ["mcrate"],
                "RemoveBaseCarryableBehaviour": [{{string.Join(", ", removeBase.Select(m => $"\"{m}\""))}}],
                "RemoveCarryableBehaviour": ["game:banner", "game:clutter-devastation"]
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

        /// <summary>Writes the payload and blocks until the live config shows the new filter.</summary>
        private async Task WriteAndAwaitFilterAsync(IReadOnlyList<string> removeBase, string because)
        {
            File.WriteAllText(ConfigPath, FullConfig([.. removeBase]));
            try
            {
                await World.Until(
                    () => Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour.SequenceEqual(removeBase),
                    WaitTimeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    $"Timed out after {WaitTimeoutTicks} ticks waiting for the reloaded base filter - {because}. "
                    + $"Now: [{string.Join(", ", Mod.Config.CarryablesFilters.RemoveBaseCarryableBehaviour)}].");
            }
        }

        /// <summary>Re-seeds the class fixture so rows are order-independent.</summary>
        private async Task ResetToFixtureAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            await WriteAndAwaitFilterAsync(
                DefaultBaseFilter,
                "re-seeding the class fixture's RemoveBaseCarryableBehaviour");
            await World.Ticks(20); // > 500 ms debounce - next write's event is not swallowed
        }

        /// <summary>
        /// A programmatically-appended carryable with no <c>Initialize</c> - blank properties
        /// JSON - is stripped by a matched base prefix but never recorded, so clearing
        /// the prefix restores nothing. A fresh boot with the cleared config would have
        /// the append; the reconditioned surface does not.
        /// </summary>
        [Trait("summary", "A carryable appended without Initialize (blank properties JSON) is stripped when its base prefix matches and restored when the prefix is cleared.")]
        [Trait("pins", "A stripped base restores even when its pristine JSON is blank, because the recorded object is the only restore source it has.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while RecordRemovedBaseCarryables skipped the blank-JSON append, so clearing the prefix restored nothing.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BareAppend_BasePrefixCleared_NothingRestores()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();

            var planks = World.Api.World.GetBlock(new AssetLocation(PlanksCode));
            Assert.True(
                planks != null,
                $"Row premise: the registry must hold '{PlanksCode}'");
            var boot = CarryOnParityAssert.Inspect(planks!);
            Assert.True(
                !boot.InEitherSide,
                $"Row premise: '{PlanksCode}' must start with no carryable - it is patchless, "
                + $"entityclass-less and automap-inert. Actual: {boot.Describe()}");

            // The C4 shape: appended without Initialize, so CurrentPropertiesJson is
            // blank and the baseline captures it as an InstanceReference entry. One
            // instance shared by both views - the engine's own
            // lone-base shape (the G7 finding), kept here so the shared-instance half
            // of the mirror contract is exercised too.
            var appended = new BlockBehaviorCarryable(planks!);
            planks!.BlockBehaviors = [.. planks.BlockBehaviors, appended];
            planks.CollectibleBehaviors = [.. planks.CollectibleBehaviors, appended];

            var before = CarryOnParityAssert.Inspect(planks);
            Assert.True(
                before.InBothSides && before.SharedInstance && before.MaxPriority == 0,
                $"Row premise: the bare append must sit in both arrays as one shared "
                + $"priority-0 instance. Actual: {before.Describe()}");

            // Matched: the strip must still clear the union (premise, green today -
            // marking the entry dirty is part of the removal, not an extra step).
            await WriteAndAwaitFilterAsync(
                [PlanksCode],
                "the edit adds the planks code to RemoveBaseCarryableBehaviour");
            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertRemovalCovered(
                before,
                planks,
                "the bare append is the block's only carryable and its survivor is a "
                + "priority-0 base, so the matched pass must clear the union of both "
                + "arrays even though its properties JSON is blank");

            // Cleared: pass 0 re-adds the recorded instance for the blank-JSON entry.
            await WriteAndAwaitFilterAsync(
                DefaultBaseFilter,
                "the edit clears the planks code from RemoveBaseCarryableBehaviour");
            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertRestoredTo(
                before,
                planks,
                "clearing the base prefix must restore the stripped append - a fresh "
                + "boot with the cleared config would hold it. The block is still not "
                + "carryable and the arrays are empty because the strip did not mark the "
                + "entry dirty, or the baseline captured it as a Json entry with a blank "
                + "pristine string instead of the InstanceReference shape this append "
                + "needs (C4: the recorded object is the only restore source a blank-JSON "
                + "base has)");
        }
    }
}
