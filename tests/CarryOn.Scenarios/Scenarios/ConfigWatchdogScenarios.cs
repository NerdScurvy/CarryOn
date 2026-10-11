using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Config auto-reload watchdog rows. The class fixture
    /// seeds <c>ModConfig/CarryOnConfig.json</c> before boot with
    /// <c>Carryables.Chest = false</c> plus a distinctive non-default scalar
    /// (<c>CarryOptions.InteractSpeedMultiplier = 2.0</c>), so every row can tell its
    /// own value landed from a reset-to-defaults (1.0) without ambiguity. Each row
    /// self-seeds its full file content and waits for it first, because the class shares
    /// one server boot (and therefore one watcher + one in-memory config): rows write
    /// through <see cref="GamePaths.DataPath"/>/ModConfig, the same directory the
    /// production watcher watches.
    ///
    /// Rows B1/B2/B3 are the data-clobber reproducers reported from the field: they
    /// assert the config stays unchanged AND the on-disk bytes stay byte-for-byte what
    /// the test wrote. B2 writes a truncated-but-parseable body
    /// (<c>{"ConfigVersion":6}</c>) - the realistic mid-save read window - and is the
    /// row expected to go RED against current code. D1 (the DisableConfigWatcher gap)
    /// lives in its own class because its seed must differ at boot time.
    ///
    /// Rows R1-R7 are the reload-feedback rows: reload rejections, dropped-section
    /// reports, mode-mismatch warnings and Mode A pending-restart warnings reach the
    /// player channel, not only the server log - through the command result on
    /// <c>/carryon-reload</c> (the harness runs commands as the console, i.e. the
    /// command-with-no-player-caller leg) and through the watcher broadcast with a
    /// joined player reading <c>player.Client.ChatLines()</c>.
    /// Each chat row flips a section or key no other row in this class touches, so
    /// its warning text is unique and the per-session chat dedup cannot have
    /// consumed it regardless of row order.
    /// </summary>
    [Trait("area", "Config reload")]
    [Trait("summary", "Config auto-reload watchdog rows: a valid edit applies live without reconditioning (Mode A), and invalid, degraded or partial writes are rejected without clobbering the config or the user's file.")]
    [AtlasDataFiles("fixtures/WatchdogConfig", TargetPath = "ModConfig")]
    public class ConfigWatchdogScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;          // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;                // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private int reloadCount;
        private Action<CarryOnConfig>? reloadHandler;
        private int reloadedFromFileCount;
        private Action<CarryOnConfig>? reloadedFromFileHandler;

        private sealed class UnsubscribeAction(Action dispose) : IDisposable
        {
            private readonly Action dispose = dispose;

            public void Dispose() => dispose();
        }

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        private void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

        private byte[] ReadConfigBytes() => File.ReadAllBytes(ConfigPath);

        /// <summary>
        /// Writes <paramref name="payload"/> with <see cref="ILogger.EntryAdded"/> attached and
        /// returns every <see cref="EnumLogType.Warning"/> line seen across the settle window.
        /// The subscription is only live for this call, which is why a row cannot assert on a
        /// boot-time warning - each row therefore triggers the reload itself.
        /// </summary>
        private async Task<List<string>> CaptureWarningsAsync(string payload, int settleTicks = SettleTicks)
        {
            var warnings = new List<string>();
            var gate = new object();
            void OnWarning(EnumLogType type, string message, object[] args)
            {
                if (type != EnumLogType.Warning) return;
                lock (gate) warnings.Add(message);
            }

            World.Api.Logger.EntryAdded += OnWarning;
            try
            {
                WriteConfig(payload);
                await World.Ticks(settleTicks);
            }
            finally
            {
                World.Api.Logger.EntryAdded -= OnWarning;
            }

            return warnings;
        }

        /// <summary>Lines that name both reasons the skeleton guard can fire.</summary>
        private static bool NamesBothSkeletonReasons(string warning) =>
            warning.Contains("no settable content", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("partial write caught mid-save", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("deliberately minimal config", StringComparison.OrdinalIgnoreCase);

        /// <summary>Full-file config body every row uses as its self-seed/overwrite payload.</summary>
        private static string FullConfig(bool chest, float speed)
            => $$"""
            {
              "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
              "Carryables": {
                "Chest": {{chest.ToString().ToLowerInvariant()}}
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{speed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}}
              }
            }
            """;

        /// <summary>
        /// Normalizes a row's starting state. First drains ~1 s so any prior row's watcher
        /// event (which can land within the 500 ms debounce of this row's first write)
        /// cannot swallow the seed write; then writes and waits for the fixture baseline
        /// (Chest=false, speed=2.0) and waits ~20 ticks so the 500 ms window reopens before
        /// the row's next write - otherwise a payload written this close to the seed write
        /// is debounced (dropped) and the row never exercises a reload at all.
        /// </summary>
        private async Task SelfSeedBaselineAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            WriteConfig(FullConfig(chest: false, speed: SeedSpeed));
            await WaitForConfigAsync(
                c => !c.Carryables.Chest && c.CarryOptions.InteractSpeedMultiplier == SeedSpeed,
                "seed baseline (Chest=false, InteractSpeedMultiplier=2.0)");
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
                    + $"InteractSpeedMultiplier={Mod.Config.CarryOptions.InteractSpeedMultiplier}.");
            }
        }

        /// <summary>Asserts the config does not change across a hold window.</summary>
        private async Task AssertConfigStableAsync(string because, int coolDownTicks = SettleTicks)
        {
            var before = (Chest: Mod.Config.Carryables.Chest, Speed: Mod.Config.CarryOptions.InteractSpeedMultiplier);
            await World.Ticks(coolDownTicks);
            Assert.True(
                before.Chest == Mod.Config.Carryables.Chest
                    && before.Speed == Mod.Config.CarryOptions.InteractSpeedMultiplier,
                $"Config changed during hold window - {because}: "
                + $"was Chest={before.Chest},Speed={before.Speed}; now "
                + $"Chest={Mod.Config.Carryables.Chest},Speed={Mod.Config.CarryOptions.InteractSpeedMultiplier}.");
        }

        private IDisposable SubscribeReloadCounter()
        {
            reloadCount = 0;
            reloadHandler = _ => Interlocked.Increment(ref reloadCount);
            Mod.ConfigService.OnConfigChanged += reloadHandler;
            return new UnsubscribeAction(() =>
            {
                if (reloadHandler != null) Mod.ConfigService.OnConfigChanged -= reloadHandler;
                reloadHandler = null;
            });
        }

        private int ReloadCount => Volatile.Read(ref reloadCount);

        /// <summary>
        /// Subscribes a counter to <see cref="CarryOnConfigService.OnConfigReloadedFromFile"/>
        /// so a row can prove the post-apply callback fired for its own reload. Kept separate
        /// from <see cref="SubscribeReloadCounter"/> because the two events are distinct:
        /// <c>OnConfigChanged</c> fires on every <see cref="CarryOnConfigService.Replace"/>,
        /// while <c>OnConfigReloadedFromFile</c> fires only once the reload has applied and
        /// the warning path has run to completion.
        /// </summary>
        private IDisposable SubscribeReloadedFromFileCounter()
        {
            reloadedFromFileCount = 0;
            reloadedFromFileHandler = _ => Interlocked.Increment(ref reloadedFromFileCount);
            Mod.ConfigService.OnConfigReloadedFromFile += reloadedFromFileHandler;
            return new UnsubscribeAction(() =>
            {
                if (reloadedFromFileHandler != null) Mod.ConfigService.OnConfigReloadedFromFile -= reloadedFromFileHandler;
                reloadedFromFileHandler = null;
            });
        }

        private int ReloadedFromFileCount => Volatile.Read(ref reloadedFromFileCount);

        /// <summary>
        /// A1 - overwriting the file with <c>Chest=true</c> flips the live config without a
        /// restart (the watcher applied the edit), but with the default Mode A the recondition
        /// is gated off: behavioral conditioning is NOT re-run, so the chest's carryable
        /// behavior takes effect only on the next server start. Picking up a chest therefore
        /// fails with <see cref="FailureCodes.NotCarryable"/> and the block carries no
        /// carryable behavior at all after the reload.
        /// </summary>
        [Trait("summary", "Overwriting the file with Chest=true flips the live config without a restart, but Mode A does not re-run conditioning, so the chest still reports not-carryable.")]
        [Trait("pins", "The Mode A half-apply: the config updates, the behavior surface does not.")]
        [Trait("role", "regression")]
        [Trait("decision", "22")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_ValidEdit_AppliesConfig_WithoutReconditioning()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig(FullConfig(chest: true, speed: SeedSpeed));
            await WaitForConfigAsync(c => c.Carryables.Chest, "config flips Carryables.Chest to true after the overwrite");

            Assert.True(Mod.Config.Carryables.Chest, "Config did not flip to Chest=true");
            Assert.Equal(SeedSpeed, Mod.Config.CarryOptions.InteractSpeedMultiplier);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);
            Assert.Null(carried);
            Assert.Equal(FailureCodes.NotCarryable, failureCode);

            var chestBlock = World.Api.World.BlockAccessor.GetBlock(pos);
            Assert.Null(chestBlock.GetBehavior<CarryOn.Common.Behaviors.BlockBehaviorCarryable>());
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// A-new-1 - Mode A (the default): a behavior-surface config change is applied to
        /// the live config and synced to clients via <c>ConfigSyncMessage</c>, but no
        /// <c>CarryabilityStateMessage</c> is ever broadcast - conditioning was not re-run,
        /// so there is nothing new to reconcile.
        /// </summary>
        [Trait("summary", "A behavior-surface change applies to the live config and syncs via ConfigSyncMessage, but no CarryabilityStateMessage is broadcast in Mode A.")]
        [Trait("pins", "Mode A emits no carryability snapshot because conditioning did not run.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_BehaviorChange_SkipsReconditionBroadcast()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            player.Client.Clear();

            WriteConfig(FullConfig(chest: true, speed: SeedSpeed));
            await WaitForConfigAsync(c => c.Carryables.Chest, "config flips Carryables.Chest to true");
            await World.Ticks(SettleTicks);

            Assert.NotEmpty(player.Client.Packets<ConfigSyncMessage>(Channel));
            Assert.Empty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// A-new-2 - even a non-behavior scalar reload (speed flip) produces
        /// <c>ConfigSyncMessage</c> only. In Mode A no carryability snapshot exists to
        /// broadcast or to hand to a joining player.
        /// </summary>
        [Trait("summary", "A speed-only reload produces ConfigSyncMessage only; no carryability snapshot exists to broadcast or to hand a joining player.")]
        [Trait("pins", "Mode A never creates a carryability snapshot.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_ScalarOnly_NoCarryabilityMessage_AtAll()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            player.Client.Clear();

            WriteConfig(FullConfig(chest: false, speed: 0.5f));
            await WaitForConfigAsync(c => c.CarryOptions.InteractSpeedMultiplier == 0.5f, "speed-only flip applied");
            await World.Ticks(SettleTicks);

            Assert.NotEmpty(player.Client.Packets<ConfigSyncMessage>(Channel));
            Assert.Empty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// A-new-3 - Mode A pin:
        /// a <c>CarryablesOnBack</c> edit applies to the live config but never
        /// reconditions, so the barrel's Back slot keeps its boot state (restart-only
        /// semantics). Expected GREEN - this pins the Mode A contract the restore fix
        /// must not break.
        /// </summary>
        [Trait("summary", "A CarryablesOnBack edit applies to the live config but never reconditions, so the barrel's Back slot keeps its boot state (restart-only semantics).")]
        [Trait("pins", "The Mode A contract the slot-restore fix must not break.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_OnBackEdit_DoesNotTouchBehavior()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            var onBackOff = $$"""
                {
                  "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
                  "Carryables": {
                    "Barrel": true
                  },
                  "CarryablesOnBack": {
                    "Barrel": false
                  },
                  "CarryOptions": {
                    "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}}
                  }
                }
                """;
            WriteConfig(onBackOff);
            await WaitForConfigAsync(
                c => c.Carryables.Barrel && !c.CarryablesOnBack.Barrel,
                "CarryablesOnBack.Barrel=false applies to the live config");
            await World.Ticks(SettleTicks);

            var barrel = World.Api.World.GetBlock(new AssetLocation("barrel"));
            Assert.NotNull(barrel);
            var behavior = barrel!.GetBehavior<BlockBehaviorCarryable>();
            Assert.NotNull(behavior);
            Assert.NotNull(behavior!.Slots[CarrySlot.Hands]);
            Assert.True(behavior!.Slots[CarrySlot.Back] != null,
                "Mode A must not touch behavior state - the Back slot keeps its boot value");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// A2 - a non-default scalar reloads both on the way in (0.5, differing from the
        /// seed's 2.0) and back to the seed value (2.0), proving the watcher landed the
        /// written value rather than merely re-reading the boot file.
        /// <para>
        /// Deliberately no pre-SelfSeed premise assert on the shared applied config.
        /// Rows run in discovery order (not declaration order) over one server boot, and
        /// a predecessor whose final write is a partial payload - the dropped-section
        /// rows end on a DebuggingOptions-only file - legitimately leaves the applied
        /// config at the defaults (speed 1.0) at this row's start. The baseline the
        /// 0.5/2.0 writes are read against is the one <see cref="SelfSeedBaselineAsync"/>
        /// writes and waits for, so the row stays order-independent.
        /// </para>
        /// </summary>
        [Trait("summary", "A speed write of 0.5 applies, then a write back to 2.0 applies; the loaded config takes the latest value each time, both directions.")]
        [Trait("pins", "The watcher applies the written value rather than merely re-reading the boot file.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_NonDefaultScalar_Applies()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig(FullConfig(chest: false, speed: 0.5f));
            await WaitForConfigAsync(
                c => c.CarryOptions.InteractSpeedMultiplier == 0.5f,
                "self-seeded InteractSpeedMultiplier=0.5");

            await World.Ticks(SettleTicks); // let the 500 ms debounce lapse before the next write

            WriteConfig(FullConfig(chest: false, speed: SeedSpeed));
            await WaitForConfigAsync(
                c => c.CarryOptions.InteractSpeedMultiplier == SeedSpeed,
                "overwrite lands InteractSpeedMultiplier=2.0");

            Assert.Equal(SeedSpeed, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// A3 - a reload broadcasts the applied config to connected clients
        /// (<c>ConfigSyncMessage</c> on the carryon channel): the captured payload carries
        /// the new Chest/speed values, not the stale ones.
        /// </summary>
        [Trait("summary", "After the watcher applies an edit, a connected client receives a ConfigSyncMessage whose payload matches the new values.")]
        [Trait("pins", "Connected clients are re-synced on a reload without a restart.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_SyncsToConnectedClient()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            player.Client.Clear();

            WriteConfig(FullConfig(chest: true, speed: 0.5f));
            await WaitForConfigAsync(
                c => c.Carryables.Chest && c.CarryOptions.InteractSpeedMultiplier == 0.5f,
                "server config applied after the overwrite");

            var messages = player.Client.Packets<ConfigSyncMessage>(Channel);
            Assert.NotEmpty(messages);
            var latest = messages[messages.Count - 1];
            Assert.NotNull(latest.ConfigJson);
            var decoded = JsonConvert.DeserializeObject<CarryOnConfig>(latest.ConfigJson!)!;
            Assert.True(decoded.Carryables.Chest, "broadcast payload did not carry Chest=true");
            Assert.Equal(0.5f, decoded.CarryOptions.InteractSpeedMultiplier);
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// B1 - syntactically invalid JSON must be rejected without losing the previous
        /// config and without rewriting the user's file (no defaults written back).
        /// Expected RED if a degraded read path clobbers instead of throwing.
        /// </summary>
        [Trait("summary", "A syntactically invalid JSON write is rejected: the loaded config and the on-disk bytes both stay byte-exact.")]
        [Trait("pins", "A partial or wrong write is never applied and the user's file is never rewritten.")]
        [Trait("role", "regression")]
        [Trait("decision", "22")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_InvalidJson_KeepsPreviousConfig_AndPreservesUserFile()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            var bad = "{\"ConfigVersion\":6,\"Carryables\":{\"Chest\"::true}}";
            WriteConfig(bad);
            await World.Ticks(SettleTicks);

            Assert.False(Mod.Config.Carryables.Chest, "Config was clobbered by an invalid JSON read");
            Assert.Equal(SeedSpeed, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.Equal(Encoding.UTF8.GetBytes(bad), ReadConfigBytes());
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// B2 - a truncated-but-parseable body (<c>{"ConfigVersion":6}</c>) simulates the
        /// mid-save read window: every key but the version silently degrades to defaults.
        /// Expected RED: this is the exact shape that reproduces the reported
        /// data clobber - the
        /// reload accepts the degraded file, persists defaults over the user's edits.
        /// </summary>
        [Trait("summary", "A truncated-but-parseable skeleton is rejected as a degraded short write; the config keeps the user's values and the file stays byte-exact.")]
        [Trait("pins", "The reported config data-loss shape: no reload-originated persist overwrites the user's edits.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the degraded read was accepted and defaults were persisted over the file.")]
        [Trait("decision", "22")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_TruncatedPartialWrite_KeepsConfig_FileNotClobbered()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            var truncated = "{\"ConfigVersion\":6}";
            WriteConfig(truncated);
            await World.Ticks(SettleTicks);

            Assert.False(Mod.Config.Carryables.Chest, "Config was clobbered by a truncated (degraded) JSON read");
            Assert.Equal(SeedSpeed, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.Equal(Encoding.UTF8.GetBytes(truncated), ReadConfigBytes());
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// B3 - a zero-byte file must not clobber the live config, and must stay empty on
        /// disk (the watcher must not rewrite it with defaults).
        /// </summary>
        [Trait("summary", "A zero-byte file throws in the loader exactly like invalid JSON: the config and the on-disk file both stay untouched.")]
        [Trait("pins", "An empty read path does not clobber the live config or rewrite the file.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_EmptyFile_KeepsConfig_FileStaysEmpty()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig("");
            await World.Ticks(SettleTicks);

            Assert.False(Mod.Config.Carryables.Chest, "Config was clobbered by an empty-file read");
            Assert.Equal(SeedSpeed, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.Empty(ReadConfigBytes());
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// W15 — premise: skeleton (empty sections) still rejected and previous config kept.
        /// Green before and after; asserts the reworded warning names both possibilities.
        /// Red-first on the unfixed head:
        /// <c>Skeleton warning must name both possibilities; warnings were: CarryOn: Config
        /// file is a skeleton/short write (no settable content), keeping current config</c>
        /// </summary>
        [Trait("summary", "A skeleton payload is rejected with the config and file untouched, and the warning names both reasons the guard can fire.")]
        [Trait("pins", "The rejection is unchanged; only the warning text is under test.")]
        [Trait("role", "premise")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, where the warning named only the short-write reason.")]
        [Trait("decision", "28")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SkeletonWarning_NamesBothPossibilities()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            var skeleton = "{\"ConfigVersion\":" + CarryCodes.CurrentConfigVersion + "}";
            var warnings = await CaptureWarningsAsync(skeleton);

            Assert.False(Mod.Config.Carryables.Chest, "Skeleton must not clobber existing config");
            Assert.Equal(SeedSpeed, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.Equal(Encoding.UTF8.GetBytes(skeleton), ReadConfigBytes());

            var matching = warnings.Where(w => NamesBothSkeletonReasons(w)
                && w.Contains("keeping current config", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.True(matching.Count == 1, "Skeleton warning must name both possibilities; warnings were: " + string.Join(" | ", warnings));
        }

        /// <summary>
        /// W16 — control: legitimate {"Carryables":{}} is still classified as a skeleton
        /// (the guard is not tightened) and the warning is emitted with both possibilities.
        /// Red-first on the unfixed head, same reason as W15:
        /// <c>Shell still emits skeleton warning; warnings were: CarryOn: Config file is a
        /// skeleton/short write (no settable content), keeping current config</c>
        /// </summary>
        [Trait("summary", "A legitimate empty Carryables shell is still classified as a skeleton and still warns.")]
        [Trait("pins", "The strict half of the guard is deliberately not relaxed.")]
        [Trait("role", "control")]
        [Trait("decision", "28")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SkeletonWarning_ShellSectionStillEmits()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            var shell = $$"""
                {
                  "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
                  "Carryables": {}
                }
                """;
            var warnings = await CaptureWarningsAsync(shell);

            Assert.False(Mod.Config.Carryables.Chest, "Empty shell must still be treated as skeleton");
            Assert.Equal(SeedSpeed, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.Equal(Encoding.UTF8.GetBytes(shell), ReadConfigBytes());

            var matching = warnings.Where(NamesBothSkeletonReasons).ToList();
            Assert.True(matching.Count == 1, "Shell still emits skeleton warning; warnings were: " + string.Join(" | ", warnings));
        }

        /// <summary>
        /// W17 — hole case: a DebuggingOptions-only file is still applied (control, unchanged
        /// behaviour) and the differential warning names the sections that reset toward
        /// defaults. Red-first on the unfixed head for the warning only:
        /// <c>Assert.NotNull() Failure: Value is null</c> on the captured differential line,
        /// with every apply-side assertion already green.
        /// </summary>
        [Trait("summary", "A DebuggingOptions-only file is still applied, and the differential warning names the sections that reset toward defaults.")]
        [Trait("pins", "Partial applies warn about the settings they drop, so operators see what they are about to lose.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head for the warning only, with the apply-side assertions green.")]
        [Trait("decision", "28")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DifferentialWarning_ReportsDroppedSectionsOnPartialApply()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            // LoggingEnabled=true proves the apply happened (it differs from the seeded state and
            // from its own default), while the omitted sections must reset to their defaults.
            var partial = $$"""
                {
                  "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
                  "DebuggingOptions": {
                    "LoggingEnabled": true
                  }
                }
                """;
            var warnings = await CaptureWarningsAsync(partial);

            Assert.True(Mod.Config.DebuggingOptions.LoggingEnabled, "The DebuggingOptions-only file must still be applied");
            Assert.Equal(1.0f, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.True(Mod.Config.Carryables.Chest);

            var diff = warnings.FirstOrDefault(w => w.Contains("missing the", StringComparison.OrdinalIgnoreCase)
                && w.Contains("section", StringComparison.OrdinalIgnoreCase)
                && w.Contains("reset to defaults", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(diff);
            Assert.True(diff!.Contains("Carryables", StringComparison.OrdinalIgnoreCase)
                || diff.Contains("CarryOptions", StringComparison.OrdinalIgnoreCase)
                || diff.Contains("CarryablesFilters", StringComparison.OrdinalIgnoreCase)
                || diff.Contains("Interactables", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// B4 - after an invalid edit the watchdog must recover: the next valid write
        /// reloads and applies normally (no stuck state, no raised suppression).
        /// </summary>
        [Trait("summary", "After an invalid write is ignored, the next valid write reloads and applies normally.")]
        [Trait("pins", "The watchdog does not get stuck or raise suppression after a rejected event.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_Recovers_AfterInvalidEdit()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig("{");
            await World.Ticks(SettleTicks);
            Assert.False(Mod.Config.Carryables.Chest, "Invalid edit changed the config");
            Assert.Equal(SeedSpeed, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.Equal(Encoding.UTF8.GetBytes("{"), ReadConfigBytes());

            WriteConfig(FullConfig(chest: true, speed: 0.5f));
            await WaitForConfigAsync(
                c => c.Carryables.Chest && c.CarryOptions.InteractSpeedMultiplier == 0.5f,
                "config recovers to the valid edit after the invalid one");

            Assert.True(Mod.Config.Carryables.Chest);
            Assert.Equal(0.5f, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// C1 - a rapid multi-save burst whose first four writes are partial/invalid and
        /// whose last write is valid must end with exactly the last valid file applied:
        /// no intermediate applied, no crash. The trailing valid write lands after the
        /// debounce reopens so "last valid wins" is decided by the read, not by the write.
        /// </summary>
        [Trait("summary", "Four writes land inside the debounce window (three invalid or degraded, one valid); only the final valid payload is applied.")]
        [Trait("pins", "No intermediate or invalid payload ever surfaces; last valid wins.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_RapidMultiSave_LastValidWins_NoInvalidApplied()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            var burst = new[]
            {
                "{",
                "{\"ConfigVersion\":6,\"Carryables\":{\"Chest\":tru",
                "",
                "{\"ConfigVersion\""
            };
            foreach (var partial in burst) WriteConfig(partial);

            await World.Ticks(20); // > 500 ms: debounce reopened before the trailing valid write

            WriteConfig(FullConfig(chest: true, speed: 2.5f));
            await WaitForConfigAsync(
                c => c.Carryables.Chest && c.CarryOptions.InteractSpeedMultiplier == 2.5f,
                "last valid write wins over the invalid burst");

            Assert.True(Mod.Config.Carryables.Chest);
            Assert.Equal(2.5f, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// C2 - a config-change persist (the same handler a reload runs) must not feed a
        /// watcher reload loop: <c>StoreModConfig</c> writes the file, and the watcher
        /// must not re-enter a persist→reload→persist cycle. Expected RED if the
        /// persist-originated write is not suppressed from the watcher.
        /// </summary>
        [Trait("summary", "A config change through the persist path records exactly one reload cycle, and the user file keeps its edits.")]
        [Trait("pins", "The persist-originated write is suppressed from the watcher, so there is no persist-reload loop.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HotReload_Persist_DoesNotSelfLoop()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            await World.Ticks(SettleTicks); // let the self-seed's own persist write quiesce

            using (SubscribeReloadCounter())
            {
                Mod.ConfigService.NotifyChanged(); // fires the persist handler synchronously

                await World.Ticks(SettleTicks); // ~2 s: several debounce windows
                var c1 = ReloadCount;

                await World.Ticks(45); // ~1.5 s more
                var c2 = ReloadCount;

                Assert.True(
                    c2 - c1 <= 1,
                    $"Persist triggered a watcher reload loop: OnConfigChanged counter grew {c1} -> {c2} across the hold window");

                Assert.False(Mod.Config.Carryables.Chest, "Config drifted during the persist loop");
                Assert.Equal(SeedSpeed, Mod.Config.CarryOptions.InteractSpeedMultiplier);

                var onDisk = JsonConvert.DeserializeObject<CarryOnConfig>(File.ReadAllText(ConfigPath));
                Assert.NotNull(onDisk);
                Assert.False(onDisk!.Carryables.Chest, "User file lost the Chest=false edit");
                Assert.Equal(SeedSpeed, onDisk.CarryOptions.InteractSpeedMultiplier);
            }
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// D2 - the server command <c>/carryon-reload</c> applies the edited file on
        /// demand (watcher on or off): the command succeeds and the live config matches
        /// the file's new values.
        /// </summary>
        [Trait("summary", "Editing the file then /carryon-reload applies it synchronously via the command path, independent of the watcher.")]
        [Trait("pins", "The command reads on demand even when the watcher is not the trigger.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ReloadCommand_AppliesValidEdit()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig(FullConfig(chest: true, speed: 0.5f));
            var result = await World.ExecuteCommand("/carryon-reload");
            Assert.True(result.Ok, $"carryon-reload failed: {result.Message}");

            Assert.True(Mod.Config.Carryables.Chest, "Reload command did not apply the Chest=true edit");
            Assert.Equal(0.5f, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// D3 - deleting the config and recreating it with edited content is picked up by
        /// the watcher (the <c>Created</c> event) and applied.
        /// </summary>
        [Trait("summary", "After the file is deleted and recreated, the watcher re-attaches and applies the recreated content.")]
        [Trait("pins", "The watcher recovers its watch after the file is replaced.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Watcher_PicksUpRecreatedFile()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            File.Delete(ConfigPath);
            await World.Ticks(20); // deletion is not watched; let in-flight events drain

            WriteConfig(FullConfig(chest: true, speed: 0.5f));
            await WaitForConfigAsync(
                c => c.Carryables.Chest && c.CarryOptions.InteractSpeedMultiplier == 0.5f,
                "recreated file reapplied by the watcher");

            Assert.True(Mod.Config.Carryables.Chest);
            Assert.Equal(0.5f, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// R1 - command path: a file whose <c>ReconditionOnConfigChange</c> disagrees with
        /// the boot latch still applies, and the command result carries the mode-mismatch
        /// warning, so the invoking player reads it in their own chat. The harness runs
        /// commands as the console - exactly the design's "command with no player caller"
        /// leg: log plus command result, no broadcast.
        /// </summary>
        [Trait("summary", "A /carryon-reload of a file that sets ReconditionOnConfigChange=true while the session booted without it succeeds and its result text carries the mode-mismatch warning.")]
        [Trait("pins", "Reload feedback reaches the command result, not only the server log.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, whose command result was a bare success line with no notices.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ReloadCommand_ModeMismatchWarning_InCommandResult()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            var mismatched = $$"""
                {
                  "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
                  "Carryables": {
                    "Chest": false
                  },
                  "CarryOptions": {
                    "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}}
                  },
                  "DebuggingOptions": {
                    "ReconditionOnConfigChange": true
                  }
                }
                """;
            WriteConfig(mismatched);
            var result = await World.ExecuteCommand("/carryon-reload");

            Assert.True(result.Ok, $"carryon-reload failed: {result.Message}");
            Assert.True(
                result.Message.Contains(
                    "ReconditionOnConfigChange is true in the config file but was false at server start",
                    StringComparison.OrdinalIgnoreCase),
                $"Command result must carry the mode-mismatch warning; result was: {result.Message}");
            Assert.True(Mod.Config.DebuggingOptions.ReconditionOnConfigChange,
                "The mismatching file must still apply its own flag value");
        }

        /// <summary>
        /// R2 - command path: a skeleton file is rejected and the command returns an
        /// error carrying the rejection warning. The pre-fix handler returned success
        /// unconditionally, even over a rejected reload.
        /// </summary>
        [Trait("summary", "A /carryon-reload against a skeleton file returns a command error carrying the rejection warning, and the running config is kept.")]
        [Trait("pins", "The command never reports success over a rejected reload.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, which returned a success line unconditionally, even over a rejected file.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ReloadCommand_SkeletonFile_ReturnsError()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            var skeleton = "{\"ConfigVersion\":" + CarryCodes.CurrentConfigVersion + "}";
            WriteConfig(skeleton);
            var result = await World.ExecuteCommand("/carryon-reload");

            Assert.False(result.Ok,
                $"A rejected reload must return a command error; result was: {result.Message}");
            Assert.True(
                result.Message.Contains("no settable content", StringComparison.OrdinalIgnoreCase)
                    && result.Message.Contains("keeping current config", StringComparison.OrdinalIgnoreCase),
                $"Command error must carry the skeleton rejection warning; result was: {result.Message}");
            Assert.False(Mod.Config.Carryables.Chest, "Skeleton must not clobber existing config");
            Assert.Equal(SeedSpeed, Mod.Config.CarryOptions.InteractSpeedMultiplier);
        }

        /// <summary>
        /// R3 - watcher path, Mode A: an edit that changes a behavior-surface section
        /// (<c>Interactables</c> - a section no other row in this class touches, so this
        /// row's chat text is unique under the per-session dedup) broadcasts the
        /// pending-restart warning to the online player through the non-dedicated fallback,
        /// naming the changed section and the next-server-start effect.
        /// </summary>
        [Trait("summary", "A watcher reload that flips Interactables.Door in Mode A broadcasts the pending-restart warning, naming the section and the restart requirement, to the online player's chat.")]
        [Trait("pins", "Mode A behavior-surface edits are no longer silent in game: the warning names what changed and says it takes effect on the next server start.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, which emitted no player-facing signal for a Mode A behavior-surface edit.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WatcherReload_ModeABehaviorChange_WarnsInChat()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            ITestPlayer player = await JoinPlayer("Tester");
            player.Client.Clear();

            var doorOff = $$"""
                {
                  "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
                  "Carryables": {
                    "Chest": false
                  },
                  "Interactables": {
                    "Door": false
                  },
                  "CarryOptions": {
                    "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}}
                  }
                }
                """;
            WriteConfig(doorOff);
            await WaitForConfigAsync(
                c => !c.Interactables.Door,
                "Interactables.Door=false applies to the live config");
            await World.Ticks(2);

            var warning = player.Client.ChatLines().FirstOrDefault(l =>
                l.Contains("This reload changes Interactables", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(warning);
            Assert.True(
                warning!.Contains("next server start", StringComparison.OrdinalIgnoreCase),
                $"Pending-restart warning must state the restart requirement; line was: {warning}");
        }

        /// <summary>
        /// R4 - Mode A revert guard: the pending-restart warning is driven by the surface
        /// the running world reflects (the boot-seeded signature), not by the previous
        /// file. Boot S0, edit to S1 warns; reverting to S0 does not - the file matches the
        /// running world again, nothing is pending. A per-reload comparator would
        /// false-warn on the revert. The flip targets <c>CarryablesOnBack</c>, a section
        /// whose solo-change text is unique to this row; the revert's absence half reads
        /// the server log (never deduped, no chat timing) rather than chat lines.
        /// </summary>
        [Trait("summary", "A Mode A edit that flips CarryablesOnBack.Chest warns in chat once, and reverting the file to the boot-equivalent surface emits no new pending-restart warning.")]
        [Trait("pins", "The pending-restart comparator is the running world's surface, so a revert never false-warns.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WatcherReload_ModeARevert_DoesNotRewarn()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            ITestPlayer player = await JoinPlayer("Tester");
            player.Client.Clear();

            var onBackOff = $$"""
                {
                  "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
                  "Carryables": {
                    "Chest": false
                  },
                  "CarryablesOnBack": {
                    "Chest": false
                  },
                  "CarryOptions": {
                    "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}}
                  }
                }
                """;
            WriteConfig(onBackOff);
            await WaitForConfigAsync(
                c => !c.CarryablesOnBack.Chest,
                "CarryablesOnBack.Chest=false applies to the live config");
            await World.Ticks(2);

            Assert.Equal(1, player.Client.ChatLines().Count(l =>
                l.Contains("This reload changes CarryablesOnBack", StringComparison.OrdinalIgnoreCase)));

            var revertWarnings = await CaptureWarningsAsync(FullConfig(chest: false, speed: SeedSpeed));
            Assert.True(
                revertWarnings.All(w => !w.Contains("This reload changes", StringComparison.OrdinalIgnoreCase)),
                $"Reverting to the boot-equivalent surface must not re-warn; warnings were: "
                + string.Join(" | ", revertWarnings));
            Assert.True(Mod.Config.CarryablesOnBack.Chest,
                "The revert payload resets CarryablesOnBack.Chest to its default");
        }

        /// <summary>
        /// R5 - Mode A edit of a <c>CarryOptions</c> key included in the behavior surface
        /// signature: the warning names the key prefixed with its section and notes that
        /// its immediately-live effects still apply while its carry-behavior effects wait
        /// for the restart.
        /// </summary>
        [Trait("summary", "A Mode A watcher reload that flips CarryOptions.PickupGroundStorageBagWhileCarrying warns in chat, naming the key and noting its immediately-live effects still apply.")]
        [Trait("pins", "The warning's diff surface matches the behavior-surface signature exactly, including the two CarryOptions keys.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, which had no warning at all for a Mode A behavior-surface edit.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WatcherReload_ModeACarryOptionsKeyChange_WarnsInChat()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            ITestPlayer player = await JoinPlayer("Tester");
            player.Client.Clear();

            var pickupOff = $$"""
                {
                  "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
                  "Carryables": {
                    "Chest": false
                  },
                  "CarryOptions": {
                    "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}},
                    "PickupGroundStorageBagWhileCarrying": false
                  }
                }
                """;
            WriteConfig(pickupOff);
            await WaitForConfigAsync(
                c => !c.CarryOptions.PickupGroundStorageBagWhileCarrying,
                "PickupGroundStorageBagWhileCarrying=false applies to the live config");
            await World.Ticks(2);

            var warning = player.Client.ChatLines().FirstOrDefault(l =>
                l.Contains("This reload changes CarryOptions.PickupGroundStorageBagWhileCarrying", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(warning);
            Assert.True(
                warning!.Contains("immediately-live effects", StringComparison.OrdinalIgnoreCase),
                $"CarryOptions-key warning must note the immediately-live effects; line was: {warning}");
        }

        /// <summary>
        /// R6 - watcher broadcast leg: a partial payload (DebuggingOptions-only) drops
        /// populated sections, and the dropped-sections warning reaches the online
        /// player's chat via the non-dedicated fallback - not only the server log, which
        /// W17 already covers.
        /// </summary>
        [Trait("summary", "A watcher reload of a DebuggingOptions-only payload broadcasts the dropped-sections warning to the online player's chat.")]
        [Trait("pins", "Watcher reload feedback is visible in game, not only in the server log.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, where the watcher reload path had no player-facing output at all.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WatcherReload_DroppedSectionsWarning_ReachesChat()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            ITestPlayer player = await JoinPlayer("Tester");
            player.Client.Clear();

            var partial = $$"""
                {
                  "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
                  "DebuggingOptions": {
                    "LoggingEnabled": true
                  }
                }
                """;
            WriteConfig(partial);
            await WaitForConfigAsync(
                c => c.DebuggingOptions.LoggingEnabled,
                "the DebuggingOptions-only payload applies");
            await World.Ticks(2);

            var warning = player.Client.ChatLines().FirstOrDefault(l =>
                l.Contains("missing the", StringComparison.OrdinalIgnoreCase)
                && l.Contains("reset to defaults", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(warning);
            Assert.True(
                warning!.Contains("Carryables", StringComparison.OrdinalIgnoreCase)
                    || warning.Contains("CarryOptions", StringComparison.OrdinalIgnoreCase),
                $"Dropped-sections warning must name the dropped sections; line was: {warning}");
        }

        /// <summary>
        /// R7 - watcher chat dedup: the mode-mismatch warning fires on every reload while
        /// the file/boot disagreement persists but reaches chat only once per session;
        /// the applied confirmation is per-event feedback and re-sends on every reload.
        /// Two identical watcher reloads of a mismatching file: one warning line, two
        /// confirmation lines (the second count is also what proves the second reload
        /// ran). The file shape is shared with the command row, but the command path
        /// never touches the chat dedup set, so the rows stay order-independent.
        /// </summary>
        [Trait("summary", "Two identical watcher reloads of a ReconditionOnConfigChange-mismatching file deliver the mismatch warning to chat once and the applied confirmation twice.")]
        [Trait("pins", "Warning-class chat notices are deduped per session; the applied confirmation is per-event and never deduped.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, which had no chat delivery at all.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WatcherReload_RepeatedWarning_DeliveredToChatOnce()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            ITestPlayer player = await JoinPlayer("Tester");
            player.Client.Clear();

            var mismatched = $$"""
                {
                  "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
                  "Carryables": {
                    "Chest": false
                  },
                  "CarryOptions": {
                    "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}}
                  },
                  "DebuggingOptions": {
                    "ReconditionOnConfigChange": true
                  }
                }
                """;
            WriteConfig(mismatched);
            await WaitForConfigAsync(
                c => c.DebuggingOptions.ReconditionOnConfigChange,
                "the mismatching file applies");
            await World.Ticks(2);
            await World.Ticks(20); // let the first write's debounce window fully lapse

            WriteConfig(mismatched);
            await World.Ticks(SettleTicks);

            IReadOnlyList<string> lines = player.Client.ChatLines();
            Assert.Equal(1, lines.Count(l =>
                l.Contains("ReconditionOnConfigChange is true in the config file", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(2, lines.Count(l =>
                l.Contains("Config reloaded and applied", StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// R8 - applied-truth contract: a watcher reload that changes the behavior surface
        /// while Mode A is active makes <c>WarnOnBehaviorSurfacePendingRestart</c> run, yet
        /// the reload must still report <c>applied: true</c> in
        /// <see cref="CarryOnConfigService.LastReloadResult"/> and still fire
        /// <see cref="CarryOnConfigService.OnConfigReloadedFromFile"/>. The warning path is a
        /// diagnostic; a throw inside it (e.g. the signature re-parse) previously fell into
        /// the outer catch and reported a reload that had already swapped the config as
        /// rejected, skipping the reloaded callback. This pins the success path with the
        /// pending-restart warning live.
        /// <para>
        /// The throw itself has no fault-injection seam in the boot-per-class harness, so the
        /// isolation of the warning path from the apply outcome is verified by reasoning
        /// (the warnings are wrapped in their own try/catch); the applied truth with the
        /// warning path exercised is the pinned contract here.
        /// </para>
        /// </summary>
        [Trait("summary", "A Mode A watcher reload that triggers the pending-restart warning still reports applied: true and fires OnConfigReloadedFromFile.")]
        [Trait("pins", "A failure in the warning path cannot misreport a reload that did apply or skip the reloaded callback.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WatcherReload_PendingRestartWarning_StillReportsApplied()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            using (SubscribeReloadedFromFileCounter())
            {
                // Interactables.Door=false differs from the boot-seeded behavior surface
                // (Door defaults true) and, under Mode A, the applied signature never
                // refreshes, so the pending-restart warning is guaranteed to run.
                var doorOff = $$"""
                    {
                      "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
                      "Carryables": {
                        "Chest": false
                      },
                      "Interactables": {
                        "Door": false
                      },
                      "CarryOptions": {
                        "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}}
                      }
                    }
                    """;
                WriteConfig(doorOff);

                try
                {
                    await World.Until(() => ReloadedFromFileCount >= 1, WaitTimeoutTicks);
                }
                catch (ScenarioTimeoutException)
                {
                    Assert.Fail(
                        $"Timed out waiting for OnConfigReloadedFromFile - the pending-restart warning reload must still fire the callback. "
                        + $"Applied: {Mod.ConfigService.LastReloadResult?.Applied.ToString() ?? "<null>"}, "
                        + $"fetches: {ReloadedFromFileCount}.");
                }

                Assert.NotNull(Mod.ConfigService.LastReloadResult);
                Assert.True(
                    Mod.ConfigService.LastReloadResult!.Applied,
                    "A behavior-surface change that only warns must still report applied: true");
                Assert.False(Mod.Config.Interactables.Door, "The edited behavior surface must have applied");
                Assert.Equal(1, ReloadedFromFileCount);
            }
        }
    }
}