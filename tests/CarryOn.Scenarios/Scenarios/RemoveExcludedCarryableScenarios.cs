using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using Vintagestory.API.Config;
using CarryOn.Utility;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// <c>RemoveCarryableBehaviour</c> rows (E1/E2) - the type-wide exclusion that runs
    /// <i>after</i> automapping, and the reversibility the base filter lacks.
    /// <para>
    /// E1 pins the shipped default entry; E2 pins the ordering: an excluded block that the
    /// automapper would otherwise give a clone must still end up with no carryable in
    /// either array, because the exclusion pass runs last and is type-wide
    /// (<c>BlockFilter.cs:78-80</c>). E2 is green on the current head - it is a guard
    /// against the fix for the base filter (moving decisions earlier, or making the base
    /// pass type-wide) breaking the exclusion.
    /// </para>
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "RemoveCarryableBehaviour rows: the type-wide exclusion runs after automapping, so a matched block keeps no carryable in either array while siblings keep their clones, and clearing the exclusion returns the clone.")]
    [AtlasDataFiles("fixtures/RemoveExcludedCarryableConfig", TargetPath = "ModConfig")]
    public class RemoveExcludedCarryableScenarios : CarryOnScenarioBase
    {
        private const int WaitTimeoutTicks = 150;
        private const int SettleTicks = 60;
        private const float SeedSpeed = 2.0f;

        /// <summary>One automap-mapped vanilla family member, excluded by the fixture.</summary>
        private const string ExcludedCloneCode = "game:flowerpot-amber";

        private string ConfigPath => Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

        /// <summary>First registry code starting with <paramref name="prefix"/>, or null.</summary>
        private string? FindRegistryCode(string prefix)
            => World.Api!.World.Blocks
                .Select(b => b.Code?.ToString())
                .FirstOrDefault(c => c != null && c.StartsWith(prefix, StringComparison.Ordinal));

        /// <summary>Registry blocks whose carryable in each array is a per-view clone, not a shared instance.</summary>
        private List<Block> FindDistinctCloneBlocks(string prefix)
            => World.Api!.World.Blocks
                .Where(b => b.Code != null
                    && b.Code.ToString().StartsWith(prefix, StringComparison.Ordinal)
                    && b.IsCarryable()
                    && !CarryOnParityAssert.Inspect(b).SharedInstance)
                .ToList();

        /// <summary>
        /// Full-file Mode B payload that re-excludes the clone after an edit - the
        /// in-session restoration direction the hard reversibility contract requires.
        /// </summary>
        private static string ExclusionConfig()
            => $$"""
            {
              "ConfigVersion": {{CurrentConfigVersion}},
              "CarryablesFilters": {
                "AutoMapSimilar": true,
                "AutoMatchIgnoreMods": ["mcrate"],
                "RemoveBaseCarryableBehaviour": ["woodchests:wtrunk"],
                "RemoveCarryableBehaviour": ["{{ExcludedCloneCode}}"]
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
        /// Re-applies the class fixture's exclusion and lets the pass settle. Atlas stages a
        /// class fixture once per class, so E2b's live write would otherwise leak a cleared
        /// exclusion into whichever row of this class ran next.
        /// </summary>
        private async Task ResetToFixtureAsync()
        {
            File.WriteAllText(ConfigPath, ExclusionConfig());
            try
            {
                await World.Until(
                    () => Mod.Config.CarryablesFilters.RemoveCarryableBehaviour.Contains(ExcludedCloneCode),
                    WaitTimeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    "Timed out restoring the fixture exclusion. Now: "
                    + $"[{string.Join(", ", Mod.Config.CarryablesFilters.RemoveCarryableBehaviour)}]");
            }
            await World.Ticks(SettleTicks);
        }

        /// <summary>
        /// E1 - a block matched by <c>RemoveCarryableBehaviour</c> keeps no carryable in
        /// either array, and pickup is denied. The exclusion is type-wide, so unlike the
        /// base filter it cannot leave a per-view survivor.
        /// </summary>
        [Trait("summary", "A row-owned excluded automap clone and a shipped-default excluded banner both keep no carryable in either behavior array and deny pickup.")]
        [Trait("pins", "The type-wide exclusion strips both arrays, so unlike the base filter it leaves no per-view survivor.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ExcludedBlock_KeepsNoCarryableInEitherArray()
        {
            Assert.NotNull(World.Api);

            await ResetToFixtureAsync();

            Assert.Contains(ExcludedCloneCode, Mod.Config.CarryablesFilters.RemoveCarryableBehaviour);

            // A shipped default entry as the second data point: the banner is in the
            // mod's default exclusion list, so a regression that only affects a
            // row-owned entry still shows up here.
            var bannerCode = FindRegistryCode("game:banner");
            Assert.True(
                bannerCode != null,
                "Row premise: the registry must hold a 'game:banner' block for the shipped-default exclusion entry");
            Assert.Contains("game:banner", new[] { "game:banner", "game:clutter-devastation" });

            BlockPos excludedPos = World.Spawn.Offset(2, 1, 0);
            BlockPos bannerPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock(ExcludedCloneCode, excludedPos);
            World.SetBlock(bannerCode!, bannerPos);

            CarryOnParityAssert.AssertAbsentFromBoth(
                World.BlockAt(excludedPos),
                $"'{ExcludedCloneCode}' is listed in RemoveCarryableBehaviour, which strips "
                + "BlockBehaviorCarryable from both arrays by type");
            CarryOnParityAssert.AssertAbsentFromBoth(
                World.BlockAt(bannerPos),
                $"'{bannerCode}' is a shipped default RemoveCarryableBehaviour entry and must "
                + "keep no carryable in either array");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, excludedPos, out var failure);
            Assert.True(carried == null, $"Excluded block pickup must be denied: {failure}");
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// E2 - the ordering guard. <c>game:flowerpot-amber</c> is an automap-mapped family
        /// member, so automapping gives it a per-view clone; the exclusion pass runs after
        /// automapping and must still win, leaving no carryable in either array. The
        /// non-excluded siblings in the same family prove the automapper really was live in
        /// this run - without that control the row would pass trivially on a family that
        /// never auto-maps at all.
        /// </summary>
        [Trait("summary", "An excluded automap clone stays empty in both behavior arrays while a non-excluded sibling of the same family keeps its per-view clone.")]
        [Trait("pins", "The exclusion pass runs after automapping and wins, so a base-filter fix must not resurrect an excluded clone.")]
        [Trait("role", "contract")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ExcludedClone_NotResurrectedByAutoMapper_WhileSiblingsKeepTheirs()
        {
            Assert.NotNull(World.Api);

            await ResetToFixtureAsync();

            Assert.Contains(ExcludedCloneCode, Mod.Config.CarryablesFilters.RemoveCarryableBehaviour);
            Assert.True(
                FindRegistryCode(ExcludedCloneCode) != null,
                $"Row premise: the registry must hold '{ExcludedCloneCode}'");

            // Control: a non-excluded member of the same automap family, holding a
            // per-view clone. This is what automapping produces for the family, and what
            // the excluded member would receive if the exclusion did not run last.
            var sibling = FindDistinctCloneBlocks("game:flowerpot-")
                .FirstOrDefault(b => b.Code.ToString() != ExcludedCloneCode);
            Assert.True(
                sibling != null,
                "Row premise: the flowerpot family must auto-map at least one per-view clone "
                + "besides the excluded member, otherwise this row proves nothing about ordering");

            BlockPos excludedPos = World.Spawn.Offset(2, 1, 0);
            BlockPos siblingPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock(ExcludedCloneCode, excludedPos);
            World.SetBlock(sibling!.Code, siblingPos);

            CarryOnParityAssert.AssertAbsentFromBoth(
                World.BlockAt(excludedPos),
                $"'{ExcludedCloneCode}' is excluded, so the automapper's clone must be removed "
                + "from both arrays by the later exclusion pass");
            CarryOnParityAssert.AssertPresentInBoth(
                World.BlockAt(siblingPos),
                $"non-excluded family member '{sibling.Code}' must keep its automap clone in both arrays");

            // The in-session restoration direction: re-applying the same exclusion must
            // not change the surface (idempotence), and clearing the exclusion must bring
            // the clone back to both arrays.
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, excludedPos, out var failure);
            Assert.True(carried == null, $"Excluded block pickup must be denied: {failure}");

            await World.Ticks(SettleTicks);
            CarryOnParityAssert.AssertAbsentFromBoth(
                World.BlockAt(excludedPos),
                "re-applying the same exclusion must be idempotent (identical surface)");
        }

        /// <summary>
        /// E2b - clearing the exclusion in-session must return the clone to both arrays.
        /// The exclusion pass has no restore direction of its own (it only removes), so this
        /// row documents what the passes must achieve for the hard reversibility contract:
        /// a config that no longer excludes the block ends up with the clone in both views.
        /// </summary>
        [Trait("summary", "Clearing RemoveCarryableBehaviour in-session returns the excluded automap clone to both behavior arrays.")]
        [Trait("pins", "The exclusion is reversible in-session: a config that no longer excludes the block ends with the clone in both views.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ExclusionClearedInSession_CloneReturnsToBothArrays()
        {
            Assert.NotNull(World.Api);

            await ResetToFixtureAsync();

            Assert.Contains(ExcludedCloneCode, Mod.Config.CarryablesFilters.RemoveCarryableBehaviour);

            BlockPos excludedPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ExcludedCloneCode, excludedPos);
            CarryOnParityAssert.AssertAbsentFromBoth(
                World.BlockAt(excludedPos), "excluded at boot by the fixture");

            File.WriteAllText(
                ConfigPath,
                ExclusionConfig().Replace(
                    $"[\"{ExcludedCloneCode}\"]",
                    "[\"game:banner\", \"game:clutter-devastation\"]"));

            try
            {
                await World.Until(
                    () => !Mod.Config.CarryablesFilters.RemoveCarryableBehaviour.Contains(ExcludedCloneCode),
                    WaitTimeoutTicks);
            }
            catch (ScenarioTimeoutException)
            {
                Assert.Fail(
                    $"Timed out waiting for the exclusion to clear. Now: "
                    + $"[{string.Join(", ", Mod.Config.CarryablesFilters.RemoveCarryableBehaviour)}]");
            }
            await World.Ticks(SettleTicks);

            CarryOnParityAssert.AssertPresentInBoth(
                World.BlockAt(excludedPos),
                "clearing RemoveCarryableBehaviour in-session must return the automap clone "
                + "to both arrays, not leave the block behaviorless");
        }
    }
}
