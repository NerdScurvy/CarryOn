using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
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
    /// Mode B rows (<c>DebuggingOptions.ReconditionOnConfigChange = true</c>, seeded by the
    /// class fixture): a file reload re-runs behavioral conditioning when - and only when -
    /// the reloaded config's behavior surface differs from the applied one (the dirty
    /// gate), then broadcasts a <see cref="CarryabilityStateMessage"/> snapshot so
    /// connected and joining clients reconcile onto the post-conditioning state.
    ///
    /// Like the Mode A watchdog class, every row self-seeds the full baseline first - the
    /// class shares one server boot (one watcher + one in-memory config), and the baseline
    /// write also normalizes the applied behavior signature back to Chest=false so rows
    /// are order-independent regardless of which row ran before (a prior row may have left
    /// Chest=true applied). Rows that assert "no broadcast" settle after the join so the
    /// join-time snapshot send cannot land after the packet Clear().
    ///
    /// The W rows that used to sit here - the pure-function rows pinning the interim
    /// non-reversible-edit warning - are gone with the warning itself: every edit class it named
    /// is reversible now, so it warned about a loss that no longer happens. The
    /// reversibility is pinned where the edits are made instead - B1/B2/B3 for the removal
    /// prefixes, B5a/B5b for the automapper's off-switches, B5c/B10 structurally for the
    /// two clone kinds, and R1-R9 for the reload path they all run through.
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Mode B dirty-gate rows: a file reload re-runs behavioral conditioning only when the behavior surface changed, then broadcasts a carryability snapshot so connected and joining clients reconcile.")]
    [AtlasDataFiles("fixtures/ReconditionOnChangeConfig", TargetPath = "ModConfig")]
    public class ReconditionOnChangeScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;          // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;                // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        private void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

        private byte[] ReadConfigBytes() => File.ReadAllBytes(ConfigPath);

        /// <summary>
        /// Full-file Mode B payload every row writes: emits the recondition flag (on unless
        /// the row under test is about that flag) and pins every signature-relevant key so
        /// only the row's measured change moves the behavior surface. Omitted keys
        /// deserialize to the same defaults the fixture boots with.
        /// <para>
        /// The flag key is still written by every row, deliberately. The gate no longer
        /// reads it - it reads the boot-time latch - so a row that omits the key would not
        /// keep the gate open, it would leave <c>Mod.Config</c> at whatever the previous row
        /// left behind and make rows order-dependent. Writing it explicitly keeps each
        /// row's intent visible: <c>true</c> everywhere except R10/R11, where turning it
        /// <i>off</i> is the point.
        /// </para>
        /// </summary>
        private static string FullConfigB(
            bool chest,
            float speed,
            bool allowHighCapacityStorageOnBack = false,
            bool autoMapSimilar = true,
            bool reconditionOnConfigChange = true,
            string[]? removeCarryable = null)
        {
            // Only the row that measures a removal-prefix edit emits the key, so every
            // other row's payload stays byte-identical to what it was before the W rows.
            var removeCarryableJson = removeCarryable == null ? ""
                : ",\n    \"RemoveCarryableBehaviour\": ["
                  + string.Join(", ", removeCarryable.Select(p => $"\"{p}\"")) + "]";
            return $$"""
            {
              "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
              "Carryables": {
                "Chest": {{chest.ToString().ToLowerInvariant()}}
              },
              "CarryablesFilters": {
                "AutoMapSimilar": {{autoMapSimilar.ToString().ToLowerInvariant()}}{{removeCarryableJson}}
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{speed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}},
                "AllowHighCapacityStorageOnBack": {{allowHighCapacityStorageOnBack.ToString().ToLowerInvariant()}}
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": {{reconditionOnConfigChange.ToString().ToLowerInvariant()}}
              }
            }
            """;
        }

        private async Task SelfSeedBaselineAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            WriteConfig(FullConfigB(chest: false, speed: SeedSpeed));
            await WaitForConfigAsync(
                c => !c.Carryables.Chest && c.CarryOptions.InteractSpeedMultiplier == SeedSpeed
                    && c.DebuggingOptions.ReconditionOnConfigChange,
                "Mode B baseline (Chest=false, speed=2.0, flag on)");
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
                    + $"InteractSpeedMultiplier={Mod.Config.CarryOptions.InteractSpeedMultiplier}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>
        /// Joins a player, lets the Mode B join-time snapshot send land, then clears packet
        /// captures - so a later "no broadcast" assertion cannot be polluted by the join
        /// send arriving after the Clear.
        /// </summary>
        private async Task<ITestPlayer> JoinAndClearAsync(string name)
        {
            ITestPlayer player = await JoinPlayer(name);
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await World.Ticks(SettleTicks); // join send must land BEFORE the Clear
            player.Client.Clear();
            return player;
        }

        /// <summary>The chest-east carryable entry of the latest captured snapshot, if any.</summary>
        private CarryabilityStateMessage.CarryabilityEntry? LatestChestEntry(ITestPlayer player)
        {
            var messages = player.Client.Packets<CarryabilityStateMessage>(Channel);
            if (messages.Count == 0) return null;
            var latest = messages[messages.Count - 1];
            return latest.Entries?.FirstOrDefault(e =>
                e.Kind == BlockBehaviorCarryable.Name && e.BlockCode == "game:chest-east");
        }

        /// <summary>
        /// R1 - a dirty reload (Chest=true) re-runs conditioning and the change is live end
        /// to end: the chest block regains its carryable behavior and a player can pick it
        /// up without a restart. This row is also the functional proof the fixture landed -
        /// a missed flag degrades to Mode A and the pickup fails.
        /// </summary>
        [Trait("summary", "A dirty Chest flip re-runs conditioning live: the chest block regains its carryable behavior and a player picks it up without a restart.")]
        [Trait("pins", "The Mode B dirty gate applies the changed behavior surface end to end.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Recondition_DirtyChestFlip_RegainsBehavior_EndToEnd()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig(FullConfigB(chest: true, speed: SeedSpeed));
            await WaitForConfigAsync(c => c.Carryables.Chest, "config flips Carryables.Chest to true");
            await World.Ticks(SettleTicks); // recondition + broadcast complete inside the reload

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            var chestBlock = World.Api.World.BlockAccessor.GetBlock(pos);
            Assert.NotNull(chestBlock.GetBehavior<BlockBehaviorCarryable>());

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);
            Assert.True(carried != null, $"Chest pickup after Mode B recondition failed: {failureCode}");
            Assert.Equal("game:chest-east", carried?.Block?.Code?.ToString());
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// R2 - a dirty reload broadcasts a <see cref="CarryabilityStateMessage"/> to
        /// connected clients, and the snapshot reflects the post-recondition state: the
        /// chest entry carries non-empty properties JSON (the boot snapshot had it
        /// stripped, i.e. null).
        /// </summary>
        [Trait("summary", "A dirty reload broadcasts a carryability snapshot whose chest entry carries post-condition properties JSON rather than the stripped boot value.")]
        [Trait("pins", "The post-recondition state, not the boot-era one, is what connected clients receive.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DirtyGate_ChestFlip_BroadcastsSnapshotWithProperties()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            ITestPlayer player = await JoinAndClearAsync("Tester");

            WriteConfig(FullConfigB(chest: true, speed: SeedSpeed));
            await WaitForConfigAsync(c => c.Carryables.Chest, "config flips Carryables.Chest to true");
            await World.Ticks(SettleTicks);

            Assert.NotEmpty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            var chest = LatestChestEntry(player);
            Assert.NotNull(chest);
            Assert.False(string.IsNullOrEmpty(chest!.PropertiesJson),
                "chest carryable entry should carry post-condition properties after the recondition");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// R3 - a speed-only reload is clean: the config applies (and syncs via
        /// <c>ConfigSyncMessage</c>) but the behavior surface is untouched, so no
        /// <see cref="CarryabilityStateMessage"/> is broadcast.
        /// </summary>
        [Trait("summary", "A speed-only reload applies and syncs via ConfigSyncMessage but touches no behavior surface, so no carryability snapshot is sent.")]
        [Trait("pins", "The dirty gate suppresses a reload whose behavior surface did not change.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DirtyGate_SpeedOnly_NoBroadcast()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            ITestPlayer player = await JoinAndClearAsync("Tester");

            WriteConfig(FullConfigB(chest: false, speed: 0.5f));
            await WaitForConfigAsync(c => c.CarryOptions.InteractSpeedMultiplier == 0.5f, "speed-only flip applied");
            await World.Ticks(SettleTicks);

            Assert.NotEmpty(player.Client.Packets<ConfigSyncMessage>(Channel));
            Assert.Empty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// R4 - flipping <c>CarryOptions.AllowHighCapacityStorageOnBack</c> (a
        /// behavior-relevant CarryOptions key) dirties the gate and broadcasts.
        /// </summary>
        [Trait("summary", "Flipping the behavior-relevant CarryOptions.AllowHighCapacityStorageOnBack key dirties the gate and broadcasts a snapshot.")]
        [Trait("pins", "A behavior-relevant CarryOptions key counts as a surface change.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DirtyGate_AllowHighCapacityOnBack_Broadcasts()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            ITestPlayer player = await JoinAndClearAsync("Tester");

            WriteConfig(FullConfigB(chest: false, speed: SeedSpeed, allowHighCapacityStorageOnBack: true));
            await WaitForConfigAsync(c => c.CarryOptions.AllowHighCapacityStorageOnBack, "AllowHighCapacityStorageOnBack flips to true");
            await World.Ticks(SettleTicks);

            Assert.NotEmpty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// R5 - flipping a <c>CarryablesFilters</c> key (AutoMapSimilar) dirties the gate
        /// and broadcasts, even though no Carryables entry changed - and the surface must
        /// converge with it: with automapping off, the boot-era clone on the unpatched
        /// flowerpot family member must be gone from both arrays (this row extends
        /// the convergence contract; see the
        /// assertion below for the C5a divergence it pins).
        /// </summary>
        [Trait("summary", "Flipping a CarryablesFilters key dirties the gate and broadcasts even though no Carryables entry changed, and the boot-era automap clone is gone from both arrays.")]
        [Trait("pins", "A CarryablesFilters change is behavior-relevant and the surface converges to the fresh-boot state.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DirtyGate_CarryablesFiltersOnly_Broadcasts()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            ITestPlayer player = await JoinAndClearAsync("Tester");

            WriteConfig(FullConfigB(chest: false, speed: SeedSpeed, autoMapSimilar: false));
            await WaitForConfigAsync(c => !c.CarryablesFilters.AutoMapSimilar, "AutoMapSimilar flips to false");
            await World.Ticks(SettleTicks);

            Assert.NotEmpty(player.Client.Packets<CarryabilityStateMessage>(Channel));

            // The surface half of the row: with AutoMapSimilar=false a fresh boot with this config would never
            // clone the unpatched flowerpot family member, so the clone the boot run made
            // must be gone from both arrays - not merely un-refreshed while still mounted.
            var cloneBlock = World.Api.World.GetBlock(new AssetLocation("game:flowerpot-amber"));
            Assert.True(
                cloneBlock != null,
                "Row premise: the registry must hold 'game:flowerpot-amber'");
            CarryOnParityAssert.AssertAbsentFromBoth(
                cloneBlock!,
                "AutoMapSimilar=false must converge the surface to the fresh-boot state - "
                + "the boot-era automap clone on 'game:flowerpot-amber' must be gone from "
                + "both arrays, because no run with this config would have made it (C5a: "
                + "the automapper only ever adds)");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// R6 - rewriting the identical file triggers a reload (watcher Changed fires on
        /// LastWrite, not content) but the surface is clean: no recondition, no broadcast,
        /// and no writeback - the file stays byte-for-byte what was written.
        /// </summary>
        [Trait("summary", "Rewriting the identical file fires the watcher but leaves the surface clean: no recondition, no broadcast, and the file stays byte-for-byte.")]
        [Trait("pins", "A no-op reload neither reconditions nor writes back.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task NoOpReload_NoBroadcast_FileUntouched()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            ITestPlayer player = await JoinAndClearAsync("Tester");

            var baseline = FullConfigB(chest: false, speed: SeedSpeed);
            WriteConfig(baseline); // identical bytes - still bumps LastWrite and fires the watcher
            await World.Ticks(SettleTicks); // debounce + reload + (absent) broadcast window

            Assert.Empty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            Assert.Equal(Encoding.UTF8.GetBytes(baseline), ReadConfigBytes());
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// R7 - a player joining after a recondition receives the CURRENT snapshot (not the
        /// boot-era one): the chest entry carries non-empty properties, which the boot
        /// snapshot (chest stripped) never had.
        /// </summary>
        [Trait("summary", "A player joining after a recondition receives the current snapshot, whose chest entry carries post-condition properties, not the stripped boot state.")]
        [Trait("pins", "The join-time snapshot reflects the latest recondition.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PlayerJoin_AfterRecondition_ReceivesCurrentSnapshot()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig(FullConfigB(chest: true, speed: SeedSpeed));
            await WaitForConfigAsync(c => c.Carryables.Chest, "config flips Carryables.Chest to true");
            await World.Ticks(SettleTicks); // recondition + snapshot cache refresh complete

            ITestPlayer joiner = await JoinPlayer("LateJoiner");
            await joiner.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await World.Ticks(SettleTicks); // join send lands and is captured

            Assert.NotEmpty(joiner.Client.Packets<CarryabilityStateMessage>(Channel));
            var chest = LatestChestEntry(joiner);
            Assert.NotNull(chest);
            Assert.False(string.IsNullOrEmpty(chest!.PropertiesJson),
                "joiner snapshot should reflect the post-recondition carryable chest, not the stripped boot state");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// R8 - the gate resets after a recondition: a dirty write broadcasts, then a
        /// follow-up write that only changes a non-behavior scalar (same surface) does not.
        /// </summary>
        [Trait("summary", "After a dirty recondition the gate resets: a follow-up same-surface speed-only write broadcasts no snapshot.")]
        [Trait("pins", "The dirty gate re-arms after each recondition.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DirtyGate_ResetsAfterRecondition()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            ITestPlayer player = await JoinAndClearAsync("Tester");

            WriteConfig(FullConfigB(chest: true, speed: SeedSpeed)); // dirty → recondition + broadcast
            await WaitForConfigAsync(c => c.Carryables.Chest, "chest flip applied");
            await World.Ticks(SettleTicks);
            Assert.NotEmpty(player.Client.Packets<CarryabilityStateMessage>(Channel));

            player.Client.Clear();

            WriteConfig(FullConfigB(chest: true, speed: 0.5f)); // same surface, speed only → clean
            await WaitForConfigAsync(c => c.CarryOptions.InteractSpeedMultiplier == 0.5f, "speed-only flip applied");
            await World.Ticks(SettleTicks);
            Assert.Empty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// R9 - the Mode B flag itself: this class boots with
        /// <c>ReconditionOnConfigChange = true</c>. It self-seeds first, so the assertion
        /// holds regardless of row order - R10 and R11 deliberately leave the flag
        /// <i>off</i>, and without the seed this row would depend on whether they ran
        /// before it. R1 is the functional guard that the fixture actually landed.
        /// </summary>
        [Trait("summary", "This class boots with ReconditionOnConfigChange on, and the row self-seeds so the flag-on assertion holds regardless of row order.")]
        [Trait("pins", "Mode B is the boot mode for this class.")]
        [Trait("role", "premise")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ModeB_FlagOn_InBootConfig()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            Assert.True(
                Mod.Config.DebuggingOptions.ReconditionOnConfigChange,
                "ReconditionOnConfigChange must be on for this class (fixture or payload)");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// R10 - RED before the fix. The inverse of G1: this class boots Mode B, so the
        /// session latched live reconditioning <i>on</i>. Turning the flag off in the file
        /// must not disarm that - a behavior-surface edit (Chest false -&gt; true) must
        /// still recondition and still broadcast.
        /// <para>
        /// Before the fix the reload gate read the reloaded file's own flag, so the
        /// <c>false</c> written here closed the gate and nothing reconditioned. The latch is
        /// the documented contract (the flag tooltip says "requires restart"), so the file's
        /// value describes the <i>next</i> session, not this one.
        /// </para>
        /// </summary>
        [Trait("summary", "A Mode B session whose file turns ReconditionOnConfigChange off still reconditions and broadcasts on a behavior-surface edit.")]
        [Trait("pins", "The flag is read once at server start in both directions, so a file's flag-off cannot disarm live reconditioning.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the reload gate read the reloaded file's flag, so the written false closed the gate and nothing reconditioned.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ModeB_FileTurnsFlagOff_BehaviorEdit_StillReconditions()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            ITestPlayer player = await JoinAndClearAsync("Tester");

            WriteConfig(FullConfigB(chest: true, speed: SeedSpeed, reconditionOnConfigChange: false));
            await WaitForConfigAsync(
                c => c.Carryables.Chest && !c.DebuggingOptions.ReconditionOnConfigChange,
                "flag-off + Chest=true payload applied");
            await World.Ticks(SettleTicks);

            // Premise: the payload's flag value really landed on the config object. Without
            // this the row could go green because the flag key was dropped or misspelled -
            // the gate would then have been open for the wrong reason and the row would
            // prove nothing at all.
            Assert.False(
                Mod.Config.DebuggingOptions.ReconditionOnConfigChange,
                "premise: the reloaded file's flag value must be visible on Mod.Config. If this "
                + "is true the payload never flipped it, the gate was never actually closed, and "
                + "this row is not measuring what it claims to measure.");

            Assert.NotEmpty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            var chest = LatestChestEntry(player);
            Assert.NotNull(chest);
            Assert.False(string.IsNullOrEmpty(chest!.PropertiesJson),
                "a Mode B session must still recondition after a mid-session flag-off: the chest "
                + "entry only gains properties when a conditioning pass runs against it");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// R11 - control for R10, green before and after. With the flag off in the file, a
        /// <i>behaviour-free</i> reload in a Mode B session still sends nothing, because the
        /// dirty gate - not the mode - is what suppresses it. This is the guard that R10 is
        /// not passing by accident: if the latch were implemented as "always recondition",
        /// R10 would be green and this row would be red.
        /// </summary>
        [Trait("summary", "With the file flag off, a behavior-free reload in a Mode B session applies and syncs but sends no carryability snapshot.")]
        [Trait("pins", "The dirty gate, not the mode, is what suppresses a behavior-free reload's snapshot.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ModeB_FileTurnsFlagOff_BehaviorFreeReload_NoBroadcast()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            ITestPlayer player = await JoinAndClearAsync("Tester");

            WriteConfig(FullConfigB(chest: false, speed: 0.5f, reconditionOnConfigChange: false));
            await WaitForConfigAsync(
                c => c.CarryOptions.InteractSpeedMultiplier == 0.5f
                    && !c.DebuggingOptions.ReconditionOnConfigChange,
                "flag-off speed-only flip applied");
            await World.Ticks(SettleTicks);

            Assert.NotEmpty(player.Client.Packets<ConfigSyncMessage>(Channel));
            Assert.Empty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            Assert.NotNull(World.Api);
        }
    }
}
