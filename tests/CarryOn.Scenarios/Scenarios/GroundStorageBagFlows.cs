using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Ground-storage bag flows over the public manager seam: picking bags out of
    /// game:groundstorage slots into the hands slot (wearable hunter backpack plus the
    /// held-only pair - linen sack and hand basket - that never render worn), the
    /// empty-slot rejection, stowing the carried bag into the vanilla backpack
    /// inventory (with free and full backpack states), and the place-down matrix:
    /// a carried quiver landing in the aimed half of an existing WallHalves pile, and
    /// a new wall-attached storage created next to a wall.
    /// </summary>
    [Trait("area", "Pickup/Place-down")]
    [Trait("summary", "Ground-storage bag flows: picking wearable and held-only bags out of storage slots into Hands, empty-slot and full-backpack rejections, stowing into the vanilla backpack, and the place-down matrix for existing piles and new wall-attached storage.")]
    public class GroundStorageBagFlows : CarryOnScenarioBase
    {
        /// <summary>
        /// The bag leaves its ground-storage slot and becomes the carried hands block; the
        /// now-empty storage self-deletes. Positive guard for the dedicated bag pickup branch.
        /// </summary>
        [Trait("summary", "Picking the hunter backpack out of a ground-storage slot through an exact-hit selection carries it in Hands as an item and the emptied storage cell becomes air.")]
        [Trait("pins", "The dedicated bag-pickup branch moves the bag item out of storage and empty storages vanish per vanilla behavior.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickupBagFromGroundStorage_CarriesInHandsAndStorageClears()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            BlockSelection selection = GroundStorageFixture.PlaceBag(World.Api.World, pos, GroundStorageFixture.BagCode);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, selection, out var failureCode);

            Assert.True(carried != null, $"Bag pickup failed: {failureCode}");
            Assert.Equal("game:hunterbackpack", carried?.ItemStack?.Collectible?.Code?.ToString());
            Assert.Equal(CarrySlot.Hands, carried?.Slot);
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Equal(0, World.BlockAt(pos).Id);
        }

        /// <summary>
        /// A ground-storage slot with no bag (no storage properties adopted) is not a bag
        /// target; pickup rejects with not-carryable and leaves the storage in place.
        /// </summary>
        [Trait("summary", "Targeting a ground-storage slot with no bag stack rejects with not-carryable, leaving the storage in place and the hands empty.")]
        [Trait("pins", "Bag pickup applies only to slots that actually hold a bag-like stack.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickupBag_EmptyTargetSlot_RejectsWithNotCarryable()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            BlockSelection selection = GroundStorageFixture.PlaceEmptyStorage(World.Api.World, pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, selection, out var failureCode);

            Assert.Null(carried);
            Assert.Equal(FailureCodes.NotCarryable, failureCode);
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Equal("game:groundstorage", World.BlockAt(pos).Code?.ToString());
        }

        /// <summary>
        /// Stowing the carried bag moves it into the vanilla backpack inventory and clears
        /// the hands slot.
        /// </summary>
        [Trait("summary", "Stowing the carried bag moves it into the vanilla backpack inventory and clears the hands carry slot.")]
        [Trait("pins", "Stow moves the item into vanilla backpack slots.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StoreBagInBackpack_MovesCarriedBagIntoBackpackInventory()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            BlockSelection selection = GroundStorageFixture.PlaceBag(World.Api.World, pos, GroundStorageFixture.BagCode);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, selection, out var pickupCode);
            Assert.True(carried != null, $"Bag pickup failed: {pickupCode}");

            bool stored = Actions.StoreBagInBackpack(player.Entity, out var storeCode);

            Assert.True(stored, $"Backpack store failed: {storeCode}");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));

            var backpack = player.Player.InventoryManager.GetOwnInventory("backpack");
            Assert.NotNull(backpack);
            bool bagFound = backpack!.Any(s => s is ItemSlotBackpack && s.Itemstack != null
                && s.Itemstack.Collectible?.Code?.ToString() == "game:hunterbackpack");
            Assert.True(bagFound, "Carried bag not found in the vanilla backpack inventory");
        }

        /// <summary>
        /// With every vanilla backpack slot full, stowing rejects with the backpack-full
        /// failure code and the bag stays carried.
        /// </summary>
        [Trait("summary", "With every vanilla backpack slot pre-filled, stowing rejects with backpack-slots-full and the bag stays carried.")]
        [Trait("pins", "A full-backpack rejection retains the carry rather than dropping or losing it.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StoreBagInBackpack_BackpackFull_RejectsAndKeepsCarried()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            BlockSelection selection = GroundStorageFixture.PlaceBag(World.Api.World, pos, GroundStorageFixture.BagCode);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var backpack = player.Player.InventoryManager.GetOwnInventory("backpack");
            Assert.NotNull(backpack);
            var fillStack = new ItemStack(World.Api.World.GetItem(new AssetLocation("stick")));
            foreach (var slot in backpack!.Where(s => s is ItemSlotBackpack))
            {
                slot.Itemstack = fillStack.Clone();
                slot.MarkDirty();
            }

            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, selection, out var pickupCode);
            Assert.True(carried != null, $"Bag pickup failed: {pickupCode}");

            bool stored = Actions.StoreBagInBackpack(player.Entity, out var storeCode);

            Assert.False(stored, "Store must fail when the backpack is full");
            Assert.Equal(FailureCodes.BackpackSlotsFull, storeCode);
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Equal("game:hunterbackpack",
                Carry.GetCarried(player.Entity, CarrySlot.Hands)?.ItemStack?.Collectible?.Code?.ToString());
        }

        /// <summary>
        /// A held-only linen sack (no attachableToEntity attribute, never renders worn)
        /// picks up out of ground storage into the hands slot; the storage self-deletes.
        /// </summary>
        [Trait("summary", "A held-only linen sack with no wearable attribute picks up out of ground storage into Hands and the storage self-deletes.")]
        [Trait("pins", "The bag branch does not require the wearable attribute.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickupLinenSackFromGroundStorage_CarriesInHandsAndStorageClears()
        {
            await AssertHeldOnlyBagPickup("linensack");
        }

        /// <summary>
        /// A held-only hand basket (variant-coded game:basket-*, no attachableToEntity
        /// attribute) picks up the same way - the dedicated bag pickup branch covers bags
        /// beyond the wearable hunter backpack.
        /// </summary>
        [Trait("summary", "A variant-coded hand basket, also held-only, picks up out of ground storage the same way and the storage self-deletes.")]
        [Trait("pins", "The dedicated bag branch covers bags beyond the wearable hunter backpack.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickupHandBasketFromGroundStorage_CarriesInHandsAndStorageClears()
        {
            await AssertHeldOnlyBagPickup("basket");
        }

        /// <summary>
        /// A carried quiver placed down onto an existing quiver pile (WallHalves layout)
        /// with a selection aiming at the pile's other half lands in that aimed slot -
        /// not the default slot 0 - and both slots then hold a quiver.
        /// </summary>
        [Trait("summary", "A carried quiver placed onto an existing quiver pile with a selection aiming at the other half lands in that aimed slot, leaving both slots holding a quiver.")]
        [Trait("pins", "Bag place-down resolves the hit-position slot like vanilla's own placement.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PlaceDownQuiverIntoExistingPile_LandsInAimedHalfSlot()
        {
            BlockPos pilePos = World.Spawn.Offset(2, 1, 0);
            GroundStorageFixture.PlaceBag(World.Api.World, pilePos, "quiver");

            BlockPos sourcePos = World.Spawn.Offset(4, 1, 0);
            BlockSelection sourceSelection = GroundStorageFixture.PlaceBag(World.Api.World, sourcePos, "quiver");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));

            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, sourceSelection, out var pickupFailure);
            Assert.True(carried != null, $"Quiver pickup failed: {pickupFailure}");
            Assert.Equal(0, World.BlockAt(sourcePos).Id);

            var aimed = new BlockSelection
            {
                Position = pilePos,
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.75, 0.5, 0.75),
                SelectionBoxIndex = 0,
            };

            BlockPos? placedAt = Actions.PlaceDown(player.Player, CarrySlot.Hands, aimed, out var failureCode);

            Assert.True(placedAt != null, $"Quiver place-down failed: {failureCode}");
            Assert.Equal(pilePos, placedAt);
            Assert.Equal("game:groundstorage", World.BlockAt(pilePos).Code?.ToString());

            var storage = World.Api.World.BlockAccessor.GetBlockEntity(pilePos) as BlockEntityGroundStorage;
            Assert.NotNull(storage);
            Assert.Equal("game:quiver", storage!.Inventory[0].Itemstack?.Collectible?.Code?.ToString());
            Assert.Equal("game:quiver", storage.Inventory[1].Itemstack?.Collectible?.Code?.ToString());
            Assert.Same(storage.Inventory[1], storage.GetSlotAt(aimed));
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// A carried quiver placed down onto open ground next to a wall creates a new
        /// ground storage attached to that wall: the storage materializes above the
        /// clicked ground cell, hang face pointing at the wall, quiver in the aimed
        /// half's slot, hands cleared.
        /// </summary>
        [Trait("summary", "A carried quiver placed onto open ground next to a wall creates a new wall-attached ground storage above the clicked cell, with the quiver in the aimed half's slot.")]
        [Trait("pins", "The create-new-storage half of the bag place-down branch.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PlaceDownQuiverNearWall_CreatesWallAttachedStorage()
        {
            BlockPos groundPos = World.Spawn.Offset(2, 0, 0);
            Assert.NotEqual(0, World.BlockAt(groundPos).Id);

            BlockPos cell = groundPos.UpCopy(1);
            BlockPos wallPos = cell.NorthCopy();
            World.SetBlock(World.BlockAt(groundPos).Code!.ToString(), wallPos);

            BlockPos sourcePos = World.Spawn.Offset(4, 1, 0);
            BlockSelection sourceSelection = GroundStorageFixture.PlaceBag(World.Api.World, sourcePos, "quiver");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, sourceSelection, out var pickupFailure);
            Assert.True(carried != null, $"Quiver pickup failed: {pickupFailure}");

            var aimed = new BlockSelection
            {
                Position = groundPos,
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.5, 0.5, 0.5),
                SelectionBoxIndex = 0,
            };

            BlockPos? placedAt = Actions.PlaceDown(player.Player, CarrySlot.Hands, aimed, out var failureCode);

            Assert.True(placedAt != null, $"Quiver wall creation failed: {failureCode}");
            Assert.Equal(cell, placedAt);
            Assert.Equal("game:groundstorage", World.BlockAt(cell).Code?.ToString());

            var storage = World.Api.World.BlockAccessor.GetBlockEntity(cell) as BlockEntityGroundStorage;
            Assert.NotNull(storage);
            Assert.Equal(BlockFacing.NORTH, storage!.AttachFace);
            Assert.Equal(1, storage.Inventory.Count(
                s => s.Itemstack?.Collectible?.Code?.ToString() == "game:quiver"));
            Assert.Equal("game:quiver",
                storage.GetSlotAt(aimed)?.Itemstack?.Collectible?.Code?.ToString());
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// Phase 3 - the ground-storage bag pickup reach gate, survival half. The bag sits well
        /// beyond the configured reach, so reach is the only reason to refuse. The failure code
        /// is <b>block-out-of-reach</b>, not the cannot-interact this path used to answer: the
        /// code names the kind of target, and the target here is a block cell. Nothing pinned the
        /// old code, so this row is where the new one is pinned instead. The storage stays in the
        /// world with its bag still in the slot, and the hands stay empty.
        /// </summary>
        [Trait("summary", "A survival player whose bag sits well beyond configured reach is refused with block-out-of-reach, the storage keeps its bag and the hands stay empty.")]
        [Trait("pins", "Reaching too far is distinguishable from a block that is not carryable, answered with block-out-of-reach rather than cannot-interact.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before the fix, when this branch refused with cannot-interact instead of block-out-of-reach.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickupBagBeyondReach_Survival_RejectsWithBlockOutOfReach()
        {
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;
            BlockPos pos = World.Spawn.Offset(reach + 8, 1, 0);
            BlockSelection selection = GroundStorageFixture.PlaceBag(
                World.Api.World, pos, GroundStorageFixture.BagCode);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            Assert.True(pos.ToVec3d().DistanceTo(player.Entity.Pos.XYZ) > reach + 2,
                "Row premise: the bag must sit far enough out that either anchor rejects");

            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, selection, out var failureCode);

            Assert.Null(carried);
            Assert.Equal(FailureCodes.BlockOutOfReach, failureCode);
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Equal("game:groundstorage", World.BlockAt(pos).Code?.ToString());
            var kept = World.Api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityGroundStorage;
            Assert.NotNull(kept);
            Assert.Equal("game:hunterbackpack",
                kept!.Inventory[0].Itemstack?.Collectible?.Code?.ToString());
        }

        /// <summary>
        /// The creative twin of the ground-storage reach row: a creative player lifts the same
        /// distant bag out of its slot, pinning the creative exemption inside the shared reach
        /// predicate on this third path (Decision 19).
        /// </summary>
        [Trait("summary", "A creative player lifts the same out-of-reach bag out of its slot into Hands and the emptied storage self-deletes.")]
        [Trait("pins", "The creative exemption on the ground-storage bag-pickup path.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed without the production change that added the creative exemption.")]
        [Trait("decision", "19")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickupBagBeyondReach_Creative_Allowed()
        {
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;
            BlockPos pos = World.Spawn.Offset(reach + 8, 1, 0);
            BlockSelection selection = GroundStorageFixture.PlaceBag(
                World.Api.World, pos, GroundStorageFixture.BagCode);

            ITestPlayer player = await JoinPlayer("Tester", EnumGameMode.Creative);
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));
            Assert.True(pos.ToVec3d().DistanceTo(player.Entity.Pos.XYZ) > reach + 2,
                "Row premise: the bag must sit far enough out that either anchor rejects");

            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, selection, out var failureCode);

            Assert.True(carried != null, $"Creative must lift a bag beyond reach - {failureCode}");
            Assert.Equal("game:hunterbackpack", carried?.ItemStack?.Collectible?.Code?.ToString());
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Equal(0, World.BlockAt(pos).Id);
        }

        private async Task AssertHeldOnlyBagPickup(string itemCodePart)
        {
            string itemCode = GroundStorageFixture.ResolveItemCode(World.Api.World, itemCodePart);
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            BlockSelection selection = GroundStorageFixture.PlaceBag(World.Api.World, pos, itemCode);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, selection, out var failureCode);

            Assert.True(carried != null, $"{itemCodePart} pickup failed: {failureCode}");
            Assert.Equal(itemCode, carried?.ItemStack?.Collectible?.Code?.ToString());
            Assert.Equal(CarrySlot.Hands, carried?.Slot);
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Equal(0, World.BlockAt(pos).Id);
            var attachable = carried?.ItemStack?.ItemAttributes?["attachableToEntity"];
            Assert.False(attachable?.Exists == true
                && attachable?["categoryCode"]?.AsString() == "backpack",
                $"{itemCode} is not held-only - it renders worn like a wearable backpack");
        }
    }
}