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
    /// Premise/control rows for the recondition render-cache staleness. <b>All three rows are
    /// green today and none of them covers the defect itself</b> - the render caches
    /// are client-only and this harness is a headless dedicated server, so a red-first
    /// behavioral row cannot exist. They pin the *input* to the staleness, the carry
    /// state the fix must not touch, and the harness fact that makes a client-side row
    /// impossible. The behavioral half of H3 is pinned by the wiring test
    /// (<c>CarryabilitySyncWiringTests</c>) plus the manual real-client gate recorded in
    /// the plan; G3 says so in-band.
    ///
    /// The premise is shipped vanilla content, not an invented block. <c>game:woodbarrel</c>
    /// carries one <c>carryon:carryable</c> with <c>transformTemplates: ["carry-barrel"]</c>
    /// and a <b>slot-level</b> condition -
    /// <c>slots.Back.enabledCondition = "carryon.CarryablesOnBack.Barrel"</c>
    /// (<c>resources/assets/carryon/patches/carryable/barrel.json</c>). Flipping
    /// <c>CarryablesOnBack.Barrel</c> therefore strips the Back slot from a behavior that
    /// <b>stays mounted and stays carryable</b> (unlike a block-level flip, which removes
    /// the whole behavior and is already handled live: a null carry behavior takes the
    /// "nocarry" branch and never consults a cache). That is the shape H3 needs - a
    /// present behavior whose render-relevant properties change under a Mode-B reload.
    ///
    /// It is render-relevant because <c>TransformGroupExists</c> returns false outright
    /// for a null <c>behavior.Slots[carried.Slot]</c>
    /// (<c>TransformTemplateManager.cs</c>), and that predicate is what
    /// <c>ResolvePrimaryGroupFromCandidates</c> uses to pick a plan's primary group
    /// (<c>CarryTransformPlanBuilder.cs</c>). The client-side plan cache key
    /// (<c>CarrySignatureBuilder.BuildTransformPlanSignature</c>) contains none of the
    /// seven values a slot strip changes, and the per-slot sidecar memo does not consult
    /// the plan cache at all when it is not dirty - so a barrel carried in the Back slot
    /// keeps its pre-flip pose. Back is the only slot where the change is render-relevant,
    /// which is why the manual gate uses it and G2 (the carry-state control) does not.
    /// </summary>
    [Trait("area", "Render cache")]
    [Trait("summary", "Render-cache premise and control rows: a slot-level CarryablesOnBack.Barrel flip changes a present carryable's render-relevant properties and reaches the client while carry state is untouched, but the client-only render caches cannot be exercised on a headless server.")]
    [AtlasDataFiles("fixtures/ReconditionRenderCacheConfig", TargetPath = "ModConfig")]
    public class ReconditionRenderCacheScenarios : CarryOnScenarioBase
    {
        private const string BarrelCode = "game:barrel";

        private const int WaitTimeoutTicks = 150; // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;       // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        private void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

        /// <summary>
        /// Full-file Mode B payload every row writes: the recondition flag on (the dirty
        /// gate reads it from the reloaded file), every signature-relevant key pinned to
        /// the fixture's boot values, and only <c>CarryablesOnBack.Barrel</c> moving.
        /// <c>Carryables.Barrel</c> is always written true on purpose - flipping it would
        /// remove the whole behavior (presence/absence), which is a different, already-covered
        /// case and would silently degrade G1 into it.
        /// </summary>
        private static string FullConfigB(bool barrelOnBack)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Barrel": true
              },
              "CarryablesOnBack": {
                "Barrel": {{barrelOnBack.ToString().ToLowerInvariant()}}
              },
              "CarryablesFilters": {
                "AutoMapSimilar": true
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}},
                "AllowHighCapacityStorageOnBack": false
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": true
              }
            }
            """;

        private async Task SelfSeedBaselineAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            WriteConfig(FullConfigB(barrelOnBack: true));
            await WaitForConfigAsync(
                c => c.Carryables.Barrel && c.CarryablesOnBack.Barrel
                    && c.DebuggingOptions.ReconditionOnConfigChange,
                "Mode B baseline (Carryables.Barrel=true, CarryablesOnBack.Barrel=true, flag on)");
            await World.Ticks(20); // > 500 ms debounce - next write's event is not swallowed
        }

        /// <summary>Polls <see cref="Mod.Config"/> once per tick until <paramref name="predicate"/> holds.</summary>
        private async Task WaitForConfigAsync(System.Func<CarryOn.Common.Models.CarryOnConfig, bool> predicate, string because, int timeoutTicks = WaitTimeoutTicks)
        {
            try
            {
                await World.Until(() => predicate(Mod.Config), timeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    $"Timed out after {timeoutTicks} ticks waiting for config state - {because}. "
                    + $"Now: Barrel={Mod.Config.Carryables.Barrel}, "
                    + $"BarrelOnBack={Mod.Config.CarryablesOnBack.Barrel}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>
        /// Joins a player, lets the Mode B join-time snapshot send land, then clears packet
        /// captures - so the pre-flip and post-flip snapshots are not confused with it.
        /// </summary>
        private async Task<ITestPlayer> JoinAndClearAsync(string name)
        {
            ITestPlayer player = await JoinPlayer(name);
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await World.Ticks(SettleTicks); // join send must land BEFORE the Clear
            player.Client.Clear();
            return player;
        }

        /// <summary>The barrel carryable entry of the latest captured snapshot, if any.</summary>
        private CarryabilityStateMessage.CarryabilityEntry? LatestBarrelEntry(ITestPlayer player)
        {
            var messages = player.Client.Packets<CarryabilityStateMessage>(Channel);
            if (messages.Count == 0) return null;
            var latest = messages[messages.Count - 1];
            return latest.Entries?.FirstOrDefault(e =>
                e.Kind == BlockBehaviorCarryable.Name && e.BlockCode == BarrelCode);
        }

        /// <summary>
        /// Whether a properties JSON still declares the Back slot. Asserted as key
        /// presence rather than against a formatted string so the row survives a JSON
        /// layout change. A null/empty JSON means the behavior is absent entirely, which
        /// is reported as "no Back slot" - callers assert presence of the behavior
        /// separately, so the two cases never hide each other.
        /// </summary>
        private static bool HasBackSlot(string? propertiesJson)
        {
            if (string.IsNullOrWhiteSpace(propertiesJson)) return false;
            var slots = JObject.Parse(propertiesJson!)["slots"] as JObject;
            return slots != null && slots["Back"] != null;
        }

        private BlockBehaviorCarryable? CarryableAt(BlockPos pos)
            => World.BlockAt(pos)?.GetBehavior<BlockBehaviorCarryable>();

        /// <summary>
        /// Asserts non-null and returns the value. xUnit 2's <c>Assert.NotNull</c> takes no
        /// message, and every guard in this class is a premise guard whose failure has to
        /// explain itself, so the explanation is threaded through here instead.
        /// </summary>
        private static T Require<T>(T? value, string because) where T : class
        {
            Assert.True(value != null, because);
            return value!;
        }

        /// <summary>
        /// G1 (premise, green) - the <b>input</b> to H3. A Mode-B reload that flips
        /// <c>CarryablesOnBack.Barrel</c> to false really does change a render-relevant
        /// property of a <b>still-present</b> carryable, and the change reaches the client:
        /// the server's mounted behavior loses <c>Slots[Back]</c>, and the broadcast
        /// snapshot's <c>PropertiesJson</c> for the same code loses the <c>slots.Back</c> key
        /// while keeping <c>slots.Hands</c>. Read off the wire (the packet the client
        /// receives), before and after, so the row is about the snapshot rather than about
        /// the server's own state.
        ///
        /// Why this is the premise H3 needs: it fails if a future change stops the
        /// snapshot from carrying post-condition properties, and it is what makes a stale
        /// client plan observable at all.
        /// </summary>
        [Trait("summary", "A live CarryablesOnBack.Barrel=false flip strips Slots[Back] from a still-present carryable and removes slots.Back from the broadcast snapshot while keeping slots.Hands.")]
        [Trait("pins", "The render-relevant input to the stale-plan defect: a present behavior whose slot properties change under a Mode B reload.")]
        [Trait("role", "premise")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Reload_StripsTheManagedBlocksRenderRelevantSlot()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(BarrelCode, pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await World.Ticks(SettleTicks); // join-time snapshot lands and is captured

            // Boot guards - a missed fixture, a wrong block code or a barrel without the
            // slot-level condition would each degrade this row into asserting nothing.
            var bootEntry = Require(
                LatestBarrelEntry(player),
                "Row premise: the join-time snapshot must carry a carryable entry for the barrel");
            var bootBehavior = Require(
                CarryableAt(pos),
                $"Row premise: {BarrelCode} must be carryable at boot (Carryables.Barrel = true)");
            Assert.True(
                bootBehavior.Slots[CarrySlot.Back] != null,
                "Row premise: the fixture boots with CarryablesOnBack.Barrel = true, so the Back slot must exist");
            Assert.True(
                HasBackSlot(bootEntry.PropertiesJson),
                "Row premise: the boot snapshot must declare the Back slot");

            player.Client.Clear(); // so the post-flip snapshot is unambiguous

            WriteConfig(FullConfigB(barrelOnBack: false));
            await WaitForConfigAsync(
                c => c.Carryables.Barrel && !c.CarryablesOnBack.Barrel,
                "config flips CarryablesOnBack.Barrel to false");
            await World.Ticks(SettleTicks); // recondition + broadcast complete inside the reload

            // The behavior survives - this is a property change on a present behavior, not
            // a presence flip (which the render path already handles live).
            var reconditioned = Require(
                CarryableAt(pos),
                "the barrel must stay carryable in Hands: only the Back slot was conditioned away, "
                + "so a null behavior here means the row is testing presence/absence instead");
            Assert.True(
                reconditioned.Slots[CarrySlot.Back] == null,
                "the condition-disabled Back slot must be stripped from the mounted behavior");
            Assert.True(
                reconditioned.Slots[CarrySlot.Hands] != null,
                "the Hands slot is untouched by the flip and must survive it");

            // And the client is told about it.
            Assert.NotEmpty(player.Client.Packets<CarryabilityStateMessage>(Channel));
            var after = Require(
                LatestBarrelEntry(player),
                "the recondition must broadcast a snapshot the client can read");
            Assert.False(
                HasBackSlot(after.PropertiesJson),
                "the broadcast snapshot must not declare the condition-disabled Back slot - "
                + "otherwise the client keeps resolving a transform group for a slot its own "
                + "behavior no longer has, and H3's premise would be unfalsifiable");
            Assert.True(
                HasBackSlot(bootEntry.PropertiesJson) && !HasBackSlot(after.PropertiesJson),
                "the snapshot must flip the Back slot across the reload (before/after guard)");

            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// G2 (control, green) - the reload must not mutate carry state to force a
        /// recompute. A barrel in the <b>hands</b> slot (unaffected by the flip) crosses a
        /// <c>CarryablesOnBack.Barrel</c> strip with its carried revision unchanged, no
        /// drop, and the block still in hands afterwards.
        ///
        /// Hands deliberately, not Back: the Back slot is the render-relevant one, and what
        /// should happen to a block already carried in a slot that conditioning just removed
        /// is a separate design question that belongs to the manual gate, not to a control
        /// row. This row's only job is to forbid a "fix" that clears carried state to force
        /// a plan rebuild - a barrel lost or re-picked here is a regression the fix would
        /// have introduced.
        /// </summary>
        [Trait("summary", "A barrel carried in the hands slot crosses a CarryablesOnBack.Barrel strip with its carried revision unchanged, no drop, and the block still in hands.")]
        [Trait("pins", "The reload must not clear carried state to force a render-plan rebuild.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Reload_WhileCarrying_LeavesTheCarryIntact()
        {
            Assert.NotNull(World.Api);
            await SelfSeedBaselineAsync();

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(BarrelCode, pos);

            ITestPlayer player = await JoinAndClearAsync("Tester");

            var picked = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);
            var carriedBefore = Require(picked, $"Barrel pickup into hands failed: {failureCode}");
            Assert.Equal(BarrelCode, carriedBefore.Block?.Code?.ToString());
            var revisionBefore = Carry.GetCarriedRevision(player.Entity);

            WriteConfig(FullConfigB(barrelOnBack: false));
            await WaitForConfigAsync(
                c => c.Carryables.Barrel && !c.CarryablesOnBack.Barrel,
                "config flips CarryablesOnBack.Barrel to false");
            await World.Ticks(SettleTicks); // recondition + broadcast complete inside the reload

            var carriedAfter = Require(
                Carry.GetCarried(player.Entity, CarrySlot.Hands),
                "the reload must not drop the carried barrel");
            Assert.Equal(BarrelCode, carriedAfter.Block?.Code?.ToString());
            var revisionAfter = Carry.GetCarriedRevision(player.Entity);
            Assert.True(
                revisionBefore == revisionAfter,
                $"a recondition must not bump the carried revision (was {revisionBefore}, now "
                + $"{revisionAfter}): the render caches are the fix's business, and clearing "
                + "carried state to force a plan rebuild is the regression this row catches");
            Assert.True(
                Carry.GetCarried(player.Entity, CarrySlot.Back) == null,
                "row premise: nothing may be in the Back slot, or the strip would double as a "
                + "carry-state change and this row would no longer isolate the hands carry");

            Assert.NotNull(World.Api);
        }

        /// <summary>
        /// G3 (control, green) - why H3 has no red-first behavioral row in this suite, pinned
        /// in-band so the next reader does not spend a day looking for one. On a headless
        /// dedicated server <c>Mod.ClientApi</c> is never assigned, the renderer is
        /// client-only and never constructed, and no template manager exists - so
        /// <c>RebuildClientDerivedState</c> returns at its first line and the render caches
        /// are unreachable from here. The behavioral coverage for the render-cache staleness is the
        /// wiring test
        /// (<c>CarryabilitySyncWiringTests</c>) plus a manual real-client gate; the premise half is
        /// G1 above.
        /// </summary>
        [Trait("summary", "On this headless dedicated server Mod.ClientApi, Mod.EntityCarryRenderer and Mod.TransformTemplateManager are all null.")]
        [Trait("pins", "Why the client-side render-cache defect has no red-first behavioral row in this suite.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DedicatedServer_NeverBuildsTheRenderPipeline()
        {
            Assert.True(
                Mod.ClientApi == null,
                "a dedicated server has no client API, so RebuildClientDerivedState is a no-op here");
            Assert.True(
                Mod.EntityCarryRenderer == null,
                "EntityCarryRenderer is constructed on the client only, so the render caches "
                + "this class is about cannot exist in this harness");
            Assert.True(
                Mod.TransformTemplateManager == null,
                "no transform template manager exists without a client - the same reason the "
                + "client-side half of H3 has no behavioral row in this suite");
            await Task.CompletedTask;
        }
    }
}
