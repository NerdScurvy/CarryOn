using System.Collections.Generic;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Network;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Player-owned land-claim coverage (plan group A) plus the claim-spike
    /// acceptance facts: claims are built in-scenario through the vanilla
    /// ILandClaimAPI (LandClaim + Claims.Add - the same registration worldgen
    /// uses), players are forced to survival mode because vanilla claim checks
    /// are creative-sensitive (LandClaim.TestPlayerAccess OkPrivilege and the
    /// claims manager's everywhere-privileges bypass both require Creative),
    /// and the vanilla comparator (Block.CanPlaceBlock) pins parity for the
    /// same geometry. Claim area covers Spawn offsets -4..+7 x/z and y 0..15
    /// (the cuboid end-corner at +8/16 is EXCLUSIVE - spike-pinned in
    /// DropOnClaimScenarios via Claims.TestAccess, which reads LandClaimed at
    /// offset 7 but Granted at offset 8) so every inside cell used by rows is
    /// claimed, and cells at +8/+20 x are outside.
    /// </summary>
    [Trait("area", "Claims/Permissions")]
    [Trait("summary", "An in-scenario player-owned claim guards spawn with survival forced, and the rows assert carry pickup and place-down on claimed land track the vanilla claim answers: denied for strangers, allowed for the owner and explicitly granted members.")]
    public class ClaimPermissionScenarios : CarryOnScenarioBase
    {
        private BlockPos ChestInside => World.Spawn.Offset(2, 1, 0);
        private BlockPos AirInside => World.Spawn.Offset(6, 1, 0);
        private BlockPos Outside => World.Spawn.Offset(20, 1, 0);

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
        /// Empirically pinned: Atlas's RollbackWorld does NOT revert
        /// claims - they are server save state, not block data, and survive the
        /// rollback. Every row drains claims leaked by earlier rows of this class
        /// so it starts from unclaimed land.
        /// </summary>
        private void ClearStaleClaims()
        {
            foreach (var claim in World.Api.World.Claims.All.ToArray())
            {
                World.Api.World.Claims.Remove(claim);
            }
        }

        private async Task<(ITestPlayer Owner, ITestPlayer Stranger)> JoinOwnerAndStrangerAsync()
        {
            var owner = await JoinPlayer("Owner");
            var stranger = await JoinPlayer("Stranger");
            await owner.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await stranger.TeleportTo(World.Spawn.Offset(4, 0, 2));

            return (owner, stranger);
        }

        /// <summary>
        /// Spike acceptance (plan 0.1): a scenario-created claim answers Granted
        /// for its owner and a non-Granted response for a second joined player,
        /// inside the claim only. The vanilla placement comparator
        /// (Block.CanPlaceBlock) agrees: "claimed" for the stranger, pass for
        /// the owner. Also pins that no claims leak through RollbackWorld.
        /// </summary>
        [Trait("summary", "A scenario-created claim answers Granted for its owner and not-Granted for a stranger inside only, and the vanilla placement comparator agrees, pinning the harness facts that claims are constructible headless and survive RollbackWorld.")]
        [Trait("pins", "Claims are constructible headless, survive RollbackWorld, and the vanilla comparator agrees with the claim answers.")]
        [Trait("role", "premise")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ClaimRegistration_OwnerGranted_StrangerDenied_VanillaComparatorMatches()
        {
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            World.SetBlock("game:chest-east", ChestInside);
            var chest = World.BlockAt(ChestInside);

            AddClaimAroundSpawn(owner.Player);

            Assert.Single(World.Api.World.Claims.All);
            Assert.True(World.Api.World.Claims.Get(ChestInside).Length == 1,
                "Claim must be registered at the inside cell");

            var flags = EnumBlockAccessFlags.BuildOrBreak;
            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(owner.Player, ChestInside, flags));
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, ChestInside, flags));
            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, Outside, flags));

            string failure = "";
            var selection = new BlockSelection { Position = AirInside };
            Assert.False(chest!.CanPlaceBlock(World.Api.World, stranger.Player, selection, ref failure),
                "Vanilla comparator should deny stranger placement inside the claim");
            Assert.Equal("claimed", failure);
            Assert.True(chest.CanPlaceBlock(World.Api.World, owner.Player, selection, ref failure),
                $"Vanilla comparator should allow the owner (failure: {failure})");
        }

        /// <summary>
        /// Owner positive control (group A row 1): pickup and place-down of a
        /// chest on one's own claim both succeed through the manager seam.
        /// </summary>
        [Trait("summary", "The owner picks up a chest inside their own claim and places it down at a free cell inside, with both actions succeeding through the manager seam.")]
        [Trait("pins", "The claim gate must not over-block the owner's own pickup and place-down.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task OwnerOnOwnClaim_PickUpAndPlaceDown_Succeed()
        {
            ClearStaleClaims();

            var (owner, _) = await JoinOwnerAndStrangerAsync();
            World.SetBlock("game:chest-east", ChestInside);
            AddClaimAroundSpawn(owner.Player);

            Assert.True(Actions.PickUpIntoHands(owner.Entity, ChestInside, out var pickupFailure) != null,
                $"Owner pickup on own claim failed: {pickupFailure}");
            Assert.True(World.BlockAt(ChestInside).BlockId == 0);

            var placedAt = Actions.PlaceDown(owner.Player, CarrySlot.Hands,
                CarryActions.AimDownAt(AirInside), out var placeFailure);
            Assert.True(placedAt != null, $"Owner place-down on own claim failed: {placeFailure}");
            Assert.True(IsChest(World.BlockAt(AirInside)), "Chest should be placed inside the claim");
            Assert.False(Carry.IsCarrySlotOccupied(owner.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// Non-member pickup denial (group A row 2) through both drivers: the
        /// manager seam rejects with NoPermission leaving the chest in the
        /// world, and the handler path additionally emits the client-visible
        /// failure observables (revision bump + LockSlotsMessage).
        /// </summary>
        [Trait("summary", "A stranger's chest pickup inside a foreign claim is denied by the manager seam with the chest left in the world, and the handler path adds the same denial plus the revision bump and client notification.")]
        [Trait("pins", "Pickup requires build permission on claimed land, through both the manager and handler drivers.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerPickUpOnClaim_Denied_ByManagerAndHandler()
        {
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            World.SetBlock("game:chest-east", ChestInside);
            AddClaimAroundSpawn(owner.Player);

            // Manager path: rejected with AlreadyReported (the claim gate already named the
            // refusal through Claims.TryAccess), world and hands unchanged.
            Assert.Null(Actions.PickUpIntoHands(stranger.Entity, ChestInside, out var managerFailure));
            Assert.Equal(FailureCodes.AlreadyReported, managerFailure);
            Assert.True(IsChest(World.BlockAt(ChestInside)), "Chest must stay in the world");
            Assert.False(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands));

            // Handler path: same denial plus client-visible failure observables.
            long revisionBefore = Carry.GetCarriedRevision(stranger.Entity);
            HandlerInvoke.PickUp(Mod, stranger.Player, new PickUpMessage(ChestInside, CarrySlot.Hands));

            Assert.False(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Handler pickup must not grant the carry");
            Assert.True(IsChest(World.BlockAt(ChestInside)), "Handler pickup must leave the chest in place");
            Assert.True(Carry.GetCarriedRevision(stranger.Entity) > revisionBefore,
                "Denied handler pickup should bump the carried revision");
            Assert.True(stranger.Client.Packets<LockSlotsMessage>(Channel).Count > 0,
                "Denied handler pickup should notify the client");
        }

        /// <summary>
        /// Non-member place-down denial (group A row 3) through both drivers: a
        /// chest picked up outside the claim cannot be placed inside it. The
        /// vanilla comparator denies the identical placement with "claimed" -
        /// CarryOn place-down delegates to TryPlaceBlock and matches. No ghost
        /// cells, carry retained.
        /// </summary>
        [Trait("summary", "A chest picked up outside the claim cannot be placed inside it: the vanilla comparator denies the identical placement with 'claimed', CarryOn denies with the carry retained and no ghost cells, and the handler observables are present.")]
        [Trait("pins", "Place-down matches vanilla's target-cell claim check and never writes a ghost cell.")]
        [Trait("role", "parity")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerPlaceDownOnClaim_Denied_NoGhost_VanillaParity()
        {
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            // The stranger's carry comes from a chest outside the claim.
            World.SetBlock("game:chest-east", Outside);
            await stranger.TeleportTo(World.Spawn.Offset(20, 0, 2));
            Assert.True(Actions.PickUpIntoHands(stranger.Entity, Outside, out var pickupFailure) != null,
                $"Stranger pickup outside the claim failed: {pickupFailure}");
            Assert.True(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands));

            // Vanilla comparator for the identical geometry: denied, "claimed".
            var chest = World.Api.World.GetBlock(new AssetLocation("game:chest-east"));
            string vanillaFailure = "";
            Assert.False(chest!.CanPlaceBlock(World.Api.World, stranger.Player,
                new BlockSelection { Position = AirInside }, ref vanillaFailure),
                "Vanilla comparator should deny the stranger's placement");
            Assert.Equal("claimed", vanillaFailure);

            // Manager path: rejected, carry retained, no ghost anywhere in the claim.
            var placedAt = Actions.PlaceDown(stranger.Player, CarrySlot.Hands,
                CarryActions.AimDownAt(AirInside), out var managerFailure);
            Assert.True(placedAt == null, $"Manager place-down inside the claim unexpectedly succeeded ({managerFailure})");
            Assert.True(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Denied place-down must retain the carry");
            Assert.True(World.BlockAt(AirInside).BlockId == 0, "Target cell must stay air (no ghost)");

            // Handler path: same denial plus client-visible failure observables.
            long revisionBefore = Carry.GetCarriedRevision(stranger.Entity);
            HandlerInvoke.PlaceDown(Mod, stranger.Player,
                new PlaceDownMessage(CarrySlot.Hands, CarryActions.AimDownAt(AirInside), AirInside, [AirInside]));

            Assert.True(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Denied handler place-down must retain the carry");
            Assert.True(World.BlockAt(AirInside).BlockId == 0,
                "Denied handler place-down must not write the claimed cell");
            Assert.True(CountChestsInClaimArea() == 0,
                "No chest may exist inside the claim (no ghost placement anywhere)");
            Assert.True(Carry.GetCarriedRevision(stranger.Entity) > revisionBefore,
                "Denied handler place-down should bump the carried revision");
            Assert.True(stranger.Client.Packets<LockSlotsMessage>(Channel).Count > 0,
                "Denied handler place-down should notify the client");
        }

        /// <summary>
        /// Claim member (group A row 4): a player explicitly granted
        /// BuildOrBreak through PermittedPlayerUids is allowed - the permission
        /// lever is claim data, not just ownership. Both pickup and place-down
        /// inside the claim succeed; the vanilla comparator agrees.
        /// </summary>
        [Trait("summary", "A stranger explicitly granted BuildOrBreak through the claim's permitted-player list picks up and places down inside the claim, with the vanilla comparator agreeing.")]
        [Trait("pins", "The permission lever is claim data (grants), not just ownership.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task ClaimMemberGrantedBuildOrBreak_CanPickUpAndPlaceDown()
        {
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            World.SetBlock("game:chest-east", ChestInside);
            var claim = AddClaimAroundSpawn(owner.Player);
            claim.PermittedPlayerUids[stranger.Player.PlayerUID] = EnumBlockAccessFlags.BuildOrBreak;

            var flags = EnumBlockAccessFlags.BuildOrBreak;
            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, ChestInside, flags));

            var chest = World.BlockAt(ChestInside);
            string vanillaFailure = "";
            Assert.True(chest!.CanPlaceBlock(World.Api.World, stranger.Player,
                new BlockSelection { Position = AirInside }, ref vanillaFailure),
                $"Vanilla comparator should allow the member (failure: {vanillaFailure})");

            Assert.True(Actions.PickUpIntoHands(stranger.Entity, ChestInside, out var pickupFailure) != null,
                $"Member pickup inside the claim failed: {pickupFailure}");

            var placedAt = Actions.PlaceDown(stranger.Player, CarrySlot.Hands,
                CarryActions.AimDownAt(AirInside), out var placeFailure);
            Assert.True(placedAt != null, $"Member place-down inside the claim failed: {placeFailure}");
            Assert.True(IsChest(World.BlockAt(AirInside)));
            Assert.False(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands));
        }

        private int CountChestsInClaimArea()
        {
            int chests = 0;
            for (int dx = -4; dx <= 8; dx++)
            {
                for (int dz = -4; dz <= 8; dz++)
                {
                    for (int dy = 0; dy <= 3; dy++)
                    {
                        var block = World.BlockAt(World.Spawn.Offset(dx, dy, dz));
                        if (IsChest(block)) chests++;
                    }
                }
            }
            return chests;
        }

        private static bool IsChest(Block? block)
        {
            return block != null
                && block.BlockId != 0
                && block.Code != null
                && block.Code.FirstCodePart() == "chest";
        }
    }
}
