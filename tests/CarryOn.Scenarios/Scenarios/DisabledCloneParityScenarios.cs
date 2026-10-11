using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// P1 - the disabled strip must mirror its removal for per-array instances.
    /// <para>
    /// <c>RemoveDisabledConditionalBehaviors</c> walks <c>block.BlockBehaviors</c> and then
    /// reference-removes that same instance from <c>CollectibleBehaviors</c>
    /// (<c>BlockFilter.cs:39-52</c>). For a patch-loaded or remounted behavior both arrays
    /// hold one object, so that is correct. An automap clone is built twice - once per view
    /// (<c>BehaviorAutoMapper.cs:93-102</c>) - so the collectible-side call matches nothing
    /// and silently no-ops, leaving the clone's collectible twin in place. Because
    /// <c>IsCarryable()</c> is an OR over both arrays, a block whose donor condition is off
    /// stays carryable.
    /// </para>
    /// <para>
    /// The row proves the two instance classes in one run, so the failure cannot be
    /// misattributed: <c>SharedInstance=false</c> on the automap clone versus
    /// <c>SharedInstance=true</c> on the patch-loaded twin, both verified while the
    /// condition is still on, and only then the toggle is flipped.
    /// </para>
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Disabled-strip parity row: with a per-view automap clone and a shared patch-loaded twin both proven present while the condition is on, flipping the donor toggle off must clear both instance classes from both behavior arrays.")]
    [AtlasDataFiles("fixtures/DisabledCloneParityConfig", TargetPath = "ModConfig")]
    public class DisabledCloneParityScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;
        private const int SettleTicks = 60;
        private const float SeedSpeed = 2.0f;

        /// <summary>Per-view automap clone (two instances, distinct property objects).</summary>
        private const string CloneCode = "game:flowerpot-amber";

        /// <summary>Patch-loaded twin: one instance referenced from both arrays.</summary>
        private const string SharedCode = "game:flowerpot-blue-fired";

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>Full-file payload; only <c>Carryables.Flowerpot</c> differs from the fixture.</summary>
        private static string FullConfig(bool flowerpot)
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "Carryables": {
                "Chest": true,
                "Flowerpot": {{flowerpot.ToString().ToLowerInvariant()}}
              },
              "CarryablesFilters": {
                "AutoMapSimilar": true,
                "AutoMatchIgnoreMods": ["mcrate"],
                "RemoveBaseCarryableBehaviour": ["woodchests:wtrunk"],
                "RemoveCarryableBehaviour": ["game:banner", "game:clutter-devastation"]
              },
              "CarryOptions": {
                "InteractSpeedMultiplier": {{SeedSpeed.ToString("0.0###", CultureInfo.InvariantCulture)}},
                "AllowHighCapacityStorageOnBack": false,
                "PickupGroundStorageBagWhileCarrying": false
              },
              "DebuggingOptions": {
                "ReconditionOnConfigChange": true
              }
            }
            """;

        /// <summary>
        /// With the donor's condition off, both a per-view automap clone and a shared
        /// patch-loaded twin must end up with no carryable in either array. Today only the
        /// shared twin is stripped: the clone keeps its collectible-side copy and still
        /// reports carryable.
        /// </summary>
        [Trait("summary", "After proving the clone is per-view and the twin is shared while the condition is on, flipping Carryables.Flowerpot off clears both from both behavior arrays.")]
        [Trait("pins", "The disabled strip resolves the instance per view, so an automap clone's collectible twin is removed as well.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with the clone's collectible twin surviving (block-side=0, collectible-side=1), so the block still reported carryable.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DisabledDonor_AutomapCloneAndSharedTwin_BothStrippedFromBothArrays()
        {
            Assert.NotNull(World.Api);

            Assert.True(Mod.Config.Carryables.Flowerpot, "fixture must boot with Carryables.Flowerpot = true");
            Assert.True(Mod.Config.DebuggingOptions.ReconditionOnConfigChange, "fixture must boot in Mode B");

            BlockPos clonePos = World.Spawn.Offset(2, 1, 0);
            BlockPos sharedPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock(CloneCode, clonePos);
            World.SetBlock(SharedCode, sharedPos);

            // Phase 1 - both are mirrored while the condition is on, and the two rows
            // differ in instance class. This is what makes the post-toggle failure
            // attributable to the strip's reference removal rather than to a missing
            // clone or a different registry shape.
            var cloneBefore = CarryOnParityAssert.Inspect(World.BlockAt(clonePos));
            var sharedBefore = CarryOnParityAssert.Inspect(World.BlockAt(sharedPos));

            Assert.True(
                cloneBefore.InBothSides && !cloneBefore.SharedInstance,
                $"Row premise: '{CloneCode}' must be an automap clone - a carryable in both "
                + $"arrays held by two separate instances. Actual: {cloneBefore.Describe()}");
            Assert.True(
                sharedBefore.InBothSides && sharedBefore.SharedInstance,
                $"Row premise: '{SharedCode}' must be a patch-loaded shared instance - one "
                + $"carryable instance in both arrays. Actual: {sharedBefore.Describe()}");

            // Phase 2 - flip the donor's toggle off in-session.
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            File.WriteAllText(ConfigPath, FullConfig(flowerpot: false));
            try
            {
                await World.Until(() => !Mod.Config.Carryables.Flowerpot, WaitTimeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    "Timed out waiting for Carryables.Flowerpot to flip to false. Now: "
                    + $"Flowerpot={Mod.Config.Carryables.Flowerpot}, "
                    + $"ReconditionOnConfigChange={Mod.Config.DebuggingOptions.ReconditionOnConfigChange}");
            }
            await World.Ticks(SettleTicks);

            // The shared twin is the control that must be green today.
            CarryOnParityAssert.AssertAbsentFromBoth(
                World.BlockAt(sharedPos),
                "the patch-loaded instance is one object in both arrays, so the strip's "
                + "reference removal must clear it from both");

            // The regression: the per-view clone must be stripped in both views too.
            CarryOnParityAssert.AssertAbsentFromBoth(
                World.BlockAt(clonePos),
                "the automapper builds one carryable instance per view, so the disabled strip "
                + "must resolve the instance in each array instead of reference-removing the "
                + "block-side one");

            var carried = Actions.PickUpIntoHands(player.Entity, clonePos, out var failure);
            Assert.True(
                carried == null,
                $"A clone whose donor condition is off must not be carryable: {failure}");
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
        }
    }
}
