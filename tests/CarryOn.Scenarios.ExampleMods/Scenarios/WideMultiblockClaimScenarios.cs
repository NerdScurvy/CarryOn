using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Scenarios;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios.ExampleMods
{
    /// <summary>
    /// Rows for the claim-gate and
    /// sign-capture rows for a multiblock whose configured footprint reaches
    /// <b>3 cells</b> from the controller.
    ///
    /// The pickup gate's multiblock branch (CarryFootprint.GetFootprint) hard-codes
    /// radius 2 around the controller and keeps only cells holding a BlockMultiblock
    /// whose OffsetInv resolves back to it, while the placement gate walks the
    /// behavior's own IterateOverEach box. The two gates therefore disagree for any
    /// configured footprint that leaves the ±2 box - here the dx=+3 dummy, which the
    /// engine's own OnBlockRemoved sweep still clears, so the pre-fix gate under-counts
    /// the real write set and a claim-straddling structure can be picked up past the
    /// gate while the identical placement is denied.
    ///
    /// Vehicle: <c>carrieddemo:wide-multiblock-block</c> (CarryEntityDemo), a 4×1×1
    /// <c>Multiblock</c> behavior block with an unconditional Hands carryable and
    /// dummies at dx +1..+3. Three facts make it the right vehicle:
    /// <list type="bullet">
    /// <item>Vanilla's registered dummy family (assets/game/blocktypes/multiblock.json)
    /// caps dx/dz at ±2, so a dx=+3 dummy needs a mod-registered game-domain family -
    /// <c>game:multiblock-carrydemo-p1-0-0</c>..<c>-p3-0-0</c>, the same registration
    /// mechanism any wider-multiblock mod must use (BlockBehaviorMultiblock resolves
    /// dummy codes in the default domain only).</item>
    /// <item>A 4-long horizontal structure is the only shape that can straddle a
    /// normal full-height claim with a beyond-±2 cell, which is why the vertical
    /// vanilla vector (game:jonas-poweramplifier-sideend-*, dy=+3) carries only the
    /// footprint/verdict rows in the main suite.</item>
    /// <item>The carryable is unconditional (no enabledCondition), so only sign
    /// capture needs a config seed - the WideMultiblockConfig fixture.</item>
    /// </list>
    ///
    /// Footprint table (pinned from wide-multiblock-block.json: sizex 4, sizey 1,
    /// sizez 1, cposition (0,0,0), type carrydemo): controller plus three dummies at
    /// dx +1..+3, same y/z as the controller.
    ///
    /// Claim geometry (the main suite's ClaimFootprintSignScenarios convention):
    /// Cuboidi(Spawn−4 .. Spawn+9) half-open, so inside = x/z in [Spawn−4, Spawn+8]
    /// and the west edge runs between Spawn−5 (outside) and Spawn−4 (inside).
    ///
    /// The straddle rows put the controller at <b>Spawn−7</b> with the dummy row
    /// running east to <b>Spawn−4</b>: the claim's inside range is contiguous, so the
    /// only geometry in which the dx=+3 cell is the <i>sole</i> denied cell is a
    /// controller that sits three cells west of the claim's west edge. Then the
    /// controller and the dx=+1/+2 dummies are all outside (the pre-fix scan sees
    /// only those three and lets the pickup through - the bypass) and the dx=+3 dummy
    /// is inside (the write set the gate misses). Putting the controller on the east
    /// edge instead would leave the dx=+1 dummy inside as well, and the pre-fix gate
    /// would deny for the wrong reason.
    /// </summary>
    [Trait("area", "Example mods")]
    [Trait("summary", "Claim-straddle rows on a 4-long multiblock whose configured footprint leaves the old radius scan: pickup must walk the real footprint so no in-claim dummy or its sign is carried off.")]
    [AtlasDataFiles("fixtures/WideMultiblockConfig", TargetPath = "ModConfig")]
    public class WideMultiblockClaimScenarios : CarryOnScenarioBase
    {
        private const string WideBlockCode = "carrieddemo:wide-multiblock-block";

        /// <summary>Dummy offsets from the controller (dx +1..+3, same y/z).</summary>
        private static readonly int[] DummyOffsetsX = [1, 2, 3];

        /// <summary>
        /// The dummy codes BlockBehaviorMultiblock.OnBlockPlaced writes for those
        /// offsets, in the same order: type carrydemo, dx p1..p3, dy 0, dz 0.
        /// </summary>
        private static readonly string[] DummyCodes =
        [
            "game:multiblock-carrydemo-p1-0-0",
            "game:multiblock-carrydemo-p2-0-0",
            "game:multiblock-carrydemo-p3-0-0",
        ];

        /// <summary>Straddling-controller x offset: 3 cells west of the claim edge.</summary>
        private const int StraddlingControllerDx = -7;

        private LandClaim AddClaimAroundSpawn(IPlayer owner)
        {
            var claim = LandClaim.CreateClaim(owner);
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
            await stranger.TeleportTo(World.Spawn.Offset(2, 0, 6));

            return (owner, stranger);
        }

        private bool IsInsideClaim(BlockPos pos)
        {
            return pos.X >= World.Spawn.X - 4 && pos.X < World.Spawn.X + 9
                && pos.Z >= World.Spawn.Z - 4 && pos.Z < World.Spawn.Z + 9;
        }

        /// <summary>
        /// Places the wide multiblock fixture and pins its four cells: the controller
        /// and the three dummies the behavior's own sizes write (the server
        /// accessor's SetBlock runs BlockBehaviorMultiblock.OnBlockPlaced
        /// synchronously). Returns controller-first.
        /// </summary>
        private List<BlockPos> PlaceWideFixture(BlockPos controller)
        {
            World.SetBlock(WideBlockCode, controller);
            Assert.Equal(WideBlockCode, World.BlockAt(controller).Code?.ToString());

            var cells = new List<BlockPos> { controller };
            for (int i = 0; i < DummyOffsetsX.Length; i++)
            {
                var dummyPos = controller.Copy().Add(DummyOffsetsX[i], 0, 0);
                Assert.Equal(DummyCodes[i], World.BlockAt(dummyPos).Code?.ToString());
                cells.Add(dummyPos);
            }

            return cells;
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

        private Block? FindWallSignFacing(string facingCode)
        {
            foreach (var block in World.Api.World.Blocks)
            {
                if (block?.Code == null || block.Class != CarryCodes.BlockClasses.Sign) continue;
                if (block.Variant == null) continue;
                if (!block.Variant.TryGetValue("attachment", out var attachment) || attachment != "wall") continue;
                if (block.Code.Path.EndsWith("-" + facingCode, StringComparison.Ordinal)) return block;
            }
            return null;
        }

        private void PlaceSignFacing(BlockPos signPos, string facingCode)
        {
            var sign = FindWallSignFacing(facingCode);
            Assert.True(sign != null, $"No wall sign block with {facingCode} facing found");
            World.SetBlock(sign.Code!.ToString(), signPos);
            Assert.Equal(sign.Code.ToString(), World.BlockAt(signPos).Code?.ToString());
        }

        private void AssertAir(BlockPos pos)
        {
            Assert.True(World.BlockAt(pos).BlockId == 0,
                $"Cell {pos} should be air, found {World.BlockAt(pos).Code}");
        }

        private void AssertStructureIntact(IReadOnlyList<BlockPos> cells)
        {
            Assert.Equal(WideBlockCode, World.BlockAt(cells[0]).Code?.ToString());
            for (int i = 0; i < DummyCodes.Length; i++)
            {
                Assert.Equal(DummyCodes[i], World.BlockAt(cells[i + 1]).Code?.ToString());
            }
        }

        private void AssertStructureCleared(IReadOnlyList<BlockPos> cells)
        {
            foreach (var cell in cells)
            {
                AssertAir(cell);
            }
        }

        /// <summary>
        /// R1 - <b>the bypass row (RED before the fix)</b>: a wide multiblock
        /// straddles the claim's west edge with the controller and the dx=+1/+2
        /// dummies outside and only the dx=+3 dummy inside. The stranger's pickup
        /// must be denied NoPermission with the structure intact. Pre-fix the gate's
        /// radius-2 scan only sees the three outside cells, the pickup succeeds, and
        /// the engine's own box sweep removes the in-claim dx=+3 dummy - the row's
        /// denial assert fails and the assert on the intact structure is the
        /// blast-radius evidence.
        /// </summary>
        [Trait("summary", "A wide multiblock straddles a claim's west edge with only its far dummy inside; a stranger's pickup must be denied no-permission with the whole structure intact.")]
        [Trait("pins", "The pickup footprint is the multiblock's own configured box, so an in-claim far dummy denies the pickup.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before the fix, where the radius scan missed the in-claim far dummy and the pickup was allowed.")]
        [Trait("decision", "26")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerPickUp_StraddlingWideMultiblock_FarDummyInClaim_Denied()
        {
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var flags = EnumBlockAccessFlags.BuildOrBreak;
            var cells = PlaceWideFixture(World.Spawn.Offset(StraddlingControllerDx, 1, 4));
            var farDummy = cells[^1];

            Assert.False(IsInsideClaim(cells[0]), "Fixture controller must be outside the claim");
            Assert.True(IsInsideClaim(farDummy),
                $"Fixture dx=+3 dummy at {farDummy} must be inside the claim (the cell the radius-2 scan misses)");
            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, cells[0], flags));
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, farDummy, flags));

            Assert.Null(Actions.PickUpIntoHands(stranger.Entity, cells[0], out var failure));
            Assert.Equal(FailureCodes.AlreadyReported, failure);
            AssertStructureIntact(cells);
            Assert.False(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Denied pickup must not grant the carry");
        }

        /// <summary>
        /// R2 - owner pickup of the same straddling geometry (positive control,
        /// green before and after): the owner's own claim permits all four cells, so
        /// the pickup succeeds and every structure cell clears. It also fails loudly
        /// if the blocktype / carryable / dummy-family wiring is broken, which keeps
        /// R1 from passing vacuously.
        /// </summary>
        [Trait("summary", "The owner of the same straddling geometry may pick it up: every structure cell clears, so the fix does not over-block a permitted straddle.")]
        [Trait("pins", "A permitted straddle is not over-blocked by the footprint walk.")]
        [Trait("role", "control")]
        [Trait("decision", "26")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task OwnerPickUp_SameStraddle_AllCellsCleared()
        {
            ClearStaleClaims();

            var (owner, _) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var cells = PlaceWideFixture(World.Spawn.Offset(StraddlingControllerDx, 1, 4));

            var carried = Actions.PickUpIntoHands(owner.Entity, cells[0], out var pickupFailure);
            Assert.True(carried != null, $"Owner pickup of the straddling structure failed: {pickupFailure}");
            Assert.Equal(WideBlockCode, carried!.Block.Code?.ToString());
            Assert.True(Carry.IsCarrySlotOccupied(owner.Entity, CarrySlot.Hands));
            AssertStructureCleared(cells);
        }

        /// <summary>
        /// R3 - <b>placement-gate parity (green before and after)</b>: the stranger
        /// acquires the same block in wilderness and place-downs it at the straddling
        /// controller cell, so the dx=+3 cell lands inside the claim. The placement
        /// gate already walks the behavior's own box (CarryFootprint
        /// .GetPlacementFootprint → AddMultiblockFootprint), so it denies today -
        /// this row pins the asymmetry the finding describes and the post-fix
        /// symmetry with R1: pickup and placement must agree on the same write set.
        /// </summary>
        [Trait("summary", "Acquired in wilderness and placed at the straddling cell, the placement gate already walks the behavior box and denies no-permission with no cells written, pinning pickup as the odd surface out.")]
        [Trait("pins", "Pickup and placement agree on the same write set; placement was already exact.")]
        [Trait("role", "parity")]
        [Trait("decision", "26")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerPlaceDown_FarDummyInClaim_Denied_PlacementGateAlreadyExact()
        {
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            // Acquire in wilderness, far west of the claim edge.
            var acquireCells = PlaceWideFixture(World.Spawn.Offset(-20, 1, 4));
            var carried = Actions.PickUpIntoHands(stranger.Entity, acquireCells[0], out var pickupFailure);
            Assert.True(carried != null, $"Wilderness wide-multiblock pickup failed: {pickupFailure}");
            AssertStructureCleared(acquireCells);

            var target = World.Spawn.Offset(StraddlingControllerDx, 1, 4);
            var selection = AimDownAtRelative(target);
            await stranger.TeleportTo(target.Copy().Add(-3, -1, 0));

            var flags = EnumBlockAccessFlags.BuildOrBreak;
            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, target, flags));
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, target.Copy().Add(3, 0, 0), flags));

            var placedAt = Actions.PlaceDown(stranger.Player, CarrySlot.Hands, selection, out var placeFailure);
            Assert.True(placedAt == null,
                $"Place-down whose dx=+3 cell lands inside the claim must be denied: {placeFailure}");
            Assert.Equal(FailureCodes.AlreadyReported, placeFailure);
            Assert.True(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Denied place-down must retain the carry");
            AssertAir(target);
            AssertAir(target.Copy().Add(3, 0, 0));
        }

        /// <summary>
        /// R4 - <b>sign capture on the far dummy (RED before the fix)</b>: a wall
        /// sign hanging on the dx=+3 dummy (its own cell is the dummy's east
        /// neighbour, facing west at the support) is in wilderness, so the pickup
        /// must capture it along with the block and leave every structure cell and
        /// the sign cell empty. Pre-fix the sign scan walks the same under-counted
        /// footprint, so the sign is not a candidate: the pickup succeeds, the
        /// engine sweep still removes its support dummy, and the sign is left
        /// orphaned on air (vanilla BlockSign has no auto-break on neighbor removal).
        /// The row fails at the HasAttachedBlocks assert. The captured sign's facing is
        /// asserted through <c>OriginalLocalFace</c>, the sign's own code through the
        /// engine's normalized pick form (see the inline note).
        /// </summary>
        [Trait("summary", "A wall sign hanging on the in-claim far dummy must be captured with the block, leaving every structure cell and the sign cell empty.")]
        [Trait("pins", "Sign capture follows the configured footprint, not a two-cell scan.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before the fix, where the sign on the in-claim far dummy was left behind while the block was carried.")]
        [Trait("decision", "26")]
        [Trait("caveat", "The captured sign code is asserted through the engine's normalized pick form and its face through OriginalLocalFace, not the placed block code.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickUp_SignOnFarDummy_Captured()
        {
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");
            ClearStaleClaims();

            var player = await JoinPlayer("Player");
            await player.TeleportTo(World.Spawn.Offset(20, 0, 4));

            var cells = PlaceWideFixture(World.Spawn.Offset(20, 1, 4));
            var signPos = cells[^1].Copy().Add(1, 0, 0);
            PlaceSignFacing(signPos, "west");

            // Engine fact (decompile-pinned, VSSurvivalMod BlockSign.OnPickBlock):
            // picking a sign yields the *normalized* variant - ground-north if it
            // exists, else wall-north - not the block's real facing, so the captured
            // child code is the engine's pick form. Anchor the assertion to what the
            // engine itself picks for this cell instead of hard-coding a variant.
            var expectedSignCode = World.BlockAt(signPos)
                .SafeOnPickBlock(World.Api.World, signPos)?.Block.Code?.ToString();

            var carried = Actions.PickUpIntoHands(player.Entity, cells[0], out var pickupFailure);
            Assert.True(carried != null, $"Wilderness wide-multiblock pickup failed: {pickupFailure}");
            Assert.True(carried!.HasAttachedBlocks,
                "A wall sign hanging on the dx=+3 dummy must be captured - it is a candidate "
                + "the moment that dummy is in the pickup footprint; the engine sweep removes "
                + "its support either way, so an uncaptured sign is orphaned on air");
            Assert.Equal(1, carried.AttachedBlocks?.Count);
            Assert.Equal(expectedSignCode, carried.AttachedBlocks![0].Block.Code?.ToString());
            Assert.Equal(BlockFacing.WEST, carried.AttachedBlocks![0].OriginalLocalFace);
            AssertStructureCleared(cells);
            AssertAir(signPos);
        }
    }
}
