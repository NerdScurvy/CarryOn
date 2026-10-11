using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// Premise probe: the
    /// <c>carrieddemo:multicarry-block</c>'s two <c>carryon:carryable</c>
    /// <c>behaviors</c> array entries must instantiate as TWO live behaviors. This
    /// needs a pristine boot (the class boots its own world; no row flips the
    /// conditional), so it lives in its own class rather than the flipped
    /// <see cref="MultiCarryableCoexistenceScenarios"/>:
    ///
    /// With Chest=false at a pristine boot the two shapes are distinguishable:
    ///   * two instances → reconcile skips (conditional disabled), the resolver strips
    ///     only the disabled conditional and keeps the base, and the final removal
    ///     pass leaves the base → block IS carryable (via the base, patchPriority 2).
    ///   * only one instance loaded → the lone disabled conditional is stripped →
    ///     block NOT carryable.
    ///
    /// Expected GREEN both today and after the fix; a red result here falsifies the
    /// two-instance premise (or the engine's empty-enabled-condition evaluation),
    /// which would force spike fallback (two distinct codes both registering
    /// BlockBehaviorCarryable). Also pins the final removal pass's treatment of an
    /// unconditional (empty-EnabledCondition) behavior.
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "Pristine-boot discriminator: with the conditional disabled, two same-name carryable behaviors must coexist as two instances and the unconditional base must survive.")]
    [AtlasDataFiles("fixtures/MultiCarryablePremise", TargetPath = "ModConfig")]
    public class MultiCarryableBasePremiseScenarios : CarryOnScenarioBase
    {
        private const string MultiCarryBlock = "carrieddemo:multicarry-block";

        /// <summary>
        /// With the conditional disabled at a pristine boot the unconditional base must
        /// survive: reconcile skips the disabled conditional, the resolver keeps the
        /// base (stripping only the disabled conditional), and the removal pass leaves
        /// the empty-condition base alone. This is the discriminator between "two
        /// same-name behaviors loaded as two instances" (GREEN) and "only one loaded"
        /// (RED, not carryable).
        /// </summary>
        [Trait("summary", "With the conditional disabled at a pristine boot, the unconditional base must survive: reconcile skips the disabled conditional, the resolver keeps the base, and the removal pass leaves the empty-condition base alone.")]
        [Trait("pins", "Two same-name carryable behaviors load as two live instances, and the unconditional base survives a disabled conditional.")]
        [Trait("role", "premise")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Boot_ConditionalDisabled_UnconditionalBaseSurvives()
        {
            Assert.NotNull(World.Api);
            Assert.False(Mod.Config.Carryables.Chest, "Fixture must boot with chest carryables off");
            Assert.NotNull(World.Api.World.GetBlock(new Vintagestory.API.Common.AssetLocation(MultiCarryBlock)));

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(MultiCarryBlock, pos);
            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var failureCode);

            Assert.True(carried != null,
                "premise: two same-name carryon:carryable behaviors must coexist - the unconditional base must survive a disabled conditional at a pristine boot");
            var survived = World.Api.World.GetBlock(new Vintagestory.API.Common.AssetLocation(MultiCarryBlock))
                ?.GetBehavior<BlockBehaviorCarryable>();
            Assert.NotNull(survived);
            Assert.Equal(2, survived!.PatchPriority);
            Assert.NotNull(survived.Slots[CarrySlot.Hands]);
        }
    }
}