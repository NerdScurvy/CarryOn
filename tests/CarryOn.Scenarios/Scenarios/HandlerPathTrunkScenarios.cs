using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Handler-path place-down of a trunk (a two-cell block), claiming every
    /// footprint cell in WrittenCells. Vanilla trunks are gated behind the
    /// Carryables.ChestTrunk flag (default false), so this class seeds a config
    /// that enables it.
    ///
    /// The claim mirrors the prediction's full footprint: the controller cell
    /// plus the live multiblock dummy at the placed variant's own offset,
    /// resolved through the engine's <see cref="Block.SuggestedHVOrientation"/>
    /// exactly the way the server resolves the placed variant
    /// (footprint table pinned from
    /// assets/survival/blocktypes/wood/chest-trunk.json).
    /// </summary>
    [Trait("area", "Handler path")]
    [Trait("summary", "Handler-path place-down of a two-cell trunk (seeded Carryables.ChestTrunk=true): pickup clears the controller and the multiblock dummy, and a place-down whose claim covers the translated two-cell footprint reconciles both cells to the placed trunk with the carry slot cleared.")]
    [AtlasDataFiles("fixtures/TrunkConfig", TargetPath = "ModConfig")]
    public class HandlerPathTrunkScenarios : CarryOnScenarioBase
    {
        private static readonly Dictionary<string, (int Dx, int Dy, int Dz, string DummyCode)> TrunkFootprint = new()
        {
            ["north"] = (1, 0, 0, "game:multiblock-monolithic-p1-0-0"),
            ["east"] = (0, 0, 1, "game:multiblock-monolithic-0-0-p1"),
            ["south"] = (-1, 0, 0, "game:multiblock-monolithic-n1-0-0"),
            ["west"] = (0, 0, -1, "game:multiblock-monolithic-0-0-n1"),
        };

        [Trait("summary", "Picks up a live trunk, resolves the placed variant via SuggestedHVOrientation, and asserts a place-down claiming the controller and the multiblock dummy reconciles both cells to the placed trunk with the hands slot cleared.")]
        [Trait("pins", "Multi-cell claims reconcile to the placed structure; the trunk claim must include the live multiblock dummy at the placed variant's own offset.")]
        [Trait("role", "regression")]
        [Trait("decision", "10")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_TrunkTwoCellsClaim_BothCellsReconcileToServerTruth()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk,
                "Seed did not enable trunk carrying");

            var trunks = World.Api.World.Blocks
                .Where(b => b?.Code != null && b.Code.Domain == "game" && b.Code.FirstCodePart() == "trunk")
                .ToList();
            var carryableTrunks = trunks
                .Where(b => b!.GetBehavior<BlockBehaviorCarryable>() != null)
                .ToList();
            Assert.True(carryableTrunks.Count > 0,
                "No carryable trunk variant found. Trunk codes: "
                + string.Join(", ", trunks.Select(b => b!.Code)));

            Block trunk = carryableTrunks[0]!;
            string trunkFirstPart = trunk.Code!.FirstCodePart();

            BlockPos trunkPos = World.Spawn.Offset(6, 1, 0);
            World.SetBlock(trunk.Code!.ToString(), trunkPos);

            // Full footprint: controller + the live multiblock dummy the trunk's
            // placement behavior wrote at the variant's own side offset.
            var fixtureSide = CarryRotationHelper.GetBlockFacing(trunk.Code!);
            Assert.True(fixtureSide != null,
                $"Trunk {trunk.Code} should carry a facing suffix");
            var (fdx, fdy, fdz, _) = TrunkFootprint[fixtureSide!.Code!];
            BlockPos fixtureDummy = trunkPos.Copy().Add(fdx, fdy, fdz);
            BlockPos[] footprint = { trunkPos.Copy(), fixtureDummy };

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(6, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, trunkPos, out var pf) != null, $"Trunk pickup failed: {pf}");
            Assert.All(footprint, cell => Assert.True(World.BlockAt(cell).BlockId == 0,
                "Every footprint cell should be air after pickup"));

            // Claim every footprint cell, translated to the new location - what a
            // client that predicted the whole trunk would send. The placed
            // variant (and therefore the dummy offset) is chosen by the engine's
            // public static SuggestedHVOrientation from the player's own geometry,
            // so resolve it the same way the server will and derive the dummy cell.
            BlockPos newTarget = World.Spawn.Offset(9, 1, 0);
            var selection = AimDownAtRelative(newTarget);
            var facing = Block.SuggestedHVOrientation(player.Player, selection)[0].Code!;
            var (dx, dy, dz, dummyCode) = TrunkFootprint[facing];
            BlockPos dummyCell = newTarget.Copy().Add(dx, dy, dz);
            var claimedCells = new[] { newTarget.Copy(), dummyCell };

            var message = new PlaceDownMessage(CarrySlot.Hands, selection, newTarget, claimedCells);

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            Assert.True(World.BlockAt(newTarget).Code?.FirstCodePart() == trunkFirstPart,
                $"The controller cell should reconcile to the placed trunk, found {World.BlockAt(newTarget).Code}");
            Assert.Equal(dummyCode, World.BlockAt(dummyCell).Code?.ToString());
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// Builds a placement selection with a block-relative HitPosition (vanilla
        /// convention - Block.SuggestedHVOrientation reads blockPos + HitPosition;
        /// the shared CarryActions.AimDownAt helper fills an absolute vector, which
        /// skews HorizontalOrientable's look-aware variant choice).
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
    }
}
