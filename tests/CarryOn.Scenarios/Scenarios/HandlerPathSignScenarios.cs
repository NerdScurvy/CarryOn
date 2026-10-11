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
    /// Handler-path place-down of a chest with an attached wall sign, claiming both
    /// cells in WrittenCells (matrix row: chest with attached wall signs, server
    /// rejects → parent and sign cells cleared). Wall-sign capture is opt-in config
    /// (CarryOptions.CarryAttachedWallSigns, default false), so this class seeds a
    /// config that enables it.
    /// </summary>
    [Trait("area", "Handler path")]
    [Trait("summary", "Handler-path place-down of a chest with an attached wall sign (seeded CarryAttachedWallSigns=true): pickup captures the sign cluster, and a place-down claiming both cells either re-places them rotation-aware or, on rejection, keeps the whole cluster with nothing written.")]
    [AtlasDataFiles("fixtures/SignConfig", TargetPath = "ModConfig")]
    public class HandlerPathSignScenarios : CarryOnScenarioBase
    {
        [Trait("summary", "Picks up a chest-east with its north-facing wall sign, claims parent and child at the new location, and asserts the chest is placed with the sign re-placed at a rotated neighbor cell facing its parent and the slot cleared.")]
        [Trait("pins", "Cluster place-down re-places attached children rotation-aware and orientation-agnostically, attached to the new parent.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_AttachedWallSignClaim_BothCellsReconcile()
        {
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            // A wall sign south of the chest, facing north (attached to the chest):
            // exactly the state CaptureAttachedSigns recognizes (wall attachment
            // variant + facing code pointing at the parent).
            Block? sign = FindWallSignFacing("north");
            Assert.True(sign != null, "No wall sign block with north facing found");
            // South() mutates BlockPos in place; use AddCopy to keep chestPos intact.
            BlockPos signPos = chestPos.AddCopy(0, 0, 1);
            World.SetBlock(sign.Code!.ToString(), signPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            string pickupFailure = "";
            bool pickedUp = Carry.TryPickUp(player.Entity, chestPos, CarrySlot.Hands, ref pickupFailure);
            Assert.True(pickedUp, $"Chest pickup failed: {pickupFailure}; " + DiagnoseChestState(chestPos, signPos));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.True(World.BlockAt(chestPos).BlockId == 0, "Chest cell cleared by pickup");
            Assert.True(World.BlockAt(signPos).BlockId == 0, "Attached sign captured with the chest");

            // Claim both cells - parent and attached child - exactly like a client
            // that predicted the whole cluster.
            BlockPos newChest = World.Spawn.Offset(5, 1, 0);
            BlockPos newSign = newChest.AddCopy(0, 0, 1);
            var selection = CarryActions.AimDownAt(newChest);
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, newChest, new[] { newChest, newSign });

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            Assert.True(IsChest(World.BlockAt(newChest)), "Chest placed at the new cell");

            // The placed chest's orientation is chosen by vanilla placement (player
            // yaw), so the attached sign is re-placed at the correspondingly rotated
            // neighbor cell with a rotated facing. The contract: a wall sign exists
            // next to the new chest and is attached to it.
            Block? placedSign = null;
            BlockPos? placedSignPos = null;
            foreach (var face in BlockFacing.HORIZONTALS)
            {
                var candidatePos = newChest.Copy().Offset(face);
                var candidate = World.BlockAt(candidatePos);
                if (candidate?.Class == CarryCodes.BlockClasses.Sign)
                {
                    placedSign = candidate;
                    placedSignPos = candidatePos;
                    break;
                }
            }
            Assert.True(placedSign != null,
                "Attached sign re-placed at a neighbor cell of the new chest (rotated with it)");

            var signFacing = CarryRotationHelper.GetBlockFacing(placedSign!.Code);
            Assert.True(signFacing != null && placedSignPos!.Copy().Offset(signFacing).Equals(newChest),
                "Re-placed wall sign must face its parent chest (attached, not floating)");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
        }

        [Trait("summary", "Places the same cluster at an occupied target with a blocker above, and asserts the rejection retains the carry with the attached sign, writes no ghost chest or sign, bumps the carried revision, and notifies the client.")]
        [Trait("pins", "A rejected cluster place-down keeps the whole cluster intact and writes nothing: no ghost placement, no dropped or duplicated sign.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_WallSignClaimOnRejection_BothClaimedCellsReconcile()
        {
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            Block? sign = FindWallSignFacing("north");
            Assert.True(sign != null, "No wall sign block with north facing found");
            BlockPos signPos = chestPos.AddCopy(0, 0, 1);
            World.SetBlock(sign.Code!.ToString(), signPos);

            // An occupied target with a blocker above it, so the face-offset
            // fallback placement cannot sneak the carry in either.
            BlockPos occupiedTarget = World.Spawn.Offset(5, 1, 0);
            World.SetBlock("game:chest-north", occupiedTarget);
            World.SetBlock("game:chest-east", occupiedTarget.UpCopy());

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            string pickupFailure = "";
            bool pickedUp = Carry.TryPickUp(player.Entity, chestPos, CarrySlot.Hands, ref pickupFailure);
            Assert.True(pickedUp, $"Chest pickup failed: {pickupFailure}; " + DiagnoseChestState(chestPos, signPos));

            var carried = Carry.GetCarried(player.Entity, CarrySlot.Hands);
            Assert.True(carried != null && carried.HasAttachedBlocks, "Carry must include the attached sign");

            // A client that predicted the whole cluster claims both cells at the
            // (occupied) target - the server must reject and leave both claimed
            // cells at server truth: no ghost chest, no ghost sign.
            BlockPos claimedSign = occupiedTarget.AddCopy(0, 0, 1);
            long revisionBefore = Carry.GetCarriedRevision(player.Entity);
            var selection = CarryActions.AimDownAt(occupiedTarget);
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, occupiedTarget,
                new[] { occupiedTarget, claimedSign });

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Rejected place-down must retain the carry (chest and attached sign)");
            var retained = Carry.GetCarried(player.Entity, CarrySlot.Hands);
            Assert.True(retained != null && retained.HasAttachedBlocks,
                "The attached sign must stay part of the carry - not dropped, not duped");
            Assert.True(World.BlockAt(occupiedTarget).Code?.ToString() == "game:chest-north",
                "Claimed parent cell keeps the fixture chest (no ghost placement)");
            Assert.True(World.BlockAt(claimedSign).BlockId == 0,
                "Claimed sign cell stays air (no ghost sign)");
            Assert.True(Carry.GetCarriedRevision(player.Entity) > revisionBefore,
                "Rejected message should bump the carried revision (failure observable)");
            Assert.True(player.Client.Packets<CarryOn.Common.Network.LockSlotsMessage>(Channel).Count > 0,
                "Rejected message should notify the client");
        }

        private string DiagnoseChestState(BlockPos chestPos, BlockPos signPos)
        {
            var chest = World.BlockAt(chestPos);
            var sign = World.BlockAt(signPos);
            return $"chestAt={chest.Code} "
                + $"chestCarryable={chest.GetBehavior<BlockBehaviorCarryable>() != null} "
                + $"configChest={Mod.Config.Carryables.Chest} "
                + $"signAt={sign.Code}";
        }

        private Block? FindWallSignFacing(string facingCode)
        {
            foreach (var block in World.Api.World.Blocks)
            {
                if (block?.Code == null || block.Class != CarryCodes.BlockClasses.Sign) continue;
                if (block.Variant == null) continue;
                if (!block.Variant.TryGetValue("attachment", out var attachment) || attachment != "wall") continue;
                if (block.Code.Path.EndsWith("-" + facingCode, System.StringComparison.Ordinal)) return block;
            }
            return null;
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
