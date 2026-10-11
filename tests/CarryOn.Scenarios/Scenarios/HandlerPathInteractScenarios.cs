using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Network;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Handler-path interact-while-carrying rows: the entry point a real client's
    /// InteractMessage takes on the server (CarryServerHandler.OnInteractMessage) -
    /// the surface the wattle/bamboo gate gap fix and the namespaced gate patches
    /// (fencegate.json, gated by carryon.Interactables.Door) feed. Vanilla gates
    /// toggle their block state variant in place, so world truth at the gate cell
    /// is the observable; the vanilla OnBlockInteractStop dispatch is a no-op for
    /// gates (they commit on Start). Also pins the namespaced carryable behavior
    /// on vanilla content.
    /// </summary>
    [Trait("area", "Handler path")]
    [Trait("summary", "Handler-path interact-while-carrying rows driving the real interact message entry point: vanilla gates toggle their state variant in place while the player carries a chest, the handler no-ops on non-interactable terrain, and the placed chest carries the namespaced carryable behavior.")]
    public class HandlerPathInteractScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// A wattle gate (the headline block of the interact patch) toggles from closed to
        /// opened through the interact handler while the player carries a chest - and
        /// the carry is retained.
        /// </summary>
        [Trait("summary", "A closed wattle gate toggles to opened through the interact handler while the player carries a chest, and the carry is retained.")]
        [Trait("pins", "The wattle gate interact fix works through the real handler path while carrying.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WattleGate_TogglesOpened_WhileCarrying()
        {
            await AssertGateTogglesWhileCarrying("wattlegate");
        }

        /// <summary>
        /// A bamboo gate (the fix's second block, inconsistent file naming
        /// fence-bamboogate) toggles the same way.
        /// </summary>
        [Trait("summary", "A closed bamboo gate toggles to opened the same way, pinning the inconsistently named bamboo gate blocktype patch.")]
        [Trait("pins", "The bamboo gate is covered by the same interact fix as the wattle gate.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BambooGate_TogglesOpened_WhileCarrying()
        {
            await AssertGateTogglesWhileCarrying("bamboogate");
        }

        /// <summary>
        /// Control: the interact handler is a no-op against a block without the
        /// carryableinteract behavior - terrain stays untouched and the carry is kept.
        /// </summary>
        [Trait("summary", "The interact handler aimed at a terrain block without the carryableinteract behavior leaves the terrain unchanged and keeps the carry.")]
        [Trait("pins", "The handler does not over-fire on blocks it does not act on.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task InteractAtTerrainBlock_WhileCarrying_IsNoOp()
        {
            Block? terrain = null;
            BlockPos? terrainPos = null;
            for (int d = 1; d <= 6 && terrain == null; d++)
            {
                var pos = World.Spawn.DownCopy(d);
                var candidate = World.BlockAt(pos);
                if (candidate != null && candidate.Id != 0)
                {
                    terrain = candidate;
                    terrainPos = pos;
                }
            }
            Assert.True(terrain != null && terrainPos != null,
                "No terrain block found under the sandbox spawn");
            Assert.False(terrain!.HasBehavior<BlockBehaviorCarryableInteract>(),
                "Row premise: the terrain control must not be interact-while-carrying");

            BlockPos chestPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var pickupFailure);
            Assert.True(carried != null, $"Chest pickup failed: {pickupFailure}");

            string terrainBefore = World.BlockAt(terrainPos!).Code!.ToString();

            HandlerInvoke.Interact(Mod, player.Player,
                new InteractMessage(CarryActions.AimDownAt(terrainPos!)));

            Assert.Equal(terrainBefore, World.BlockAt(terrainPos!).Code?.ToString());
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "The no-op interact must not drop the carried chest");
        }

        /// <summary>
        /// The chest's carryable behavior is the one the namespaced JSON patches add
        /// (carryon:carryable) - pinning the namespaced-codes release note's active use
        /// on vanilla content.
        /// </summary>
        [Trait("summary", "A placed chest carries the carryable behavior added by the namespaced JSON patch, whose canonical name is carryon:carryable.")]
        [Trait("pins", "Namespaced codes are in active use on vanilla content.")]
        [Trait("role", "contract")]
        [Trait("decision", "13")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ChestCarryableBehavior_UsesNamespacedCode()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");

            var behavior = World.BlockAt(pos).GetBehavior<BlockBehaviorCarryable>();
            Assert.True(behavior != null,
                "Chest must carry BlockBehaviorCarryable - added by the namespaced JSON patch");
            Assert.Equal("carryon:carryable", BlockBehaviorCarryable.Name);
        }

        private async Task AssertGateTogglesWhileCarrying(string gateCodePart)
        {
            Assert.True(Mod.Config.Interactables.Door,
                "Default config must enable Interactables.Door for this row");

            var gate = GateFixture.FindClosedGate(World.Api.World, gateCodePart);
            BlockPos gatePos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(gate.Code.ToString(), gatePos);

            BlockPos chestPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));

            Assert.True(World.BlockAt(gatePos).HasBehavior<BlockBehaviorCarryableInteract>(),
                $"Placed {gateCodePart} must carry the carryableinteract behavior " +
                "(patched by fencegate.json)");

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var pickupFailure);
            Assert.True(carried != null, $"Chest pickup failed: {pickupFailure}");

            string before = World.BlockAt(gatePos).Code!.ToString();
            Assert.Equal("closed", GateFixture.StateOf(World.BlockAt(gatePos)));

            HandlerInvoke.Interact(Mod, player.Player,
                new InteractMessage(CarryActions.AimDownAt(gatePos)));

            string after = World.BlockAt(gatePos).Code!.ToString();
            Assert.NotEqual(before, after);
            Assert.Equal("opened", GateFixture.StateOf(World.BlockAt(gatePos)));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Interacting must not drop the carried chest");
            Assert.Equal("game:chest-east",
                Carry.GetCarried(player.Entity, CarrySlot.Hands)?.OriginalBlockCode?.ToString());
        }
    }
}
