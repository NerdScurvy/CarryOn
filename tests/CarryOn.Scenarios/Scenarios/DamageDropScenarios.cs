using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Server.Behaviors;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Damage-drop rows for the shipped <c>carryon:dropondamage</c> behavior: the behavior
    /// is attached to joined players, a sub-lethal hit clears the hands carry while the
    /// back carry survives until its own (higher) threshold is crossed, the thresholds are
    /// strict comparisons, a player carrying nothing just takes the hit, and the
    /// <c>DropCarriedOnDamage</c> config flag turns the hands drop off. Rows drive
    /// <c>Entity.ReceiveDamage</c> with the same synthetic source the death-drop row uses,
    /// so the behavior runs before health is applied.
    /// </summary>
    [Trait("area", "Drop")]
    [Trait("summary", "Damage-drop rows: joined players take a synthetic Entity.ReceiveDamage hit, and the row asserts the dropondamage behavior attaches and drops the hands or back carry at its configured thresholds.")]
    public class DamageDropScenarios : CarryOnScenarioBase
    {
        private const string CrateCode = "game:crate";
        private const string ChestCode = "game:chest-east";

        private void Hit(EntityPlayer player, float damage)
        {
            player.ReceiveDamage(
                new DamageSource { Type = EnumDamageType.BluntAttack, SourcePos = player.Pos.XYZ },
                damage);
        }

        /// <summary>
        /// 4.1 premise - the shipped patch attaches <c>carryon:dropondamage</c> to
        /// humanoid players, so the behavior (not some test seam) is what the damage rows
        /// below exercise.
        /// </summary>
        [Trait("summary", "The shipped patch attaches carryon:dropondamage to a joined player, so the damage rows exercise the real behavior rather than a test seam.")]
        [Trait("pins", "The behavior under test is attached by the shipped patch.")]
        [Trait("role", "premise")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Dropondamage_BehaviorAttachedToJoinedPlayer()
        {
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var behavior = player.Entity.GetBehavior<EntityBehaviorDropCarriedOnDamage>();
            Assert.True(behavior != null, "the dropondamage patch must attach carryon:dropondamage to a joined player");
            Assert.Equal(EntityBehaviorDropCarriedOnDamage.Name,
                behavior!.PropertyName());

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 4.1.1 - a sub-lethal hit of 5 (above the 1.0 hands threshold, below the 6.0
        /// back threshold) drops only the hands carry: the back carry stays carried and the
        /// player survives.
        /// </summary>
        [Trait("summary", "A 5-point hit, above the 1.0 hands threshold and below the 6.0 back threshold, drops the hands carry and leaves the back carry and the player alive.")]
        [Trait("pins", "Hands and back drops are governed by separate thresholds in one hit.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SubLethalHit_DropsHandsCarry_BackCarrySurvives()
        {
            BlockPos cratePos = World.Spawn.Offset(2, 1, 0);
            BlockPos chestPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock(CrateCode, cratePos);
            World.SetBlock(ChestCode, chestPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            Assert.True(Actions.PickUpIntoHands(player.Entity, cratePos, out var handsCode) != null,
                $"crate hands pickup failed: {handsCode}");
            Assert.True(Actions.PickUpIntoSlot(player.Entity, chestPos, CarrySlot.Back, out var backCode) != null,
                $"chest back pickup failed: {backCode}");

            Hit(player.Entity, 5f);
            await World.Ticks(4);

            Assert.True(player.Entity.Alive, "a 5-point hit must not kill the test player");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "5 damage exceeds the 1.0 hands threshold and must drop the hands carry");
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Back),
                "5 damage is below the 6.0 back threshold and must leave the back carry alone");
        }

        /// <summary>
        /// 4.1 threshold boundary - the back drop uses a strict comparison, so a hit equal
        /// to the 6.0 threshold leaves the carry in place and only a hit above it drops.
        /// Each half uses its own player (and hit) because a second <c>ReceiveDamage</c> on
        /// the same player inside the hurt window would be swallowed as a no-op.
        /// </summary>
        [Trait("summary", "The back drop is a strict comparison: a hit equal to the 6.0 threshold survives and a hit above it drops, each on its own player.")]
        [Trait("pins", "The back threshold is strict, and each half uses a fresh player because a second hit in the hurt window is swallowed.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BackCarry_AtThresholdSurvives_AboveThresholdDrops()
        {
            BlockPos atPos = World.Spawn.Offset(4, 1, 0);
            BlockPos abovePos = World.Spawn.Offset(6, 1, 0);
            World.SetBlock(ChestCode, atPos);
            World.SetBlock(ChestCode, abovePos);

            var threshold = Mod.Config.DropCarriedOnDamage.BackDamageThreshold;
            Assert.True(6.0f == threshold, "row premise: the back threshold ships at 6.0");

            ITestPlayer atThreshold = await JoinPlayer("AtThreshold");
            await atThreshold.TeleportTo(World.Spawn.Offset(3, 0, 2));
            Assert.True(Actions.PickUpIntoSlot(atThreshold.Entity, atPos, CarrySlot.Back, out var atCode) != null,
                $"chest back pickup failed: {atCode}");

            Hit(atThreshold.Entity, threshold);
            await World.Ticks(4);
            Assert.True(Carry.IsCarrySlotOccupied(atThreshold.Entity, CarrySlot.Back),
                $"a hit exactly equal to the {threshold} threshold must not drop the back carry");
            Assert.True(atThreshold.Entity.Alive, "the threshold hit must stay sub-lethal");

            ITestPlayer above = await JoinPlayer("Above");
            await above.TeleportTo(World.Spawn.Offset(5, 0, 2));
            Assert.True(Actions.PickUpIntoSlot(above.Entity, abovePos, CarrySlot.Back, out var aboveCode) != null,
                $"chest back pickup failed: {aboveCode}");

            Hit(above.Entity, threshold + 0.5f);
            await World.Ticks(4);
            Assert.False(Carry.IsCarrySlotOccupied(above.Entity, CarrySlot.Back),
                $"a hit above the {threshold} threshold must drop the back carry");
            Assert.True(above.Entity.Alive, "the above-threshold hit must stay sub-lethal");
        }

        /// <summary>
        /// 4.1 no-op path - a player carrying nothing still takes the damage (health drops,
        /// the player lives) and the behavior simply has no slot to drop.
        /// </summary>
        [Trait("summary", "A hit with no carry still applies damage, drops health, and leaves the player alive with both slots empty.")]
        [Trait("pins", "The no-carry path is a no-op for the drop logic while damage still applies.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HitWhileCarryingNothing_AppliesDamageWithoutError()
        {
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var health = player.Entity.GetBehavior<EntityBehaviorHealth>();
            Assert.True(health != null, "row premise: a survival player must carry the health behavior");
            var before = health!.Health;

            Hit(player.Entity, 5f);
            await World.Ticks(4);

            Assert.True(health.Health < before,
                $"the hit must still apply damage without a carry ({before} -> {health.Health})");
            Assert.True(player.Entity.Alive, "a 5-point hit must not kill the test player");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Back));
        }

        /// <summary>
        /// 4.1.6 - <c>DropCarriedOnDamage.HandsEnabled=false</c> turns the hands drop off,
        /// so the same hit that cleared the carry above now leaves it in place.
        /// </summary>
        [Trait("summary", "With DropCarriedOnDamage.HandsEnabled set false, the same 5-point hit leaves the hands carry in place.")]
        [Trait("pins", "The hands drop is gated by the config flag.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandsFlagDisabled_HandsCarrySurvivesHit()
        {
            var dropConfig = Mod.Config.DropCarriedOnDamage;
            dropConfig.HandsEnabled = false;
            try
            {
                BlockPos cratePos = World.Spawn.Offset(2, 1, 0);
                World.SetBlock(CrateCode, cratePos);

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

                Assert.True(Actions.PickUpIntoHands(player.Entity, cratePos, out var handsCode) != null,
                    $"crate hands pickup failed: {handsCode}");

                Hit(player.Entity, 5f);
                await World.Ticks(4);

                Assert.True(player.Entity.Alive, "a 5-point hit must not kill the test player");
                Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                    "with DropCarriedOnDamage.HandsEnabled=false the hands carry must survive the hit");
            }
            finally
            {
                dropConfig.HandsEnabled = true;
            }

            Assert.NotNull(World.Api);
            await Task.CompletedTask;
        }
    }
}
