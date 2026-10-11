using System;
using System.IO;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Common.Behaviors;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// B6 - the post-boot conditional append on an <b>already-captured</b> code
    /// (C6). Capture runs on every <c>Init</c> and is first-wins per code
    /// (<c>BehavioralConditioning.cs:188</c>), so a conditional carryable
    /// appended after the boot capture is never captured itself - reconcile
    /// cannot own it, and flipping its condition back on restores nothing.
    /// A fresh boot with the condition enabled would mount it, so the row's
    /// restore expectation is the boot surface, not a nicety.
    /// <para>
    /// The target is the legacy door (<c>game:door-plank-north-down-closed-left</c>):
    /// its interact patch makes the code boot-captured for the interact entry; it
    /// has no carryable patch, and its <c>BlockDoor</c> class and absent
    /// entityclass make it automap-inert - never a clone target, no donor keys.
    /// The appended sibling is conditional on <c>carryon.Carryables.Crate</c>, a
    /// real shipped family key the row can flip without touching the door's
    /// interact (its condition is <c>carryon.Interactables.Door</c>).
    /// </para>
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Post-boot conditional append row: a conditional carryable appended onto an already-captured code is stripped when its condition flips off and must be restored when the condition flips back on.")]
    [AtlasDataFiles("fixtures/ConvergenceConfig", TargetPath = "ModConfig")]
    public class ConvergencePostBootScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;   // ~5 s at ~33 ms pacing
        private const int SettleTicks = 60;         // ~2 s - past the 500 ms debounce + dispatch
        private const float SeedSpeed = 2.0f;

        private const string DoorCode = "game:door-plank-north-down-closed-left";

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>
        /// Full-file Mode B payload. Only <c>Carryables.Crate</c> moves - the
        /// sibling's condition - pinned explicitly because its default is true,
        /// so an omitted key would silently re-enable it.
        /// </summary>
        private static string FullConfig(bool crate)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": true,
                "Crate": {{crate.ToString().ToLowerInvariant()}}
              },
              "CarryablesFilters": {
                "AutoMapSimilar": true
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}},
                "AllowHighCapacityStorageOnBack": false,
                "PickupGroundStorageBagWhileCarrying": false
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": true
              }
            }
            """;

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
                    + $"Now: Crate={Mod.Config.Carryables.Crate}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}.");
            }
        }

        /// <summary>Re-seeds the class fixture so rows are order-independent.</summary>
        private async Task ResetToFixtureAsync()
        {
            await World.Ticks(30); // drain any prior row's debounce window + in-flight reloads
            File.WriteAllText(ConfigPath, FullConfig(crate: true));
            await WaitForConfigAsync(
                c => c.Carryables.Crate && c.DebuggingOptions.ReconditionOnConfigChange,
                "re-seeding the class fixture (Crate=true, flag on)");
            await World.Ticks(20); // > 500 ms debounce - next write's event is not swallowed
        }

        /// <summary>
        /// A conditional carryable appended after the boot capture is stripped when
        /// its condition flips off, and nothing restores it when the condition flips
        /// back on: the code's capture entry (the door's interact, from boot) blocks
        /// the sibling from ever being captured - first-wins per code - so no pass
        /// owns its return. The door's interact must ride through untouched, and the
        /// restored sibling must be the shared instance, never a per-view clone.
        /// </summary>
        [Trait("summary", "A conditional carryable appended after the boot capture is stripped when its condition flips off and restored, as the shared instance, when it flips back on, with the door's interact untouched.")]
        [Trait("pins", "Capture's first-wins-per-code rule does not strand a post-boot append.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the per-code first-wins capture never owned the post-boot sibling, so flipping the condition back restored nothing.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PostBootConditional_ConditionToggles_SiblingRestored()
        {
            Assert.NotNull(World.Api);
            await ResetToFixtureAsync();

            var door = World.Api.World.GetBlock(new AssetLocation(DoorCode));
            Assert.True(
                door != null,
                $"Row premise: the registry must hold '{DoorCode}'");
            Assert.True(
                door!.GetBehavior<BlockBehaviorCarryableInteract>() != null,
                "Row premise: the legacy door must carry its patched carryable-interact "
                + "(carryon.Interactables.Door defaults true)");
            var boot = CarryOnParityAssert.Inspect(door);
            Assert.True(
                !boot.InEitherSide,
                $"Row premise: the door must start with no carryable - its only patch is "
                + $"the interact. Actual: {boot.Describe()}");

            // The C6 shape: appended AFTER boot, with Initialize so it is a real
            // conditional - but onto a code the boot run already captured (for the
            // interact), so the per-code first-wins capture can never take it.
            var sibling = new BlockBehaviorCarryable(door);
            sibling.Initialize(new JsonObject(JToken.Parse(
                """{ "enabledCondition": "carryon.Carryables.Crate", "slots": { "Hands": {} } }""")));
            door.BlockBehaviors = [.. door.BlockBehaviors, sibling];
            door.CollectibleBehaviors = [.. door.CollectibleBehaviors, sibling];

            var before = CarryOnParityAssert.Inspect(door);
            Assert.True(
                before.InBothSides && before.SharedInstance,
                $"Row premise: the enabled sibling (Crate=true) must sit in both arrays "
                + $"as one shared instance. Actual: {before.Describe()}");

            // Condition off: the disabled strip must remove the sibling (premise).
            File.WriteAllText(ConfigPath, FullConfig(crate: false));
            await WaitForConfigAsync(
                c => !c.Carryables.Crate,
                "config flips Carryables.Crate to false");
            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertRemovalCovered(
                before,
                door,
                "the sibling's condition is false, so the final disabled strip must "
                + "remove it from both arrays");
            Assert.True(
                door.GetBehavior<BlockBehaviorCarryableInteract>() != null,
                "the interact is conditioned on carryon.Interactables.Door, not on Crate - "
                + "the flip must not touch it");

            // Condition on: nothing restores the sibling (RED).
            File.WriteAllText(ConfigPath, FullConfig(crate: true));
            await WaitForConfigAsync(
                c => c.Carryables.Crate,
                "config flips Carryables.Crate back to true");
            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertRestoredTo(
                before,
                door,
                "flipping the condition back must restore the post-boot append - a fresh "
                + "boot with Crate=true would mount it. The block is still not carryable "
                + "and the arrays are empty because the door's code was captured at boot "
                + "for its interact and capture is first-wins per code, so the sibling was "
                + "never captured and no pass owns its return (C6)");

            var after = CarryOnParityAssert.Inspect(door);
            Assert.True(
                after.InBothSides && after.SharedInstance,
                $"the restored sibling must be the shared instance it was appended as, "
                + $"never a per-view automap clone (the door is entityclass-less and "
                + $"registers no donor keys). Actual: {after.Describe()}");
        }
    }
}
