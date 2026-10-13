using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.API.Event.Data;
using CarryOn.Common.Models;
using CarryOn.Server.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Event handlers whose subscriptions are decided once at boot from a config flag do not
    /// pick up a later live reload that turns the flag on. The boot fixture starts with
    /// dropped-block tracking off; the row turns it on through the config watcher and then
    /// drops a carried block, which must be recorded once tracking is enabled.
    /// </summary>
    [Trait("area", "Config reload")]
    [Trait("summary", "A config flag enabled by a live reload must take effect for handlers that subscribe from the flag, not only for the in-memory config value: a block dropped after enabling dropped-block tracking is recorded.")]
    [AtlasDataFiles("fixtures/EventSubscriptionReloadConfig", TargetPath = "ModConfig")]
    public class EventSubscriptionReloadScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch

        private List<BlockDroppedEventArgs>? activeDrops;
        private List<BlockDroppedEventArgs> Drops => activeDrops ??= new List<BlockDroppedEventArgs>();

        private void OnBlockDropped(object? sender, BlockDroppedEventArgs e) => Drops.Add(e);

        private IDisposable SubscribeBlockDropped()
        {
            Drops.Clear();
            Carry.CarryEvents.BlockDropped += OnBlockDropped;
            return new UnsubscribeAction(() => Carry.CarryEvents.BlockDropped -= OnBlockDropped);
        }

        private sealed class UnsubscribeAction(Action dispose) : IDisposable
        {
            private readonly Action dispose = dispose;

            public void Dispose() => dispose();
        }

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        private void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

        private static string TrackingConfig(bool trackDroppedBlocks) => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": true
              },
              "CarryOptions": {
                "TrackDroppedBlocks": {{trackDroppedBlocks.ToString().ToLowerInvariant()}}
              }
            }
            """;

        /// <summary>Deletes any dropped-block records left behind so the row starts clean.</summary>
        private void ClearTrackedDrops()
        {
            string dir = World.Api.GetOrCreateDataPath(
                Path.Combine("ModData", World.Api.World.SavegameIdentifier, ModId));
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "dropped-*"))
            {
                File.Delete(file);
            }
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
                    + $"Now: TrackDroppedBlocks={Mod.Config.CarryOptions.TrackDroppedBlocks}.");
            }
        }

        /// <summary>Picks the chest at <paramref name="source"/>, drops it, and returns the placed position.</summary>
        private async Task<BlockPos> PickUpAndDropChestAsync(ITestPlayer player, BlockPos source)
        {
            World.SetBlock("game:chest-east", source);
            var carried = Actions.PickUpIntoHands(player.Entity, source, out var pickupCode);
            Assert.True(carried != null, $"Chest pickup failed: {pickupCode}");

            using (SubscribeBlockDropped())
            {
                Actions.Drop(player.Entity, CarrySlot.Hands);
            }

            Assert.Single(Drops);
            Assert.True(Drops[0].BlockPlaced, "the drop must place the block to be a tracking candidate");
            await Task.CompletedTask;
            return Drops[0].Position!;
        }

        /// <summary>
        /// Turning dropped-block tracking on through a live reload must make the next dropped
        /// block recorded. Before the fix, the tracker only subscribed at boot, so the
        /// post-reload drop left no record even though the in-memory config flag was true.
        /// </summary>
        [Trait("summary", "Enabling dropped-block tracking via a live config reload records the next block dropped, not just the in-memory flag.")]
        [Trait("pins", "A handler that subscribes from a config flag at boot responds to a later reload that enables the flag.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, where the tracker only subscribed at boot and the post-reload drop left no dropped-block record.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DroppedBlockTracking_EnabledByReload_TracksSubsequentDrop()
        {
            ClearTrackedDrops();

            ITestPlayer player = await JoinPlayer("Tester");

            // Premise/control: the boot fixture has tracking off, so the first dropped block
            // is not recorded. This holds with or without the fix.
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            BlockPos firstDropPos = await PickUpAndDropChestAsync(player, World.Spawn.Offset(2, 1, 0));
            Assert.Null(DroppedBlockInfo.Get(firstDropPos, player.Player));

            // Enable tracking through the live watcher and wait for the in-memory config to flip.
            WriteConfig(TrackingConfig(trackDroppedBlocks: true));
            await WaitForConfigAsync(c => c.CarryOptions.TrackDroppedBlocks, "TrackDroppedBlocks enabled by reload");
            await World.Ticks(SettleTicks);

            // A block dropped now that tracking is on must be recorded.
            await player.TeleportTo(World.Spawn.Offset(8, 0, 8));
            BlockPos secondDropPos = await PickUpAndDropChestAsync(player, World.Spawn.Offset(8, 1, 8));

            try
            {
                Assert.NotNull(DroppedBlockInfo.Get(secondDropPos, player.Player));
            }
            finally
            {
                DroppedBlockInfo.Remove(secondDropPos, World.Api);
            }
        }
    }
}
