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
    /// Model-level rows for the Mode A / Mode B config surface: the
    /// <c>DebuggingOptions.ReconditionOnConfigChange</c> flag defaults to false, survives
    /// tree round-trips in both directions, and the behavior-surface signature ignores
    /// non-behavior scalars while reacting to carryability sections. This class boots with
    /// no fixture, so the live config is also the generated default (Mode A).
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Mode A/B model-surface rows with no fixture: the ReconditionOnConfigChange flag defaults false and round-trips through the world-config tree, and the behavior-surface signature ignores non-behavior scalars while reacting to carryability flips.")]
    public class ConfigModeFlagScenarios : CarryOnScenarioBase
    {
        private static CarryOnConfig FromJson(string json)
        {
            var config = JsonConvert.DeserializeObject<CarryOnConfig>(json);
            Assert.NotNull(config);
            config!.UpgradeVersion();
            return config;
        }

        private static string Signature(string json) => FromJson(json).ComputeBehaviorSurfaceSignature();

        /// <summary>
        /// S1 - the flag itself: absent from JSON it deserializes to false (Mode A is the
        /// default), the no-fixture boot config reports false, and the world-config tree
        /// round-trip (the StoreModConfig / world tree path) preserves the flag in both
        /// directions.
        /// </summary>
        [Trait("summary", "The recondition flag deserializes to false when absent, the no-fixture boot config reports false, and the world-config tree round-trip preserves the flag in both directions.")]
        [Trait("pins", "Mode A is the default and a tree round-trip neither forces the flag on nor loses it.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ReconditionFlag_DefaultsFalse_AndRoundTrips()
        {
            var bare = $"{{\"ConfigVersion\":{CarryCodes.CurrentConfigVersion},\"Carryables\":{{\"Chest\":false}}}}";
            var config = FromJson(bare);

            Assert.False(config.DebuggingOptions.ReconditionOnConfigChange,
                "absent ReconditionOnConfigChange must deserialize to false (Mode A default)");
            Assert.False(Mod.Config.DebuggingOptions.ReconditionOnConfigChange,
                "generated default boot config must run in Mode A");

            config.DebuggingOptions.ReconditionOnConfigChange = true;
            var roundOn = CarryOnConfig.FromTreeAttribute(config.ToTreeAttribute());
            Assert.True(roundOn.DebuggingOptions.ReconditionOnConfigChange,
                "tree round-trip lost a true ReconditionOnConfigChange");

            roundOn.DebuggingOptions.ReconditionOnConfigChange = false;
            var roundOff = CarryOnConfig.FromTreeAttribute(roundOn.ToTreeAttribute());
            Assert.False(roundOff.DebuggingOptions.ReconditionOnConfigChange,
                "tree round-trip must not force ReconditionOnConfigChange back on");

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// S2 - the dirty gate's premise at model level: the behavior-surface signature is
        /// insensitive to a non-behavior scalar (speed) but sensitive to a carryability
        /// flip, so Mode B reconditions exactly when the conditioning output could differ.
        /// </summary>
        [Trait("summary", "The behavior-surface signature is insensitive to InteractSpeedMultiplier but differs across a Carryables.Chest flip.")]
        [Trait("pins", "The dirty gate's definition of behavior-relevant, so Mode B reconditions exactly when the conditioning output could differ.")]
        [Trait("role", "premise")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BehaviorSurfaceSignature_IgnoresSpeed_ReactToCarryables()
        {
            var chestOff = $"{{\"ConfigVersion\":{CarryCodes.CurrentConfigVersion},\"Carryables\":{{\"Chest\":false}},\"CarryOptions\":{{\"InteractSpeedMultiplier\":2.0}}}}";
            var chestOffSlow = $"{{\"ConfigVersion\":{CarryCodes.CurrentConfigVersion},\"Carryables\":{{\"Chest\":false}},\"CarryOptions\":{{\"InteractSpeedMultiplier\":0.5}}}}";
            var chestOn = $"{{\"ConfigVersion\":{CarryCodes.CurrentConfigVersion},\"Carryables\":{{\"Chest\":true}},\"CarryOptions\":{{\"InteractSpeedMultiplier\":2.0}}}}";

            Assert.Equal(Signature(chestOff), Signature(chestOffSlow));
            Assert.NotEqual(Signature(chestOff), Signature(chestOn));

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }
    }
}
