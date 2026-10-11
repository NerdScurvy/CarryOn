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
    /// Claim-edge straddle coverage (plan group B): a two-cell trunk across the
    /// claim boundary, both orientations (controller inside / part cell inside).
    ///
    /// Measured vanilla semantics this class encodes (decompile-verified VS
    /// 1.22.6, plan status log): placement gates claims at the target cell only
    /// (Block.CanPlaceBlock -> "claimed"); multiblock part cells get only a
    /// replacable check (BlockBehaviorMultiblock.CanPlaceBlock), so vanilla
    /// ALLOWS a trunk whose part cell intrudes into a claim. Break gates at the
    /// aimed cell only. The server's default block accessor runs
    /// OnBlockPlaced/OnBlockRemoved synchronously inside SetBlock, so the
    /// vanilla multiblock sweep clears dummy cells whenever the controller is
    /// removed - CarryOn pickup (raw SetBlock(0) on the controller, no explicit
    /// sweep) inherits that behavior. BlockSelection.HitPosition is
    /// block-relative in vanilla; CarryActions.AimDownAt fills an absolute
    /// vector (orientation-only quirk, pinned in the status log), so trunk
    /// place-down selections here use a relative hit position. Trunk footprint
    /// table pinned from assets/survival/blocktypes/wood/chest-trunk.json.
    ///
    /// Since the claim-overlap fix, CarryOn is deliberately
    /// stricter than vanilla: pickup and place-down gate the whole
    /// main/multiblock footprint, so any straddling half-touch of a foreign
    /// claim is denied with NoPermission in both directions. The vanilla
    /// comparator asserts stay as documentation of the divergence (they still
    /// pass - vanilla semantics are unchanged); the CarryOn outcome asserts
    /// encode the stricter decided behavior.
    /// </summary>
    [Trait("area", "Claims/Permissions")]
    [Trait("summary", "Seeded with trunk carrying enabled, a two-cell trunk straddles a player-owned claim edge in both orientations, and the rows assert the whole-footprint permission gate is deliberately stricter than vanilla, keeping the vanilla comparator as documentation of the divergence.")]
    [AtlasDataFiles("fixtures/TrunkConfig", TargetPath = "ModConfig")]
    public class ClaimEdgeStraddleScenarios : CarryOnScenarioBase
    {
        // Claim: Spawn -4..+8 x/z inclusive (Cuboidi max +9 - Contains is
        // half-open), so the edge runs between x = Spawn+8 (inside) and
        // x = Spawn+9 (outside).
        private const string TrunkFirstPart = "trunk";

        private static readonly Dictionary<string, (int Dx, int Dy, int Dz, string DummyCode)> TrunkFootprint = new()
        {
            ["north"] = (1, 0, 0, "game:multiblock-monolithic-p1-0-0"),
            ["east"] = (0, 0, 1, "game:multiblock-monolithic-0-0-p1"),
            ["south"] = (-1, 0, 0, "game:multiblock-monolithic-n1-0-0"),
            ["west"] = (0, 0, -1, "game:multiblock-monolithic-0-0-n1"),
        };

        private LandClaim AddClaimAroundSpawn(IPlayer owner)
        {
            var claim = LandClaim.CreateClaim(owner);
            // Cuboidi.Contains is half-open [Min, Max) (decompile-pinned), so a
            // max of +9 claims cells Spawn-4..Spawn+8: the edge runs between
            // x = Spawn+8 (inside) and x = Spawn+9 (outside).
            claim.Areas =
            [
                new Cuboidi(World.Spawn.Offset(-4, 0, -4), World.Spawn.Offset(9, 16, 9))
            ];
            World.Api.World.Claims.Add(claim);
            return claim;
        }

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
        /// Places a vanilla trunk fixture with its live multiblock dummy: the
        /// controller via raw SetBlock (the server accessor fires
        /// BlockBehaviorMultiblock.OnBlockPlaced synchronously, which places the
        /// dummy cell), then asserts both cells. Returns the dummy position.
        /// </summary>
        private BlockPos PlaceTrunkFixture(string side, BlockPos controller)
        {
            World.SetBlock($"game:trunk-{side}", controller);
            var (dx, dy, dz, dummyCode) = TrunkFootprint[side];
            var dummyPos = controller.Copy().Add(dx, dy, dz);

            Assert.True(World.BlockAt(controller).Code?.FirstCodePart() == TrunkFirstPart,
                $"Fixture controller at {controller} should hold the trunk, found {World.BlockAt(controller).Code}");
            var dummy = World.BlockAt(dummyPos);
            Assert.True(dummy.Code?.ToString() == dummyCode,
                $"Fixture dummy at {dummyPos} should be {dummyCode} (synchronous Multiblock OnBlockPlaced), "
                + $"found {dummy.Code}. If it is air, Atlas SetBlock does not run placement behaviors and "
                + $"the fixture needs an explicit dummy SetBlock.");

            return dummyPos;
        }

        /// <summary>
        /// Builds a placement selection with a block-relative HitPosition
        /// (vanilla convention - Block.SuggestedHVOrientation reads
        /// blockPos + HitPosition; the shared CarryActions.AimDownAt helper
        /// fills an absolute vector, which skews HorizontalOrientable's
        /// look-aware variant choice).
        /// </summary>
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

        /// <summary>
        /// Teleports the player around the target until vanilla's own
        /// SuggestedHVOrientation (engine-true, public static) reports the
        /// desired horizontal facing for the selection, making the
        /// HorizontalOrientable placement variant deterministic.
        /// </summary>
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

        /// <summary>
        /// Resolves the trunk variant vanilla would place for this player and
        /// selection (HorizontalOrientable picks the side variant through
        /// SuggestedHVOrientation).
        /// </summary>
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
        /// Non-owner place-down of a carried trunk across the claim edge (row 5,
        /// both directions), under the whole-footprint permission gate: a
        /// placement is denied unless every cell it writes - target and
        /// multiblock part cell alike - is permitted. The part-inside straddle
        /// (controller outside, dummy inside the claim) is therefore denied
        /// where vanilla allows it - the deliberate claim-overlap divergence
        /// (vanilla claim-checks the target cell only; the vanilla comparator
        /// assert stays as documentation). The control-inside leg was already
        /// denied and stays denied. Denied place-downs retain the carry and
        /// write nothing.
        /// </summary>
        [Trait("summary", "A stranger places a carried trunk across the claim edge in both directions: the part-inside straddle is denied where the vanilla comparator allows it, and the control-inside leg is denied by both, with the carry retained and neither cell written.")]
        [Trait("pins", "Placement requires permission on every written cell, including the multiblock part cell inside a foreign claim.")]
        [Trait("role", "regression")]
        [Trait("decision", "1")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerPlaceDownStraddle_Denied_WholeFootprint()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            // The stranger's carry comes from a trunk outside the claim. The
            // fixture also proves the pickup lifecycle: both footprint cells
            // (controller + dummy) clear - the vanilla multiblock sweep runs on
            // controller removal.
            var acquirePos = World.Spawn.Offset(20, 1, 4);
            var acquireDummy = PlaceTrunkFixture("south", acquirePos);
            await stranger.TeleportTo(World.Spawn.Offset(21, 0, 4));
            Assert.True(Actions.PickUpIntoHands(stranger.Entity, acquirePos, out var acquireFailure) != null,
                $"Stranger trunk pickup outside the claim failed: {acquireFailure}");
            AssertAir(acquirePos);
            AssertAir(acquireDummy);

            // Part-inside direction: controller outside the claim, dummy inside
            // (trunk-south places its part cell at -X). Denied by the
            // whole-footprint gate.
            var target = World.Spawn.Offset(9, 1, 4);
            var selection = AimDownAtRelative(target);
            await TeleportForFacingAsync(stranger, target, "south");

            var carriedStack = Carry.GetCarried(stranger.Entity, CarrySlot.Hands)?.ItemStack;
            Assert.NotNull(carriedStack);
            var variant = ResolvePlacementVariant(stranger.Player, selection, carriedStack!.Block!);
            Assert.NotNull(variant);

            string vanillaFailure = "";
            Assert.True(variant!.CanPlaceBlock(World.Api.World, stranger.Player, selection, ref vanillaFailure),
                $"Vanilla comparator should allow the straddling placement (part cell inside the claim) - "
                + $"the denied CarryOn outcome is the deliberate whole-footprint divergence; failure: {vanillaFailure}");

            var placedAt = Actions.PlaceDown(stranger.Player, CarrySlot.Hands, selection, out var placeFailure);
            Assert.True(placedAt == null,
                $"CarryOn place-down must deny the part-inside straddle (whole-footprint gate): {placeFailure}");
            Assert.Equal(FailureCodes.AlreadyReported, placeFailure);
            Assert.True(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Denied place-down must retain the carry");
            AssertAir(target);
            AssertAir(World.Spawn.Offset(8, 1, 4)); // the straddling dummy cell stays unwritten

            // Clear the retained carry for the second leg. The drop happens far
            // from both legs' cells; any candidate inside the claim is denied
            // by the same footprint gate (fallback to items), so this writes
            // nothing into the claim either way.
            await stranger.TeleportTo(World.Spawn.Offset(30, 0, 30));
            Actions.Drop(stranger.Entity, CarrySlot.Hands);
            Assert.False(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Drop cleanup should clear the retained carry");

            // Denied direction: controller inside the claim, dummy outside.
            var acquire2 = World.Spawn.Offset(20, 1, 10);
            PlaceTrunkFixture("south", acquire2);
            await stranger.TeleportTo(World.Spawn.Offset(21, 0, 10));
            Assert.True(Actions.PickUpIntoHands(stranger.Entity, acquire2, out var acquireFailure2) != null,
                $"Second trunk acquisition failed: {acquireFailure2}");

            var target2 = World.Spawn.Offset(8, 1, 6);
            var selection2 = AimDownAtRelative(target2);
            await TeleportForFacingAsync(stranger, target2, "north");

            var carriedStack2 = Carry.GetCarried(stranger.Entity, CarrySlot.Hands)?.ItemStack;
            Assert.NotNull(carriedStack2);
            var variant2 = ResolvePlacementVariant(stranger.Player, selection2, carriedStack2!.Block!);
            Assert.NotNull(variant2);

            string vanillaFailure2 = "";
            Assert.False(variant2!.CanPlaceBlock(World.Api.World, stranger.Player, selection2, ref vanillaFailure2),
                "Vanilla comparator should deny the placement whose controller lands inside the claim");
            Assert.Equal("claimed", vanillaFailure2);

            var placedAt2 = Actions.PlaceDown(stranger.Player, CarrySlot.Hands, selection2, out var placeFailure2);
            Assert.True(placedAt2 == null,
                $"CarryOn place-down should deny the control-inside straddle ({placeFailure2})");
            Assert.Equal(FailureCodes.AlreadyReported, placeFailure2);
            Assert.True(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Denied place-down must retain the carry");
            AssertAir(target2);
            AssertAir(World.Spawn.Offset(9, 1, 6));
        }

        /// <summary>
        /// Non-owner pickup targeting the trunk half inside the claim (row 6):
        /// the controller cell on the control-in orientation, and the multiblock
        /// dummy on the control-out orientation. Both are denied by the
        /// whole-footprint gate: the control-in controller sits inside the
        /// claim, and the dummy-targeted footprint redirects to the controller
        /// (a BlockMultiblock target maps to its controller's footprint), with
        /// permission firing before the carryable check. Handler path adds the
        /// client-visible denial observables.
        /// </summary>
        [Trait("summary", "Pickups targeting the trunk half inside the claim - the controller on the control-in orientation and the dummy on the control-out orientation - are denied no-permission with the structure intact, and the handler path adds the client-visible denial observables.")]
        [Trait("pins", "The pickup footprint gate denies the inside half through both drivers, with permission firing before the carryable check.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerPickUpStraddle_InsideHalf_Denied_ByManagerAndHandler()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var flags = EnumBlockAccessFlags.BuildOrBreak;

            // Control-in orientation: the real trunk half is inside the claim.
            var inController = World.Spawn.Offset(8, 1, 4);
            var outDummy = PlaceTrunkFixture("north", inController);

            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, inController, flags));

            Assert.Null(Actions.PickUpIntoHands(stranger.Entity, inController, out var managerFailure));
            Assert.Equal(FailureCodes.AlreadyReported, managerFailure);
            AssertTrunk(inController);
            AssertDummy(outDummy, "north");

            // Handler path: same denial plus client-visible failure observables.
            long revisionBefore = Carry.GetCarriedRevision(stranger.Entity);
            HandlerInvoke.PickUp(Mod, stranger.Player, new PickUpMessage(inController, CarrySlot.Hands));

            Assert.False(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Handler pickup must not grant the carry");
            AssertTrunk(inController);
            Assert.True(Carry.GetCarriedRevision(stranger.Entity) > revisionBefore,
                "Denied handler pickup should bump the carried revision");
            Assert.True(stranger.Client.Packets<LockSlotsMessage>(Channel).Count > 0,
                "Denied handler pickup should notify the client");

            // Control-out orientation: the multiblock dummy is inside the claim.
            var outController = World.Spawn.Offset(9, 1, 6);
            var inDummy = PlaceTrunkFixture("south", outController);

            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, inDummy, flags));

            Assert.Null(Actions.PickUpIntoHands(stranger.Entity, inDummy, out var dummyFailure));
            Assert.Equal(FailureCodes.AlreadyReported, dummyFailure);
            AssertTrunk(outController);
            AssertDummy(inDummy, "south");
        }

        /// <summary>
        /// Non-owner pickup targeting the trunk half outside the claim (row 7,
        /// the sharpest row): under the whole-footprint permission gate both
        /// outside halves are denied with NoPermission, and the standing
        /// structure survives untouched. Targeting the outside dummy of a
        /// control-in trunk previously failed NotCarryable (dummy cells are not
        /// carryable targets); now the footprint gate fires first and the
        /// dummy's footprint redirects to the claim-blocked controller. Picking
        /// up the outside controller of a control-out trunk previously
        /// succeeded (vanilla aimed-cell parity - vanilla break gates at the
        /// aimed cell only, and its multiblock sweep removes the whole
        /// structure, a decompile-pinned vanilla claim bypass); the stricter
        /// no-write-into-claims outcome is the deliberate claim-overlap
        /// divergence recorded in the plan.
        /// </summary>
        [Trait("summary", "Pickups targeting the trunk half outside the claim are both denied no-permission: the outside dummy redirects to the claim-blocked controller where it previously answered not-carryable, and the outside controller whose part cell sits inside - previously succeeding like vanilla's aimed-cell break - leaves the standing structure untouched.")]
        [Trait("pins", "No removing blocks from claims via the outside half, and no grandfathering of a standing structure.")]
        [Trait("role", "regression")]
        [Trait("decision", "1")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerPickUpStraddle_OutsideHalf_Denied_WholeFootprint()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var flags = EnumBlockAccessFlags.BuildOrBreak;

            // Outside dummy first (hands empty): control-in trunk, dummy outside.
            var inController = World.Spawn.Offset(8, 1, 4);
            var outDummy = PlaceTrunkFixture("north", inController);

            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, outDummy, flags));

            // Footprint gate precedes the carryable check: the dummy's
            // footprint redirects to the claim-blocked controller.
            Assert.Null(Actions.PickUpIntoHands(stranger.Entity, outDummy, out var dummyFailure));
            Assert.Equal(FailureCodes.AlreadyReported, dummyFailure);
            AssertTrunk(inController);
            AssertDummy(outDummy, "north");

            // Outside controller with the part cell inside the claim.
            var outController = World.Spawn.Offset(9, 1, 6);
            var inDummy = PlaceTrunkFixture("south", outController);

            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, outController, flags));

            // No grandfathering: the standing structure's claim-blocked dummy
            // cell denies the pickup, leaving both cells intact.
            Assert.Null(Actions.PickUpIntoHands(stranger.Entity, outController, out var managerFailure));
            Assert.Equal(FailureCodes.AlreadyReported, managerFailure);
            AssertTrunk(outController);
            AssertDummy(inDummy, "south");
            Assert.False(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Denied pickup must not grant the carry");
        }

        /// <summary>
        /// Owner straddle positive control (row 8): pickups of both
        /// orientations succeed with both footprint cells clearing, and
        /// place-downs in both directions succeed with the dummy landing at
        /// vanilla's look-aware variant offset.
        /// </summary>
        [Trait("summary", "The owner picks up trunks of both straddle orientations with both footprint cells clearing, and place-downs in both directions place the look-aware variant with the dummy at that variant's own offset.")]
        [Trait("pins", "The whole-footprint gate must not over-block owners, and variant and dummy placement match vanilla's look-aware resolution.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task OwnerStraddle_BothDirectionsAndBothTargets_Succeed()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            // Pickup, control-out orientation: controller outside, dummy inside.
            var pickOut = World.Spawn.Offset(9, 1, 4);
            var pickOutDummy = PlaceTrunkFixture("south", pickOut);
            Assert.True(Actions.PickUpIntoHands(owner.Entity, pickOut, out var pickFailure1) != null,
                $"Owner pickup of the outside half failed: {pickFailure1}");
            AssertAir(pickOut);
            AssertAir(pickOutDummy);

            // Place-down, part-inside direction (trunk-south).
            var placeTarget = World.Spawn.Offset(9, 1, 6);
            var selection = AimDownAtRelative(placeTarget);
            await TeleportForFacingAsync(owner, placeTarget, "south");
            var carriedStack = Carry.GetCarried(owner.Entity, CarrySlot.Hands)?.ItemStack;
            Assert.NotNull(carriedStack);
            var variant = ResolvePlacementVariant(owner.Player, selection, carriedStack!.Block!);
            Assert.NotNull(variant);

            var placedAt = Actions.PlaceDown(owner.Player, CarrySlot.Hands, selection, out var placeFailure1);
            Assert.True(placedAt != null, $"Owner place-down (part inside) failed: {placeFailure1}");
            Assert.Equal(placeTarget, placedAt);
            Assert.Equal(variant!.Code, World.BlockAt(placeTarget).Code);
            Assert.Equal(TrunkFootprint["south"].DummyCode,
                World.BlockAt(placeTarget.Copy().Add(-1, 0, 0)).Code?.ToString());
            Assert.False(Carry.IsCarrySlotOccupied(owner.Entity, CarrySlot.Hands));

            // Pickup, control-in orientation: controller inside own claim.
            var pickIn = World.Spawn.Offset(8, 1, 4);
            var pickInDummy = PlaceTrunkFixture("north", pickIn);
            Assert.True(Actions.PickUpIntoHands(owner.Entity, pickIn, out var pickFailure2) != null,
                $"Owner pickup of the inside half failed: {pickFailure2}");
            AssertAir(pickIn);
            AssertAir(pickInDummy);

            // Place-down, control-inside direction (trunk-north).
            var placeTarget2 = World.Spawn.Offset(8, 1, 8);
            var selection2 = AimDownAtRelative(placeTarget2);
            await TeleportForFacingAsync(owner, placeTarget2, "north");
            var carriedStack2 = Carry.GetCarried(owner.Entity, CarrySlot.Hands)?.ItemStack;
            Assert.NotNull(carriedStack2);
            var variant2 = ResolvePlacementVariant(owner.Player, selection2, carriedStack2!.Block!);
            Assert.NotNull(variant2);

            var placedAt2 = Actions.PlaceDown(owner.Player, CarrySlot.Hands, selection2, out var placeFailure2);
            Assert.True(placedAt2 != null, $"Owner place-down (control inside) failed: {placeFailure2}");
            Assert.Equal(placeTarget2, placedAt2);
            Assert.Equal(variant2!.Code, World.BlockAt(placeTarget2).Code);
            Assert.Equal(TrunkFootprint["north"].DummyCode,
                World.BlockAt(placeTarget2.Copy().Add(1, 0, 0)).Code?.ToString());
        }
    }
}
