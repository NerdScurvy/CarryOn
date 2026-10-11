using System;
using System.IO;
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
    /// Phase-1 rows: a block carrying an unconditional
    /// <c>carryon:carryable</c> base plus a conditional <c>carryon:carryable</c> must
    /// keep the base alive through conditioning - reconcile and the final removal pass
    /// must be instance-targeted, not type-wide (they currently flatten by runtime
    /// type, so the enabled conditional destroys the base, and a Mode-B
    /// enabled→disabled reload strips the flattened survivor leaving nothing).
    ///
    /// Two custom blocks from CarryEntityDemo:
    ///   carrieddemo:multicarry-block - base carryable (patchPriority 2, Hands+Back)
    ///     + conditional carryable on carryon.Carryables.Chest (patchPriority 1, Hands
    ///     only). The coexistence premise; correct survivor is the base (higher
    ///     priority, enabled).
    ///   carrieddemo:conditional-only-block - single conditional carryable on Chest
    ///     (the shipped single-behavior shape). Green controls: block-level enable /
    ///     disable for a lone conditional must keep working unchanged.
    ///
    /// Correct expectations are written; R2–R4 are RED on the current head (reconcile
    /// flatten / reload-strip), G1/G2 are green controls. The fixture boots with
    /// Chest=true so the boot pass itself flattens (R2 is assertion-only and pins the
    /// boot vector); every flipping row self-seeds its own state first (order-independent).
    /// Fix: instance-owned reconcile + instance-targeted removal +
    /// whitespace guard. No test-row changes between the red proof and the green fix.
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "A block carrying an unconditional base plus a conditional carryable must keep the base through conditioning, while the shipped single-behavior enable and disable controls still work.")]
    [AtlasDataFiles("fixtures/ReconditionOnChangeConfig", TargetPath = "ModConfig")]
    public class MultiCarryableCoexistenceScenarios : CarryOnScenarioBase
    {
        private const string MultiCarryBlock = "carrieddemo:multicarry-block";
        private const string ConditionalOnlyBlock = "carrieddemo:conditional-only-block";

        private const int WaitTimeoutTicks = 150; // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;       // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        private void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

        /// <summary>
        /// Full-file Mode B payload every row writes: recondition flag on (the dirty gate
        /// reads it from the reloaded file), the other signature-relevant keys at the
        /// fixture's defaults, only Carryables.Chest moving per row.
        /// </summary>
        private static string FullConfigB(bool chest)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": {{chest.ToString().ToLowerInvariant()}}
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}}
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": true
              }
            }
            """;

        private async Task SelfSeedBaselineAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            WriteConfig(FullConfigB(chest: false));
            await WaitForConfigAsync(
                c => !c.Carryables.Chest && c.DebuggingOptions.ReconditionOnConfigChange,
                "Mode B baseline (Chest=false, flag on)");
            await World.Ticks(20); // > 500 ms debounce - next write's event is not swallowed
        }

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

        private async Task<ITestPlayer> JoinAndTeleportAsync(string name, BlockPos pos)
        {
            ITestPlayer player = await JoinPlayer(name);
            await player.TeleportTo(pos);
            return player;
        }

        private BlockBehaviorCarryable? CarryableOf(string code)
            => World.Api.World.GetBlock(new AssetLocation(code))?.GetBehavior<BlockBehaviorCarryable>();

        /// <summary>
        /// R2 (RED) - the unconditional base must be the survivor of conditioning in ANY
        /// state: the resolver keeps the higher-priority base (patchPriority 2, Hands+Back).
        /// Assertion-only (no config flip) so it pins the pristine boot pass when it runs
        /// first; in any row order the assertion is identical because the current
        /// flatten/strip is irreversible within a booted world (once the conditional was
        /// enabled, reloads cannot resurrect the base). Boot vector: the fixture boots
        /// Chest=true, so <c>ReconcileConditionalCarryables</c> type-removes both
        /// behaviors and remounts only the conditional (patchPriority 1, Hands-only).
        /// </summary>
        [Trait("summary", "Assertion-only on the class boot: in any conditioning state the unconditional base must be the survivor with its priority-2 Back and Hands slots, and pickup through it must work.")]
        [Trait("pins", "The unconditional base survives conditioning; the higher-priority base is the survivor, not the conditional.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the pre-fix head, where reconcile flattened both behaviors to the conditional and left the base destroyed.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Multicarry_BaseSurvives_AnyConditioningState()
        {
            Assert.NotNull(World.Api);
            await World.Ticks(30); // settle; no write - the boot (or last reload) result is what is asserted

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(MultiCarryBlock, pos);

            var survived = CarryableOf(MultiCarryBlock);
            Assert.NotNull(survived);
            Assert.True(survived!.PatchPriority == 2,
                "flattened to conditional: the unconditional base (patchPriority 2) must be the survivor");
            Assert.NotNull(survived.Slots[CarrySlot.Back]);
            Assert.NotNull(survived.Slots[CarrySlot.Hands]);

            ITestPlayer player = await JoinAndTeleportAsync("Tester", World.Spawn.Offset(2, 0, 2));
            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);
            Assert.True(carried != null, $"Pickup through the surviving base failed: {failureCode}");
        }

        /// <summary>
        /// R3 (RED) - Mode-B reload Chest=true→false. Boot flatten has already destroyed
        /// the base (only the reconciled conditional exists), so stripping the now-disabled
        /// conditional leaves the block with no carryable at all. Correct: the
        /// unconditional base survives the whole cycle and the block stays carryable.
        /// </summary>
        [Trait("summary", "A Mode-B enabled-to-disabled reload must leave the block carryable through its unconditional base, with the base still the priority-2 survivor.")]
        [Trait("pins", "An enabled-to-disabled reload must not strip the base along with the now-disabled conditional.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the pre-fix head; the boot flatten left only the conditional, which the reload then stripped.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ModeB_Reload_EnabledToDisabled_BaseSurvives()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig(FullConfigB(chest: true));
            await WaitForConfigAsync(c => c.Carryables.Chest, "config flips Carryables.Chest to true");
            await World.Ticks(SettleTicks);

            WriteConfig(FullConfigB(chest: false));
            await WaitForConfigAsync(c => !c.Carryables.Chest, "config flips Carryables.Chest back to false");
            await World.Ticks(SettleTicks);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(MultiCarryBlock, pos);
            ITestPlayer player = await JoinAndTeleportAsync("Tester", World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);

            Assert.True(carried != null,
                "multi-carryable base lost: block must stay carryable through a Mode-B enabled→disabled reload");
            var survived = CarryableOf(MultiCarryBlock);
            Assert.NotNull(survived);
            Assert.Equal(2, survived!.PatchPriority);
        }

        /// <summary>
        /// R4 (RED) - Mode-B reload Chest=false→true. The base survived boot and the
        /// unconditional state; re-enabling the conditional must re-add it without
        /// destroying the base (resolver keeps the base). Today the reload reconcile
        /// type-removes the base and remounts only the conditional (PatchPriority 1).
        /// </summary>
        [Trait("summary", "A Mode-B disabled-to-enabled reload must re-add the conditional without destroying the base: the priority-2 base still wins and its Back slot picks up.")]
        [Trait("pins", "Re-enabling the conditional must not type-remove the unconditional base.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the pre-fix head, where the reload reconcile removed the base and remounted only the conditional.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ModeB_Reload_DisabledToEnabled_BaseWins()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig(FullConfigB(chest: true));
            await WaitForConfigAsync(c => c.Carryables.Chest, "config flips Carryables.Chest to true");
            await World.Ticks(SettleTicks);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(MultiCarryBlock, pos);

            var survived = CarryableOf(MultiCarryBlock);
            Assert.NotNull(survived);
            Assert.True(survived!.PatchPriority == 2,
                "base lost on re-enable: the unconditional base (patchPriority 2) must win over the re-added conditional");
            Assert.NotNull(survived.Slots[CarrySlot.Back]);

            ITestPlayer player = await JoinAndTeleportAsync("Tester", World.Spawn.Offset(2, 0, 2));
            var carriedBack = Actions.PickUpIntoSlot(player.Entity, pos, CarrySlot.Back, out var failureCode);
            Assert.True(carriedBack != null, $"Pickup into the base-only Back slot failed: {failureCode}");
        }

        /// <summary>
        /// G1 (green control) - a block that carries only a disabled conditional must be
        /// stripped to NOT carryable (the shipped single-behavior semantics the fix must
        /// not weaken). With fix: the lone disabled conditional is removed by instance.
        /// </summary>
        [Trait("summary", "A block carrying only a disabled conditional is stripped to not-carryable, the shipped single-behavior semantics the fix must not weaken.")]
        [Trait("pins", "Single-behavior disable still strips the block to not-carryable.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Boot_ConditionalDisabled_SingleConditionalBlock_NotCarryable()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            Assert.False(Mod.Config.Carryables.Chest, "Baseline must leave chest carryables off");

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ConditionalOnlyBlock, pos);
            ITestPlayer player = await JoinAndTeleportAsync("Tester", World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);

            Assert.True(carried == null, "A lone disabled conditional must not be carryable");
            Assert.Equal(FailureCodes.NotCarryable, failureCode);
            Assert.Equal(ConditionalOnlyBlock, World.BlockAt(pos).Code?.ToString());
        }

        /// <summary>
        /// G2 (green control) - a block carrying a single enabled conditional stays
        /// carryable (remount-from-pristine must keep working for the shipped shape).
        /// </summary>
        [Trait("summary", "A block carrying a single enabled conditional stays carryable, so the shipped single-behavior remount keeps working after the fix.")]
        [Trait("pins", "Single-behavior enable still stays carryable.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Boot_ConditionalEnabled_SingleConditionalBlock_Carryable()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig(FullConfigB(chest: true));
            await WaitForConfigAsync(c => c.Carryables.Chest, "config flips Carryables.Chest to true");
            await World.Ticks(SettleTicks);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ConditionalOnlyBlock, pos);
            ITestPlayer player = await JoinAndTeleportAsync("Tester", World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);

            Assert.True(carried != null, $"Single conditional carryable failed to pick up: {failureCode}");
            Assert.Equal(ConditionalOnlyBlock, carried?.Block?.Code?.ToString());
        }
    }
}