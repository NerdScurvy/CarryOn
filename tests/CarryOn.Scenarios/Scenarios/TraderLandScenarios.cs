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
    /// Trader-land coverage (plan group C): carry actions against a claim
    /// whose owner is not any joined player, replicated in-scenario with the
    /// exact worldgen recipe (VSEssentials ~:11548): an ownerless LandClaim
    /// with ProtectionLevel 10 (WorldGenStructureBase default), AllowUseEveryone
    /// false, AllowTraverseEveryone true, LastKnownOwnerName "Trader". Vanilla
    /// answers are pinned per row (Claims.TestAccess / Block.CanPlaceBlock) and
    /// CarryOn must match. The trader claim uses the same half-open-corrected
    /// area as group B (Cuboidi max +9): inside = x &lt;= Spawn+8, edge between
    /// x = Spawn+8 and x = Spawn+9. Survival is forced because vanilla's
    /// privilege bypasses (OkPrivilege, everywhere-privileges) are
    /// creative-gated. Trunk rows reuse the group B pins: footprint table,
    /// synchronous multiblock behavior sweep, SuggestedHVOrientation-driven
    /// placement variants, block-relative HitPosition selections.
    ///
    /// Since the claim-overlap fix, CarryOn is deliberately
    /// stricter than vanilla on trunk straddles: the whole main/multiblock
    /// footprint must be permitted, so a part cell touching trader land is
    /// denied where vanilla allows it. Vanilla comparator asserts stay as
    /// documentation of the divergence.
    /// </summary>
    [Trait("area", "Claims/Permissions")]
    [Trait("summary", "An ownerless claim with the exact trader-worldgen recipe guards spawn, and the rows assert carry pickup and place-down on trader land match vanilla, with trunk straddles denied under the deliberately stricter whole-footprint gate.")]
    [AtlasDataFiles("fixtures/TrunkConfig", TargetPath = "ModConfig")]
    public class TraderLandScenarios : CarryOnScenarioBase
    {
        private const string TrunkFirstPart = "trunk";

        private static readonly Dictionary<string, (int Dx, int Dy, int Dz, string DummyCode)> TrunkFootprint = new()
        {
            ["north"] = (1, 0, 0, "game:multiblock-monolithic-p1-0-0"),
            ["east"] = (0, 0, 1, "game:multiblock-monolithic-0-0-p1"),
            ["south"] = (-1, 0, 0, "game:multiblock-monolithic-n1-0-0"),
            ["west"] = (0, 0, -1, "game:multiblock-monolithic-0-0-n1"),
        };

        private void AddTraderClaimAroundSpawn()
        {
            World.Api.World.Claims.Add(new LandClaim
            {
                Areas = [new Cuboidi(World.Spawn.Offset(-4, 0, -4), World.Spawn.Offset(9, 16, 9))],
                ProtectionLevel = 10,
                LastKnownOwnerName = "Trader",
                Description = "Trader Perimeter",
                AllowUseEveryone = false,
                AllowTraverseEveryone = true,
            });
        }

        private void ClearStaleClaims()
        {
            foreach (var claim in World.Api.World.Claims.All.ToArray())
            {
                World.Api.World.Claims.Remove(claim);
            }
        }

        private async Task<ITestPlayer> JoinVisitorAsync()
        {
            var visitor = await JoinPlayer("Visitor");
            await visitor.TeleportTo(World.Spawn.Offset(4, 0, 2));

            return visitor;
        }

        private BlockPos PlaceTrunkFixture(string side, BlockPos controller)
        {
            World.SetBlock($"game:trunk-{side}", controller);
            var (dx, dy, dz, dummyCode) = TrunkFootprint[side];
            var dummyPos = controller.Copy().Add(dx, dy, dz);

            Assert.True(World.BlockAt(controller).Code?.FirstCodePart() == TrunkFirstPart,
                $"Fixture controller at {controller} should hold the trunk, found {World.BlockAt(controller).Code}");
            Assert.Equal(dummyCode, World.BlockAt(dummyPos).Code?.ToString());

            return dummyPos;
        }

        private static BlockSelection AimDownAtRelative(BlockPos target)
        {
            return new BlockSelection
            {
                Position = target,
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.5, 1.0, 0.5),
                SelectionBoxIndex = 0,
            };
        }

        private async Task TeleportForFacingAsync(ITestPlayer player, BlockPos target, string desiredSide)
        {
            var selection = AimDownAtRelative(target);
            var offsets = new (int Dx, int Dz)[]
            {
                (0, -3), (3, 0), (0, 3), (-3, 0),
                (0, -6), (6, 0), (0, 6), (-6, 0),
            };
            var observed = new List<string>();
            foreach (var (dx, dz) in offsets)
            {
                await player.TeleportTo(target.Copy().Add(dx, -1, dz));
                var facing = Block.SuggestedHVOrientation(player.Player, selection)[0];
                observed.Add(facing.Code!);
                if (facing.Code == desiredSide) return;
            }
            Assert.Fail(
                $"No position around {target} produces facing '{desiredSide}' for the trunk placement "
                + $"(observed: {string.Join(", ", observed)})");
        }

        private Block? ResolvePlacementVariant(IPlayer player, BlockSelection selection, Block stackBlock)
        {
            var facing = Block.SuggestedHVOrientation(player, selection)[0];
            var variantCode = stackBlock.CodeWithVariant("side", facing.Code!);
            return World.Api.World.GetBlock(variantCode);
        }

        private void AssertAir(BlockPos pos)
        {
            Assert.True(World.BlockAt(pos).BlockId == 0,
                $"Cell {pos} should be air, found {World.BlockAt(pos).Code}");
        }

        private void AssertTrunk(BlockPos pos)
        {
            Assert.True(World.BlockAt(pos).Code?.FirstCodePart() == TrunkFirstPart,
                $"Cell {pos} should hold the trunk, found {World.BlockAt(pos).Code}");
        }

        private void AssertDummy(BlockPos pos, string side)
        {
            Assert.Equal(TrunkFootprint[side].DummyCode, World.BlockAt(pos).Code?.ToString());
        }

        /// <summary>
        /// Chest pickup on trader-claimed land (row 9): an ordinary player is
        /// denied - the claim is ownerless with protection level 10 - matching
        /// vanilla's own TestAccess answer for the same cell.
        /// </summary>
        [Trait("summary", "A visitor's chest pickup inside the trader perimeter is denied no-permission, matching vanilla's own claim answer for the same cell, with the chest left in the world.")]
        [Trait("pins", "Non-player-owned land denies ordinary players, in parity with vanilla.")]
        [Trait("role", "parity")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PlayerPickUpChestOnTraderLand_Denied_VanillaParity()
        {
            ClearStaleClaims();

            var visitor = await JoinVisitorAsync();
            var chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);
            AddTraderClaimAroundSpawn();

            Assert.Single(World.Api.World.Claims.All);
            var flags = EnumBlockAccessFlags.BuildOrBreak;
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(visitor.Player, chestPos, flags));

            Assert.Null(Actions.PickUpIntoHands(visitor.Entity, chestPos, out var managerFailure));
            Assert.Equal(FailureCodes.AlreadyReported, managerFailure);
            Assert.True(World.BlockAt(chestPos).Code?.FirstCodePart() == "chest",
                "The chest must stay in the world");
            Assert.False(Carry.IsCarrySlotOccupied(visitor.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// Chest place-down on trader-claimed land (row 10): a chest picked up
        /// outside the trader perimeter cannot be placed inside it. The
        /// vanilla comparator (Block.CanPlaceBlock) denies the identical
        /// placement with "claimed"; CarryOn delegates to TryPlaceBlock and
        /// matches. Carry retained, no ghost cells.
        /// </summary>
        [Trait("summary", "A chest picked up outside the trader perimeter cannot be placed inside it: the vanilla comparator denies the identical placement with 'claimed', and CarryOn denies with the carry retained and no ghost cells.")]
        [Trait("pins", "Place-down parity on trader land.")]
        [Trait("role", "parity")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PlayerPlaceDownChestOnTraderLand_Denied_VanillaParity()
        {
            ClearStaleClaims();

            var visitor = await JoinVisitorAsync();
            AddTraderClaimAroundSpawn();

            var outside = World.Spawn.Offset(20, 1, 0);
            World.SetBlock("game:chest-east", outside);
            await visitor.TeleportTo(World.Spawn.Offset(20, 0, 2));
            Assert.True(Actions.PickUpIntoHands(visitor.Entity, outside, out var pickupFailure) != null,
                $"Chest pickup outside the trader claim failed: {pickupFailure}");
            Assert.True(Carry.IsCarrySlotOccupied(visitor.Entity, CarrySlot.Hands));

            var target = World.Spawn.Offset(6, 1, 0);
            var chest = World.Api.World.GetBlock(new AssetLocation("game:chest-east"));
            string vanillaFailure = "";
            Assert.False(chest!.CanPlaceBlock(World.Api.World, visitor.Player,
                new BlockSelection { Position = target }, ref vanillaFailure),
                "Vanilla comparator should deny the placement inside the trader claim");
            Assert.Equal("claimed", vanillaFailure);

            var placedAt = Actions.PlaceDown(visitor.Player, CarrySlot.Hands,
                CarryActions.AimDownAt(target), out var placeFailure);
            Assert.True(placedAt == null,
                $"Place-down inside the trader claim should match vanilla and be denied ({placeFailure})");
            Assert.True(Carry.IsCarrySlotOccupied(visitor.Entity, CarrySlot.Hands),
                "Denied place-down must retain the carry");
            Assert.True(World.BlockAt(target).BlockId == 0, "Target cell must stay air (no ghost)");
        }

        /// <summary>
        /// Trunk straddle against the trader claim edge (row 11), under the
        /// whole-footprint permission gate: a carried trunk whose part cell
        /// would intrude into trader land is DENIED place-down - the deliberate
        /// claim-overlap divergence from vanilla, which claim-checks the target
        /// cell only and allows the intrusion (the vanilla comparator assert
        /// stays as documentation). Denied place-downs retain the carry and
        /// write nothing. A trunk whose controller is inside trader land is
        /// denied pickup as before.
        /// </summary>
        [Trait("summary", "A carried trunk whose part cell would intrude into trader land is denied place-down where the vanilla comparator allows it, with the carry retained and nothing written; a trunk whose controller is inside trader land is denied pickup.")]
        [Trait("pins", "The whole-footprint permission rule extends to non-player-owned trader land.")]
        [Trait("role", "regression")]
        [Trait("decision", "1")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task TrunkStraddleAgainstTraderClaim_Denied_WholeFootprint()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");
            ClearStaleClaims();

            var visitor = await JoinVisitorAsync();
            AddTraderClaimAroundSpawn();

            var flags = EnumBlockAccessFlags.BuildOrBreak;

            // Acquisition outside the trader perimeter.
            var acquirePos = World.Spawn.Offset(20, 1, 4);
            var acquireDummy = PlaceTrunkFixture("south", acquirePos);
            await visitor.TeleportTo(World.Spawn.Offset(21, 0, 4));
            Assert.True(Actions.PickUpIntoHands(visitor.Entity, acquirePos, out var acquireFailure) != null,
                $"Trunk pickup outside the trader claim failed: {acquireFailure}");
            AssertAir(acquirePos);
            AssertAir(acquireDummy);

            // Straddling place-down: controller outside, dummy inside. Denied
            // by the whole-footprint gate.
            var target = World.Spawn.Offset(9, 1, 4);
            var selection = AimDownAtRelative(target);
            await TeleportForFacingAsync(visitor, target, "south");

            var carriedStack = Carry.GetCarried(visitor.Entity, CarrySlot.Hands)?.ItemStack;
            Assert.NotNull(carriedStack);
            var variant = ResolvePlacementVariant(visitor.Player, selection, carriedStack!.Block!);
            Assert.NotNull(variant);

            string vanillaFailure = "";
            Assert.True(variant!.CanPlaceBlock(World.Api.World, visitor.Player, selection, ref vanillaFailure),
                $"Vanilla comparator should allow the straddling placement (part cell inside trader land) - "
                + $"the denied CarryOn outcome is the deliberate whole-footprint divergence; failure: {vanillaFailure}");

            var placedAt = Actions.PlaceDown(visitor.Player, CarrySlot.Hands, selection, out var placeFailure);
            Assert.True(placedAt == null,
                $"CarryOn place-down must deny the trader-land straddle (whole-footprint gate): {placeFailure}");
            Assert.Equal(FailureCodes.AlreadyReported, placeFailure);
            Assert.True(Carry.IsCarrySlotOccupied(visitor.Entity, CarrySlot.Hands),
                "Denied place-down must retain the carry");
            AssertAir(target);
            AssertAir(World.Spawn.Offset(8, 1, 4)); // the straddling dummy cell stays unwritten

            // Clear the retained carry for the pickup leg. The drop happens
            // far from both legs' cells; candidates inside the perimeter are
            // denied by the same footprint gate (fallback to items), so this
            // writes nothing into trader land either way.
            await visitor.TeleportTo(World.Spawn.Offset(30, 0, 30));
            Actions.Drop(visitor.Entity, CarrySlot.Hands);
            Assert.False(Carry.IsCarrySlotOccupied(visitor.Entity, CarrySlot.Hands),
                "Drop cleanup should clear the retained carry");

            // Control-in trunk: pickup denied.
            var inController = World.Spawn.Offset(8, 1, 6);
            var outDummy = PlaceTrunkFixture("north", inController);

            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(visitor.Player, inController, flags));

            Assert.Null(Actions.PickUpIntoHands(visitor.Entity, inController, out var managerFailure));
            Assert.Equal(FailureCodes.AlreadyReported, managerFailure);
            AssertTrunk(inController);
            AssertDummy(outDummy, "north");
        }
    }
}
