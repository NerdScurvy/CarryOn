using System.IO;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using Vintagestory.API.Config;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Own class because these rows' seeds must differ at boot time: the seed lands in
    /// <c>ModConfig/CarryOnConfig.json</c> (Atlas collides on that file name), and only a
    /// boot-time config can carry <c>DebuggingOptions.DisableConfigWatcher = true</c> into
    /// the state <c>SetupFileWatcher</c> sees.
    /// <para>
    /// That matters beyond D1. With the watcher genuinely off, <c>/carryon-reload</c> is the
    /// only way into <c>ReloadFromFile</c> - so D4 can attribute an observed reload to the
    /// command path specifically, instead of racing the watcher for the same edit.
    /// </para>
    /// <para>
    /// The fixture leaves <c>ReconditionOnConfigChange</c> unset, so this class boots Mode A.
    /// </para>
    /// </summary>
    [Trait("area", "Config reload")]
    [Trait("summary", "Watcher-disabled config rows: with the file watcher boot-disabled a config edit does not hot-reload, and /carryon-reload is the only path into the reload, so a Mode A session's command reload stays Mode A.")]
    [AtlasDataFiles("fixtures/WatchdogDisabledConfig", TargetPath = "ModConfig")]
    public class ConfigWatchdogDisabledScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// The known-good state both rows seed with: chest off, stock speed, watcher still
        /// disabled, recondition flag off. Carries <c>DisableConfigWatcher</c> explicitly
        /// because a reload replaces the whole <c>DebuggingOptions</c> section - dropping
        /// the key would leave the live config saying "watcher on".
        /// </summary>
        private const string BaselinePayload =
            "{\"ConfigVersion\":6,"
            + "\"Carryables\":{\"Chest\":false},"
            + "\"CarryOptions\":{\"InteractSpeedMultiplier\":2.0},"
            + "\"DebuggingOptions\":{"
            + "\"ReconditionOnConfigChange\":false,"
            + "\"DisableConfigWatcher\":true}}";
        /// <summary>
        /// D1 - <c>DisableConfigWatcher = true</c> must stop the hot-reload: after the
        /// file is overwritten and a hold window elapses (well past the debounce plus
        /// dispatch), the live config must still be the state it was in before the write.
        /// <para>
        /// The baseline is established through <c>/carryon-reload</c> rather than assumed
        /// to be the boot state. The server is shared across the rows of a class, so the
        /// live config carries whatever the previous row left behind - this row's own
        /// "the config did not change" assertion was reading D4's leftovers and reporting
        /// them as a hot-reload, which reads as a completely unrelated failure. Using the
        /// command to seed is not a contradiction of what is under test: the watcher is
        /// demonstrably off at that point (it is the boot state), and the row is about the
        /// <i>watcher</i> path not firing, not about the command being unavailable.
        /// </para>
        /// </summary>
        [Trait("summary", "With the watcher boot-disabled, overwriting the config file does not reload it, so the live config keeps its baseline after the hold window.")]
        [Trait("pins", "DebuggingOptions.DisableConfigWatcher suppresses the watcher entirely so no file edit hot-reloads.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DisableConfigWatcher_True_StopsHotReload()
        {
            Assert.NotNull(World.Api);

            var configPath = Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);
            File.WriteAllText(configPath, BaselinePayload);
            var seed = await World.ExecuteCommand("/carryon-reload");
            Assert.True(seed.Ok, $"baseline reload failed: {seed.Message}");

            Assert.True(
                Mod.Config.DebuggingOptions.DisableConfigWatcher,
                "premise: DisableConfigWatcher must still be true after the baseline reload");
            Assert.False(
                Mod.Config.Carryables.Chest,
                "premise: the baseline payload must have applied, or the assertion below is void");

            File.WriteAllText(configPath,
                "{\"ConfigVersion\":6,\"Carryables\":{\"Chest\":true},\"CarryOptions\":{\"InteractSpeedMultiplier\":0.5}}");

            await World.Ticks(90); // ~3 s hold - well past debounce + main-thread dispatch

            Assert.False(
                Mod.Config.Carryables.Chest,
                "Config reloaded despite DebuggingOptions.DisableConfigWatcher=true");
            Assert.Equal(2.0f, Mod.Config.CarryOptions.InteractSpeedMultiplier);
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// D4 - RED before the fix. <b>The command path, isolated.</b> The watchdog is off
        /// here (fixture sets <c>DisableConfigWatcher: true</c>), so <c>/carryon-reload</c>
        /// is the <i>only</i> way into <c>ReloadFromFile</c> and this row cannot race the
        /// watcher for the edit. That is the whole reason this row lives in this class
        /// rather than next to D2: in <c>ConfigWatchdogScenarios</c> the watcher is armed,
        /// so a reload may arrive by either path and you cannot tell which one you measured.
        /// <para>
        /// This fixture also leaves <c>ReconditionOnConfigChange</c> unset, so the session
        /// boots Mode A. The payload therefore sets the flag <b>on</b> together with a
        /// behavior-surface edit (<c>Carryables.Chest</c>), and neither may take effect
        /// behaviorally: the file's flag must not re-arm a running server. Both halves of the
        /// config edit must still <i>apply</i> - that is asserted, so the row cannot pass by
        /// the command quietly failing.
        /// </para>
        /// <para>
        /// Before the fix the gate read the reloaded flag, so the applied <c>true</c> opened
        /// the gate, the Chest edit reconditioned and broadcast.
        /// </para>
        /// </summary>
        [Trait("summary", "A /carryon-reload payload that turns ReconditionOnConfigChange on and flips Carryables.Chest still applies both halves but broadcasts no carryability snapshot in a Mode A session.")]
        [Trait("pins", "The reload gate reads the boot-latched mode, so a file's flag-on cannot re-arm a running Mode A server.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the gate read the reloaded flag, so the applied true opened the gate and the Chest edit reconditioned and broadcast.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ReloadCommand_FlagOnPlusBehaviorEdit_StillNoBroadcastInModeA()
        {
            Assert.NotNull(World.Api);
            Assert.True(
                Mod.Config.DebuggingOptions.DisableConfigWatcher,
                "premise: this row isolates the command path, so the watcher must be off");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await World.Ticks(60);
            player.Client.Clear();

            var configPath = Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);
            // DisableConfigWatcher is carried through on purpose: a reload replaces the
            // DebuggingOptions section wholesale, so omitting it here would leave the file
            // saying "watcher on" and make this class order-dependent - D1 asserts that
            // flag at its own first line and failed once when D4 ran ahead of it.
            File.WriteAllText(configPath,
                "{\"ConfigVersion\":6,"
                + "\"Carryables\":{\"Chest\":true},"
                + "\"CarryOptions\":{\"InteractSpeedMultiplier\":2.0},"
                + "\"DebuggingOptions\":{"
                + "\"ReconditionOnConfigChange\":true,"
                + "\"DisableConfigWatcher\":true}}");

            var result = await World.ExecuteCommand("/carryon-reload");
            Assert.True(result.Ok, $"carryon-reload failed: {result.Message}");
            await World.Ticks(60);

            // The command must have applied the file - both keys. If either is false the
            // payload never landed and the "no broadcast" below would be vacuous.
            Assert.True(
                Mod.Config.DebuggingOptions.ReconditionOnConfigChange,
                "premise: /carryon-reload must have applied ReconditionOnConfigChange=true");
            Assert.True(
                Mod.Config.Carryables.Chest,
                "premise: /carryon-reload must have applied Carryables.Chest=true");

            Assert.Empty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            Assert.NotNull(World.Api);
        }
    }
}