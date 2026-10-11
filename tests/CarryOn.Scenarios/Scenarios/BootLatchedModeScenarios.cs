using System.IO;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// M1 boot-latch rows: a session that boots **Mode A** (<c>ReconditionOnConfigChange</c>
    /// false) stays Mode A for its whole life, even after the file says otherwise.
    /// <para>
    /// <c>DebuggingOptions.ReconditionOnConfigChange</c> is documented as "(requires
    /// restart)" and is read once per server session, like its three siblings. The reload
    /// gate in <c>CarryOnConfigService.ReloadFromFile</c> read it off the
    /// <b>reloaded</b> config instead, so editing the file opted a running Mode A server
    /// into live reconditioning mid-session — a capability the operator never turned on for
    /// this session, silently granted by a file edit.
    /// </para>
    /// <para>
    /// The mirror direction (a Mode B session whose file says <c>false</c> must keep
    /// reconditioning) lives in <see cref="ReconditionOnChangeScenarios"/> R10/R11, and the
    /// command entry point is isolated in <c>ConfigWatchdogDisabledScenarios</c> D4. G2
    /// exists so the capability removal is pinned rather than merely implied: without it,
    /// "a Mode A session does not recondition" could later be "fixed" back into a broadcast
    /// on join, and no other row would notice.
    /// </para>
    /// <para>
    /// <b>Fixture note.</b> These rows reuse <c>fixtures/WatchdogConfig</c> rather than
    /// booting with no <c>AtlasDataFiles</c> seed. A Mode A payload is required, but the
    /// rows are watchdog-driven, and <c>SetupFileWatcher</c> returns early when
    /// <c>ModConfig/CarryOnConfig.json</c> does not exist at boot — so a fixture-less boot
    /// would leave no watcher and silently reduce every row to "nothing happened". Seeding
    /// the file is what makes the reload path the thing under test. Each class gets its own
    /// server boot, so sharing the fixture with <c>ConfigWatchdogScenarios</c> is safe.
    /// </para>
    /// </summary>
    [Trait("area", "Config reload")]
    [Trait("summary", "Boot-latch rows: a session that boots Mode A stays Mode A for its whole life, so a file edit cannot opt a running server into live reconditioning.")]
    [AtlasDataFiles("fixtures/WatchdogConfig", TargetPath = "ModConfig")]
    public class BootLatchedModeScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;          // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;                // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        private void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

        /// <summary>
        /// Full-file payload. <paramref name="reconditionOnConfigChange"/> is emitted only
        /// when the row asks for it, so the control row's payload stays byte-identical to
        /// the boot seed and cannot be distinguished from "nothing was written".
        /// <para>
        /// The flag is nested under <c>DebuggingOptions</c> because that is where the
        /// property lives. A first draft put it at the root, where deserialization silently
        /// drops it — and then G1/G2 passed for the wrong reason (the gate read a
        /// <c>false</c> that no one had written). G4 is what caught it.
        /// </para>
        /// </summary>
        private static string FullConfig(bool chest, float speed, bool? reconditionOnConfigChange = null)
        {
            var flagValue = reconditionOnConfigChange?.ToString().ToLowerInvariant();
            var debuggingJson = flagValue == null
                ? ""
                : ",\n  \"DebuggingOptions\": {\n    \"ReconditionOnConfigChange\": " + flagValue + "\n  }";
            return $$"""
            {
              "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
              "Carryables": {
                "Chest": {{chest.ToString().ToLowerInvariant()}}
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{speed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}}
              }{{debuggingJson}}
            }
            """;
        }

        /// <summary>
        /// Drains any prior row's debounce window, then re-establishes the boot baseline
        /// (<c>Chest=false</c>, speed 2.0, flag absent) and waits for it. Required because
        /// the class shares one server boot, so a prior row may have left
        /// <c>Chest=true</c> and the flag set.
        /// <para>
        /// The trailing settle is a full <see cref="SettleTicks"/> window, not the ~20 ticks
        /// the Mode B class uses, and it is load-bearing. This class's fixture already boots
        /// at exactly the baseline values, so the wait predicate is satisfied on the first
        /// poll <i>before the seed write has even been read</i>; a short trailing wait then
        /// leaves the seed's own reload in flight, and its broadcast lands after the next
        /// caller's <c>Client.Clear()</c> — showing up as a snapshot the row did not cause.
        /// Settling the full window lets the seed's reload and broadcast complete first.
        /// </para>
        /// </summary>
        private async Task SelfSeedBaselineAsync()
        {
            await World.Ticks(30);
            WriteConfig(FullConfig(chest: false, speed: SeedSpeed));
            await WaitForConfigAsync(
                c => !c.Carryables.Chest && c.CarryOptions.InteractSpeedMultiplier == SeedSpeed,
                "Mode A baseline (Chest=false, speed=2.0, flag absent)");
            await World.Ticks(SettleTicks); // > 500 ms debounce + dispatch: the seed's reload is done
        }

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
                    + $"InteractSpeedMultiplier={Mod.Config.CarryOptions.InteractSpeedMultiplier}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        private async Task<ITestPlayer> JoinAndClearAsync(string name)
        {
            ITestPlayer player = await JoinPlayer(name);
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await World.Ticks(SettleTicks);
            player.Client.Clear();
            return player;
        }

        private int SnapshotCount(ITestPlayer player)
            => player.Client.Packets<CarryabilityStateMessage>(Channel).Count;

        /// <summary>
        /// How many carryability snapshots arrived <i>since</i> <paramref name="before"/>.
        /// <para>
        /// Rows here assert on a delta, not on absolute emptiness, and that is deliberate
        /// rather than defensive. The class shares one server boot, so whether a carryability
        /// snapshot exists at all depends on what earlier rows did — and under the pre-fix
        /// gate the very first flag-on row *builds* one, which then exists for every later
        /// row (including the join-time send). An absolute "no snapshot" assertion would
        /// therefore make each row's verdict depend on the execution order, and a control row
        /// would fail for a reason that has nothing to do with the behavior it exists to
        /// pin. The claim under test is always about <i>this</i> edit: "a Mode A session
        /// produces no new carryability message in response to this write".
        /// </para>
        /// </summary>
        private int SnapshotDelta(ITestPlayer player, int before)
            => SnapshotCount(player) - before;

        /// <summary>
        /// G1 - <b>RED before the fix.</b> Boot Mode A, then write a payload that turns
        /// <c>ReconditionOnConfigChange</c> on <i>and</i> makes a behavior-surface edit
        /// (<c>Carryables.Chest</c> false → true). The config must apply, but the session
        /// must stay Mode A: no conditioning pass runs, so no carryability snapshot is
        /// broadcast and the chest keeps the boot-time stripped behavior.
        /// <para>
        /// Pre-fix the reloaded flag opens the gate, the dirty gate sees a changed surface,
        /// and both assertions below fail — the broadcast is emitted and the chest regains
        /// its carryable. That is the capability being taken away.
        /// </para>
        /// </summary>
        [Trait("summary", "After a Mode A boot, a payload that turns ReconditionOnConfigChange on and flips Carryables.Chest true still applies but broadcasts no snapshot and leaves the chest stripped.")]
        [Trait("pins", "The reload gate reads the boot-latched mode, so a file's flag cannot open reconditioning mid-session.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the reloaded flag opened the gate and the changed surface reconditioned and broadcast.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BootModeA_FileTurnsFlagOn_BehaviorEdit_StillDoesNotRecondition()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            // The precondition G4 pins: the session really did boot Mode A.
            Assert.False(
                Mod.Config.DebuggingOptions.ReconditionOnConfigChange,
                "Row premise: this session must boot Mode A for the latch to be under test");

            ITestPlayer player = await JoinAndClearAsync("Tester");
            int snapshotCountBefore = SnapshotCount(player);

            WriteConfig(FullConfig(chest: true, speed: SeedSpeed, reconditionOnConfigChange: true));
            await WaitForConfigAsync(c => c.Carryables.Chest, "config flips Carryables.Chest to true");
            await World.Ticks(SettleTicks); // reload + any recondition + broadcast land inside this window

            Assert.True(
                SnapshotDelta(player, snapshotCountBefore) == 0,
                "a Mode A session must not broadcast a carryability snapshot even when the "
                + "file turns ReconditionOnConfigChange on - the flag is read once at server "
                + $"start, so this edit cannot open the gate for a running server. Got "
                + $"{SnapshotDelta(player, snapshotCountBefore)} new snapshot(s) from the edit.");
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);
            ITestPlayer late = await JoinPlayer("Late");
            await late.TeleportTo(World.Spawn.Offset(2, 0, 2));
            var carried = Actions.PickUpIntoHands(late.Entity, pos, out var failureCode);
            Assert.True(carried == null,
                $"a Mode A session must not have reconditioned, so the chest stays stripped: {failureCode}");
            Assert.Equal(FailureCodes.NotCarryable, failureCode);
            Assert.True(
                World.Api.World.BlockAccessor.GetBlock(pos).GetBehavior<BlockBehaviorCarryable>() == null,
                "no conditioning pass may have run, so the chest must still carry no "
                + "carryable behavior after the reload");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// G2 - <b>RED before the fix.</b> The capability-removal pin: a player who joins
        /// <i>after</i> the payload landed must receive no
        /// <see cref="CarryabilityStateMessage"/>. Under the pre-fix code the reload built
        /// a snapshot, and the join-time send hands it to the new player — so the capability
        /// removal is observable at the join path and not only as "no live broadcast".
        /// </summary>
        [Trait("summary", "After a Mode A boot and a flag-on behavior edit, a player joining afterwards receives no carryability snapshot.")]
        [Trait("pins", "A Mode A session builds no snapshot at all, so the join-time send has nothing to hand a new player.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the reload built a snapshot that the join-time send handed to the new player.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BootModeA_JoinerAfterFlagFlip_ReceivesNoSnapshot()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig(FullConfig(chest: true, speed: SeedSpeed, reconditionOnConfigChange: true));
            await WaitForConfigAsync(c => c.Carryables.Chest, "config flips Carryables.Chest to true");
            await World.Ticks(SettleTicks);

            ITestPlayer joiner = await JoinPlayer("LateJoiner");
            await joiner.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await World.Ticks(SettleTicks);

            Assert.True(
                joiner.Client.Packets<CarryabilityStateMessage>(Channel).Count == 0,
                "a Mode A session builds no carryability snapshot at all, so a joining player "
                + "has nothing to reconcile onto - receiving one here means a conditioning "
                + $"pass ran that the boot-time latch should have prevented. Got "
                + $"{joiner.Client.Packets<CarryabilityStateMessage>(Channel).Count} snapshot(s).");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// G3 - <b>control, green before and after.</b> A behavior-free reload in a Mode A
        /// session still applies and still syncs via <c>ConfigSyncMessage</c>, and still
        /// pushes no carryability snapshot. The point is that latching the mode on the boot
        /// value does not break ordinary reloads.
        /// <para>
        /// The payload deliberately leaves the flag <b>out</b>. An earlier draft set it to
        /// <c>true</c> here on the theory that this row should prove "even with the flag on,
        /// a behavior-free edit pushes nothing". That row is not green before the fix, and
        /// cannot be: the class shares one server boot, so an earlier flag-on row leaves the
        /// session's dirty-gate state dirty, and every subsequent reload with the gate open
        /// re-runs a pass. Its verdict would depend on execution order rather than on the
        /// behavior under test. The flag-on dirty-edit case is G1's job; this row's job is
        /// only to show that an ordinary reload still works.
        /// </para>
        /// </summary>
        [Trait("summary", "A behavior-free reload in a Mode A session still applies and syncs via ConfigSyncMessage and pushes no carryability snapshot.")]
        [Trait("pins", "Latching the mode on the boot value does not break ordinary reloads.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BootModeA_BehaviorFreeReload_AppliesAndSyncs_NoCarryabilityBroadcast()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            ITestPlayer player = await JoinAndClearAsync("Tester");
            int snapshotCountBefore = SnapshotCount(player);

            WriteConfig(FullConfig(chest: false, speed: 0.5f));
            await WaitForConfigAsync(c => c.CarryOptions.InteractSpeedMultiplier == 0.5f, "speed-only flip applied");
            await World.Ticks(SettleTicks);

            Assert.Equal(0.5f, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.NotEmpty(player.Client.Packets<ConfigSyncMessage>(Channel));
            Assert.True(
                SnapshotDelta(player, snapshotCountBefore) == 0,
                "a behavior-free reload must push no carryability snapshot - the dirty gate, "
                + "not the mode, is what suppresses it. Got "
                + $"{SnapshotDelta(player, snapshotCountBefore)} new snapshot(s).");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// G4 - <b>premise.</b> The payload really did land and really did flip the live
        /// config's own flag, while the session itself stayed Mode A. Without this, G1/G2
        /// could pass for the wrong reason — a write that never applied would also produce
        /// "no recondition", and the rows would prove nothing.
        /// </summary>
        [Trait("summary", "A flag-on payload lands and flips the live config's ReconditionOnConfigChange value while the session stays Mode A.")]
        [Trait("pins", "The write really applied, so the no-recondition rows cannot pass for a payload that never landed.")]
        [Trait("role", "premise")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BootModeA_PayloadLands_ConfigFlagFlips_WhileSessionStaysModeA()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            Assert.False(
                Mod.Config.DebuggingOptions.ReconditionOnConfigChange,
                "Row premise: the session boots Mode A (flag absent from the seed)");

            WriteConfig(FullConfig(chest: true, speed: SeedSpeed, reconditionOnConfigChange: true));
            await WaitForConfigAsync(
                c => c.Carryables.Chest && c.DebuggingOptions.ReconditionOnConfigChange,
                "both the behavior edit and the flag edit apply to the live config");
            await World.Ticks(SettleTicks);

            // The write landed. Note this is the *config object's* value, which is exactly
            // what the operator sees in their file and in the config screen - it is the
            // session's behavior that is latched, not the value.
            Assert.True(
                Mod.Config.DebuggingOptions.ReconditionOnConfigChange,
                "Row premise: the reloaded flag must be visible in the live config, otherwise "
                + "G1/G2 prove only that the write did not happen");
            Assert.True(Mod.Config.Carryables.Chest);
            Assert.NotNull(World.Api);
        }
    }
}
