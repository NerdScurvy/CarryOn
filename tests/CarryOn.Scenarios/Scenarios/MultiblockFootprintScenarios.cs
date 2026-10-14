using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Rows for the pickup footprint of a
    /// <b>shipped vanilla</b> multiblock whose configured box leaves the gate's
    /// hard-coded radius-2 scan.
    ///
    /// <c>CarryFootprint.GetFootprint</c> is the shared input of the pickup
    /// permission gate (CarryPickupService.TryPickUp) and the attached-sign scan
    /// (CarrySignScan), and its <c>BlockBehaviorMultiblock</c> branch
    /// hard-codes <c>radiusX = radiusY = radiusZ = 2</c> and keeps only cells
    /// holding a <c>BlockMultiblock</c> whose <c>OffsetInv</c> resolves back to the
    /// controller. The placement view (<c>GetPlacementFootprint</c> →
    /// <c>AddMultiblockFootprint</c>) instead walks the behavior's own
    /// <c>IterateOverEach</c> box, so the two gates disagree for every configured
    /// footprint that leaves the ±2 box.
    ///
    /// Vehicle: <c>game:jonas-poweramplifier-sideend-east</c>
    /// (assets/survival/blocktypes/machine/jonas/gensideend.json) - a
    /// <c>Multiblock</c> behavior block with <c>sizex 1, sizey 4, sizez 3,
    /// cposition (0,0,1)</c>, so its configured box is 1×4×3 = <b>12 cells</b>
    /// (controller + 11 dummies) and its <c>dy = +3</c> row sits three cells above
    /// the controller, outside the radius-2 box. Vanilla registers the
    /// <c>dy: "p3"</c> dummy state in assets/game/blocktypes/multiblock.json
    /// precisely for this block. It is the one shipped multiblock that exceeds ±2;
    /// every default-carryable vanilla multiblock (trunk 2×1×1, banner 1×3×1,
    /// mannequin 1×2×1, painting 2×1×1) stays inside it, which is why the rest of
    /// the suite never tripped the defect.
    ///
    /// The row pins the <b>computation</b> through a reflection seam because
    /// <c>CarryFootprint</c> is internal; the end-to-end consequence of the same
    /// under-count (a claim-straddling structure picked up past the gate, an
    /// orphaned sign) is pinned by the ExampleMods suite's
    /// <c>WideMultiblockClaimScenarios</c> R1/R4 rows, which need a wider
    /// structure than vanilla can register. G3 is the main suite's end-to-end gate
    /// verdict on shipped content; its claim is a synthetic <b>y-band</b>, which
    /// exists only so a 1×4×3 column can be made to straddle a claim - the
    /// realistic full-height straddle is R1's job. The row doc says so in-band.
    ///
    /// No <c>AtlasDataFiles</c> seed: the block is not carryable by default, so no
    /// config surface is involved.
    /// </summary>
    [Trait("area", "Multiblock")]
    [Trait("summary", "A shipped vanilla multiblock whose configured box exceeds the pickup gate's hard-coded radius-2 scan anchors rows asserting the footprint computation covers the full configured box and the permission gate verdict follows the real write set.")]
    public class MultiblockFootprintScenarios : CarryOnScenarioBase
    {
        private const string JonasSideEndEast = "game:jonas-poweramplifier-sideend-east";
        private const string TrunkSouth = "game:trunk-south";

        /// <summary>
        /// The configured box of gensideend-east relative to its controller
        /// (dx 0, dy 0..+3, dz −1..+1), pinned from the blocktype's
        /// sizex/sizey/sizez and cposition. (0, 0) is the controller cell; the other
        /// 11 are the dummy cells the engine's OnBlockPlaced writes.
        /// </summary>
        private static readonly (int Dy, int Dz)[] JonasBoxOffsets =
        [
            (0, -1), (0, 0), (0, 1),
            (1, -1), (1, 0), (1, 1),
            (2, -1), (2, 0), (2, 1),
            (3, -1), (3, 0), (3, 1),
        ];

        /// <summary>gensideend-east's controller cell height in the rows below.</summary>
        private const int BaseY = 1;

        private static string Signed(int value)
            => value < 0 ? "n" + Math.Abs(value) : value > 0 ? "p" + value : "0";

        /// <summary>
        /// The dummy code <c>BlockBehaviorMultiblock.OnBlockPlaced</c> writes for a
        /// gensideend box cell: <c>multiblock-{type}-{dx}-{dy}-{dz}</c> in the
        /// default game domain, with dx 0 for this block.
        /// </summary>
        private static string JonasDummyCode(int dy, int dz)
            => $"game:multiblock-monolithic-0-{Signed(dy)}-{Signed(dz)}";

        private BlockPos BasePos => World.Spawn.Offset(6, BaseY, 0);

        /// <summary>
        /// The exact 12-cell write set the engine's own removal sweep clears:
        /// every cell of the configured box, controller included.
        /// </summary>
        private static HashSet<BlockPos> ExpectedJonasBox(BlockPos controller)
        {
            var cells = new HashSet<BlockPos>();
            foreach (var (dy, dz) in JonasBoxOffsets)
            {
                cells.Add(controller.Copy().Add(0, dy, dz));
            }

            return cells;
        }

        /// <summary>
        /// Calls the internal <c>CarryFootprint.GetFootprint</c> through reflection.
        /// The seam resolves the type and method from the <b>runtime</b> mod
        /// assembly (<c>Mod.GetType().Assembly</c> - the assembly the game actually
        /// loaded), not the copy this test compiled against, and fails fast naming
        /// the drifted symbol if a rename ever lands - the same pattern as
        /// <c>HandlerInvoke</c>.
        /// </summary>
        private HashSet<BlockPos> GetFootprintViaSeam(BlockPos pos)
        {
            var footprintType = Mod.GetType().Assembly.GetType("CarryOn.Common.Logic.CarryFootprint")
                ?? throw new InvalidOperationException(
                    "CarryOn.Common.Logic.CarryFootprint is not in the runtime CarryOn assembly - "
                    + "the type moved or was renamed; update the MultiblockFootprintScenarios seam.");

            var method = footprintType.GetMethod("GetFootprint",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    "CarryFootprint.GetFootprint(IWorldAccessor, BlockPos, Block) is not in the "
                    + "runtime CarryOn assembly - the method moved or was renamed; update the "
                    + "MultiblockFootprintScenarios seam.");

            var block = World.Api.World.BlockAccessor.GetBlock(pos);
            return (HashSet<BlockPos>)method.Invoke(null, [World.Api.World, pos, block])!;
        }

        private static void AssertSameCellSet(HashSet<BlockPos> actual, HashSet<BlockPos> expected, string because)
        {
            var missing = expected.Except(actual).OrderBy(p => p.Y).ThenBy(p => p.Z).ToArray();
            var extra = actual.Except(expected).OrderBy(p => p.Y).ThenBy(p => p.Z).ToArray();
            Assert.True(missing.Length == 0 && extra.Length == 0,
                $"{because} Expected {expected.Count} cells, got {actual.Count}; "
                + $"missing [{string.Join(", ", missing.Select(p => p.ToString()))}]; "
                + $"extra [{string.Join(", ", extra.Select(p => p.ToString()))}]");
        }

        /// <summary>
        /// Places gensideend-east and pins the fixture: the controller holds the
        /// jonas block and all eleven dummy cells the behavior's sizes write are
        /// present with their expected codes (the server accessor's SetBlock runs
        /// BlockBehaviorMultiblock.OnBlockPlaced synchronously). This doubles as the
        /// content premise the design asks for.
        /// </summary>
        private void PlaceJonasFixture(BlockPos controller)
        {
            World.SetBlock(JonasSideEndEast, controller);
            Assert.Equal(JonasSideEndEast, World.BlockAt(controller).Code?.ToString());

            foreach (var (dy, dz) in JonasBoxOffsets)
            {
                if (dy == 0 && dz == 0) continue; // the controller cell
                var cell = controller.Copy().Add(0, dy, dz);
                Assert.Equal(JonasDummyCode(dy, dz), World.BlockAt(cell).Code?.ToString());
            }
        }

        private LandClaim AddClaimBandAroundSpawn(IPlayer owner)
        {
            var claim = LandClaim.CreateClaim(owner);
            claim.Areas =
            [
                new Cuboidi(World.Spawn.Offset(-4, 4, -4), World.Spawn.Offset(9, 7, 9))
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

        private void AssertJonasIntact(BlockPos controller)
        {
            Assert.Equal(JonasSideEndEast, World.BlockAt(controller).Code?.ToString());
            foreach (var (dy, dz) in JonasBoxOffsets)
            {
                if (dy == 0 && dz == 0) continue;
                var cell = controller.Copy().Add(0, dy, dz);
                Assert.Equal(JonasDummyCode(dy, dz), World.BlockAt(cell).Code?.ToString());
            }
        }

        /// <summary>
        /// G1 - <b>RED before the fix</b>: the pickup footprint of a shipped vanilla
        /// multiblock must be the configured box, all 12 cells. Pre-fix the
        /// radius-2 scan collects the controller plus the eight dummies inside ±2
        /// (dy 0..+2) and misses the three <c>dy = +3</c> cells - which the engine's
        /// own <c>OnBlockRemoved</c> sweep still clears, so the gate under-counts the
        /// real write set. The row is the computation-level pin; WideMultiblockClaimScenarios
        /// R1/R4 pin the consequence.
        /// </summary>
        [Trait("summary", "The pickup footprint of the shipped jonas power-amplifier multiblock covers its configured 12-cell box, including the top row that sits outside the gate's radius-2 scan.")]
        [Trait("pins", "The pickup footprint is the multiblock's configured box, not a hard-coded radius-2 neighborhood.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before the fix with 9 cells instead of the configured 12.")]
        [Trait("caveat", "Pins the computation through a reflection seam; the end-to-end consequence is pinned by the ExampleMods suite.")]
        [AtlasScenario(RollbackWorld = true)]
        public Task GetFootprint_GensideEnd_CoversTheFullConfiguredBox_G1()
        {
            var controller = BasePos;
            PlaceJonasFixture(controller);

            var footprint = GetFootprintViaSeam(controller);
            AssertSameCellSet(footprint, ExpectedJonasBox(controller),
                "gensideend-east's pickup footprint must be its configured 1x4x3 box (controller + 11 dummies, "
                + "including the dy=+3 row that leaves the gate's radius-2 scan).");

            return Task.CompletedTask;
        }

        /// <summary>
        /// G2 - <b>green before and after</b>: a vanilla trunk's footprint is its
        /// controller plus its single dummy, and the fix must not move it. Same
        /// vehicle class as G1 (a <c>BlockBehaviorMultiblock</c> controller target),
        /// so the ±2 boundary is visible in one place; the trunk is the no-regression
        /// control the whole suite's existing multiblock rows rest on.
        /// </summary>
        [Trait("summary", "A vanilla trunk's footprint stays exactly its controller plus its single dummy cell - the shape the rest of the suite's multiblock rows rest on.")]
        [Trait("pins", "The footprint fix must not move footprints that already fit inside the radius-2 scan.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public Task GetFootprint_TrunkWithinTwoCells_Unchanged_G2()
        {
            var controller = BasePos;
            World.SetBlock(TrunkSouth, controller);
            Assert.Equal(TrunkSouth, World.BlockAt(controller).Code?.ToString());
            var dummy = controller.Copy().Add(-1, 0, 0);
            Assert.Equal("game:multiblock-monolithic-n1-0-0", World.BlockAt(dummy).Code?.ToString());

            var footprint = GetFootprintViaSeam(controller);
            AssertSameCellSet(footprint, [controller, dummy],
                "A trunk's pickup footprint is exactly its controller and its one dummy cell.");

            return Task.CompletedTask;
        }

        /// <summary>
        /// G3 - <b>RED before the fix</b>: the gate verdict on shipped content. A
        /// claim band covering only the structure's <c>dy = +3</c> row must make the
        /// pickup fail <c>NoPermission</c>, because that row is inside the real write
        /// set. Pre-fix the gate's nine-cell footprint contains no banded cell, the
        /// gate passes, and the pickup fails later on the carryability check as
        /// <c>NotCarryable</c> - the row fails on the failure code.
        /// <para>
        /// Honest label: the band is a <b>synthetic claim shape</b>, used only to make
        /// a 1×4×3 column straddle a claim at all; every real claim in the game is
        /// full height, and the realistic full-height straddle needs a wider
        /// structure than vanilla can register - that is the ExampleMods suite's R1
        /// row. The premise asserts below are the content check (does
        /// <c>Claims.TestAccess</c> honor an arbitrary y-band?) and the
        /// jonas-carryability check; if either fails the row fails loudly instead of
        /// silently proving nothing.
        /// </para>
        /// </summary>
        [Trait("summary", "A claim band covering the structure's top row makes a stranger's pickup of the jonas block fail no-permission through the footprint gate, with the structure left intact.")]
        [Trait("pins", "The permission gate verdict follows the real write set, top row included, rather than under-counting the footprint.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before the fix with not-carryable, the wrong verdict for the wrong reason.")]
        [Trait("caveat", "The claim is a synthetic band around a real vanilla multiblock; realistic full-height straddles are covered by the ExampleMods suite.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerPickUp_JonasWithClaimBandOverTopRow_DeniedByFootprint_G3()
        {
            ClearStaleClaims();

            var owner = await JoinPlayer("Owner");
            var stranger = await JoinPlayer("Stranger");
            await owner.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await stranger.TeleportTo(World.Spawn.Offset(4, 0, 2));

            var controller = BasePos;
            PlaceJonasFixture(controller);
            AddClaimBandAroundSpawn(owner.Player);

            var flags = EnumBlockAccessFlags.BuildOrBreak;
            var topRowCell = controller.Copy().Add(0, 3, 0);

            // Spike 1: the claim band gates the dy=+3 row and nothing below it.
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, topRowCell, flags));
            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, controller, flags));

            // Premise: the jonas block is not carryable by default (no shipped
            // patch), so pre-fix the gate passes and the pickup fails later as
            // NotCarryable. If automap ever clones a carryable onto it, the row's
            // expected failure code changes and this assert says so.
            Assert.False(Carry.IsCarryable(World.BlockAt(controller), CarrySlot.Hands),
                "game:jonas-poweramplifier-sideend-east is not carryable by default - G3 observes the "
                + "gate verdict on a block that fails the carryability check afterwards.");

            Assert.Null(Actions.PickUpIntoHands(stranger.Entity, controller, out var failure));
            Assert.Equal(FailureCodes.AlreadyReported, failure);
            Assert.False(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Denied pickup must not grant the carry");
            AssertJonasIntact(controller);
        }
    }
}
