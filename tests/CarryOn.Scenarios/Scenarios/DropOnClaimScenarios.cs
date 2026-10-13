using System.Collections.Generic;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Class C - dropped carried blocks over player-owned land claims, reusing
    /// the ClaimPermissionScenarios claim topology (claim covers Spawn x/z
    /// offsets -4..+7, y 0..15 - the cuboid end-corner is exclusive; cells at
    /// +8 and +20 x/z are outside; players are survival). The carry is always
    /// acquired OUTSIDE the claim, then the player stands inside (or on the
    /// boundary) and drops. Spike-pinned outcomes: a claim
    /// DENIES the placement for a stranger before any mode-specific fallback, so
    /// EntityOnFailedPlacement spawns the carried block as an entity and Items
    /// spawns a game:item, EntityAlways always spawns an entity, while the claim
    /// owner's placement is permitted. The item-class carry twin (a bag picked
    /// from ground storage outside the claim) pins that a claim-denied
    /// Items-mode bag drop lands the carried-block entity rather than spilling
    /// it as loose items. The boundary rows (C5 feet INSIDE at the
    /// claimed maximum x=7, C6 feet at the first unclaimed cell x=8) resolve
    /// deterministically to the player's own column and pin the invariant that
    /// no claim cell is ever written and the line between claimed-inside and
    /// unclaimed-outside is drawn by the claim end-corner.
    /// </summary>
    [Trait("area", "Drop")]
    [Trait("summary", "Dropped carried blocks over player-owned land claims: a stranger's drop inside the claim falls back per mode, the owner's places, and the boundary rows pin that no claim cell is ever written; the item-class bag twin pins that a denied Items-mode drop lands the carried-block entity instead of spilling items.")]
    public class DropOnClaimScenarios : DropModeScenarioBase
    {
        private BlockPos DropInside => World.Spawn.Offset(2, 0, 2);
        private BlockPos DropEdgeInside => World.Spawn.Offset(7, 0, 2);
        private BlockPos DropBoundary => World.Spawn.Offset(8, 0, 2);
        private BlockPos FixtureOutside => World.Spawn.Offset(20, 1, 2);

        private LandClaim AddClaimAroundSpawn(IPlayer owner)
        {
            var claim = LandClaim.CreateClaim(owner);
            claim.Areas =
            [
                new Cuboidi(World.Spawn.Offset(-4, 0, -4), World.Spawn.Offset(8, 16, 8))
            ];
            World.Api.World.Claims.Add(claim);
            return claim;
        }

        /// <summary>
        /// Atlas's RollbackWorld reverts blocks but NOT claims (server save state).
        /// Every row drains stale claims so it starts from unclaimed land.
        /// </summary>
        private void ClearStaleClaims()
        {
            foreach (var claim in World.Api.World.Claims.All.ToArray())
            {
                World.Api.World.Claims.Remove(claim);
            }
        }

        private async Task<(ITestPlayer Owner, ITestPlayer Stranger)> JoinOwnerAndStrangerAsync(string ownerName, string strangerName)
        {
            var owner = await JoinPlayer(ownerName);
            var stranger = await JoinPlayer(strangerName);
            await owner.TeleportTo(DropInside);
            await stranger.TeleportTo(DropInside);
            return (owner, stranger);
        }

        /// <summary>
        /// Acquires a chest outside the claim, then stands the dropper at
        /// <paramref name="stand"/> for the drop.
        /// </summary>
        private async Task<(ITestPlayer Player, CarriedBlock Carried)> ArmChestForDropperAsync(ITestPlayer player, BlockPos stand)
        {
            var carried = await AcquireChestAsync(player, FixtureOutside);
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            await player.TeleportTo(stand);
            return (player, carried);
        }

        /// <summary>
        /// Acquires a bag from ground storage outside the claim (an item-class
        /// carry with no world block), then stands the dropper at
        /// <paramref name="stand"/> for the drop.
        /// </summary>
        private async Task<(ITestPlayer Player, CarriedBlock Carried)> ArmBagForDropperAsync(ITestPlayer player, BlockPos stand)
        {
            var bagSelection = GroundStorageFixture.PlaceBag(World.Api.World, FixtureOutside, GroundStorageFixture.BagCode);
            // Stand two cells south of the storage - the bag-pickup reach
            // geometry the bag-flow rows use, not the chest's stand-below stand.
            await player.TeleportTo(FixtureOutside.DownCopy().Add(0, 0, 2));
            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, bagSelection, out var failure);
            Assert.True(carried != null, $"Bag pickup failed: {failure}");
            Assert.Null(carried!.Block);
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            await player.TeleportTo(stand);
            return (player, carried);
        }

        private void AssertDeniedOutcome(BlockPos dropDst,
            int expectedCarriedEntities, int expectedItems)
        {
            Assert.False(Drops[0].BlockPlaced, "Claim preflight must deny the placement");
            Assert.Equal(0, CountChestsInClaimArea());
            Assert.Equal(expectedCarriedEntities, CarriedBlockEntitiesAround(dropDst, 4).Count);
            Assert.Equal(expectedItems, CountItemEntitiesAround(dropDst, 4));
        }

        /// <summary>
        /// Counts chests strictly inside the claim region (x/z offsets -4..7,
        /// y 0..15, matching the exclusive cuboid end-corner at +8/16).
        /// </summary>
        private int CountChestsInClaimArea()
        {
            int chests = 0;
            for (int dx = -4; dx <= 7; dx++)
            {
                for (int dz = -4; dz <= 7; dz++)
                {
                    for (int dy = 0; dy <= 15; dy++)
                    {
                        var block = World.BlockAt(World.Spawn.Offset(dx, dy, dz));
                        if (block.Code?.FirstCodePart() == "chest") chests++;
                    }
                }
            }
            return chests;
        }

        /// <summary>
        /// C1 - EntityOnFailedPlacement dropped by a stranger inside the claim:
        /// the claim denies the placement, so the fallback spawns the carried
        /// block as a carriedblock entity. No block anywhere in the claim, no
        /// items.
        /// </summary>
        [Trait("summary", "A stranger's EntityOnFailedPlacement drop inside a claim is denied the placement, so the carry spawns as one carriedblock entity with no chest anywhere in the claim.")]
        [Trait("pins", "A claim denies the dropped placement before the mode's placement fallback.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerDropInsideClaim_EntityOnFailedPlacement_SpawnsEntity()
        {
            AssertPremise();
            var dropDst = DropInside;
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync("ClaimOwner1", "ClaimDropper1");
            AddClaimAroundSpawn(owner.Player);
            _ = await ArmChestForDropperAsync(stranger, dropDst);

            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.EntityOnFailedPlacement))
            {
                DropHands(stranger);
            }

            AssertCarryCleared(stranger);
            Assert.Single(Drops);
            AssertDeniedOutcome(dropDst, expectedCarriedEntities: 1, expectedItems: 0);
        }

        /// <summary>
        /// C2 - EntityAlways dropped by a stranger inside the claim: entities
        /// are not claim-checked; exactly one carriedblock entity materializes
        /// and nothing is placed.
        /// </summary>
        [Trait("summary", "A stranger's EntityAlways drop inside a claim spawns exactly one carriedblock entity and places nothing.")]
        [Trait("pins", "EntityAlways is not placement-gated by a claim.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerDropInsideClaim_EntityAlways_SpawnsEntityInClaim()
        {
            AssertPremise();
            var dropDst = DropInside;
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync("ClaimOwner2", "ClaimDropper2");
            AddClaimAroundSpawn(owner.Player);
            _ = await ArmChestForDropperAsync(stranger, dropDst);

            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.EntityAlways))
            {
                DropHands(stranger);
            }

            AssertCarryCleared(stranger);
            Assert.Single(Drops);
            AssertDeniedOutcome(dropDst, expectedCarriedEntities: 1, expectedItems: 0);
        }

        /// <summary>
        /// C3 - Items mode dropped by a stranger inside the claim: the claim
        /// denies the placement and the mode genuinely falls back to spawning a
        /// game:item. No carriedblock entity, no chest in the claim.
        /// </summary>
        [Trait("summary", "A stranger's Items-mode drop inside a claim falls back to one game:item, with no carriedblock entity and no chest in the claim.")]
        [Trait("pins", "The item fallback of a claim-denied drop.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerDropInsideClaim_ItemsMode_DropsItemOnClaim()
        {
            AssertPremise();
            var dropDst = DropInside;
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync("ClaimOwner3", "ClaimDropper3");
            AddClaimAroundSpawn(owner.Player);
            _ = await ArmChestForDropperAsync(stranger, dropDst);

            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.Items))
            {
                DropHands(stranger);
            }

            AssertCarryCleared(stranger);
            Assert.Single(Drops);
            AssertDeniedOutcome(dropDst, expectedCarriedEntities: 0, expectedItems: 1);
        }

        /// <summary>
        /// C4 - the claim owner drops inside their own claim with
        /// EntityOnFailedPlacement: placement is permitted and the chest lands
        /// inside the claim; no entity or item.
        /// </summary>
        [Trait("summary", "The claim owner's EntityOnFailedPlacement drop inside their own claim places the chest, with no entity or item.")]
        [Trait("pins", "The claim gate must not over-block owners.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task OwnerDropInsideClaim_EntityOnFailedPlacement_PlacesChest()
        {
            AssertPremise();
            var dropDst = DropInside;
            ClearStaleClaims();

            var (owner, _) = await JoinOwnerAndStrangerAsync("ClaimOwner4", "ClaimDropper4");
            AddClaimAroundSpawn(owner.Player);
            _ = await ArmChestForDropperAsync(owner, dropDst);

            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.EntityOnFailedPlacement))
            {
                DropHands(owner);
            }

            AssertCarryCleared(owner);
            Assert.Single(Drops);
            Assert.True(Drops[0].BlockPlaced, "Owner placement inside own claim must succeed");
            Assert.Equal(1, CountChestsInClaimArea());
            Assert.Empty(CarriedBlockEntitiesAround(dropDst, 4));
            Assert.Equal(0, CountItemEntitiesAround(dropDst, 4));
        }

        /// <summary>
        /// C5 - stranger stands one cell inside the claim edge (x=7, the
        /// claimed maximum; the cuboid end-corner is exclusive) and drops with
        /// EntityOnFailedPlacement: the placement
        /// footprint still reads as contested, EFPM denies and the carried
        /// chest spawns as an entity. Proves the near-edge geometry does NOT
        /// slip past the preflight.
        /// </summary>
        [Trait("summary", "A stranger standing at the claimed maximum x-cell drops with EntityOnFailedPlacement and is still denied, spawning one carriedblock entity.")]
        [Trait("pins", "The near-edge inside cell still reads as contested.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerDropAtClaimEdge_EntityOnFailedPlacement_SpawnsEntity()
        {
            AssertPremise();
            var dropDst = DropEdgeInside;
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync("ClaimOwner5", "ClaimDropper5");
            AddClaimAroundSpawn(owner.Player);
            _ = await ArmChestForDropperAsync(stranger, dropDst);

            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.EntityOnFailedPlacement))
            {
                DropHands(stranger);
            }

            AssertCarryCleared(stranger);
            Assert.Single(Drops);
            AssertDeniedOutcome(dropDst, expectedCarriedEntities: 1, expectedItems: 0);
        }

        /// <summary>
        /// C6 - stranger stands at the first cell OUTSIDE the claim (x=8, the
        /// exclusive cuboid end) and drops with
        /// EntityOnFailedPlacement: all footprint cells are unclaimed, placement
        /// succeeds and the chest lands at the boundary footprint. Invariant:
        /// no cell inside the claim is written (the claim area holds zero
        /// chests), the carry is cleared, and no entity or item materializes.
        /// </summary>
        [Trait("summary", "A stranger standing at the first unclaimed cell drops with EntityOnFailedPlacement and the chest lands inside the boundary footprint with zero claim cells written.")]
        [Trait("pins", "The outside cell resolves deterministically to an unclaimed placement and no claim cell is ever written.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerDropJustOutsideClaim_EntityOnFailedPlacement_PlacesOutsideClaim()
        {
            AssertPremise();
            var dropDst = DropBoundary;
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync("ClaimOwner6", "ClaimDropper6");
            AddClaimAroundSpawn(owner.Player);
            _ = await ArmChestForDropperAsync(stranger, dropDst);

            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.EntityOnFailedPlacement))
            {
                DropHands(stranger);
            }

            AssertCarryCleared(stranger);
            Assert.Single(Drops);
            Assert.True(Drops[0].BlockPlaced, "Unclaimed boundary placement must succeed");

            var chests = ChestCellsAround(dropDst, 2, 2);
            Assert.Single(chests);
            Assert.True(chests[0].X >= DropBoundary.X && chests[0].X <= DropBoundary.X + 1
                && chests[0].Z >= DropBoundary.Z && chests[0].Z <= DropBoundary.Z + 1,
                $"Chest must land within the boundary footprint x {DropBoundary.X}..{DropBoundary.X + 1} z {DropBoundary.Z}..{DropBoundary.Z + 1}, found {chests[0]}");
            Assert.Equal(0, CountChestsInClaimArea());
            Assert.Empty(CarriedBlockEntitiesAround(dropDst, 4));
            Assert.Equal(0, CountItemEntitiesAround(dropDst, 4));
        }

        /// <summary>
        /// C7 - the item-class twin of the Items-mode row: a bag (no world
        /// block) picked from ground storage OUTSIDE the claim, then dropped
        /// by a stranger standing inside it. The claim denies the stranger's
        /// ground-storage placement, and the pinned outcome is the
        /// carried-block entity: exactly one carryon:carriedblock at the drop
        /// point, no game:item spill, no placement. Before the fix, after the
        /// refused ground storage the Items mode skipped the entity spawn, so
        /// the bag spilled as one game:item with no drop event and no
        /// carriedblock entity.
        /// </summary>
        [Trait("summary", "A stranger's Items-mode drop of a carried bag inside a claim lands one carriedblock entity at the drop point, with no game:item spill and no placement.")]
        [Trait("pins", "An item-class carry's claim-denied drop prefers the carried-block entity over the items spill once ground storage is refused.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Fails on the unfixed head, where the Items mode skips the post-storage entity spawn and the bag spills as one game:item with no drop event and no carriedblock entity.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerBagDropInsideClaim_ItemsMode_LandsCarriedBlockEntity()
        {
            AssertPremise();
            var dropDst = DropInside;
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync("ClaimOwner7", "ClaimDropper7");
            AddClaimAroundSpawn(owner.Player);
            _ = await ArmBagForDropperAsync(stranger, dropDst);

            using (SubscribeBlockDropped())
            using (UseDropMode(DropMode.Items))
            {
                DropHands(stranger);
            }

            AssertCarryCleared(stranger);
            Assert.Single(Drops);
            Assert.False(Drops[0].BlockPlaced, "The claim-denied bag drop must land as an entity, not a placement");
            Assert.Single(CarriedBlockEntitiesAround(dropDst, 4));
            Assert.Equal(0, CountItemEntitiesAround(dropDst, 4));
        }
    }
}