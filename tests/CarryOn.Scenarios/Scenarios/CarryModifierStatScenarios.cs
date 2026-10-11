using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Entity-stat rows for the hunger-rate and walk-speed modifier assertions: picking a
    /// block up writes the modifier its carryable patch declares onto
    /// <c>agent.Stats</c> under the slot-scoped code (<c>carryon:Hands</c> /
    /// <c>carryon:Back</c>), the slot enable flags gate each write, config
    /// block-code overrides win over the patched slot value, the type/group maps resolve
    /// against the block's declared patch data, swapping and dropping retarget or clear
    /// the entry, and the saturation threshold gates the hunger write. Every row drives
    /// the public manager seam (<c>Actions.PickUpIntoHands</c> /
    /// <c>PickUpIntoSlot</c> / <c>SwapCarried</c> / <c>Drop</c>) and restores the live
    /// config it mutates in a <c>finally</c>, because the config object is shared for the
    /// whole boot. The class boots against the <c>CrateOnBackConfig</c> fixture so
    /// <c>CarryablesOnBack.Crate</c> is already true when the behavior is conditioned -
    /// a slot enable flag changed in memory does not re-run the conditioning pass, so the
    /// back rows would otherwise be unable to pick the crate up at all.
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Per-block walk-speed and hunger-rate stat rows: pickup writes the modifier a carryable patch declares onto the slot-scoped entity stat, slot flags gate each write, config overrides beat the patch, swap and drop retarget or clear entries, and saturation gates the hunger write.")]
    [AtlasDataFiles("fixtures/CrateOnBackConfig", TargetPath = "ModConfig")]
    public class CarryModifierStatScenarios : CarryOnScenarioBase
    {
        private const string CrateCode = "game:crate";
        private const string AnvilCode = "game:anvil-iron";
        private const string LabeledChestCode = "game:labeledchest-east";

        private static float? Stat(EntityPlayer player, string category, CarrySlot slot)
        {
            var slotCode = GetCarryCode(slot.ToString());
            foreach (var categoryEntry in player.Stats)
            {
                if (!string.Equals(categoryEntry.Key, category, StringComparison.Ordinal)) continue;
                foreach (var value in categoryEntry.Value.ValuesByKey)
                {
                    if (string.Equals(value.Key, slotCode, StringComparison.Ordinal))
                        return value.Value.Value;
                }
            }

            return null;
        }

        private static float? Walk(EntityPlayer player, CarrySlot slot)
            => Stat(player, Stats.WalkSpeed, slot);

        private static float? Hunger(EntityPlayer player, CarrySlot slot)
            => Stat(player, Stats.HungerRate, slot);

        private void Place(string code, BlockPos pos)
        {
            World.SetBlock(code, pos);
            Assert.True(code == World.BlockAt(pos)?.Code?.ToString(),
                $"premise: {code} must resolve and place in the sandbox");
        }

        /// <summary>
        /// Tops the player's saturation up to its maximum so the hunger gate can be
        /// exercised deliberately (it is opened or raised by the row instead of being
        /// assumed from the join-time default).
        /// </summary>
        private static EntityBehaviorHunger Saturate(ITestPlayer player)
        {
            var hunger = player.Entity.GetBehavior<EntityBehaviorHunger>();
            Assert.True(hunger != null, "row premise: a survival player must carry the hunger behavior");
            hunger!.Saturation = (float)hunger.MaxSaturation;
            return hunger;
        }

        private static bool TryGet(IDictionary<string, float>? map, string key, out float value)
        {
            value = 0f;
            if (map == null || string.IsNullOrWhiteSpace(key)) return false;
            if (WildcardMatcher.TryGetValueTrimmedIgnoreCase(map, key, out value)) return true;
            return WildcardMatcher.TryGetTrailingWildcardValue(map, key, out value);
        }

        private static string? CarryType(Block block)
        {
            var variant = block.Variant;
            if (variant != null && variant.TryGetValue("type", out var fromVariant)
                && !string.IsNullOrWhiteSpace(fromVariant))
            {
                return fromVariant;
            }

            var fromAttributes = block.Attributes?["type"]?.AsString();
            return string.IsNullOrWhiteSpace(fromAttributes) ? null : fromAttributes;
        }

        /// <summary>
        /// Mirrors the public resolution chain for a hunger slot so a row can predict the
        /// stat value from the shipped patch data alone: type map, group map, the slot's
        /// declared value, the configured slot default, then the hardcoded default.
        /// </summary>
        private float ExpectedHunger(Block block, BlockBehaviorCarryable behavior, SlotSettings hands)
        {
            var type = CarryType(block);
            if (!string.IsNullOrWhiteSpace(type))
            {
                if (TryGet(hands.HungerModifierByType, type!, out var byType)) return byType;
                if (behavior.TypeGroup.TryGetValue(type!, out var group)
                    && !string.IsNullOrWhiteSpace(group)
                    && TryGet(hands.HungerModifierByGroup, group, out var byGroup))
                {
                    return byGroup;
                }
            }

            if (hands.HungerModifier.HasValue) return hands.HungerModifier.Value;

            return Mod.Config.CarryHungerRate.ModifierOverrides.SlotDefaults.Hands
                ?? Defaults.HungerRateModifier[CarrySlot.Hands];
        }

        /// <summary>
        /// 6.1 - carrying the anvil in hands applies the patched
        /// <c>walkSpeedModifier: -0.5</c> to the walk-speed stat under the hands code, the
        /// blended category drops to 1 + modifier, and dropping clears the entry again.
        /// </summary>
        [Trait("summary", "Carrying the anvil in hands applies the patch's hands walkSpeedModifier of -0.5 to the walkspeed stat, blending the category to 1 plus the modifier, and dropping clears the entry.")]
        [Trait("pins", "The hands pickup writes the patched walk-speed penalty under the hands-scoped code and dropping removes it.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WalkSpeed_Hands_AppliesPatchedAnvilPenalty()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            Place(AnvilCode, pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failure);
            Assert.True(carried != null, $"anvil hands pickup failed: {failure}");

            Assert.True(-0.5f == Walk(player.Entity, CarrySlot.Hands), "the anvil patch's hands walkSpeedModifier must land on the walkspeed stat");
            var blended = player.Entity.Stats.GetBlended(Stats.WalkSpeed);
            Assert.True(Math.Abs(blended - 0.5f) < 0.001f,
                $"blended walkspeed must be base 1 + modifier (-0.5), got {blended}");

            Actions.Drop(player.Entity, CarrySlot.Hands);
            Assert.True(Walk(player.Entity, CarrySlot.Hands) == null, "dropping must clear the walk-speed entry");

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 6.2 - carrying the crate on the back (with <c>CarryablesOnBack.Crate</c>
        /// enabled) applies the patched back penalty to the back-scoped walk-speed entry.
        /// </summary>
        [Trait("summary", "Carrying the crate on the back applies the patch's back walkSpeedModifier of -0.3 to the back-scoped entry and writes no hands-scoped entry.")]
        [Trait("pins", "The back pickup writes the patched walk-speed penalty under the back-scoped code only.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WalkSpeed_Back_AppliesPatchedCratePenalty()
        {
            var crateOnBack = Mod.Config.CarryablesOnBack.Crate;
            Mod.Config.CarryablesOnBack.Crate = true;
            try
            {
                BlockPos pos = World.Spawn.Offset(4, 1, 0);
                Place(CrateCode, pos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

                var carried = Actions.PickUpIntoSlot(player.Entity, pos, CarrySlot.Back, out var failure);
                Assert.True(carried != null, $"crate back pickup failed: {failure}");

                Assert.True(-0.3f == Walk(player.Entity, CarrySlot.Back), "the crate patch's back walkSpeedModifier must land on the back walkspeed entry");
                Assert.True(Walk(player.Entity, CarrySlot.Hands) == null, "a back carry must not write a hands-scoped entry");

                Actions.Drop(player.Entity, CarrySlot.Back);
                Assert.Null(Walk(player.Entity, CarrySlot.Back));
            }
            finally
            {
                Mod.Config.CarryablesOnBack.Crate = crateOnBack;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 6.3 - with the walk-speed flags off for a slot no walk-speed entry is written,
        /// while the hunger rate (whose own flag is untouched) still applies - proving the
        /// two modifiers are gated independently rather than by one shared switch.
        /// </summary>
        [Trait("summary", "With CarryWalkSpeed.HandsEnabled and BackEnabled off no walk entry is written for either slot, while the independently flagged hunger back value still applies.")]
        [Trait("pins", "The walk-speed and hunger-rate modifiers are gated by separate switches, not one shared flag.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WalkSpeed_SlotFlagsOff_WritesNoWalkEntry()
        {
            var walkConfig = Mod.Config.CarryWalkSpeed;
            var crateOnBack = Mod.Config.CarryablesOnBack.Crate;
            walkConfig.HandsEnabled = false;
            walkConfig.BackEnabled = false;
            Mod.Config.CarryablesOnBack.Crate = true;
            try
            {
                BlockPos pos = World.Spawn.Offset(4, 1, 0);
                Place(CrateCode, pos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
                Saturate(player);

                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var handsFailure) != null,
                    $"crate hands pickup failed: {handsFailure}");
                Assert.True(Walk(player.Entity, CarrySlot.Hands) == null, "HandsEnabled=false must suppress the walk-speed write");
                Actions.Drop(player.Entity, CarrySlot.Hands);

                Place(CrateCode, pos);
                Assert.True(Actions.PickUpIntoSlot(player.Entity, pos, CarrySlot.Back, out var backFailure) != null,
                    $"crate back pickup failed: {backFailure}");
                Assert.True(Walk(player.Entity, CarrySlot.Back) == null, "BackEnabled=false must suppress the walk-speed write");
                Assert.True(0.4f == Hunger(player.Entity, CarrySlot.Back), "the hunger Back flag is independent of the walk-speed flags and must still apply");
                Actions.Drop(player.Entity, CarrySlot.Back);
            }
            finally
            {
                walkConfig.HandsEnabled = true;
                walkConfig.BackEnabled = true;
                Mod.Config.CarryablesOnBack.Crate = crateOnBack;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 6.4 - a <c>CarryWalkSpeed.ModifierOverrides.ByBlockCode</c> entry for the block
        /// beats the value the carryable patch declares for the slot.
        /// </summary>
        [Trait("summary", "A configured CarryWalkSpeed.ModifierOverrides.ByBlockCode entry of -0.9 beats the patch's -0.3 slot value for the crate.")]
        [Trait("pins", "Configured block-code overrides win over the value the carryable patch declares for the slot.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WalkSpeed_BlockCodeOverride_BeatsPatchedSlotValue()
        {
            var entries = Mod.Config.CarryWalkSpeed.ModifierOverrides.ByBlockCode;
            var overrideEntry = new SlotModifierConfig { Key = CrateCode, Hands = -0.9f };
            entries.Add(overrideEntry);
            try
            {
                BlockPos pos = World.Spawn.Offset(4, 1, 0);
                Place(CrateCode, pos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var failure) != null,
                    $"crate hands pickup failed: {failure}");
                Assert.True(-0.9f == Walk(player.Entity, CarrySlot.Hands), "the configured block-code override must win over the patched -0.3 slot value");

                Actions.Drop(player.Entity, CarrySlot.Hands);
            }
            finally
            {
                entries.Remove(overrideEntry);
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 2.1.1 - hunger rate is off for hands by default; enabling
        /// <c>CarryHungerRate.HandsEnabled</c> makes the crate patch's hands
        /// <c>hungerModifier: 0.3</c> land on the hunger-rate stat, alongside the
        /// walk-speed entry the same pickup writes.
        /// </summary>
        [Trait("summary", "Enabling CarryHungerRate.HandsEnabled makes the crate patch's hands hungerModifier of 0.3 land on the hungerrate stat, alongside the walk entry the same pickup writes.")]
        [Trait("pins", "The hunger-rate hands flag enables a write that is off by default.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HungerRate_HandsEnabled_AppliesPatchedCrateValue()
        {
            var hungerConfig = Mod.Config.CarryHungerRate;
            hungerConfig.HandsEnabled = true;
            try
            {
                BlockPos pos = World.Spawn.Offset(4, 1, 0);
                Place(CrateCode, pos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
                Saturate(player);

                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var failure) != null,
                    $"crate hands pickup failed: {failure}");
                Assert.True(0.3f == Hunger(player.Entity, CarrySlot.Hands), "the crate patch's hands hungerModifier must land on the hungerrate stat");
                Assert.True(-0.3f == Walk(player.Entity, CarrySlot.Hands), "premise: the same pickup must also write the walk-speed entry");

                Actions.Drop(player.Entity, CarrySlot.Hands);
            }
            finally
            {
                hungerConfig.HandsEnabled = false;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 2.1.2 - the back slot keeps its own patched value, so carrying the crate on the
        /// back applies 0.4 (the hands slot's is 0.3).
        /// </summary>
        [Trait("summary", "Carrying the crate on the back applies the patch's back hungerModifier of 0.4 to the back-scoped entry and writes no hands-scoped hunger entry.")]
        [Trait("pins", "Each slot keeps its own patched hunger value rather than sharing the hands value.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HungerRate_BackEnabled_AppliesPatchedCrateValue()
        {
            var crateOnBack = Mod.Config.CarryablesOnBack.Crate;
            Mod.Config.CarryablesOnBack.Crate = true;
            try
            {
                Assert.True(Mod.Config.CarryHungerRate.BackEnabled,
                    "row premise: the back hunger flag ships enabled");

                BlockPos pos = World.Spawn.Offset(4, 1, 0);
                Place(CrateCode, pos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
                Saturate(player);

                Assert.True(Actions.PickUpIntoSlot(player.Entity, pos, CarrySlot.Back, out var failure) != null,
                    $"crate back pickup failed: {failure}");
                Assert.True(0.4f == Hunger(player.Entity, CarrySlot.Back), "the crate patch's back hungerModifier must land on the back hungerrate entry");
                Assert.True(Hunger(player.Entity, CarrySlot.Hands) == null, "a back carry must not write a hands-scoped hunger entry");

                Actions.Drop(player.Entity, CarrySlot.Back);
            }
            finally
            {
                Mod.Config.CarryablesOnBack.Crate = crateOnBack;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 2.1.3 - a labeled chest declares no hunger modifier at all, so the write falls
        /// back through the chain: the configured slot default (seeded from the hardcoded
        /// 0.2), an explicit slot default overriding it, and finally the hardcoded default
        /// itself once the configured slot default is cleared. The walk-speed entry follows
        /// the same fallback (no declared value, no override -&gt; -0.25).
        /// </summary>
        [Trait("summary", "A labeled chest declares no hunger modifier, so the write falls back through the seeded slot default, an explicit configured slot default, then the hardcoded default once cleared, with the walk entry following the same fallback.")]
        [Trait("pins", "The hunger and walk resolution chains fall back through slot defaults and the hardcoded default when a block declares nothing.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HungerRate_NoDeclaredModifier_FallsBackThroughSlotDefaults()
        {
            var hungerConfig = Mod.Config.CarryHungerRate;
            var slotDefaults = hungerConfig.ModifierOverrides.SlotDefaults;
            var seededDefault = slotDefaults.Hands;
            var hardcoded = Defaults.HungerRateModifier[CarrySlot.Hands];
            hungerConfig.HandsEnabled = true;
            try
            {
                Assert.True(seededDefault == hardcoded, "row premise: loading the config must seed the hunger hands slot default from the hardcoded default");

                BlockPos pos = World.Spawn.Offset(4, 1, 0);
                Place(LabeledChestCode, pos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
                Saturate(player);

                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var failure) != null,
                    $"labeled chest hands pickup failed: {failure}");
                Assert.True(seededDefault == Hunger(player.Entity, CarrySlot.Hands), "with nothing declared for the block the configured slot default must apply");
                Assert.True(Defaults.WalkSpeedModifier[CarrySlot.Hands] == Walk(player.Entity, CarrySlot.Hands), "the walk-speed entry must fall back the same way when the slot declares nothing");
                Actions.Drop(player.Entity, CarrySlot.Hands);

                slotDefaults.Hands = 0.55f;
                Place(LabeledChestCode, pos);
                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var overrideFailure) != null,
                    $"labeled chest re-pickup failed: {overrideFailure}");
                Assert.True(0.55f == Hunger(player.Entity, CarrySlot.Hands), "an explicit configured slot default must beat the seeded one");
                Actions.Drop(player.Entity, CarrySlot.Hands);

                slotDefaults.Hands = null;
                Place(LabeledChestCode, pos);
                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var hardcodedFailure) != null,
                    $"labeled chest re-pickup failed: {hardcodedFailure}");
                Assert.True(Defaults.HungerRateModifier[CarrySlot.Hands] == Hunger(player.Entity, CarrySlot.Hands), "with no configured slot default left the hardcoded default must apply");
                Actions.Drop(player.Entity, CarrySlot.Hands);
            }
            finally
            {
                slotDefaults.Hands = seededDefault;
                hungerConfig.HandsEnabled = false;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 2.1.4 - swapping the carried crate from hands to back moves both modifiers to
        /// the destination slot: the hands-scoped entries clear and the back-scoped ones
        /// re-resolve against the back slot's patched values (hunger 0.3 -&gt; 0.4).
        /// </summary>
        [Trait("summary", "Swapping the carried crate from hands to back clears the hands-scoped entries and re-resolves the back-scoped ones against the back slot's patched values (hunger 0.3 to 0.4).")]
        [Trait("pins", "A swap retargets both stat entries to the destination slot's values.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HungerRate_SwapCarried_RetargetsEntriesToDestinationSlot()
        {
            var hungerConfig = Mod.Config.CarryHungerRate;
            var crateOnBack = Mod.Config.CarryablesOnBack.Crate;
            hungerConfig.HandsEnabled = true;
            Mod.Config.CarryablesOnBack.Crate = true;
            try
            {
                BlockPos pos = World.Spawn.Offset(4, 1, 0);
                Place(CrateCode, pos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
                Saturate(player);

                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var failure) != null,
                    $"crate hands pickup failed: {failure}");
                Assert.Equal(0.3f, Hunger(player.Entity, CarrySlot.Hands));
                Assert.Equal(-0.3f, Walk(player.Entity, CarrySlot.Hands));

                Assert.True(Carry.SwapCarried(player.Entity, CarrySlot.Hands, CarrySlot.Back));

                Assert.True(Hunger(player.Entity, CarrySlot.Hands) == null, "the hands-scoped hunger entry must be removed by the swap");
                Assert.True(Walk(player.Entity, CarrySlot.Hands) == null, "the hands-scoped walk-speed entry must be removed by the swap");
                Assert.True(0.4f == Hunger(player.Entity, CarrySlot.Back), "the back-scoped hunger entry must re-resolve to the back slot's patched value");
                Assert.True(-0.3f == Walk(player.Entity, CarrySlot.Back), "the back-scoped walk-speed entry must re-resolve to the back slot's patched value");

                Actions.Drop(player.Entity, CarrySlot.Back);
            }
            finally
            {
                hungerConfig.HandsEnabled = false;
                Mod.Config.CarryablesOnBack.Crate = crateOnBack;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 2.1.5 - dropping the carried block clears both the walk-speed and the hunger-rate
        /// entries, so the penalties cannot outlive the carry.
        /// </summary>
        [Trait("summary", "Dropping the carried block clears both the walk-speed and the hunger-rate entries, so the penalties cannot outlive the carry.")]
        [Trait("pins", "Drop removes every stat entry the pickup wrote.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HungerRate_Drop_ClearsWalkAndHungerEntries()
        {
            var hungerConfig = Mod.Config.CarryHungerRate;
            hungerConfig.HandsEnabled = true;
            try
            {
                BlockPos pos = World.Spawn.Offset(4, 1, 0);
                Place(CrateCode, pos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
                Saturate(player);

                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var failure) != null,
                    $"crate hands pickup failed: {failure}");
                Assert.NotNull(Hunger(player.Entity, CarrySlot.Hands));
                Assert.NotNull(Walk(player.Entity, CarrySlot.Hands));

                Actions.Drop(player.Entity, CarrySlot.Hands);

                Assert.True(Hunger(player.Entity, CarrySlot.Hands) == null, "dropping must clear the hunger-rate entry");
                Assert.True(Walk(player.Entity, CarrySlot.Hands) == null, "dropping must clear the walk-speed entry");
            }
            finally
            {
                hungerConfig.HandsEnabled = false;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 2.1.6 / 2.1.7 - the hunger enable flags are per-slot: with both off the pickup
        /// writes no hunger entry at all, while the walk-speed entries the same pickup
        /// writes stay in place (the two flags are separate switches).
        /// </summary>
        [Trait("summary", "With both hunger flags off neither pickup writes a hunger entry, while the walk entries the same pickups write stay in place.")]
        [Trait("pins", "The hunger enable flags are per-slot and independent of the walk-speed switches.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HungerRate_SlotFlagsOff_WritesNoHungerEntry()
        {
            var hungerConfig = Mod.Config.CarryHungerRate;
            var crateOnBack = Mod.Config.CarryablesOnBack.Crate;
            hungerConfig.HandsEnabled = false;
            hungerConfig.BackEnabled = false;
            Mod.Config.CarryablesOnBack.Crate = true;
            try
            {
                BlockPos pos = World.Spawn.Offset(4, 1, 0);
                Place(CrateCode, pos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
                Saturate(player);

                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var handsFailure) != null,
                    $"crate hands pickup failed: {handsFailure}");
                Assert.True(Hunger(player.Entity, CarrySlot.Hands) == null, "HandsEnabled=false must suppress the hunger-rate write");
                Assert.True(-0.3f == Walk(player.Entity, CarrySlot.Hands), "premise: the walk-speed write is unaffected by the hunger flags");
                Actions.Drop(player.Entity, CarrySlot.Hands);

                Place(CrateCode, pos);
                Assert.True(Actions.PickUpIntoSlot(player.Entity, pos, CarrySlot.Back, out var backFailure) != null,
                    $"crate back pickup failed: {backFailure}");
                Assert.True(Hunger(player.Entity, CarrySlot.Back) == null, "BackEnabled=false must suppress the hunger-rate write");
                Assert.True(-0.3f == Walk(player.Entity, CarrySlot.Back), "premise: the back walk-speed write is unaffected by the hunger flags");
                Actions.Drop(player.Entity, CarrySlot.Back);
            }
            finally
            {
                hungerConfig.BackEnabled = true;
                Mod.Config.CarryablesOnBack.Crate = crateOnBack;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 2.1.8 - a configured <c>CarryHungerRate.ModifierOverrides.ByBlockCode</c> entry
        /// beats the hunger modifier the carryable patch declares for the slot.
        /// </summary>
        [Trait("summary", "A configured CarryHungerRate.ModifierOverrides.ByBlockCode entry of 0.9 beats the crate patch's 0.3 hunger slot value.")]
        [Trait("pins", "Configured hunger block-code overrides win over the value the carryable patch declares for the slot.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HungerRate_BlockCodeOverride_BeatsPatchedSlotValue()
        {
            var hungerConfig = Mod.Config.CarryHungerRate;
            var entries = hungerConfig.ModifierOverrides.ByBlockCode;
            var overrideEntry = new SlotModifierConfig { Key = CrateCode, Hands = 0.9f };
            hungerConfig.HandsEnabled = true;
            entries.Add(overrideEntry);
            try
            {
                BlockPos pos = World.Spawn.Offset(4, 1, 0);
                Place(CrateCode, pos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
                Saturate(player);

                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var failure) != null,
                    $"crate hands pickup failed: {failure}");
                Assert.True(0.9f == Hunger(player.Entity, CarrySlot.Hands), "the configured block-code override must win over the patched 0.3 slot value");

                Actions.Drop(player.Entity, CarrySlot.Hands);
            }
            finally
            {
                entries.Remove(overrideEntry);
                hungerConfig.HandsEnabled = false;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 2.1.9 - the chest patch declares its hunger modifier through per-type and
        /// per-group maps only, so the hands entry must equal the value the shipped patch
        /// data predicts for the block's own type/group (type map, then group map, then the
        /// configured slot default when neither matches the variant in this build). The row
        /// prefers a chest whose type actually matches one of the declared maps.
        /// </summary>
        [Trait("summary", "A chest whose variant matches the patch's per-type or per-group hunger maps resolves to the value the shipped patch data predicts.")]
        [Trait("pins", "Hunger modifiers declared through per-type and per-group maps resolve against the block's own variant and type group.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HungerRate_TypeAndGroupMaps_MatchDeclaredPatchData()
        {
            Block? mapped = null;
            Block? fallback = null;
            foreach (var candidate in World.Api.World.Blocks)
            {
                if (candidate == null || candidate.Id == 0 || candidate.Code == null) continue;
                var code = candidate.Code.ToString();
                if (code == null || !code.Contains("chest", StringComparison.OrdinalIgnoreCase)) continue;

                var behavior = candidate.GetBehavior<BlockBehaviorCarryable>();
                var hands = behavior?.Slots[CarrySlot.Hands];
                if (behavior == null || hands == null) continue;
                if (hands.HungerModifierByGroup == null || hands.HungerModifierByGroup.Count == 0) continue;

                fallback ??= candidate;
                var type = CarryType(candidate);
                if (string.IsNullOrWhiteSpace(type)) continue;

                if (TryGet(hands.HungerModifierByType, type!, out _))
                {
                    mapped = candidate;
                    break;
                }

                if (behavior.TypeGroup.TryGetValue(type!, out var group)
                    && TryGet(hands.HungerModifierByGroup, group, out _))
                {
                    mapped = candidate;
                    break;
                }
            }

            var chest = mapped ?? fallback;
            Assert.True(chest != null, "row premise: no chest block with hunger type/group maps is registered");
            var chestCode = chest!.Code!.ToString()!;
            var carryable = chest.GetBehavior<BlockBehaviorCarryable>();
            var chestHands = carryable!.Slots[CarrySlot.Hands]!;
            Assert.True(chestHands.HungerModifierByGroup.Count > 0, "row premise: the chest patch must declare a per-group hunger map");

            var expected = ExpectedHunger(chest, carryable, chestHands);
            var matchedMap = mapped != null;

            BlockPos pos = World.Spawn.Offset(6, 1, 0);
            Place(chestCode, pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            Saturate(player);
            Mod.Config.CarryHungerRate.HandsEnabled = true;
            try
            {
                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var failure) != null,
                    $"chest hands pickup failed: {failure}");
                var because = $"hungerrate for {chestCode} (type '{CarryType(chest) ?? "<none>"}', "
                    + $"type-map match: {matchedMap}) must equal the value the shipped patch data predicts";
                Assert.True(expected == Hunger(player.Entity, CarrySlot.Hands), because);

                Actions.Drop(player.Entity, CarrySlot.Hands);
            }
            finally
            {
                Mod.Config.CarryHungerRate.HandsEnabled = false;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 1.2.2 - the saturation gate: with saturation at its maximum the hunger modifier
        /// applies, and once <c>MinSaturationThreshold</c> is raised above the player's
        /// saturation the same pickup writes nothing (the modifier is zeroed rather than
        /// applied).
        /// </summary>
        [Trait("summary", "With the saturation gate open the patched hunger modifier applies, and once MinSaturationThreshold is raised above the player's saturation the same pickup writes nothing.")]
        [Trait("pins", "The minimum-saturation threshold gates whether the hunger modifier is applied.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HungerRate_SaturationThreshold_GatesApplication()
        {
            var hungerConfig = Mod.Config.CarryHungerRate;
            var originalThreshold = hungerConfig.MinSaturationThreshold;
            hungerConfig.HandsEnabled = true;
            try
            {
                BlockPos pos = World.Spawn.Offset(4, 1, 0);
                Place(CrateCode, pos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
                var hunger = Saturate(player);
                hungerConfig.MinSaturationThreshold = 0f;

                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var belowFailure) != null,
                    $"crate hands pickup failed: {belowFailure}");
                Assert.True(0.3f == Hunger(player.Entity, CarrySlot.Hands), "with the gate open (threshold 0) the patched hunger modifier must apply");
                Actions.Drop(player.Entity, CarrySlot.Hands);

                hungerConfig.MinSaturationThreshold = (float)hunger.Saturation + 1000f;
                Place(CrateCode, pos);
                Assert.True(Actions.PickUpIntoHands(player.Entity, pos, out var aboveFailure) != null,
                    $"crate hands re-pickup failed: {aboveFailure}");
                Assert.True(Hunger(player.Entity, CarrySlot.Hands) == null, "below the saturation threshold no hunger-rate entry must be written");
                Actions.Drop(player.Entity, CarrySlot.Hands);
            }
            finally
            {
                hungerConfig.MinSaturationThreshold = originalThreshold;
                hungerConfig.HandsEnabled = false;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }
    }
}
