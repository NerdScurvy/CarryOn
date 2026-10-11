using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.API.Event.Data;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Claim-overlap regression coverage: whole-footprint
    /// permission for carried-block pickup and placement, multiblock trunk
    /// dummy cells and attached wall signs.
    ///
    /// Decided rules encoded here (decompile-pinned vanilla seams in the plan
    /// status log): (1) a placement is denied unless every cell it writes -
    /// target, trunk dummy at the variant's own offset, attached sign at the
    /// rotated offset - is claim-permitted; (2) pickup gates the whole
    /// main/multiblock footprint under current claim state, no grandfathering;
    /// (3) attached signs never deny the parent - a sign whose own cell is
    /// denied is left behind in the world (vanilla BlockSign does not
    /// auto-break on neighbor removal, so it survives).
    ///
    /// Rotation pins: the non-dropped preflight estimate is yaw-based
    /// (CarryRotationHelper.EstimatePreflightRotation - placedMeshAngle =
    /// (yaw + PI) % 2PI), so Entity.Pos.Yaw = PI/2 with a captured
    /// OriginalMeshAngle of 0 pins the estimate at 3 steps. The actual
    /// rotation reads the placed BE's MeshAngle, which vanilla
    /// BlockGenericTypedContainer.DoPlaceBlock sets from player POSITION
    /// geometry (atan2 of entity-minus-target, snapped to multiples of PI/2
    /// with a PI/8 nudge for "22.5degnot45deg"), so a player standing due
    /// west of the target (dx=-3.5, dz=-0.5 -> atan2 = -1.713 -> snapped
    /// -PI/2) pins the actual at the same 3 steps. RotateOffset((0,0,1), 3)
    /// = (-1,0,0) and RotateFacing(north, 3) = east (order N,W,S,E): a sign
    /// captured south of its parent lands west of the new parent, facing
    /// east at it.
    ///
    /// Fixture: a combined TrunkSignConfig seed - Atlas data-file seeds
    /// collide on file name (later seed overwrites earlier, DataSeeder
    /// decompile pin), and both the trunk and sign fixtures ship a
    /// CarryOnConfig.json, so the two configs are merged into one fixture
    /// directory instead of two AtlasDataFiles attributes.
    ///
    /// Claim geometry (group B/C convention): Cuboidi(Spawn-4 .. Spawn+9),
    /// half-open, so inside = x/z &lt;= Spawn+8 and the edge runs between
    /// Spawn+8 (inside) and Spawn+9 (outside). Survival is forced (vanilla
    /// privilege bypasses are creative-gated). Trunk footprint table pinned
    /// from assets/survival/blocktypes/wood/chest-trunk.json.
    /// </summary>
    [Trait("area", "Claims/Permissions")]
    [Trait("summary", "Seeded with trunk and wall-sign carrying enabled on a combined fixture, regression rows for the claim-overlap fix assert whole-footprint permission over trunk dummy cells and attached signs at rotated offsets, with sign capture filtered per cell and no grandfathering of later claims.")]
    [AtlasDataFiles("fixtures/TrunkSignConfig", TargetPath = "ModConfig")]
    public class ClaimFootprintSignScenarios : CarryOnScenarioBase
    {
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

        private bool IsInsideClaim(BlockPos pos)
        {
            return pos.X >= World.Spawn.X - 4 && pos.X < World.Spawn.X + 9
                && pos.Z >= World.Spawn.Z - 4 && pos.Z < World.Spawn.Z + 9;
        }

        /// <summary>
        /// Places a vanilla trunk fixture with its live multiblock dummy (the
        /// server accessor's SetBlock runs BlockBehaviorMultiblock.OnBlockPlaced
        /// synchronously, which places the dummy cell). Returns the dummy position.
        /// </summary>
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

        private void AssertTrunk(BlockPos pos)
        {
            Assert.True(World.BlockAt(pos).Code?.FirstCodePart() == TrunkFirstPart,
                $"Cell {pos} should hold the trunk, found {World.BlockAt(pos).Code}");
        }

        private void AssertDummy(BlockPos pos, string side)
        {
            Assert.Equal(TrunkFootprint[side].DummyCode, World.BlockAt(pos).Code?.ToString());
        }

        private static bool IsChest(Block? block)
        {
            return block != null
                && block.BlockId != 0
                && block.Code != null
                && block.Code.FirstCodePart() == "chest";
        }

        private int CountBlocksWithCodePrefix(BlockPos center, int radius, string prefix)
        {
            var accessor = World.Api.World.BlockAccessor;
            int count = 0;
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -3; dy <= 3; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                var block = accessor.GetBlock(new BlockPos(center.X + dx, center.Y + dy, center.Z + dz));
                if (block.Code?.ToString()?.StartsWith(prefix, StringComparison.Ordinal) == true)
                    count++;
            }

            return count;
        }

        private Block? FindTrunkAround(BlockPos center, int radius, out BlockPos? trunkPos)
        {
            trunkPos = null;
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -3; dy <= 3; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                var pos = new BlockPos(center.X + dx, center.Y + dy, center.Z + dz);
                var block = World.BlockAt(pos);
                if (block.Code?.FirstCodePart() == TrunkFirstPart)
                {
                    trunkPos = pos;
                    return block;
                }
            }
            return null;
        }

        private List<BlockDroppedEventArgs>? activeDrops;

        private List<BlockDroppedEventArgs> Drops => activeDrops ??= new List<BlockDroppedEventArgs>();

        private void OnBlockDropped(object? sender, BlockDroppedEventArgs e)
        {
            Drops.Add(e);
        }

        private IDisposable SubscribeBlockDropped()
        {
            Drops.Clear();
            Carry.CarryEvents.BlockDropped += OnBlockDropped;
            return new UnsubscribeAction(() => Carry.CarryEvents.BlockDropped -= OnBlockDropped);
        }

        private sealed class UnsubscribeAction(Action dispose) : IDisposable
        {
            private readonly Action dispose = dispose;

            public void Dispose() => dispose();
        }

        /// <summary>
        /// R1 - stranger pickup of a chest whose attached wall sign sits inside
        /// a foreign claim: the parent is carried (its own cell is outside the
        /// claim) and the sign is LEFT BEHIND - not captured, not removed
        /// (decided rule 3: signs never deny the parent pickup). Vanilla
        /// BlockSign has no auto-break on neighbor change, so the sign simply
        /// stays in the world attached to the now-empty cell.
        /// </summary>
        [Trait("summary", "A stranger picks up a chest whose attached wall sign sits inside a foreign claim: the parent pickup succeeds on its own permitted cell, the sign is not captured, and it stays in the world untouched.")]
        [Trait("pins", "Attached signs never deny the parent pickup; unpermitted signs are left behind.")]
        [Trait("role", "regression")]
        [Trait("decision", "3")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerPickUp_ClusterSignInsideClaim_SignLeftBehind()
        {
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var flags = EnumBlockAccessFlags.BuildOrBreak;

            // Chest outside the claim, wall sign on its -X face inside the
            // claim (facing points at the parent).
            var chestPos = World.Spawn.Offset(9, 1, 4);
            var signPos = World.Spawn.Offset(8, 1, 4);
            World.SetBlock("game:chest-east", chestPos);
            PlaceSignFacing(signPos, "east");

            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, chestPos, flags));
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, signPos, flags));

            var signCodeBefore = World.BlockAt(signPos).Code?.ToString();

            var carried = Actions.PickUpIntoHands(stranger.Entity, chestPos, out var pickupFailure);
            Assert.True(carried != null,
                $"Parent pickup (own cell outside the claim) must succeed; failure: {pickupFailure}");
            Assert.False(carried!.HasAttachedBlocks,
                "The claim-blocked sign must NOT be captured (left behind, rule 3)");
            AssertAir(chestPos);
            Assert.True(signCodeBefore == World.BlockAt(signPos).Code?.ToString(),
                "The left-behind sign must stay in the world untouched");
            Assert.True(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// R2 - owner pickup of the same geometry: the sign IS captured and
        /// both cells clear (positive control - the claim filter must not
        /// over-block permitted sign cells).
        /// </summary>
        [Trait("summary", "The owner picks up the same chest-plus-sign geometry and the sign is captured, with both cells clearing.")]
        [Trait("pins", "The claim filter must not over-block permitted sign cells.")]
        [Trait("role", "control")]
        [Trait("decision", "3")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task OwnerPickUp_ClusterSignInsideClaim_SignCaptured()
        {
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");
            ClearStaleClaims();

            var (owner, _) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var chestPos = World.Spawn.Offset(9, 1, 4);
            var signPos = World.Spawn.Offset(8, 1, 4);
            World.SetBlock("game:chest-east", chestPos);
            PlaceSignFacing(signPos, "east");

            var carried = Actions.PickUpIntoHands(owner.Entity, chestPos, out var pickupFailure);
            Assert.True(carried != null, $"Owner pickup failed: {pickupFailure}");
            Assert.True(carried!.HasAttachedBlocks, "Owner-permitted sign must be captured");
            Assert.True(carried.AttachedBlocks?.Count == 1,
                "Exactly one attached sign should be captured");
            AssertAir(chestPos);
            AssertAir(signPos);
        }

        /// <summary>
        /// R3 - stranger place-down of a captured chest+sign cluster aimed at
        /// the claim edge, with the rotation pinned so the sign's rotated cell
        /// lands inside the claim: the whole-footprint gate denies the
        /// placement (rule 1) - NoPermission, carry retained (sign included),
        /// no cells written. The preflight estimate is pinned directly
        /// (Entity.Pos.Yaw = PI/2 with OriginalMeshAngle 0 gives 3 steps, so
        /// RotateOffset((0,0,1), 3) = (-1,0,0) puts the sign cell at Spawn+8,
        /// inside the claim) - a deviation from the plan's teleport-search
        /// wording, same determinism, recorded in the status log.
        /// </summary>
        [Trait("summary", "A stranger places a captured chest-plus-sign cluster at the claim edge with the rotation pinned so the sign's rotated cell lands inside the claim: denied no-permission, the carry retained including the sign, and no cells written.")]
        [Trait("pins", "The placement gate covers attached-sign cells at their rotated offsets.")]
        [Trait("role", "regression")]
        [Trait("decision", "1")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerPlaceDown_ClusterSignCellInsideClaim_Denied()
        {
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var flags = EnumBlockAccessFlags.BuildOrBreak;

            // Acquire the cluster in wilderness: chest + wall sign south of it
            // (facing north) - both cells far outside the claim.
            var acquireChest = World.Spawn.Offset(20, 1, 4);
            var acquireSign = acquireChest.AddCopy(0, 0, 1);
            World.SetBlock("game:chest-east", acquireChest);
            PlaceSignFacing(acquireSign, "north");

            var carried = Actions.PickUpIntoHands(stranger.Entity, acquireChest, out var pickupFailure);
            Assert.True(carried != null, $"Wilderness cluster pickup failed: {pickupFailure}");
            Assert.True(carried!.HasAttachedBlocks, "Cluster capture should include the sign");
            AssertAir(acquireChest);
            AssertAir(acquireSign);

            // Aim at the claim edge and pin the orientation: estimate 3 steps
            // puts the sign cell at target + (-1,0,0) = Spawn+8, inside the
            // claim, while the target itself stays outside.
            var target = World.Spawn.Offset(9, 1, 4);
            var selection = AimDownAtRelative(target);
            await stranger.TeleportTo(target.Copy().Add(3, -1, 0));
            stranger.Entity.Pos.Yaw = (float)(Math.PI / 2);

            Assert.True(CarryRotationHelper.EstimatePreflightRotation(carried, stranger.Entity, selection, dropped: false) == 3,
                "Yaw PI/2 with OriginalMeshAngle 0 should pin the preflight estimate at 3 steps "
                + "(sign cell at target + (-1,0,0), inside the claim)");

            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, target, flags));
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, World.Spawn.Offset(8, 1, 4), flags));

            var placedAt = Actions.PlaceDown(stranger.Player, CarrySlot.Hands, selection, out var placeFailure);
            Assert.True(placedAt == null,
                $"Place-down whose sign cell lands inside the claim must be denied: {placeFailure}");
            Assert.Equal(FailureCodes.AlreadyReported, placeFailure);
            Assert.True(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "Denied place-down must retain the carry");
            var retained = Carry.GetCarried(stranger.Entity, CarrySlot.Hands);
            Assert.True(retained != null && retained.HasAttachedBlocks,
                "The retained carry must still include the attached sign - not dropped, not duped");
            AssertAir(target);
            AssertAir(World.Spawn.Offset(8, 1, 4)); // the sign cell stays unwritten
        }

        /// <summary>
        /// R4 - owner place-down of the same straddling cluster: parent and
        /// rotated sign cell both written (positive control). The owner stands
        /// due west of the target so vanilla's position-geometry MeshAngle
        /// (-PI/2) and the pinned yaw estimate (3 steps) agree; the sign lands
        /// at Spawn+8 facing east, attached to the new parent.
        /// </summary>
        [Trait("summary", "The owner places the same straddling cluster with both rotation mechanisms pinned to agree: the parent lands at the target and the sign lands at the rotated cell facing its parent.")]
        [Trait("pins", "Permitted straddling clusters place completely.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task OwnerPlaceDown_ClusterStraddleClaim_PlacesAllCells()
        {
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            // Acquire the cluster in wilderness.
            var acquireChest = World.Spawn.Offset(20, 1, 4);
            var acquireSign = acquireChest.AddCopy(0, 0, 1);
            World.SetBlock("game:chest-east", acquireChest);
            PlaceSignFacing(acquireSign, "north");

            var carried = Actions.PickUpIntoHands(owner.Entity, acquireChest, out var pickupFailure);
            Assert.True(carried != null, $"Wilderness cluster pickup failed: {pickupFailure}");
            Assert.True(carried!.HasAttachedBlocks, "Cluster capture should include the sign");

            // Pin both rotation mechanisms to 3 steps: yaw PI/2 (estimate) and
            // a position due west of the target (vanilla DoPlaceBlock atan2
            // snaps to -PI/2, the same 3 steps from OriginalMeshAngle 0).
            var target = World.Spawn.Offset(9, 1, 4);
            var selection = AimDownAtRelative(target);
            await owner.TeleportTo(target.Copy().Add(-3, -1, 0));
            owner.Entity.Pos.Yaw = (float)(Math.PI / 2);

            Assert.True(CarryRotationHelper.EstimatePreflightRotation(carried, owner.Entity, selection, dropped: false) == 3,
                "Yaw PI/2 with OriginalMeshAngle 0 should pin the preflight estimate at 3 steps");

            var placedAt = Actions.PlaceDown(owner.Player, CarrySlot.Hands, selection, out var placeFailure);
            Assert.True(placedAt != null, $"Owner place-down failed: {placeFailure}");
            Assert.Equal(target, placedAt);
            Assert.True(IsChest(World.BlockAt(target)), "Parent chest placed at the target cell");

            // The sign lands at target + (-1,0,0), inside the owner's claim,
            // facing east - attached to the new parent.
            var signCell = World.Spawn.Offset(8, 1, 4);
            var eastSign = FindWallSignFacing("east");
            Assert.True(eastSign != null, "No east-facing wall sign block found");
            Assert.Equal(eastSign!.Code.ToString(), World.BlockAt(signCell).Code?.ToString());

            var signFacing = CarryRotationHelper.GetBlockFacing(World.BlockAt(signCell).Code);
            Assert.True(signFacing != null && signCell.Copy().Offset(signFacing).Equals(target),
                "The re-placed sign must face its parent chest (attached, not floating)");
            Assert.False(Carry.IsCarrySlotOccupied(owner.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// R5 - a trunk placed on unclaimed land whose dummy cell a second
        /// player's LATER claim now covers: the first player's pickup is
        /// denied - current claim state governs per cell, no grandfathering
        /// (rule 2) - and the standing structure stays intact.
        /// </summary>
        [Trait("summary", "A trunk placed on unclaimed land whose dummy cell a second player's later claim now covers: the original placer's pickup is denied no-permission and the standing structure stays intact.")]
        [Trait("pins", "The current claim state governs per cell; no grandfathering.")]
        [Trait("role", "regression")]
        [Trait("decision", "2")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickUp_TrunkFootprintClaimAddedAfter_Denied_NoGrandfathering()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");
            ClearStaleClaims();

            var placer = await JoinPlayer("Placer");
            var claimOwner = await JoinPlayer("ClaimOwner");
            await placer.TeleportTo(World.Spawn.Offset(12, 0, 4));
            await claimOwner.TeleportTo(World.Spawn.Offset(2, 0, 2));

            // Trunk on unclaimed land: controller outside the future claim
            // edge, dummy cell inside it.
            var controller = World.Spawn.Offset(9, 1, 4);
            var dummy = PlaceTrunkFixture("south", controller);
            Assert.True(IsInsideClaim(dummy), "Fixture dummy must be inside the late claim's area");

            // The claim is added afterwards, covering the dummy cell.
            AddClaimAroundSpawn(claimOwner.Player);
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(placer.Player, dummy, EnumBlockAccessFlags.BuildOrBreak));

            Assert.Null(Actions.PickUpIntoHands(placer.Entity, controller, out var failure));
            Assert.Equal(FailureCodes.AlreadyReported, failure);
            AssertTrunk(controller);
            AssertDummy(dummy, "south");
            Assert.False(Carry.IsCarrySlotOccupied(placer.Entity, CarrySlot.Hands),
                "Denied pickup must not grant the carry");
        }

        /// <summary>
        /// R6 - a carried trunk dropped in the open field writes BOTH footprint
        /// cells: the controller and its live multiblock dummy at the placed
        /// variant's own offset (D7 regression - the dropped path previously
        /// wrote only the controller, breaking the trunk). The drop's facing is
        /// shuffled, so the row scans for the placed controller, derives its
        /// side from the code suffix, and asserts the dummy per the footprint
        /// table.
        /// </summary>
        [Trait("summary", "A carried trunk dropped in the open places the controller and its live multiblock dummy at the placed variant's own offset, with one BlockDropped event reporting the placement.")]
        [Trait("pins", "The dropped path writes the full footprint, not only the controller cell.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DroppedTrunk_OpenField_WritesBothFootprintCells()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");
            ClearStaleClaims();

            var player = await JoinPlayer("Dropper");
            await player.TeleportTo(World.Spawn.Offset(20, 0, 4));

            var trunkPos = World.Spawn.Offset(21, 1, 6);
            PlaceTrunkFixture("south", trunkPos);

            var carried = Actions.PickUpIntoHands(player.Entity, trunkPos, out var pickupFailure);
            Assert.True(carried != null, $"Trunk pickup failed: {pickupFailure}");
            Assert.True(carried!.Block?.Code?.ToString()?.StartsWith("game:trunk-", StringComparison.Ordinal) == true,
                $"The carried block should be a trunk, found {carried.Block?.Code}");

            using (SubscribeBlockDropped())
            {
                Actions.Drop(player.Entity, CarrySlot.Hands);
            }

            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
            Assert.Single(Drops);
            Assert.True(Drops[0].BlockPlaced, "Open-field trunk drop should place the block");

            var center = player.Entity.Pos.AsBlockPos;
            var placedTrunk = FindTrunkAround(center, 5, out var placedPos);
            Assert.True(placedTrunk != null,
                "The dropped trunk controller should be placed in the drop area");
            Assert.True(CountBlocksWithCodePrefix(center, 5, "game:trunk-") == 1,
                "Exactly one trunk controller should exist in the drop area");

            var side = CarryRotationHelper.GetBlockFacing(placedTrunk!.Code);
            Assert.True(side != null,
                $"Placed trunk {placedTrunk.Code} should carry a facing suffix");
            var (dx, dy, dz, dummyCode) = TrunkFootprint[side!.Code!];
            var dummyPos = placedPos!.Copy().Add(dx, dy, dz);
            Assert.True(dummyCode == World.BlockAt(dummyPos).Code?.ToString(),
                $"The dropped trunk must write its multiblock dummy {dummyCode} at {dummyPos}, "
                + $"found {World.BlockAt(dummyPos).Code}");
        }

        /// <summary>
        /// R7 - a stranger inside a foreign claim drops a carried trunk: every
        /// BlockPlacer candidate is claim-blocked, the TryPlaceDown gate denies
        /// (the gate covers the dropped path), and the drop falls back to
        /// items - BlockDropped with BlockPlaced == false, carry cleared, and
        /// no trunk/multiblock blocks written anywhere in the area. (The plan's
        /// optional enclosure variant is skipped - BFS/shuffle would make it
        /// brittle; the shared-gate argument plus this row carry the coverage.
        /// Recorded in the status log.)
        /// </summary>
        [Trait("summary", "A stranger deep inside a foreign claim drops a carried trunk whose every placement candidate is claim-blocked: the drop falls back to items with one unplaced BlockDropped event and no trunk or dummy blocks written into the claim area.")]
        [Trait("pins", "The claim gate covers the dropped path; no claim writes via drops.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DroppedTrunk_StraddleOnlyCandidates_FallsBackToItems_NoClaimWrite()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            // Acquire outside the claim, then move inside it and drop.
            var acquirePos = World.Spawn.Offset(20, 1, 4);
            PlaceTrunkFixture("south", acquirePos);
            await stranger.TeleportTo(World.Spawn.Offset(21, 0, 4));
            var carried = Actions.PickUpIntoHands(stranger.Entity, acquirePos, out var pickupFailure);
            Assert.True(carried != null, $"Trunk pickup outside the claim failed: {pickupFailure}");
            AssertAir(acquirePos);

            // Deep inside the claim: every candidate within the drop radius
            // (Spawn-2..Spawn+6 at y<=3) is claim-covered.
            var inside = World.Spawn.Offset(2, 0, 2);
            await stranger.TeleportTo(inside);
            Assert.True(IsInsideClaim(inside.Copy().AddCopy(4, 2, 4)),
                "Sanity: the whole drop search area must be inside the claim");

            using (SubscribeBlockDropped())
            {
                Actions.Drop(stranger.Entity, CarrySlot.Hands);
            }

            Assert.Null(Carry.GetCarried(stranger.Entity, CarrySlot.Hands));
            Assert.Single(Drops);
            Assert.False(Drops[0].BlockPlaced,
                "Claim-blocked drop must fall back to items, not place a block");
            Assert.True(CountBlocksWithCodePrefix(inside, 6, "game:trunk-") == 0,
                "No trunk block may be written into the claim area");
            Assert.True(CountBlocksWithCodePrefix(inside, 6, "game:multiblock-monolithic") == 0,
                "No multiblock dummy may be written into the claim area");
        }
    }
}
