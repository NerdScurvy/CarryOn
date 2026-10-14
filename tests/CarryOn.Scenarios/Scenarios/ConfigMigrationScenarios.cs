using System;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Common.Models;
using Newtonsoft.Json;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Model-level rows for the config migration chain: a v3 config's
    /// <c>CarryOptions.WalkSpeedOverrides</c> lands in
    /// <c>CarryWalkSpeed.ModifierOverrides</c> as keyed <see cref="SlotModifierConfig"/>
    /// entries, older configs upgrade without data loss, <c>ConfigVersion</c> ends at
    /// <see cref="CarryCodes.CurrentConfigVersion"/>, and the <c>CarryHungerRate</c>
    /// section (flags + per-block overrides) deserializes intact. Every row goes through
    /// the production entry point - JSON in, <c>UpgradeVersion()</c> (which is what
    /// <c>ModConfig.Load</c> calls) - and asserts only public outcomes, never the
    /// internal <c>Legacy</c> extension-data bag the migrations read from.
    /// </summary>
    [Trait("area", "Config seeding")]
    [Trait("summary", "Config migration chain rows: each legacy or partial JSON document is deserialized through the production model and upgraded, and the row asserts the resulting section shape and current ConfigVersion.")]
    public class ConfigMigrationScenarios : CarryOnScenarioBase
    {
        private static CarryOnConfig FromJson(string json)
        {
            var config = JsonConvert.DeserializeObject<CarryOnConfig>(json);
            Assert.NotNull(config);
            config!.UpgradeVersion();
            return config;
        }

        /// <summary>
        /// 1.1.1 - a v3 <c>WalkSpeedOverrides.ByBlockCode</c> block (both the per-slot
        /// object form and the bare-float form) becomes <c>ModifierOverrides.ByBlockCode</c>
        /// entries with <c>Key</c> set from the JSON property name.
        /// </summary>
        [Trait("summary", "A v3 WalkSpeedOverrides.ByBlockCode map, in both per-slot object and bare-float form, migrates to keyed SlotModifierConfig entries under CarryWalkSpeed.ModifierOverrides.ByBlockCode.")]
        [Trait("pins", "The v3 ByBlockCode walk-speed overrides survive as keyed per-slot entries.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task M111_V3_ByBlockCode_MigratesToModifierOverrides()
        {
            var json = "{\"ConfigVersion\":3,\"CarryOptions\":{"
                + "\"WalkSpeedOverrides\":{\"ByBlockCode\":{"
                + "\"game:anvil\":{\"Hands\":-0.5,\"Back\":-0.2},"
                + "\"game:log*\":-0.4}}}}";

            var config = FromJson(json);

            var byCode = config.CarryWalkSpeed.ModifierOverrides.ByBlockCode;
            Assert.Equal(2, byCode.Count);

            var anvil = byCode.Find(e => e.Key == "game:anvil");
            Assert.NotNull(anvil);
            Assert.Equal(-0.5f, anvil!.Hands);
            Assert.Equal(-0.2f, anvil.Back);

            var log = byCode.Find(e => e.Key == "game:log*");
            Assert.NotNull(log);
            Assert.Equal(-0.4f, log!.Hands);
            Assert.Equal(-0.4f, log.Back);

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 1.1.2 - the same conversion for <c>ByBlockClass</c> entries.
        /// </summary>
        [Trait("summary", "A v3 WalkSpeedOverrides.ByBlockClass map migrates to keyed entries under CarryWalkSpeed.ModifierOverrides.ByBlockClass.")]
        [Trait("pins", "The v3 ByBlockClass walk-speed overrides survive as keyed per-slot entries.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task M112_V3_ByBlockClass_MigratesToModifierOverrides()
        {
            var json = "{\"ConfigVersion\":3,\"CarryOptions\":{"
                + "\"WalkSpeedOverrides\":{\"ByBlockClass\":{"
                + "\"BlockChest\":{\"Hands\":-0.2,\"Back\":-0.1},"
                + "\"BlockAnvil\":-0.6}}}}";

            var config = FromJson(json);

            var byClass = config.CarryWalkSpeed.ModifierOverrides.ByBlockClass;
            Assert.Equal(2, byClass.Count);

            var chest = byClass.Find(e => e.Key == "BlockChest");
            Assert.NotNull(chest);
            Assert.Equal(-0.2f, chest!.Hands);
            Assert.Equal(-0.1f, chest.Back);

            var anvil = byClass.Find(e => e.Key == "BlockAnvil");
            Assert.NotNull(anvil);
            Assert.Equal(-0.6f, anvil!.Hands);
            Assert.Equal(-0.6f, anvil.Back);

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 1.1.3 - <c>WalkSpeedOverrides.SlotDefaults</c> keeps both slot values on the
        /// migrated <c>ModifierOverrides.SlotDefaults</c> (the later
        /// <c>EnsureSlotDefaults</c> pass only fills slots the migration left null).
        /// </summary>
        [Trait("summary", "The v3 flat WalkSpeedOverrides.SlotDefaults pair migrates onto ModifierOverrides.SlotDefaults with both slot values intact.")]
        [Trait("pins", "The v3 slot defaults survive the migration to the structured modifier overrides.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task M113_V3_SlotDefaults_MigratesToModifierOverrides()
        {
            var json = "{\"ConfigVersion\":3,\"CarryOptions\":{"
                + "\"WalkSpeedOverrides\":{\"SlotDefaults\":{\"Hands\":-0.1,\"Back\":-0.05}}}}";

            var config = FromJson(json);

            Assert.Equal(-0.1f, config.CarryWalkSpeed.ModifierOverrides.SlotDefaults.Hands);
            Assert.Equal(-0.05f, config.CarryWalkSpeed.ModifierOverrides.SlotDefaults.Back);

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 1.1.4 - a v3 config without any <c>WalkSpeedOverrides</c> upgrades cleanly:
        /// current version, empty override lists, and the v3->v4 speed flags derived from
        /// the absent <c>IgnoreCarrySpeedPenalty</c> (speed penalty stays enabled).
        /// </summary>
        [Trait("summary", "A v3 config with no WalkSpeedOverrides still upgrades to the current version with empty override lists and both speed penalties enabled.")]
        [Trait("pins", "The migration cannot require the optional WalkSpeedOverrides section.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task M114_V3_NoWalkSpeedOverrides_StillUpgrades()
        {
            var json = "{\"ConfigVersion\":3,\"CarryOptions\":{"
                + "\"InteractSpeedMultiplier\":1.5,\"SomeFutureKey\":1}}";

            var config = FromJson(json);

            Assert.Equal(CarryCodes.CurrentConfigVersion, config.ConfigVersion);
            Assert.Empty(config.CarryWalkSpeed.ModifierOverrides.ByBlockCode);
            Assert.Empty(config.CarryWalkSpeed.ModifierOverrides.ByBlockClass);
            Assert.True(config.CarryWalkSpeed.HandsEnabled,
                "with no IgnoreCarrySpeedPenalty in the v3 config the hands penalty must stay enabled");
            Assert.True(config.CarryWalkSpeed.BackEnabled,
                "with no IgnoreCarrySpeedPenalty in the v3 config the back penalty must stay enabled");
            Assert.Equal(1.5f, config.CarryOptions.InteractSpeedMultiplier);

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 1.1.5 (flat half) - a pre-structured config carrying no <c>ConfigVersion</c> and
        /// only the flat <c>*Enabled</c> keys is mapped onto the structured sections and
        /// lands on the current version without losing the scalars it did carry.
        /// </summary>
        [Trait("summary", "A pre-structured flat config with no ConfigVersion maps its flat enabled keys onto the structured sections and inverts HarmonyPatchEnabled into DisableHarmonyPatch.")]
        [Trait("pins", "The flat legacy key table reaches the structured sections and inverts the harmony flag.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task M115_V1LegacyFlatConfig_Migrates()
        {
            var json = "{\"AnvilEnabled\":false,\"ChestEnabled\":true,"
                + "\"BackSlotEnabled\":false,\"InteractSpeedMultiplier\":1.25,"
                + "\"HarmonyPatchEnabled\":false}";

            var config = FromJson(json);

            Assert.Equal(CarryCodes.CurrentConfigVersion, config.ConfigVersion);
            Assert.False(config.Carryables.Anvil, "flat AnvilEnabled=false must reach Carryables.Anvil");
            Assert.True(config.Carryables.Chest, "flat ChestEnabled=true must reach Carryables.Chest");
            Assert.False(config.CarryOptions.BackSlotEnabled,
                "flat BackSlotEnabled=false must reach CarryOptions.BackSlotEnabled");
            Assert.Equal(1.25f, config.CarryOptions.InteractSpeedMultiplier);
            Assert.True(config.DebuggingOptions.DisableHarmonyPatch,
                "flat HarmonyPatchEnabled=false must invert into DebuggingOptions.DisableHarmonyPatch");

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 1.1.5 (structured half) - a v2 config's back-slot keys (which v2 kept under
        /// <c>CarryOptions</c>) move to <c>CarryablesOnBack</c> on the way to the current
        /// version, and the rest of the config survives the upgrade untouched.
        /// </summary>
        [Trait("summary", "A v2 config's back-slot keys move from CarryOptions to CarryablesOnBack and AllowHighCapacityStorageOnBack, the v3→v4 walk-speed keys move into CarryWalkSpeed, and the structured Carryables section survives.")]
        [Trait("pins", "The v2 back-slot keys land on CarryablesOnBack and the large-chest flag remaps; v3→v4 walk-speed migration runs in the chained upgrade.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task M116_V2LegacyConfig_UpgradesToCurrent()
        {
            var json = "{\"ConfigVersion\":2,\"Carryables\":{\"Chest\":false},"
                + "\"CarryOptions\":{\"AllowCratesOnBack\":true,"
                + "\"AllowChestTrunksOnBack\":true,"
                + "\"AllowLargeChestsOnBack\":true,\"InteractSpeedMultiplier\":2.0,"
                + "\"IgnoreCarrySpeedPenalty\":true,"
                + "\"WalkSpeedOverrides\":{\"ByBlockCode\":{\"game:anvil\":{\"Hands\":-0.5,\"Back\":-0.2}}}}}";

            var config = FromJson(json);

            Assert.Equal(CarryCodes.CurrentConfigVersion, config.ConfigVersion);
            Assert.False(config.Carryables.Chest, "the v2 Carryables section must survive the upgrade");
            Assert.True(config.CarryablesOnBack.Crate,
                "v2 CarryOptions.AllowCratesOnBack must reach CarryablesOnBack.Crate");
            Assert.True(config.CarryablesOnBack.ChestTrunk,
                "v2 CarryOptions.AllowChestTrunksOnBack must reach CarryablesOnBack.ChestTrunk");
            Assert.True(config.CarryOptions.AllowHighCapacityStorageOnBack,
                "v2 CarryOptions.AllowLargeChestsOnBack must reach CarryOptions.AllowHighCapacityStorageOnBack");
            Assert.Equal(2.0f, config.CarryOptions.InteractSpeedMultiplier);
            Assert.False(config.CarryWalkSpeed.HandsEnabled,
                "with IgnoreCarrySpeedPenalty true in the v2 chain the hands penalty must be disabled (inverted)");
            Assert.False(config.CarryWalkSpeed.BackEnabled,
                "with IgnoreCarrySpeedPenalty true in the v2 chain the back penalty must be disabled (inverted)");
            var anvilOverride = config.CarryWalkSpeed.ModifierOverrides.ByBlockCode.Find(e => e.Key == "game:anvil");
            Assert.NotNull(anvilOverride);
            Assert.Equal(-0.5f, anvilOverride!.Hands);
            Assert.Equal(-0.2f, anvilOverride.Back);

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// A v4 config missing some PreventSwapFromBackOnTarget defaults runs the v5→v6 merge
        /// when the chain is executed (proving chained migration beyond the starting step).
        /// </summary>
        [Trait("summary", "A v4 config missing PreventSwapFromBackOnTarget defaults receives the full default set after chained upgrade.")]
        [Trait("pins", "The v5→v6 merge runs even when starting from v4.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task M118_V4_ChainRunsV5ToV6Merge()
        {
            var json = "{\"ConfigVersion\":4,"
                + "\"CarryOptions\":{\"PreventSwapFromBackOnTarget\":[\"game:chest\"]}}";

            var config = FromJson(json);

            Assert.Equal(CarryCodes.CurrentConfigVersion, config.ConfigVersion);
            Assert.NotNull(config.CarryOptions.PreventSwapFromBackOnTarget);
            Assert.Contains("game:chest", config.CarryOptions.PreventSwapFromBackOnTarget, StringComparer.OrdinalIgnoreCase);
            var defaults = new CarryOptionsConfig().PreventSwapFromBackOnTarget;
            foreach (var d in defaults)
            {
                Assert.Contains(d, config.CarryOptions.PreventSwapFromBackOnTarget, StringComparer.OrdinalIgnoreCase);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// An explicit ConfigVersion of 0 is treated like a flat legacy config (no version)
        /// so the flat mapping still runs.
        /// </summary>
        [Trait("summary", "An explicit ConfigVersion 0 is treated as legacy, so flat enabled keys still migrate to structured sections.")]
        [Trait("pins", "Explicit version zero runs the flat legacy mapping.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task M119_ExplicitVersionZero_FlatMappingStillRuns()
        {
            var json = "{\"ConfigVersion\":0,\"AnvilEnabled\":false,\"ChestEnabled\":false}";

            var config = FromJson(json);

            Assert.Equal(CarryCodes.CurrentConfigVersion, config.ConfigVersion);
            Assert.False(config.Carryables.Anvil);
            Assert.False(config.Carryables.Chest);

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 1.1.6 - whatever version the file started at, the upgraded config reports the
        /// current <c>ConfigVersion</c> (the plan's expected value of 4 predates v5/v6).
        /// </summary>
        [Trait("summary", "Configs starting at v2, v3, or with no version key all report the current ConfigVersion after upgrade, and the booted server config is already current.")]
        [Trait("pins", "Whatever the starting version, the upgraded config reports the current version.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task M117_AfterUpgrade_ConfigVersionIsCurrent()
        {
            Assert.Equal(CarryCodes.CurrentConfigVersion,
                FromJson("{\"ConfigVersion\":3}").ConfigVersion);
            Assert.Equal(CarryCodes.CurrentConfigVersion,
                FromJson("{\"ConfigVersion\":2}").ConfigVersion);
            Assert.Equal(CarryCodes.CurrentConfigVersion,
                FromJson("{}").ConfigVersion);
            Assert.True(CarryCodes.CurrentConfigVersion == Mod.Config.ConfigVersion, "the booted server config must already be at the current version");

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 1.2.1 - the <c>CarryHungerRate</c> section deserializes: both slot enable
        /// flags, the saturation gate and the per-block override list with its slot pair
        /// and slot defaults intact.
        /// </summary>
        [Trait("summary", "The CarryHungerRate section deserializes with both slot flags, the saturation gate, and a per-block override list whose slot pair and slot defaults are intact.")]
        [Trait("pins", "The CarryHungerRate flags, saturation gate, and override list round-trip through the model.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task M121_CarryHungerRate_DeserializesWithFlagsAndOverrides()
        {
            var json = "{\"ConfigVersion\":" + CarryCodes.CurrentConfigVersion + ","
                + "\"CarryHungerRate\":{"
                + "\"HandsEnabled\":true,\"BackEnabled\":false,"
                + "\"MinSaturationThreshold\":25,"
                + "\"ModifierOverrides\":{"
                + "\"ByBlockCode\":[{\"Key\":\"game:crate\",\"Hands\":0.9,\"Back\":0.4}],"
                + "\"SlotDefaults\":{\"Hands\":0.7,\"Back\":0.6}}}}";

            var config = FromJson(json);

            Assert.True(config.CarryHungerRate.HandsEnabled);
            Assert.False(config.CarryHungerRate.BackEnabled);
            Assert.Equal(25f, config.CarryHungerRate.MinSaturationThreshold);

            var overrideEntry = Assert.Single(config.CarryHungerRate.ModifierOverrides.ByBlockCode);
            Assert.Equal("game:crate", overrideEntry.Key);
            Assert.Equal(0.9f, overrideEntry.Hands);
            Assert.Equal(0.4f, overrideEntry.Back);
            Assert.Equal(0.7f, config.CarryHungerRate.ModifierOverrides.SlotDefaults.Hands);
            Assert.Equal(0.6f, config.CarryHungerRate.ModifierOverrides.SlotDefaults.Back);

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }
    }
}
