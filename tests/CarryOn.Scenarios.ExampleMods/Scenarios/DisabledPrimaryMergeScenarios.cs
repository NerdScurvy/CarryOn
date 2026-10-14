using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// Phase-1 rows: the resolver must never merge an overlay
    /// onto a <i>condition-disabled</i> base, because the merge inherits the base's
    /// <c>enabledCondition</c> and the final removal pass then strips the merged mount
    /// too - leaving the block with no carryable at all.
    /// <para>
    /// The pre-fix mechanism, exactly: <c>MergeOverridingCarryables</c>'s second fallback
    /// (<c>ordered.Where(c =&gt; !c.OverrideExistingProperties)</c>) drops the
    /// <c>!IsConditionDisabled</c> filter its first fallback has, so a disabled non-override
    /// wins as primary. The overlay filter (<c>PatchPriority &gt;= primary.PatchPriority
    /// &amp;&amp; !IsConditionDisabled</c>) then accepts the unconditional overlay at
    /// <c>0 &gt;= 0</c>, <c>MergeCarryableProperties</c> merges the disabled base's
    /// properties (including its <c>enabledCondition</c>) into the mount, and
    /// <c>BlockFilter.RemoveDisabledConditionalBehaviors</c> - which sweeps by instance
    /// against <c>IConditionalBlockBehavior</c> - removes that mount. Post-conditioning the
    /// block holds <b>zero</b> carryables, which is why R1 fails on the pre-fix head with
    /// both counts at 0 rather than "the wrong one survived".
    /// </para>
    /// <para>
    /// No vanilla block can host the shape: <c>enabledCondition</c> is read only from
    /// behavior JSON (<c>BlockBehaviorCarryable.ParseBaseSettings</c>), and the resolver
    /// early-returns at one or fewer carryables, so the coverage lives here. The fixture
    /// (<c>carrieddemo:disabledbase-overlay-block</c>) is the composition of two existing
    /// ones - <c>overlaybase-block</c>'s priority-0 base plus priority-0
    /// <c>overrideExistingProperties</c> overlay, and <c>conditional-only-block</c>'s
    /// <c>carryon.Carryables.Chest</c> condition - with one decisive change: the condition
    /// sits on the <b>non-override</b>, and the overlay is unconditional.
    /// </para>
    /// <para>
    /// The fixture boots <c>Chest=false</c>, so the base's condition is genuinely false at
    /// boot and R1-R3 are assertion-only on the boot pass (no config write, no reload
    /// timing). That is deliberate: M7's defect is deterministic and configuration-free, so
    /// a row that needed a reload to reproduce it would be testing the reload, not the
    /// resolver. R5 is the exception - it churns the config, because the lint it observes
    /// fires at boot, before a row can subscribe to <c>ILogger.EntryAdded</c>.
    /// </para>
    /// <para>
    /// <b>Known coverage gap, recorded not hidden:</b> the resolver's third fallback - every
    /// candidate condition-disabled on an override-contributing block - is unreachable from
    /// this fixture (its overlay is unconditional, so the set is never all-disabled) and no
    /// staged blocktype reaches it either. The fix keeps that branch deterministic (highest
    /// priority wins) rather than list-ordered, but it is pinned by inspection, not by a row.
    /// </para>
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "The resolver must never merge an overlay onto a condition-disabled base: an unconditional overlay must survive unmerged when the block's only non-override is condition-disabled, while the healthy merge and the shipped strip are unchanged.")]
    [AtlasDataFiles("fixtures/MultiCarryablePremise", TargetPath = "ModConfig")]
    public class DisabledPrimaryMergeScenarios : CarryOnScenarioBase
    {
        /// <summary>The M7 shape: disabled non-override base plus unconditional overlay.</summary>
        private const string DisabledBaseOverlayBlock = "carrieddemo:disabledbase-overlay-block";

        /// <summary>R2's healthy control: both carryables unconditional, so the merge runs.</summary>
        private const string EnabledBaseOverlayBlock = "carrieddemo:overlaybase-block";

        /// <summary>R3's control: a lone disabled non-override, the shipped single-behavior shape.</summary>
        private const string SingleDisabledBlock = "carrieddemo:conditional-only-block";

        /// <summary>Declared only by this fixture's overlay; the class default is 0.8.</summary>
        private const float OverlayOnlyInteractDelay = 1.5f;

        /// <summary>Declared only by this fixture's overlay's Hands slot.</summary>
        private const float OverlayOnlyWalkSpeed = -0.33f;

        /// <summary>
        /// R2's expected merge outcome on <c>overlaybase-block</c>: that block's own overlay
        /// value, distinct from this class's fixture value so the two blocks' survivors
        /// cannot be mistaken for one another.
        /// </summary>
        private const float EnabledBaseOverlayWalkSpeed = -0.25f;

        /// <summary>
        /// An unrelated allowlist shape no staged blocktype uses - the churn edit's
        /// signature-dirty but behavior-inert delta, the same pattern
        /// <c>OverlayPatchPriorityWarningScenarios</c> uses to re-run the pipeline on
        /// demand.
        /// </summary>
        private const string InertChurnShape = "block/disabledprimaryprobe/none";

        private const int WaitTimeoutTicks = 150; // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;       // ~2 s - past the 500 ms debounce + dispatch

        private static readonly string[] DefaultAllowedShapes =
        [
            "block/clay/lootvessel",
            "block/wood/chest/normal",
            "block/wood/trunk/normal",
            "block/reed/basket-normal"
        ];

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>
        /// Full-file Mode B payload: the other signature-relevant keys pinned to the fixture's
        /// boot value, with <see cref="chest"/> and the optional inert churn entry per row.
        /// </summary>
        private static string FullConfigB(bool chest = false, string[]? allowedShapes = null)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": {{chest.ToString().ToLowerInvariant()}}
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
                    + $"Now: Chest={Mod.Config.Carryables.Chest}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>
        /// The single surviving carryable of a block, read from the <b>raw</b>
        /// <c>BlockBehaviors</c> array rather than through <c>GetBehavior&lt;T&gt;()</c>:
        /// the resolver reassigns that array to a new instance, so a behavior cache built at
        /// registration time can go stale and report no behavior for a block whose array
        /// provably holds one (the same reason <see cref="CarryableParity.MaxPriority"/> is
        /// read raw).
        /// </summary>
        private static BlockBehaviorCarryable SingleCarryableOf(Block block, string code)
        {
            var carryables = block.BlockBehaviors.OfType<BlockBehaviorCarryable>().ToList();
            Assert.True(
                carryables.Count == 1,
                $"'{code}' must resolve to exactly one carryable. "
                + $"Actual: {CarryOnParityAssert.Inspect(block).Describe()}");
            return carryables[0];
        }

        /// <summary>
        /// R1 (RED before the fix) - the finding itself. A block whose non-override is
        /// condition-disabled and whose override is unconditional must keep the block
        /// carryable, with the <b>overlay's</b> properties and without the disabled base's
        /// condition.
        /// <para>
        /// Correct survivor: the overlay standing alone, unmounted - which is what pre-MR
        /// semantics produced. The old in-place merge could not fire onto a base that had
        /// already been filtered out, so the overlay was never patched onto it and remained
        /// pickable with its own properties. Post-fix that is reproduced explicitly as "no
        /// enabled base to merge onto, so the highest-priority enabled candidate survives",
        /// rather than by accident.
        /// </para>
        /// </summary>
        [Trait("summary", "A block whose non-override is condition-disabled and whose override is unconditional must keep the block carryable with the overlay's own properties and without the disabled base's condition.")]
        [Trait("pins", "The overlay stands alone unmerged when the base is disabled, instead of the merge inheriting the disabled condition and stripping the block to nothing.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before the fix, where the merge inherited the disabled base's condition and the final removal pass stripped the mount, leaving no carryable.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DisabledBase_EnabledOverlaySurvives_Unmerged()
        {
            Assert.NotNull(World.Api);
            await World.Ticks(30); // settle; no write - the boot pass is the observation window
            Assert.False(
                Mod.Config.Carryables.Chest,
                "Row premise: the fixture must boot with chest carryables OFF, so this "
                + "block's non-override is condition-disabled at boot");

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(DisabledBaseOverlayBlock, pos);
            Block block = World.BlockAt(pos);

            CarryOnParityAssert.AssertPresentInBoth(
                block,
                "an unconditional overlay must survive on its own when the block's only "
                + "non-override is condition-disabled - merging onto it inherits the disabled "
                + "condition and strips the block to nothing");

            var survivor = SingleCarryableOf(block, DisabledBaseOverlayBlock);
            var hands = survivor.Slots[CarrySlot.Hands];

            Assert.True(
                Math.Abs(survivor.InteractDelay - OverlayOnlyInteractDelay) < 0.0001f,
                $"the survivor must carry the OVERLAY's properties. Actual InteractDelay="
                + $"{survivor.InteractDelay}, expected {OverlayOnlyInteractDelay} (the class "
                + $"default is {Defaults.PickUpSpeed}, so a base-surviving result is "
                + "distinguishable)");

            Assert.True(
                Math.Abs(hands!.WalkSpeedModifier - OverlayOnlyWalkSpeed) < 0.0001f,
                $"the survivor's Hands slot must carry the overlay's walkSpeedModifier. Actual="
                + $"{hands.WalkSpeedModifier}, expected {OverlayOnlyWalkSpeed}");

            Assert.True(
                string.IsNullOrEmpty(survivor.EnabledCondition),
                "the survivor must NOT inherit the disabled base's condition, or the final "
                + $"removal pass strips it. Actual EnabledCondition='{survivor.EnabledCondition}'");
            Assert.True(
                survivor.OverrideExistingProperties,
                "the survivor must be the OVERLAY itself, standing alone unmerged - a false "
                + "value means some other behavior (the disabled base) won as primary");

            // The operator-visible consequence: the block must still be pickable.
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);
            Assert.True(
                carried != null,
                "a block whose only enabled carryable is an unconditional overlay must stay "
                + $"pickable. Actual failureCode={failureCode}");
        }

        /// <summary>
        /// R2 (control, green before and after) - the healthy merge path is unchanged: once
        /// the base's condition is satisfied, the overlay merges onto it and the survivor is
        /// a mount carrying properties from <b>both</b>.
        /// <para>
        /// This is the row that fails if the M7 fix over-corrected into "never merge an
        /// overlay", or into "an overlay never survives as the primary". The discriminator is
        /// the base's own <c>enabledCondition</c>, which reaches the survivor <b>only</b> by
        /// merging with the base: the overlay alone has no condition, and the base alone has
        /// none of the overlay's keys. Reading the overlay's <c>walkSpeedModifier</c> is not a
        /// discriminator on this fixture - the overlay declares it, so it is equally present
        /// when the overlay survives unmerged.
        /// </para>
        /// </summary>
        [Trait("summary", "With the base's condition satisfied, the overlay merges onto it and the survivor is a mount carrying properties from both, so the healthy merge path is unchanged.")]
        [Trait("pins", "The healthy merge still produces a survivor carrying the base's condition and the overlay's keys.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task EnabledBase_OverlayStillMerges_HealthyPathUnchanged()
        {
            Assert.NotNull(World.Api);
            await World.Ticks(30);

            // Turn the base's condition on. Same block, same two behaviors - only the
            // resolver's choice changes, which is what makes this the R1 mirror case.
            File.WriteAllText(ConfigPath, FullConfigB(chest: true));
            await WaitForConfigAsync(c => c.Carryables.Chest, "the edit turns Carryables.Chest on");
            await World.Ticks(SettleTicks);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(DisabledBaseOverlayBlock, pos);
            Block block = World.BlockAt(pos);

            CarryOnParityAssert.AssertPresentInBoth(
                block,
                "with the base enabled, the merge must resolve to a survivor");

            var survivor = SingleCarryableOf(block, DisabledBaseOverlayBlock);
            var hands = survivor.Slots[CarrySlot.Hands];

            Assert.True(
                survivor.EnabledCondition == "carryon.Carryables.Chest",
                "the merged mount must carry the BASE's condition - it reaches the survivor "
                + "only by merging with the base, so a different value here means the overlay "
                + $"survived alone or the base did. Actual='{survivor.EnabledCondition}'");
            Assert.True(
                Math.Abs(survivor.InteractDelay - OverlayOnlyInteractDelay) < 0.0001f,
                "the merged mount must also carry the OVERLAY's keys. Actual InteractDelay="
                + $"{survivor.InteractDelay}, expected {OverlayOnlyInteractDelay}");
            Assert.True(
                Math.Abs(hands!.WalkSpeedModifier - OverlayOnlyWalkSpeed) < 0.0001f,
                $"the merged mount's Hands slot must carry the overlay's walkSpeedModifier. "
                + $"Actual={hands.WalkSpeedModifier}, expected {OverlayOnlyWalkSpeed}");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);
            Assert.True(carried != null, $"the healthy merge path must stay pickable: {failureCode}");
        }

        /// <summary>
        /// R3 (control, green before and after) - a lone condition-disabled non-override is
        /// still stripped to not-carryable. This is the <c>SelectHighestPriority</c> fallback
        /// contract the M7 fix must not weaken: "no enabled candidate exists" is still
        /// allowed to nominate a survivor, and the final removal pass is still what takes it
        /// away. Without this row, a fix that returned null for an all-disabled set would
        /// also go green.
        /// </summary>
        [Trait("summary", "A lone condition-disabled non-override is still stripped to not-carryable, so the fallback that nominates a survivor and the final removal pass are not weakened.")]
        [Trait("pins", "The shipped single-behavior strip is unchanged by the fix.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SingleDisabledNonOverride_StillStripped()
        {
            Assert.NotNull(World.Api);
            await World.Ticks(30);
            Assert.False(
                Mod.Config.Carryables.Chest,
                "Row premise: this block's only carryable must be condition-disabled here");

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(SingleDisabledBlock, pos);

            CarryOnParityAssert.AssertAbsentFromBoth(
                World.BlockAt(pos),
                "a lone condition-disabled non-override is the shipped single-behavior shape "
                + "and must still be stripped, not rescued by the M7 fallback rewrite");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);
            Assert.True(carried == null, "a stripped block must not be pickable");
            Assert.Equal(FailureCodes.NotCarryable, failureCode);
        }

        /// <summary>
        /// R4 (premise) - the fixture block is registered and its non-override is genuinely
        /// condition-disabled at boot.
        /// <para>
        /// <b>Why this row does not read the blocktype JSON.</b> The obvious anti-vacuity
        /// worry is that only one of the two declared behaviors instantiated, in which case the
        /// resolver early-returns at one carryable and R1 would pass <i>vacuously</i>: the lone
        /// overlay is carryable, unconditional, and carries the overlay's own keys - every R1
        /// assertion satisfied without the resolver ever choosing wrong.
        /// <para>
        /// R2 already closes that hole, permanently and without an engine-internal dependency.
        /// It flips the base's condition on and requires the survivor to carry
        /// <c>enabledCondition = "carryon.Carryables.Chest"</c>, a value that reaches a
        /// survivor <b>only</b> by merging with the base. A block that had loaded one behavior
        /// could not produce it under any configuration, so R2 is the two-instance premise.
        /// <para>
        /// An earlier draft of this row read the staged JSON out of
        /// <c>ICoreAPI.Assets</c> instead. It was dropped after two measured failures: the
        /// registered key's path carries the <c>.json</c> extension, and a blocktype asset's
        /// <c>ToText()</c> does not reliably yield the source document. Neither is worth a
        /// test dependency when R2 proves the same thing through the resolver's own output.
        /// </para>
        /// </summary>
        [Trait("summary", "The fixture block is registered and its non-override is genuinely condition-disabled at boot, with the healthy control block present and the class booting in recondition mode.")]
        [Trait("pins", "The fixture is staged and the base is condition-disabled at boot, so the defect is reachable without a config write.")]
        [Trait("role", "premise")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Premise_FixtureRegisteredAndBaseConditionDisabled()
        {
            Assert.NotNull(World.Api);
            await World.Ticks(30);

            Assert.True(
                World.Api.World.GetBlock(new AssetLocation(DisabledBaseOverlayBlock)) != null,
                $"Row premise: the registry must hold '{DisabledBaseOverlayBlock}' "
                + "(is CarryEntityDemo staged with the fixture's blocktype?)");
            Assert.True(
                World.Api.World.GetBlock(new AssetLocation(EnabledBaseOverlayBlock)) != null,
                $"Row premise: the registry must hold '{EnabledBaseOverlayBlock}', R5's "
                + "healthy control");

            Assert.False(
                Mod.Config.Carryables.Chest,
                "Row premise: the fixture boots with chest carryables OFF, so this block's "
                + "non-override is condition-disabled at boot and R1 reproduces the defect "
                + "without a config write");

            Assert.True(
                Mod.Config.DebuggingOptions.ReconditionOnConfigChange,
                "Row premise: the fixture boots in Mode B, so R2 and R5 can drive a "
                + "recondition by writing the config");
        }

        /// <summary>
        /// R5 - the authoring lint. When the merge is skipped because the block has no
        /// <i>enabled</i> base to patch, the operator gets one warning naming the block; the
        /// healthy pair stays silent.
        /// <para>
        /// The lint exists because the M7 fix makes the block work but does not explain it:
        /// without it, an author who wrote a conditional base expecting an overlay patch sees
        /// a working block with no hint that the overlay is standing alone. Severity is
        /// Warning, matching the existing <c>patchPriority</c> lint - which also warns about a
        /// benign outcome (the merge still happens) purely because the authoring intent and
        /// the resolution disagree.
        /// </para>
        /// <para>
        /// Observed on a Mode-B recondition rather than at boot, for the same reason
        /// <c>OverlayPatchPriorityWarningScenarios</c> uses that window: the lint fires during
        /// conditioning, which for the boot pass happens before a row can subscribe.
        /// </para>
        /// </summary>
        [Trait("summary", "When the merge is skipped because the block has no enabled base, the operator gets one warning naming the block, while the healthy pair stays silent, and the warning changes no resolution.")]
        [Trait("pins", "A skipped merge warns once per run and names the block; the warning is advisory only.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SkippedMerge_WarnsOnce_NamingTheBlock_HealthyPairSilent()
        {
            Assert.NotNull(World.Api);
            await World.Ticks(30);

            var warnings = new List<string>();
            var gate = new object();
            World.Api.Logger.EntryAdded += OnWarning;
            try
            {
                File.WriteAllText(
                    ConfigPath,
                    FullConfigB(allowedShapes: [.. DefaultAllowedShapes, InertChurnShape]));
                await WaitForConfigAsync(
                    c => c.CarryablesFilters.AllowedShapeOnlyMatches.Contains(InertChurnShape),
                    "the edit appends an unrelated AllowedShapeOnlyMatches entry");
                await World.Ticks(SettleTicks); // recondition (and its lint) complete inside the reload
            }
            finally
            {
                World.Api.Logger.EntryAdded -= OnWarning;
            }

            var forFixture = warnings
                .Where(w => w.Contains(DisabledBaseOverlayBlock, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(
                forFixture.Count == 1,
                "a block whose only non-override is condition-disabled must warn exactly once "
                + "per run - the overlay is surviving on its own instead of merging, which the "
                + $"author needs to know. Actual count: {forFixture.Count} (zero means the lint "
                + $"did not fire; two means a double reload). Captured: {string.Join(" | ", warnings)}");

            Assert.True(
                !warnings.Any(w => w.Contains(EnabledBaseOverlayBlock, StringComparison.OrdinalIgnoreCase)),
                "the healthy pair - an enabled base that the overlay really does patch - must "
                + $"not warn. Captured: {string.Join(" | ", warnings)}");

            // Advisory only: the warned run must still leave the block resolvable.
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(DisabledBaseOverlayBlock, pos);
            CarryOnParityAssert.AssertPresentInBoth(
                World.BlockAt(pos),
                "the lint is advisory - it must not change resolution on the run that warns");

            void OnWarning(EnumLogType type, string message, object[] args)
            {
                if (type != EnumLogType.Warning) return;
                lock (gate) warnings.Add(message);
            }
        }
    }
}
