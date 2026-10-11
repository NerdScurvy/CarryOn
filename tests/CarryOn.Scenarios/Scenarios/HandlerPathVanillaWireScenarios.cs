using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Vanilla-wire verification for the place-down failure phantom-reconciliation
    /// design, including the multiplayer/dedi rows of its verification matrix: when a carried block's
    /// place-down is rejected after an optimistic client prediction, the WrittenCells
    /// claim must drive the server's own vanilla resync channel - the 100 ms
    /// ServerSystemBlockSimulation dirty flush - so the client's phantom blocks and
    /// phantom block entities are evicted exactly at the cells the prediction wrote.
    ///
    /// Wire mechanics this class encodes (decompile-verified VS 1.22.6):
    /// MarkBlockDirty/MarkBlockEntityDirty enqueue on ServerMain; a 100 ms game-tick
    /// listener drains them into per-client packets - ExchangeBlock{X,Y,Z,BlockType}
    /// carrying server-truth block ids, and a BlockEntities packet whose entries
    /// carry either BE data or, for cells with no server-side BE, the null-classname
    /// removal marker the client handler turns into RemoveBlockEntity. The rejection
    /// rows claim the body-level cell the embedded player's selection box reaches
    /// into, so vanilla Block.CanPlaceBlock fails with "entityintersecting"
    /// (GetIntersectingEntities over the player's selection box, checked before
    /// behavior gates), which CarryOn forwards as the IngameError failure code.
    ///
    /// Capture: VanillaClientWire taps the player's dummy client connection directly
    /// (Atlas's observation surface drains and discards vanilla packets), so these
    /// rows call player.Client.Clear() before the action, make no Atlas observation
    /// calls after it, and tap once at the end. Rows wait World.Ticks(8) (~266 ms)
    /// before Clear so fixture/pickup dirty marks flush out of the capture window,
    /// and World.Ticks(12) (~400 ms) after the handler invoke so the 100 ms flush
    /// has fired several times (Atlas ticks pace at one per ~33 ms Process() pass).
    ///
    /// Trunk pin: the trunk variant is chosen from the player's own geometry
    /// (Block.SuggestedHVOrientation), which is deterministic with the player
    /// embedded one cell below the target - the facing is read at
    /// claim-construction time, the same value the server resolves during the
    /// placement attempt, and the claim mirrors the prediction's footprint:
    /// controller + multiblock dummy at the variant's own offset (table pinned
    /// from assets/survival/blocktypes/wood/chest-trunk.json). Cluster pin: the preflight
    /// rotation estimate is yaw-based (CarryRotationHelper.EstimatePreflightRotation),
    /// so Entity.Pos.Yaw = PI/2 with a captured OriginalMeshAngle of 0 pins 3 steps
    /// and RotateOffset((0,0,1), 3) = (-1,0,0) puts the claimed sign cell west of
    /// the parent. Selections use block-relative HitPositions (vanilla convention;
    /// CarryActions.AimDownAt's absolute vector skews HorizontalOrientable variant
    /// choice). The preflight sign-support check tolerates the still-air parent cell
    /// (CarryPlacementService clusters the parent position).
    ///
    /// Standing-cell pin: the rejection rows teleport the player onto the
    /// ground-surface cell (Spawn+2,0,0) and aim at the body-level air cell above
    /// it (Spawn+2,1,0). A dummy player sends no client input, so server physics
    /// never ejects it - the entity stays embedded in the surface block with its
    /// 0.6x1.85 selection box reaching 0.85 blocks up into the body-level cell
    /// (probe-verified: Pos stays (x, 2, z), box (-0.3,0,-0.3)-(0.3,1.85,0.3)).
    /// The chest's collision box is 0.8125 tall (chest.json collisionbox), so at
    /// the body-level cell it spans y 3..3.8125 - inside the embedded player's
    /// reach - and vanilla rejects with entityintersecting; the trunk's box
    /// covers the same band, so it rejects identically. Aiming at the surface
    /// cell itself would fail differently (solid ground, notreplaceable), and
    /// CarryPlacementService.TryPlaceDownAt would offset the placement up into
    /// the body-level cell anyway - the rejection the rows pin is the one the
    /// offset path produces, taken directly.
    ///
    /// Chunk-registration pin: the entityintersecting gate queries entities
    /// through per-chunk-section entity lists, and the engine's spawn-time
    /// registration is silently skipped when the section is not in the world map
    /// at the join instant - leaving the entity invisible to those queries
    /// indefinitely (the server's 1000 ms fix-up pass only repairs
    /// InChunkIndex3d mismatches, and the index was already set). The rejection
    /// rows verify visibility first and, when it went missing, bounce the
    /// entity across the section boundary Spawn straddles and back so both
    /// fix-up passes re-register it (EnsurePlayerVisibleToChunkQueries).
    ///
    /// Fixture: TrunkSignConfig - a combined seed (Atlas data-file seeds collide on
    /// file name, so the trunk and sign configs merge into one fixture directory);
    /// rows assert their own config gate.
    /// </summary>
    [Trait("area", "Handler path")]
    [Trait("summary", "Rejected place-downs (seeded ChestTrunk and CarryAttachedWallSigns=true) drive the WrittenCells claim through the engine's 100 ms vanilla dirty flush: claimed cells come back with server-truth ExchangeBlock air plus the block-entity removal marker, and only claimed cells are evicted.")]
    [AtlasDataFiles("fixtures/TrunkSignConfig", TargetPath = "ModConfig")]
    public class HandlerPathVanillaWireScenarios : CarryOnScenarioBase
    {
        private const string EntityIntersecting = "entityintersecting";

        /// <summary>
        /// The engine registers a spawned entity into its chunk section's entity
        /// list at spawn time (SpawnEntity_internal -> AddEntityToChunk) - but the
        /// registration is silently skipped when the section is not in the world
        /// map at that instant, and the server's 1000 ms fix-up pass only repairs
        /// InChunkIndex3d MISMATCHES, so a join-time miss leaves the entity
        /// invisible to GetEntitiesAround (and vanilla's entityintersecting gate)
        /// indefinitely. Spawn straddles a section boundary (x-2 is section
        /// 15999, x+2 is section 16000), so bouncing the entity across it and
        /// back forces both fix-up passes to re-register it into its current
        /// section. Verify-first: the bounce only runs when the registration
        /// actually went missing.
        /// </summary>
        private async Task EnsurePlayerVisibleToChunkQueries(ITestPlayer player)
        {
            var feet = World.Spawn.Offset(2, 0, 0);
            var center = new Vec3d(feet.X + 0.5, feet.Y + 0.5, feet.Z + 0.5);
            long entityId = player.Entity.EntityId;

            bool Visible() => World.Api.World.GetEntitiesAround(center, 5, 5)
                .Any(e => e.EntityId == entityId);

            if (Visible()) return;

            await player.TeleportTo(World.Spawn.Offset(-2, 0, 0));
            await World.Ticks(40);
            await player.TeleportTo(feet);
            await World.Ticks(40);

            Assert.True(Visible(),
                "The player entity never became visible to chunk entity queries - the registration bounce failed");
        }

        private static readonly Dictionary<string, (int Dx, int Dy, int Dz, string DummyCode)> TrunkFootprint = new()
        {
            ["north"] = (1, 0, 0, "game:multiblock-monolithic-p1-0-0"),
            ["east"] = (0, 0, 1, "game:multiblock-monolithic-0-0-p1"),
            ["south"] = (-1, 0, 0, "game:multiblock-monolithic-n1-0-0"),
            ["west"] = (0, 0, -1, "game:multiblock-monolithic-0-0-n1"),
        };

        /// <summary>
        /// R1 - a carried chest placed into the body-level cell above the cell the
        /// player entity is embedded in: vanilla rejects the placement
        /// (entityintersecting), the one-cell WrittenCells claim drives the vanilla
        /// wire, and the client is told. The claimed cell must be resynced on BOTH
        /// channels - ExchangeBlock with the server-truth id (air) and the BE removal
        /// marker - because the client prediction wrote both a phantom block and a
        /// phantom block entity there.
        /// </summary>
        [Trait("summary", "A carried chest rejected with entityintersecting at the body-level cell above the embedded player resyncs its one claimed cell on both vanilla channels - ExchangeBlock air and the BE removal marker - sends the error, retains the carry, and writes no ghost.")]
        [Trait("pins", "One-cell claims get the full two-channel eviction: server-truth block id and the phantom block-entity removal marker.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_StandingCellRejection_ResyncsClaimedCellOnVanillaWire()
        {
            BlockPos pickup = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", pickup);

            ITestPlayer player = await JoinPlayer("Tester");
            // The player stays embedded in the ground-surface cell; the aim and
            // claim target the body-level air cell above it, where the chest's
            // collision box overlaps the player's selection box (class pin notes).
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            BlockPos target = World.Spawn.Offset(2, 1, 0);
            var carried = Actions.PickUpIntoHands(player.Entity, pickup, out var pf);
            Assert.True(carried != null, $"Pickup failed: {pf}");

            var selection = AimDownAtRelative(target);

            // Deterministic chunk registration for the entityintersecting gate
            // below (see EnsurePlayerVisibleToChunkQueries).
            await EnsurePlayerVisibleToChunkQueries(player);

            // Flush the fixture/pickup dirty marks out of the capture window.
            await World.Ticks(8);
            player.Client.Clear();

            // Vanilla comparator: the embedded player's selection box reaches into
            // the body-level cell, so the placement there is rejected with
            // entityintersecting (checked before any behavior gate).
            string vanillaFailure = "no-failure";
            Assert.False(carried!.Block!.CanPlaceBlock(World.Api.World, player.Player, selection, ref vanillaFailure),
                $"Placement into the body-level cell must be rejected by vanilla - {vanillaFailure}");
            Assert.Equal(EntityIntersecting, vanillaFailure);

            var message = new PlaceDownMessage(CarrySlot.Hands, selection, target, new[] { target });
            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(player);

            Assert.True(wire.HasBlockUpdate(target, 0),
                $"The claimed body-level cell must be resynced with server truth (air) - {wire.Describe()}");
            Assert.True(wire.HasBlockEntityRemoval(target),
                $"The claimed body-level cell must get the BE removal marker (phantom chest BE eviction) - {wire.Describe()}");
            Assert.NotEmpty(wire.ErrorsWithCode(EntityIntersecting));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Rejected place-down must retain the carry");
            Assert.True(World.BlockAt(target).BlockId == 0, "No ghost placement at the target cell");
        }

        /// <summary>
        /// R2 - a carried trunk placed into the body-level cell: the claim mirrors the
        /// client prediction's two-cell footprint (controller + multiblock dummy at
        /// the variant's own offset), and BOTH cells must be resynced on both
        /// channels even though the placement never wrote anything server-side.
        /// </summary>
        [Trait("summary", "A carried trunk rejected at the standing cell resyncs both claimed footprint cells - the controller and the look-aware multiblock dummy - on both channels with the error sent, the carry retained, and neither cell written.")]
        [Trait("pins", "Multi-cell claims reconcile every predicted cell of the footprint on both vanilla channels.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_TrunkStandingCellRejection_ResyncsBothFootprintCells()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");

            BlockPos acquire = World.Spawn.Offset(20, 1, 4);
            World.SetBlock("game:trunk-south", acquire);
            var acquireDummy = acquire.Copy().Add(-1, 0, 0);
            Assert.Equal(TrunkFootprint["south"].DummyCode, World.BlockAt(acquireDummy).Code?.ToString());

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(21, 0, 4));
            var carried = Actions.PickUpIntoHands(player.Entity, acquire, out var pf);
            Assert.True(carried != null, $"Trunk pickup failed: {pf}");

            // The player stays embedded in the ground-surface cell; the aim and
            // claim target the body-level air cell above it (class pin notes).
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            BlockPos target = World.Spawn.Offset(2, 1, 0);
            var selection = AimDownAtRelative(target);

            // Deterministic chunk registration for the entityintersecting gate
            // below (see EnsurePlayerVisibleToChunkQueries).
            await EnsurePlayerVisibleToChunkQueries(player);

            await World.Ticks(8);
            player.Client.Clear();

            // The trunk variant comes from the player's own geometry - read it the
            // same way the server's placement attempt will, then mirror the
            // prediction's footprint for the claim.
            var facing = Block.SuggestedHVOrientation(player.Player, selection)[0].Code!;
            var (dx, dy, dz, _) = TrunkFootprint[facing];
            var dummyCell = target.Copy().Add(dx, dy, dz);
            Assert.True(World.BlockAt(dummyCell).BlockId == 0,
                $"Fixture sanity: the trunk dummy cell {dummyCell} should be air, found {World.BlockAt(dummyCell).Code}");

            string vanillaFailure = "no-failure";
            Assert.False(carried!.Block!.CanPlaceBlock(World.Api.World, player.Player, selection, ref vanillaFailure),
                $"Trunk placement into the body-level cell must be rejected by vanilla - {vanillaFailure}");
            Assert.Equal(EntityIntersecting, vanillaFailure);

            var message = new PlaceDownMessage(CarrySlot.Hands, selection, target, new[] { target, dummyCell });
            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(player);

            Assert.True(wire.HasBlockUpdate(target, 0),
                $"The claimed controller cell must be resynced with server truth (air) - {wire.Describe()}");
            Assert.True(wire.HasBlockUpdate(dummyCell, 0),
                $"The claimed dummy cell must be resynced with server truth (air) - {wire.Describe()}");
            Assert.True(wire.HasBlockEntityRemoval(target),
                $"The claimed controller cell must get the BE removal marker - {wire.Describe()}");
            Assert.True(wire.HasBlockEntityRemoval(dummyCell),
                $"The claimed dummy cell must get the BE removal marker - {wire.Describe()}");
            Assert.NotEmpty(wire.ErrorsWithCode(EntityIntersecting));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Rejected place-down must retain the carry");
            Assert.True(World.BlockAt(target).BlockId == 0 && World.BlockAt(dummyCell).BlockId == 0,
                "No ghost placement at either footprint cell");
        }

        /// <summary>
        /// R3 - a captured chest+wall-sign cluster placed into the body-level cell:
        /// the claim mirrors the prediction's cluster footprint (parent + sign at
        /// the estimated rotated offset), and BOTH cells must be resynced on both
        /// channels. The sign never gets placed server-side (the parent placement
        /// fails first), so its claimed cell needs the full phantom eviction too.
        /// </summary>
        [Trait("summary", "A captured chest and wall-sign cluster rejected at the standing cell resyncs the parent and the claimed rotated sign cell on both channels while the retained carry keeps the sign and nothing is written.")]
        [Trait("pins", "A sign never placed server-side still gets its claimed cell evicted - the phantom sign and its phantom block-entity are removed from the client.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_ClusterStandingCellRejection_ResyncsParentAndSignCells()
        {
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");

            BlockPos acquireChest = World.Spawn.Offset(20, 1, 4);
            BlockPos acquireSign = acquireChest.AddCopy(0, 0, 1);
            World.SetBlock("game:chest-east", acquireChest);
            PlaceSignFacing(acquireSign, "north");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(21, 0, 4));
            var carried = Actions.PickUpIntoHands(player.Entity, acquireChest, out var pf);
            Assert.True(carried != null, $"Cluster pickup failed: {pf}");
            Assert.True(carried!.HasAttachedBlocks, "Cluster capture should include the wall sign");
            Assert.True(carried.AttachedBlocks?.Count == 1, "Exactly one attached sign should be captured");

            // The player stays embedded in the ground-surface cell; the aim and
            // claim target the body-level air cell above it (class pin notes).
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            BlockPos target = World.Spawn.Offset(2, 1, 0);
            var selection = AimDownAtRelative(target);

            // Deterministic chunk registration for the entityintersecting gate
            // below (see EnsurePlayerVisibleToChunkQueries).
            await EnsurePlayerVisibleToChunkQueries(player);

            await World.Ticks(8);
            player.Client.Clear();

            // Pin the preflight rotation at 3 steps (yaw-based estimate) and derive
            // the claimed sign cell the prediction wrote: the captured offset
            // (0,0,1) rotated by 3 steps lands at (-1,0,0) from the parent.
            player.Entity.Pos.Yaw = (float)(Math.PI / 2);
            Assert.Equal(3, CarryRotationHelper.EstimatePreflightRotation(
                carried, player.Entity, selection, dropped: false));
            var rotatedOffset = CarryRotationHelper.RotateOffset(
                carried.AttachedBlocks![0].RelativeOffset, 3);
            Assert.True(rotatedOffset.X == -1 && rotatedOffset.Y == 0 && rotatedOffset.Z == 0,
                $"Yaw PI/2 with OriginalMeshAngle 0 should rotate the sign offset to (-1,0,0), got {rotatedOffset}");
            var signCell = target.Copy().Add(rotatedOffset);
            Assert.True(World.BlockAt(signCell).BlockId == 0,
                $"Fixture sanity: the claimed sign cell {signCell} should be air, found {World.BlockAt(signCell).Code}");

            string vanillaFailure = "no-failure";
            Assert.False(carried.Block!.CanPlaceBlock(World.Api.World, player.Player, selection, ref vanillaFailure),
                $"Cluster parent placement into the body-level cell must be rejected by vanilla - {vanillaFailure}");
            Assert.Equal(EntityIntersecting, vanillaFailure);

            var message = new PlaceDownMessage(CarrySlot.Hands, selection, target, new[] { target, signCell });
            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(player);

            Assert.True(wire.HasBlockUpdate(target, 0),
                $"The claimed parent cell must be resynced with server truth (air) - {wire.Describe()}");
            Assert.True(wire.HasBlockUpdate(signCell, 0),
                $"The claimed sign cell must be resynced with server truth (air) - {wire.Describe()}");
            Assert.True(wire.HasBlockEntityRemoval(target),
                $"The claimed parent cell must get the BE removal marker - {wire.Describe()}");
            Assert.True(wire.HasBlockEntityRemoval(signCell),
                $"The claimed sign cell must get the BE removal marker - {wire.Describe()}");
            Assert.NotEmpty(wire.ErrorsWithCode(EntityIntersecting));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Rejected place-down must retain the carry");
            var retained = Carry.GetCarried(player.Entity, CarrySlot.Hands);
            Assert.True(retained != null && retained.HasAttachedBlocks,
                "The retained carry must still include the attached sign - not dropped, not duped");
            Assert.True(World.BlockAt(target).BlockId == 0 && World.BlockAt(signCell).BlockId == 0,
                "No ghost placement at either cluster cell");
        }

        /// <summary>
        /// R4 - success control: a carried chest placed into a free cell sends the
        /// vanilla updates a real client needs - ExchangeBlock with the placed
        /// variant's id and a BlockEntities data entry for the placed chest's BE -
        /// and no error. Validates the tap end-to-end against the packets the
        /// success path itself produces (placedAt == PlacedAt, so no reconcile
        /// marks are involved).
        /// </summary>
        [Trait("summary", "A successful free-cell place-down sends ExchangeBlock with the placed variant's id plus a BlockEntities data entry and no error, validating the wire tap end-to-end against the success path's own packets.")]
        [Trait("pins", "The success path sends the placed block and its block entity; with placedAt equal to the claim there are no reconcile marks to check.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_Success_SendsPlacedBlockAndBlockEntityOnVanillaWire()
        {
            BlockPos pickup = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", pickup);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, pickup, out var pf) != null, $"Pickup failed: {pf}");

            BlockPos target = World.Spawn.Offset(3, 1, 0);
            var selection = AimDownAtRelative(target);

            await World.Ticks(8);
            player.Client.Clear();

            var message = new PlaceDownMessage(CarrySlot.Hands, selection, target, new[] { target });
            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            Assert.True(IsChest(World.BlockAt(target)), "Setup: the chest should be placed at the target cell");

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(player);

            Assert.True(wire.HasBlockUpdate(target, World.BlockAt(target).BlockId),
                $"The placed cell must be sent with the placed variant's id - {wire.Describe()}");
            Assert.True(wire.HasBlockEntityData(target),
                $"The placed chest's BE data must be sent for the placed cell - {wire.Describe()}");
            Assert.Empty(wire.Errors);
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Successful place-down must clear the carry");
        }

        /// <summary>
        /// R5 - negative control: a rejected place-down WITHOUT a prediction claim
        /// (empty WrittenCells - the unreadable-carry / optimistic-placement-off
        /// path) must not send any block-entity resync: nothing was predicted, so
        /// there is no phantom BE to evict. The only block update is the failure
        /// bookkeeping's own single mark at message.Position.
        /// </summary>
        [Trait("summary", "The same rejection with an empty WrittenCells claim sends only the failure bookkeeping's own block update and no block-entity resync, still tells the client, retains the carry, and writes no ghost.")]
        [Trait("pins", "The eviction is claim-driven, not unconditional: nothing was predicted, so there is no phantom block entity to evict.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_RejectionWithoutClaim_NoBlockEntityResync()
        {
            BlockPos pickup = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", pickup);

            ITestPlayer player = await JoinPlayer("Tester");
            // The player stays embedded in the ground-surface cell; the aim
            // targets the body-level air cell above it (class pin notes).
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            BlockPos target = World.Spawn.Offset(2, 1, 0);
            var carried = Actions.PickUpIntoHands(player.Entity, pickup, out var pf);
            Assert.True(carried != null, $"Pickup failed: {pf}");

            var selection = AimDownAtRelative(target);

            // Deterministic chunk registration for the entityintersecting gate
            // below (see EnsurePlayerVisibleToChunkQueries).
            await EnsurePlayerVisibleToChunkQueries(player);

            await World.Ticks(8);
            player.Client.Clear();

            string vanillaFailure = "no-failure";
            Assert.False(carried!.Block!.CanPlaceBlock(World.Api.World, player.Player, selection, ref vanillaFailure),
                $"Placement into the body-level cell must be rejected by vanilla - {vanillaFailure}");

            // No prediction was performed: empty WrittenCells (constructor default).
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, target);
            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(player);

            Assert.True(wire.HasBlockUpdate(target, 0),
                $"The failure bookkeeping's own mark should resync message.Position with server truth (air) - {wire.Describe()}");
            Assert.True(wire.BlockEntityUpdates.All(u => !u.Pos.Equals(target)),
                $"An unclaimed rejection must not send any BE resync for the target cell - {wire.Describe()}");
            Assert.True(wire.ErrorsWithCode(EntityIntersecting).Count > 0,
                $"The rejection should still notify the client - {wire.Describe()}");
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Rejected place-down must retain the carry");
            Assert.True(World.BlockAt(target).BlockId == 0, "No ghost placement at the target cell");
        }

        /// <summary>
        /// R6 - the claim-spill branch, and the one successful place-down that
        /// still leaves a phantom. The parent's placement SUCCEEDS, so the
        /// server never rejects and `placedAt == message.PlacedAt`: the
        /// WrittenCells reconcile in OnPlaceDownMessage is skipped entirely
        /// (CarryServerHandler.cs:209-220). Meanwhile the preflight only
        /// claims-checked the child at the ESTIMATED rotation, and
        /// PlaceAttachedChildren evaluates permission at the ACTUAL rotation
        /// (CarryPlacementService.cs:204-214) - a divergence is the only way to
        /// reach that branch at all, because an agreeing estimate would have
        /// been denied by the preflight. So the client is left holding a
        /// predicted sign at the estimated cell that the server never committed,
        /// and the cell is never dirtied: the wire carries the placed parent and
        /// nothing at the sign.
        ///
        /// Pinned geometry (probe-verified, not reasoned): with a captured
        /// chest-east + wall sign at RelativeOffset (0,0,1), OriginalLocalFace
        /// north and OriginalMeshAngle 0, Pos.Yaw = PI/2 estimates 3 steps while
        /// the placement's actual rotation (the placed chest-east's mesh angle)
        /// is 2. So the client predicts the sign at target + (-1,0,0) and the
        /// server evaluates the child cell at target + (0,0,-1). The claim
        /// covers only the actual cell, which is what the runtime spill check
        /// asks about.
        /// </summary>
        [Trait("summary", "A place-down that succeeds while a foreign claim covers only the actual child spills one attached sign: the client-predicted sign cell is resynced with ExchangeBlock air and the BE removal marker while the parent is placed, the carry clears, and the blocked child stays unwritten.")]
        [Trait("pins", "The spill branch must evict the phantom the client's own prediction created - the cell predicted at the estimated rotation, not the claim-blocked actual child - on both channels.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head: the spilled client-predicted sign cell was not evicted on either channel.")]
        [Trait("decision", "24")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_ClaimSpilledChild_ResyncsTheClientPredictedSignCell()
        {
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");
            ClearStaleClaims();

            // The claim belongs to somebody else: a claim owner has permission
            // everywhere in their own claim, so the runtime spill check would
            // never fire for its own claim.
            ITestPlayer owner = await JoinPlayer("Owner");

            BlockPos acquireChest = World.Spawn.Offset(20, 1, 4);
            BlockPos acquireSign = acquireChest.AddCopy(0, 0, 1);
            World.SetBlock("game:chest-east", acquireChest);
            PlaceSignFacing(acquireSign, "north");

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(21, 0, 4));
            var carried = Actions.PickUpIntoHands(player.Entity, acquireChest, out var pf);
            Assert.True(carried != null, $"Cluster pickup failed: {pf}");
            Assert.True(carried!.HasAttachedBlocks && carried.AttachedBlocks?.Count == 1,
                "Cluster capture should include exactly one attached sign");

            // Created after both players joined, and torn down before the row
            // returns: xUnit runs rows in a class in a non-fixed order and the
            // other rows aim at (2,1,0), which this claim covers.
            var claim = AddClaim(owner);
            try
            {
                await RunClaimSpillPlacement(player, carried);
            }
            finally
            {
                World.Api.World.Claims.Remove(claim);
            }
        }

        private async Task RunClaimSpillPlacement(ITestPlayer player, CarriedBlock carried)
        {
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            BlockPos target = World.Spawn.Offset(2, 1, 2);
            var selection = AimDownAtRelative(target);

            // Estimate-vs-actual divergence pin (the row's whole premise).
            player.Entity.Pos.Yaw = (float)(Math.PI / 2);
            int estimatedSteps = CarryRotationHelper.EstimatePreflightRotation(
                carried, player.Entity, selection, dropped: false);
            Assert.Equal(3, estimatedSteps);
            var predictedOffset = CarryRotationHelper.RotateOffset(
                carried.AttachedBlocks![0].RelativeOffset, estimatedSteps);
            Assert.True(predictedOffset.X == -1 && predictedOffset.Y == 0 && predictedOffset.Z == 0,
                $"Yaw PI/2 with OriginalMeshAngle 0 must predict the sign at (-1,0,0), got {predictedOffset}");
            var predictedSignCell = target.Copy().Add(predictedOffset);

            var actualOffset = CarryRotationHelper.RotateOffset(
                carried.AttachedBlocks[0].RelativeOffset, 2);
            Assert.True(actualOffset.X == 0 && actualOffset.Y == 0 && actualOffset.Z == -1,
                $"The actual rotation must evaluate the child at (0,0,-1), got {actualOffset}");
            var actualChildCell = target.Copy().Add(actualOffset);

            // The claim covers ONLY the actual child cell: the preflight footprint
            // {target, predictedSignCell} stays claim-free so the placement
            // proceeds, and the runtime check at actualChildCell fails.
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(player.Player, actualChildCell, EnumBlockAccessFlags.BuildOrBreak));
            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(player.Player, target, EnumBlockAccessFlags.BuildOrBreak));
            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(player.Player, predictedSignCell, EnumBlockAccessFlags.BuildOrBreak));

            await World.Ticks(8);
            player.Client.Clear();

            var message = new PlaceDownMessage(CarrySlot.Hands, selection, target, new[] { target, predictedSignCell });
            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(player);

            // The placement succeeded, so this is the no-reconcile success path.
            Assert.True(IsChest(World.BlockAt(target)), "Setup: the parent must be placed at the target cell");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A parent placement that succeeded must clear the carry");
            Assert.Equal(2, CarryRotationHelper.GetActualRotationSteps(carried, World.Api.World, target));
            Assert.True(World.BlockAt(actualChildCell).BlockId == 0,
                $"Setup: the claim-blocked child must not be placed, found {World.BlockAt(actualChildCell).Code}");

            Assert.True(wire.HasBlockUpdate(predictedSignCell, 0),
                $"The client-predicted sign cell must be resynced with server truth (air) - {wire.Describe()}");
            Assert.True(wire.HasBlockEntityRemoval(predictedSignCell),
                $"The client-predicted sign cell must get the BE removal marker (phantom sign BE eviction) - {wire.Describe()}");
        }

        // ------------------------------
        //  Helpers
        // ------------------------------

        /// <summary>
        /// Claims do not roll back between rows in a class world, so a row that
        /// creates one clears the rest first.
        /// </summary>
        private void ClearStaleClaims()
        {
            foreach (var stale in World.Api.World.Claims.All.ToArray())
            {
                World.Api.World.Claims.Remove(stale);
            }
        }

        private LandClaim AddClaim(ITestPlayer owner)
        {
            var claim = LandClaim.CreateClaim(owner.Player);
            claim.Areas =
            [
                // Cuboidi's max corner is EXCLUSIVE (probe-verified: a claim of
                // ..(2,16,1) left (2,1,1) free), so this covers x in [1,2] and
                // z in [0,1] - the actual child cell and nothing else the row
                // touches.
                new Cuboidi(World.Spawn.Offset(1, 0, 0), World.Spawn.Offset(3, 16, 2))
            ];
            World.Api.World.Claims.Add(claim);
            return claim;
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
            World.SetBlock(sign!.Code!.ToString(), signPos);
            Assert.Equal(sign.Code.ToString(), World.BlockAt(signPos).Code?.ToString());
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
