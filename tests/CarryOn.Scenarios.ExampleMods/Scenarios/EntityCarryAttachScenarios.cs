using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Network;
using CarryOn.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// Entity-carry attach rows: the missing half of the W4 attach failure-code
    /// distinction - <c>EntityBehaviorAttachableCarryable.CanAttach</c>'s
    /// <c>slot-entity-not-attachable</c> branch. CarryEntityDemo's <c>demo-cube</c>
    /// is picked up into hands through the real entity-pickup handler
    /// (OnPickupEntityMessage), producing an entity-carried block: no block-entity
    /// data, SpawnerFactoryCode set. Aimed at a vanilla cargo boat's chest-capable
    /// slot through the real attach handler (OnAttachMessage), CarryOn deliberately
    /// rejects with slot-entity-not-attachable (an entity-carried block cannot
    /// round-trip through a slot inventory without losing its entity factory). The
    /// sibling slot-incompatible-block branch (plain no-BE block) is pinned by the
    /// main suite's VehicleAttachScenarios; this class pins the entity half.
    /// The demo cube stands in for "any carryable entity" (felled log, animal, ...).
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "Entity-carry attach rows: pick the CarryEntityDemo cube up into an entity-carried block and pin that attaching it into a cargo-boat slot is refused with slot-entity-not-attachable.")]
    public class EntityCarryAttachScenarios : CarryOnScenarioBase
    {
        /// <summary>The demo entity type CarryEntityDemo registers (domain 'carrieddemo').</summary>
        private const string DemoCubeCode = "carrieddemo:demo-cube";

        /// <summary>The pickup/spawner handler code the demo entity's carryable behavior resolves.</summary>
        private const string DemoHandlerCode = "carrieddemo:demoentity";

        /// <summary>
        /// Picking up the demo-cube entity through the real pickup handler occupies the
        /// hands carry with an entity-carried block: no block-entity data and the demo
        /// spawner factory code set - the exact premise the slot-entity-not-attachable
        /// branch keys on - and despawns the source entity.
        /// </summary>
        [Trait("summary", "Picking up the demo-cube through the real entity-pickup handler occupies the hands carry with an entity-carried block: no block-entity data, the demo spawner factory code set, and the source entity despawned as picked-up.")]
        [Trait("pins", "The entity-carried-block premise the attach rejection keys on.")]
        [Trait("role", "premise")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickupEntity_CarriedBlockCarriesSpawnerFactoryCode_NoBlockEntityData()
        {
            Entity cube = SpawnDemoCube(World.Spawn.Offset(4, 1, 0));

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));

            HandlerInvoke.PickupEntity(Mod, player.Player, new PickupEntityMessage(cube.EntityId));

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "The demo-cube pickup must occupy the hands carry");
            var carried = Carry.GetCarried(player.Entity, CarrySlot.Hands);
            Assert.NotNull(carried);
            Assert.Null(carried!.BlockEntityData);
            Assert.Equal(DemoHandlerCode, carried.SpawnerFactoryCode);

            Assert.False(cube.Alive, "The picked-up demo cube must be despawned");
            Assert.Equal(EnumDespawnReason.PickedUp, cube.DespawnReason?.Reason);
        }

        /// <summary>
        /// Attaching the carried entity-carried block to a cargo boat slot rejects with
        /// slot-entity-not-attachable, surfaces the IngameError toast on the vanilla
        /// wire, keeps the carry (entity factory code preserved), and leaves the slot
        /// empty - the W4 remainder row, through the real attach handler.
        /// </summary>
        [Trait("summary", "Attaching the carried entity-carried block into a cargo boat's chest-capable slot rejects with slot-entity-not-attachable and an IngameError toast, retains the carry with its factory code, and leaves the slot empty.")]
        [Trait("pins", "The slot-entity-not-attachable branch: an entity-carried block never round-trips through a slot inventory.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task AttachCarriedEntity_RejectsSlotEntityNotAttachable_Toast_CarryRetained_SlotEmpty()
        {
            Entity boat = World.SpawnEntity(
                BoatFixture.ResolveCargoBoatCode(World.Api.World), World.Spawn.Offset(6, 1, 4));
            Entity cube = SpawnDemoCube(World.Spawn.Offset(4, 1, 0));

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(4, 0, 2));

            HandlerInvoke.PickupEntity(Mod, player.Player, new PickupEntityMessage(cube.EntityId));
            Assert.True(Carry.GetCarried(player.Entity, CarrySlot.Hands) != null,
                "Premise: the demo cube must be carried before the attach attempt");

            var crateStack = new ItemStack(World.Api.World.GetBlock(new AssetLocation("game", "crate")));
            int slot = BoatFixture.FindChestCargoSlot(boat, crateStack);
            Assert.True(slot >= 0, "The cargo boat has no crate-capable cargo slot");

            player.Client.Clear();
            HandlerInvoke.Attach(Mod, player.Player, new AttachMessage(boat.EntityId, slot));

            var wire = VanillaClientWire.Tap(player);
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.SlotEntityNotAttachable));

            var attachable = boat.GetBehavior<EntityBehaviorAttachable>()!;
            Assert.Null(attachable.Inventory[slot].Itemstack);
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A rejected attach must keep the carry");
            Assert.Equal(DemoHandlerCode,
                Carry.GetCarried(player.Entity, CarrySlot.Hands)?.SpawnerFactoryCode);
        }

        /// <summary>
        /// The rejected attach neither consumes nor mutates the carried entity: placing
        /// the still-carried block down afterwards re-spawns the demo-cube entity through
        /// its spawner factory, and the hands carry clears.
        /// </summary>
        [Trait("summary", "The rejected attach neither consumes nor mutates the carried entity: placing the still-carried block back down re-spawns the demo-cube through its spawner factory and clears the hands.")]
        [Trait("pins", "The rejection is read-only: the carry can still be placed back down.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task RejectedAttach_PlaceDown_StillSpawnsEntity()
        {
            Entity boat = World.SpawnEntity(
                BoatFixture.ResolveCargoBoatCode(World.Api.World), World.Spawn.Offset(6, 1, 4));
            Entity cube = SpawnDemoCube(World.Spawn.Offset(4, 1, 0));

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(4, 0, 2));

            HandlerInvoke.PickupEntity(Mod, player.Player, new PickupEntityMessage(cube.EntityId));
            Assert.True(Carry.GetCarried(player.Entity, CarrySlot.Hands) != null,
                "Premise: the demo cube must be carried");

            var crateStack = new ItemStack(World.Api.World.GetBlock(new AssetLocation("game", "crate")));
            int slot = BoatFixture.FindChestCargoSlot(boat, crateStack);
            Assert.True(slot >= 0, "The cargo boat has no crate-capable cargo slot");

            // Exercise the rejection, then prove the carry is untouched by it.
            player.Client.Clear();
            HandlerInvoke.Attach(Mod, player.Player, new AttachMessage(boat.EntityId, slot));
            _ = VanillaClientWire.Tap(player);
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "The rejected attach must leave the carry intact for the place-down");

            BlockPos target = World.Spawn.Offset(2, 1, 6);
            BlockPos? placedAt = Actions.PlaceDown(player.Player, CarrySlot.Hands,
                CarryActions.AimDownAt(target), out var failureCode);
            Assert.True(placedAt != null, $"Place-down of the carried entity failed: {failureCode}");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));

            Assert.True(
                World.Api.World.LoadedEntities.Values.Any(e =>
                    e.Code?.ToString() == DemoCubeCode && e.Alive),
                "The placed entity-carried block must re-spawn the demo-cube entity");
        }

        /// <summary>
        /// Control: with the demo entity staged and carried in the same class-world shape,
        /// a plain carried chest (block-entity data present) still attaches into the same
        /// cargo boat slot - the W4 happy path is unperturbed by the entity-carry branch.
        /// </summary>
        [Trait("summary", "With the demo entity staged, a plain carried chest still attaches into the same cargo boat slot, so the attach happy path is unperturbed by the entity-carry branch.")]
        [Trait("pins", "The attach happy path is unperturbed by the new entity-carry branch.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task Control_AttachCarriedChest_SameBoatStillGreen()
        {
            Entity boat = World.SpawnEntity(
                BoatFixture.ResolveCargoBoatCode(World.Api.World), World.Spawn.Offset(6, 1, 4));

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(5, 0, 4));

            var chestStack = new ItemStack(World.Api.World.GetBlock(new AssetLocation("chest-east")));
            int slot = BoatFixture.FindChestCargoSlot(boat, chestStack);
            Assert.True(slot >= 0, "The cargo boat has no chest-capable cargo slot");

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var pickupFailure);
            Assert.True(carried != null, $"Chest pickup failed: {pickupFailure}");

            HandlerInvoke.Attach(Mod, player.Player, new AttachMessage(boat.EntityId, slot));

            var attachable = boat.GetBehavior<EntityBehaviorAttachable>()!;
            Assert.Equal("game:chest-east",
                attachable.Inventory[slot].Itemstack?.Collectible?.Code?.ToString());
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// Spawns the CarryEntityDemo cube entity and asserts the type actually resolved
        /// (a missing/mis-staged mod fails loudly here rather than at the pickup).
        /// </summary>
        private Entity SpawnDemoCube(BlockPos pos)
        {
            Entity cube = World.SpawnEntity(DemoCubeCode, pos);
            Assert.Equal(DemoCubeCode, cube.Code?.ToString());
            return cube;
        }
    }
}
