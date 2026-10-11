using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Permission baselines over the public manager seam on a default-config world: the
    /// build-or-break grant for unclaimed, non-reinforced cells, and the current
    /// carryable-interact baseline.
    /// </summary>
    [Trait("area", "Claims/Permissions")]
    [Trait("summary", "Permission baselines on a default world: the build-or-break grant for unclaimed non-reinforced cells, and the auto-mapped interact-while-carrying baseline for chests versus terrain.")]
    public class PermissionBaselineScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// On a default server world every reachable, non-reinforced cell is granted for
        /// carry interactions.
        /// </summary>
        [Trait("summary", "On an unclaimed, non-reinforced default world, HasPermissionAt grants at both a chest cell and bare spawn ground.")]
        [Trait("pins", "The positive permission baseline that the claim rows diverge from.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HasPermissionAt_DefaultWorld_GrantsBuildOrBreak()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            Assert.True(Carry.HasPermissionAt(player.Entity, pos));
            Assert.True(Carry.HasPermissionAt(player.Entity, World.Spawn));
        }

        /// <summary>
        /// Interact-while-carrying is auto-mapped from existing carryableInteract sources to
        /// blocks sharing their entity class. Chests picked up on the map branched target
        /// surface blocks if a source exists. A non-carryable terrain block must be rejected.
        /// </summary>
        [Trait("summary", "The chest carries an auto-mapped carryableInteract behavior and CanInteractWhileCarrying returns true for it, while a terrain block carries none and returns false.")]
        [Trait("pins", "The interact-while-carrying baseline is not flat; the automapper grows the behavior onto entity-class siblings.")]
        [Trait("role", "contract")]
        [Trait("decision", "7")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task CanInteractWhileCarrying_AutoMappedFromSources_TrueForChestFalseForTerrain()
        {
            ITestPlayer player = await JoinPlayer("Tester");

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            var chest = World.BlockAt(chestPos);

            Block? ground = null;
            for (int d = 1; d <= 6 && ground == null; d++)
            {
                var candidate = World.BlockAt(World.Spawn.DownCopy(d));
                if (candidate != null && candidate.Id != 0) ground = candidate;
            }
            Assert.True(ground != null && ground.Code != null, "No non-carryable terrain block found under the sandbox spawn");
            var terrain = ground;

            var interactSources = World.Api.World.Blocks
                .Where(b => b.HasBehavior<BlockBehaviorCarryableInteract>())
                .ToArray();
            var sample = string.Join(", ", interactSources.Take(8).Select(b => $"{b.Code} [{b.EntityClass}]"));

            Assert.True(interactSources.Length > 0,
                $"No carryableInteract source blocks found; expected at least one seed. (count=0)");

            Assert.True(chest!.HasBehavior<BlockBehaviorCarryableInteract>(),
                $"Chest expected auto-mapped carryableInteract (interact sources found: {interactSources.Length} -> {sample})");
            Assert.False(terrain!.HasBehavior<BlockBehaviorCarryableInteract>(),
                $"Terrain must not carry carryableInteract (interact sources found: {interactSources.Length} -> {sample})");

            Assert.True(Carry.CanInteractWhileCarrying(chest));
            Assert.False(Carry.CanInteractWhileCarrying(terrain));
        }
    }
}