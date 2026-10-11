using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Mode B rows for the CarryablesOnBack toggle. The barrel's Back slot is
    /// slot-level condition gated
    /// (<c>slots.Back.enabledCondition: carryon.CarryablesOnBack.Barrel</c>) while its
    /// hands slot follows the block-level condition, so an onBack toggle is the exact
    /// user-reported repro: a dirty reload with the flag false strips the Back slot
    /// live, and - until the restore fix - re-enabling never brings it back
    /// (destructive one-way slot strip + capture overwrite + restore-only-when-absent).
    ///
    /// Expected state before the fix: N2/N3/N6 RED (restore direction), N1/N4 GREEN
    /// (disable direction, gate coverage, gate reset). Like the other config classes,
    /// every row self-seeds the full baseline first - the class shares one server boot,
    /// and the baseline write normalizes the applied behavior signature back to
    /// onBack=true so rows are order-independent.
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Mode B recondition rows for the CarryablesOnBack toggle: disabling strips the Back slot live, and re-enabling must restore it.")]
    [AtlasDataFiles("fixtures/OnBackBarrelOnConfig", TargetPath = "ModConfig")]
    public class CarryablesOnBackScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;          // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;                // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        private void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

        /// <summary>
        /// Full-file Mode B payload: keeps the recondition flag on, pins the block-level
        /// barrel flag (hands) and every other signature-relevant key to the fixture
        /// defaults, so only the measured <c>CarryablesOnBack.Barrel</c> key moves the
        /// behavior surface.
        /// </summary>
        private static string FullConfigC(bool barrelOnBack, float speed = SeedSpeed)
            => $$"""
            {
              "ConfigVersion": {{CarryCodes.CurrentConfigVersion}},
              "Carryables": {
                "Barrel": true
              },
              "CarryablesOnBack": {
                "Barrel": {{barrelOnBack.ToString().ToLowerInvariant()}}
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{speed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}}
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": true
              }
            }
            """;

        private async Task SelfSeedBaselineAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            WriteConfig(FullConfigC(barrelOnBack: true));
            await WaitForConfigAsync(
                c => c.Carryables.Barrel && c.CarryablesOnBack.Barrel
                    && c.CarryOptions.InteractSpeedMultiplier == SeedSpeed
                    && c.DebuggingOptions.ReconditionOnConfigChange,
                "Mode B baseline (Barrel carryable, onBack=true, speed=2.0, flag on)");
            await World.Ticks(20); // > 500 ms debounce - next write's event is not swallowed
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
                    + $"Now: Carryables.Barrel={Mod.Config.Carryables.Barrel}, "
                    + $"CarryablesOnBack.Barrel={Mod.Config.CarryablesOnBack.Barrel}, "
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

        /// <summary>The base barrel block the carryable patch targets (game:barrel).</summary>
        private Block ResolveBarrel()
        {
            var barrel = World.Api.World.GetBlock(new AssetLocation("barrel"));
            Assert.NotNull(barrel);
            Assert.NotNull(barrel!.Code);
            return barrel;
        }

        private string BarrelCode => ResolveBarrel().Code!.ToString();

        private BlockBehaviorCarryable? BarrelBehavior() => ResolveBarrel().GetBehavior<BlockBehaviorCarryable>();

        /// <summary>
        /// N1 - the disable direction works: a dirty reload with
        /// <c>CarryablesOnBack.Barrel=false</c> strips the Back slot from the live
        /// behavior, back pickup rejects with <see cref="FailureCodes.NotCarryable"/>,
        /// and the hands slot is untouched (block-level condition still true).
        /// Expected GREEN today.
        /// </summary>
        [Trait("summary", "Disabling CarryablesOnBack.Barrel on a dirty reload strips the Back slot from the live behavior; back pickup then answers not-carryable and the hands slot survives.")]
        [Trait("pins", "The disable direction of the onBack toggle is effective, and a slot-level condition does not remove the block-level hands slot.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Recondition_OnBackDisable_StripsBackSlot()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig(FullConfigC(barrelOnBack: false));
            await WaitForConfigAsync(c => !c.CarryablesOnBack.Barrel, "CarryablesOnBack.Barrel flips to false");
            await World.Ticks(SettleTicks);

            var behavior = BarrelBehavior();
            Assert.NotNull(behavior);
            Assert.True(behavior!.Slots[CarrySlot.Hands] != null, "hands slot must survive an onBack toggle");
            Assert.Null(behavior.Slots[CarrySlot.Back]);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(BarrelCode, pos);
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var backCarried = Actions.PickUpIntoSlot(player.Entity, pos, CarrySlot.Back, out var backCode);
            Assert.Null(backCarried);
            Assert.Equal(FailureCodes.NotCarryable, backCode);

            var handsCarried = Actions.PickUpIntoHands(player.Entity, pos, out var handsCode);
            Assert.True(handsCarried != null, $"Hands pickup failed after onBack disable: {handsCode}");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// N2 - the user's repro: after a disable strip, re-enabling
        /// <c>CarryablesOnBack.Barrel</c> must RESTORE the Back slot (live Slots and
        /// end-to-end pickup into the back slot). Red-first against the pre-fix head,
        /// where restore only fired when the whole behavior was absent and the
        /// pristine capture was overwritten by the re-enable pass (plan D1-D3);
        /// green since the slot strip marks the baseline entry dirty, so pass 0
        /// re-mounts the behavior with the slot still in its pristine JSON.
        /// </summary>
        [Trait("summary", "After a disable strip, re-enabling CarryablesOnBack.Barrel restores the Back slot and back pickup succeeds; the user-reported repro.")]
        [Trait("pins", "Re-enable restores the stripped slot: the slot strip marks the baseline entry dirty, so pass 0 re-mounts the behavior from its pristine JSON.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed against the pre-fix restore-only-when-absent behaviour.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Recondition_OnBackReenable_RestoresBackSlot()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            WriteConfig(FullConfigC(barrelOnBack: false));
            await WaitForConfigAsync(c => !c.CarryablesOnBack.Barrel, "disable applied");
            await World.Ticks(SettleTicks);

            WriteConfig(FullConfigC(barrelOnBack: true));
            await WaitForConfigAsync(c => c.CarryablesOnBack.Barrel, "re-enable applied");
            await World.Ticks(SettleTicks);

            var behavior = BarrelBehavior();
            Assert.NotNull(behavior);
            Assert.True(behavior!.Slots[CarrySlot.Back] != null,
                "Back slot was not restored after re-enabling CarryablesOnBack.Barrel");

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(BarrelCode, pos);
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var backCarried = Actions.PickUpIntoSlot(player.Entity, pos, CarrySlot.Back, out var backCode);
            Assert.True(backCarried != null, $"Back pickup failed after re-enable: {backCode}");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// N3 - gate and snapshot coverage: the onBack flip dirties the behavior surface
        /// (a <see cref="CarryabilityStateMessage"/> broadcasts - GREEN today), and after
        /// re-enabling, the latest snapshot's barrel entry must carry the Back slot in
        /// its properties JSON. Expected RED today - the snapshot mirrors the
        /// destructively stripped behavior, so clients re-mount the slot-less state.
        /// </summary>
        [Trait("summary", "An onBack flip dirties the behavior surface so a carryability snapshot broadcasts, and after re-enable the latest snapshot's barrel entry carries the Back slot.")]
        [Trait("pins", "The delivered snapshot reflects a restored Back slot; a destructively stripped behavior must not be mirrored to clients.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the snapshot mirrored the stripped behavior instead of the restored one.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task OnBackFlip_BroadcastsSnapshotWithBackSlot()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            ITestPlayer player = await JoinAndClearAsync("Tester");

            WriteConfig(FullConfigC(barrelOnBack: false));
            await WaitForConfigAsync(c => !c.CarryablesOnBack.Barrel, "disable applied");
            await World.Ticks(SettleTicks);
            Assert.NotEmpty(player.Client.Packets<CarryabilityStateMessage>(Channel));

            player.Client.Clear();

            WriteConfig(FullConfigC(barrelOnBack: true));
            await WaitForConfigAsync(c => c.CarryablesOnBack.Barrel, "re-enable applied");
            await World.Ticks(SettleTicks);

            var messages = player.Client.Packets<CarryabilityStateMessage>(Channel);
            Assert.NotEmpty(messages);
            var latest = messages[messages.Count - 1];
            var entry = latest.Entries?.FirstOrDefault(e =>
                e.Kind == BlockBehaviorCarryable.Name && e.BlockCode == BarrelCode);
            Assert.NotNull(entry);
            Assert.False(string.IsNullOrEmpty(entry!.PropertiesJson),
                "barrel carryable entry should carry properties after the recondition");

            var slots = JObject.Parse(entry.PropertiesJson!)["slots"] as JObject;
            Assert.NotNull(slots);
            Assert.True(slots!["Back"] != null,
                "snapshot properties lost the Back slot - clients re-mount the stripped state");
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// N4 - gate reset: after disable + re-enable (two dirty broadcasts), a
        /// speed-only write with the re-enabled surface broadcasts nothing.
        /// Expected GREEN today.
        /// </summary>
        [Trait("summary", "After disable then re-enable (two dirty broadcasts), a speed-only write with the re-enabled surface broadcasts no carryability change.")]
        [Trait("pins", "The recondition gate resets after a recondition, so unrelated writes do not re-broadcast.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task OnBackGate_ResetsAfterRecondition()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            ITestPlayer player = await JoinAndClearAsync("Tester");

            WriteConfig(FullConfigC(barrelOnBack: false));
            await WaitForConfigAsync(c => !c.CarryablesOnBack.Barrel, "disable applied");
            await World.Ticks(SettleTicks);

            WriteConfig(FullConfigC(barrelOnBack: true));
            await WaitForConfigAsync(c => c.CarryablesOnBack.Barrel, "re-enable applied");
            await World.Ticks(SettleTicks);

            player.Client.Clear();

            WriteConfig(FullConfigC(barrelOnBack: true, speed: 0.5f));
            await WaitForConfigAsync(c => c.CarryOptions.InteractSpeedMultiplier == 0.5f, "speed-only flip applied");
            await World.Ticks(SettleTicks);

            Assert.Empty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            Assert.NotEmpty(player.Client.Packets<ConfigSyncMessage>(Channel));
            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// N6 - the in-game autoconfig path: the editor's save converges on the mod
        /// persist (<see cref="CarryOnConfigService.NotifyChanged"/> → StoreModConfig →
        /// watcher reload → Mode B recondition) without the test touching the file
        /// directly. Disabling strips (GREEN today); re-enabling must restore
        /// (RED today) - the exact user-reported flow.
        /// </summary>
        [Trait("summary", "The in-game autoconfig path converges on the mod persist without the test touching the file directly: disabling strips the Back slot, re-enabling must restore it.")]
        [Trait("pins", "The editor save to persist to watcher-reload to recondition path restores the Back slot; the exact user-reported flow.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "The re-enable half failed against the pre-fix head.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task EditorPersistPath_OnBackToggle()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();
            ITestPlayer player = await JoinAndClearAsync("Tester");

            // Disable: in-memory edit + persist → file → watcher reload → recondition.
            Mod.Config.CarryablesOnBack.Barrel = false;
            Mod.ConfigService.NotifyChanged();
            try
            {
                await World.Until(() => BarrelBehavior()?.Slots[CarrySlot.Back] == null, WaitTimeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail("Editor-persist disable never stripped the back slot");
            }
            Assert.NotEmpty(player.Client.Packets<CarryabilityStateMessage>(Channel));

            // Re-enable through the same path.
            Mod.Config.CarryablesOnBack.Barrel = true;
            Mod.ConfigService.NotifyChanged();
            await World.Ticks(SettleTicks); // persist write → 500 ms debounce → reload → recondition

            var behavior = BarrelBehavior();
            Assert.NotNull(behavior);
            Assert.True(behavior!.Slots[CarrySlot.Back] != null,
                "Back slot was not restored through the editor-persist path");
            Assert.NotNull(World.Api);
        }
    }
}
