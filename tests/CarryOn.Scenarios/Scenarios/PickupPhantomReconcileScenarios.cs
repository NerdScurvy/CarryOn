using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.API.Event.Data;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Pickup-side phantom reconciliation on the vanilla wire
    /// (the pickup rows of the reconciliation verification matrix).
    ///
    /// The client predicts a pickup optimistically: it calls TryPickUp locally
    /// first (CarryInteractionStateMachine.ContinuePickUpAction), which clears
    /// the parent cell, every attached child, and - for a vanilla multiblock -
    /// the whole dummy structure through vanilla's synchronous SetBlock(0)
    /// sweep. The whole-footprint permission gate that can then deny the action
    /// is SERVER-ONLY (CarryPickupService's entity.Api.Side == Server term), so
    /// the client cannot predict that denial. When the server denies, the only
    /// reconciliation that exists is FailCarryAction's single
    /// MarkBlockDirty(message.Position) - and that mark already covers a vanilla
    /// multiblock in full, because IBlockAccessor's mark walks the block's
    /// multiblock and marks every part (block entity included: G0 pins this).
    /// What the mark does NOT cover is an attached wall sign: a sign is not part
    /// of the parent block's multiblock, so the sign cell the client cleared
    /// stays phantom-air and the phantom sign block entity is never evicted
    /// (MarkBlockDirty alone does not clear a client BE - README Decision 12).
    ///
    /// The second half is the same gap on the success branch: the claim filter
    /// that leaves a claim-blocked sign in the world is server-only too, so the
    /// client's optimistic path removed a sign the server never captured, and
    /// the success branch sends no reconciliation at all. A successful pickup
    /// emits zero ExchangeBlock and zero BlockEntities entries (the server's own
    /// SetBlock(air) only marks the chunk, not the per-block dirty queue - the
    /// client's own prediction did the removal, and a left-behind sign is a cell
    /// the server never wrote). The picker therefore carries the chest with an
    /// invisible sign until chunk reload.
    ///
    /// Wire mechanics, capture protocol and the trunk pin are HandlerPathVanillaWireScenarios'
    /// (class doc): VanillaClientWire taps the player's dummy client connection
    /// (Atlas's observation surface drains and discards vanilla packets), so
    /// these rows call player.Client.Clear() before the handler invoke, make no
    /// Atlas observation calls afterwards, and tap once at the end; World.Ticks(8)
    /// before Clear flushes the fixture dirty marks out of the window and
    /// World.Ticks(12) after gives the engine's 100 ms dirty flush time to fire.
    /// Duplicate ExchangeBlocks for one mark are normal here and every row's
    /// assertions are "at least one", never "exactly one".
    ///
    /// Claim geometry (ClaimFootprintSignScenarios group B/C convention):
    /// Cuboidi(Spawn-4 .. Spawn+9), half-open, so inside = x/z &lt;= Spawn+8 and
    /// the edge runs between Spawn+8 (inside) and Spawn+9 (outside). Survival is
    /// forced by CarryOnScenarioBase (vanilla land claims do not restrict
    /// creative). Claims do NOT roll back between rows in a class world, so
    /// every row clears them first. Trunk footprint table pinned from
    /// assets/survival/blocktypes/wood/chest-trunk.json.
    ///
    /// Note: a headless Atlas server has no client running CarryOn's
    /// prediction, so these rows cannot observe the phantom itself. The vanilla
    /// wire IS the repair channel (README Decision 12), so "the client is told
    /// the server truth at the divergent cells" is the observable form of the
    /// guarantee - the same proxy the place-down rows use.
    /// </summary>
    [Trait("area", "Pickup/Place-down")]
    [Trait("summary", "Pickup-side phantom reconciliation on the vanilla wire: a claim-denied pickup must resync the cells the client optimistically cleared, including an attached sign the engine's multiblock walk does not cover, while a fully captured cluster resyncs nothing.")]
    [AtlasDataFiles("fixtures/TrunkSignConfig", TargetPath = "ModConfig")]
    public class PickupPhantomReconcileScenarios : CarryOnScenarioBase
    {
        private const string TrunkFirstPart = "trunk";

        /// <summary>
        /// The vanilla error the claim gate itself produces, through
        /// <c>Claims.TryAccess</c> from inside the permission gate. This is the
        /// <em>only</em> message a claim-denied player should get: the handler used to
        /// add "pick-up-failed-no-permission" on top of it, naming the same refusal twice.
        /// </summary>
        private const string VanillaClaimError = "noprivilege-buildbreak-landclaimed";

        /// <summary>
        /// CarryOn's own refusal code for a claim denial. The gate already told the
        /// player, so the handler must not compose "pick-up-failed-already-reported".
        /// </summary>
        private const string PickUpAlreadyReported = FailureCodes.AlreadyReported;

        private static readonly Dictionary<string, (int Dx, int Dy, int Dz, string DummyCode)> TrunkFootprint = new()
        {
            ["north"] = (1, 0, 0, "game:multiblock-monolithic-p1-0-0"),
            ["east"] = (0, 0, 1, "game:multiblock-monolithic-0-0-p1"),
            ["south"] = (-1, 0, 0, "game:multiblock-monolithic-n1-0-0"),
            ["west"] = (0, 0, -1, "game:multiblock-monolithic-0-0-n1"),
        };

        // Test-access flags are passed inline as EnumBlockAccessFlags.BuildOrBreak
        // instead of through a field: a field of an engine enum type makes the type
        // itself unloadable without VintagestoryAPI.dll staged next to the test
        // assembly (value-typed fields need their type's size at class-layout time),
        // which skips the whole assembly at xUnit discovery in Debug builds - the
        // VS Code testing panel then shows every row as skipped with no output.

        // ------------------------------
        //  G0 - control: the engine's mark already covers a whole multiblock
        // ------------------------------

        /// <summary>
        /// G0 - control, and the spike that retired the original R1. A denied
        /// trunk pickup marks ONE cell (FailCarryAction's
        /// MarkBlockDirty(message.Position)) yet the wire carries BOTH structure
        /// cells: IBlockAccessor's mark walks the block's multiblock and marks
        /// every part, block entity included. So the vanilla multiblock footprint
        /// is already reconciled by the existing single mark, and H2 defect (a) is
        /// NOT a footprint gap - it is a sign gap (R2) plus the left-behind sign
        /// (R3).
        ///
        /// The row stays as the pin that the engine's coverage is what saves us:
        /// it also forbids the fix from spraying cells outside the structure
        /// (a blanket "mark the whole footprint plus a radius" repair would still
        /// satisfy R2, so this is the only row that catches it).
        /// </summary>
        [Trait("summary", "A denied trunk pickup marks one cell, yet the wire carries both structure cells because the engine's mark walks the whole multiblock (block entity included).")]
        [Trait("pins", "The engine's coverage is what saves us; it also forbids spraying cells outside the structure.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DeniedTrunkPickup_EngineMarkAlreadyCoversTheWholeStructure()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var controller = World.Spawn.Offset(9, 1, 4);
            var dummyCell = PlaceTrunkFixture("south", controller);

            // Straddles the claim edge: the controller is outside (claim inside =
            // x/z <= Spawn+8), the dummy inside, so the denial comes from the
            // footprint gate and the client's optimistic removal covers both.
            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, controller, EnumBlockAccessFlags.BuildOrBreak));
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, dummyCell, EnumBlockAccessFlags.BuildOrBreak));

            var controllerId = World.BlockAt(controller).BlockId;
            var dummyId = World.BlockAt(dummyCell).BlockId;

            // The controller has to sit at Spawn+9 to be outside the claim, which puts it past
            // MaxInteractionDistance from the join position. Stand next to it so the row still
            // measures claim-denied reconciliation rather than the reach gate.
            await stranger.TeleportTo(World.Spawn.Offset(10, 0, 4));

            await World.Ticks(8);
            stranger.Client.Clear();

            HandlerInvoke.PickUp(Mod, stranger.Player, new PickUpMessage(controller, CarrySlot.Hands));

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(stranger);

            Assert.False(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "A claim-denied pickup must not grant the carry");
            AssertClaimDeniedToast(wire);
            Assert.True(wire.HasBlockUpdate(controller, controllerId),
                $"The aimed footprint cell must be resynced with server truth - {wire.Describe()}");
            Assert.True(wire.HasBlockUpdate(dummyCell, dummyId),
                $"The engine's multiblock walk must resync the dummy cell - {wire.Describe()}");
            Assert.True(wire.BlockUpdates.All(u => u.Pos.Equals(controller) || u.Pos.Equals(dummyCell)),
                $"Nothing outside the marked structure may be resynced - {wire.Describe()}");
            AssertDummy(dummyCell, "south");
            AssertTrunk(controller);
        }

        // ------------------------------
        //  R2 - a denied pickup re-syncs the attached-sign cell too
        // ------------------------------

        /// <summary>
        /// R2 - RED. The same denied trunk pickup, with a wall sign attached to
        /// the trunk's DUMMY cell (inside the claim). The client optimistically
        /// removed parent + dummy + sign; the server removed nothing. A sign is
        /// not part of the parent block's multiblock, so FailCarryAction's mark
        /// (which covers the whole structure - G0) stops short of the sign cell:
        /// the sign cell needs the full phantom eviction on both channels, an
        /// ExchangeBlock with the sign's server-truth id and a BE resync (the
        /// client created a phantom sign BE there). The wire carries the two
        /// structure cells and nothing at the sign.
        /// </summary>
        [Trait("summary", "A denied trunk pickup with a wall sign on the dummy resyncs the sign cell on both channels (ExchangeBlock with the server-truth id plus a BlockEntities entry).")]
        [Trait("pins", "A sign is not part of the parent's multiblock, so the engine's walk stops short and the phantom sign survives.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Landed red on the pre-fix head: only the two structure cells were resynced, nothing at the sign.")]
        [Trait("decision", "12")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DeniedTrunkPickup_ResyncsTheAttachedSignCellToo()
        {
            Assert.True(Mod.Config.Carryables.ChestTrunk, "Seed did not enable trunk carrying");
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var controller = World.Spawn.Offset(9, 1, 4);
            var dummyCell = PlaceTrunkFixture("south", controller);

            // Sign north of the dummy, facing the dummy it hangs on: the sign's
            // support cell is the dummy, which is inside the trunk footprint and
            // inside the claim.
            var signCell = World.Spawn.Offset(8, 1, 5);
            PlaceSignFacing(signCell, "north");
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, signCell, EnumBlockAccessFlags.BuildOrBreak));

            var signId = World.BlockAt(signCell).BlockId;
            Assert.NotEqual(0, signId);

            // See DeniedTrunkPickup_EngineMarkAlreadyCoversTheWholeStructure: the controller is
            // outside the claim and therefore outside reach from the join position.
            await stranger.TeleportTo(World.Spawn.Offset(10, 0, 4));

            await World.Ticks(8);
            stranger.Client.Clear();

            HandlerInvoke.PickUp(Mod, stranger.Player, new PickUpMessage(controller, CarrySlot.Hands));

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(stranger);

            Assert.False(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "A claim-denied pickup must not grant the carry");
            AssertClaimDeniedToast(wire);
            Assert.True(wire.HasBlockUpdate(signCell, signId),
                $"denied pickup must resync the attached-sign cell - {wire.Describe()}");
            Assert.True(wire.BlockEntityUpdates.Any(u => u.Pos.Equals(signCell)),
                $"denied pickup must resync the attached-sign cell's block entity - {wire.Describe()}");
            AssertTrunkAndSignIntact(controller, dummyCell, signCell, "south");
        }

        // ------------------------------
        //  R3 - a sign the server left behind is re-synced to the picker
        // ------------------------------

        /// <summary>
        /// R3 - RED. ClaimFootprintSignScenarios.R1's geometry: a chest outside a
        /// foreign claim with a wall sign inside it, picked up by a stranger. The
        /// pickup SUCCEEDS (decided rule 3 - signs never deny the parent) and the
        /// claim-blocked sign is left behind in the world. The client's
        /// optimistic path removed it anyway, and the success branch sends no
        /// reconciliation, so the picker carries the chest with an invisible
        /// sign. The server must send the sign's own id (and its BE) back down
        /// the wire: the sign cell is a cell the server never wrote, so nothing
        /// else would ever reach the client.
        /// </summary>
        [Trait("summary", "A chest outside a foreign claim with a claim-blocked sign picks up successfully, the sign is left behind, and the sign cell is resynced to the picker.")]
        [Trait("pins", "A successful pickup emits nothing on its own; a left-behind sign is a cell the server never wrote.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Landed red on the pre-fix head: the success branch sent no reconciliation, so the picker kept an invisible sign.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickedUpClusterWithDeniedSign_ResyncsLeftBehindSign()
        {
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var chestPos = World.Spawn.Offset(9, 1, 4);
            var signCell = World.Spawn.Offset(8, 1, 4);
            World.SetBlock("game:chest-east", chestPos);
            PlaceSignFacing(signCell, "east");

            Assert.Equal(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, chestPos, EnumBlockAccessFlags.BuildOrBreak));
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, signCell, EnumBlockAccessFlags.BuildOrBreak));

            var signId = World.BlockAt(signCell).BlockId;
            Assert.NotEqual(0, signId);

            // See DeniedTrunkPickup_EngineMarkAlreadyCoversTheWholeStructure: the chest is
            // outside the claim and therefore outside reach from the join position.
            await stranger.TeleportTo(World.Spawn.Offset(10, 0, 4));

            await World.Ticks(8);
            stranger.Client.Clear();

            HandlerInvoke.PickUp(Mod, stranger.Player, new PickUpMessage(chestPos, CarrySlot.Hands));

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(stranger);

            Assert.True(Carry.IsCarrySlotOccupied(stranger.Entity, CarrySlot.Hands),
                "The parent pickup must succeed - only the sign is left behind");
            Assert.Empty(wire.Errors);
            AssertAir(chestPos);
            Assert.Equal(signId, World.BlockAt(signCell).BlockId);
            Assert.True(wire.HasBlockUpdate(signCell, signId),
                $"left-behind sign must be resynced to the actor's client - {wire.Describe()}");
            Assert.True(wire.BlockEntityUpdates.Any(u => u.Pos.Equals(signCell)),
                $"left-behind sign must be resynced with its block entity - {wire.Describe()}");
        }

        // ------------------------------
        //  G1 - a single-cell denial costs exactly the one mark it costs today
        // ------------------------------

        /// <summary>
        /// G1 - control. A denied plain-chest pickup has a one-cell footprint, so
        /// the reconcile set is exactly the cell FailCarryAction already marks.
        /// That single MarkBlockDirty carries the cell's ExchangeBlock AND the
        /// server-truth block entity data (the engine's dirty flush pairs a
        /// BlockEntities entry with every block it resyncs - probe-pinned), so
        /// the aimed cell needs no repair and no other cell may be touched.
        /// Pins the "no new traffic where the set is a single cell" invariant.
        /// </summary>
        [Trait("summary", "A denied single-cell chest pickup resyncs the aimed cell's block and block entity and touches no other cell or block entity.")]
        [Trait("pins", "No new traffic where the reconcile set is a single cell.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DeniedPlainBlockPickup_MarksNoCellBeyondTheAimedOne()
        {
            ClearStaleClaims();

            var (owner, stranger) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var chestPos = World.Spawn.Offset(2, 1, 2);
            World.SetBlock("game:chest-east", chestPos);
            Assert.NotEqual(EnumWorldAccessResponse.Granted,
                World.Api.World.Claims.TestAccess(stranger.Player, chestPos, EnumBlockAccessFlags.BuildOrBreak));

            var chestId = World.BlockAt(chestPos).BlockId;

            await World.Ticks(8);
            stranger.Client.Clear();

            HandlerInvoke.PickUp(Mod, stranger.Player, new PickUpMessage(chestPos, CarrySlot.Hands));

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(stranger);

            AssertClaimDeniedToast(wire);
            Assert.True(wire.HasBlockUpdate(chestPos, chestId),
                $"The aimed cell must be resynced with server truth - {wire.Describe()}");
            Assert.True(wire.BlockUpdates.All(u => u.Pos.Equals(chestPos)),
                $"A single-cell footprint must not reconcile any other cell - {wire.Describe()}");
            Assert.True(wire.HasBlockEntityData(chestPos),
                $"The aimed cell's own block entity must reach the client - {wire.Describe()}");
            Assert.True(wire.BlockEntityUpdates.All(u => u.Pos.Equals(chestPos)),
                $"A single-cell footprint must not reconcile any other block entity - {wire.Describe()}");
            AssertTrunkOrChest(chestPos);
        }

        // ------------------------------
        //  G2 - a successful single-cell pickup reconciles nothing
        // ------------------------------

        /// <summary>
        /// G2 - control. A successful plain-chest pickup with no attached sign in
        /// range. The picker's client already removed the cell optimistically and
        /// the server's own SetBlock(air) only marks the chunk, not the per-block
        /// dirty queue (probe-pinned: a successful pickup emits zero ExchangeBlock
        /// and zero BlockEntities entries), so the reconcile set is empty and the
        /// fix must add no traffic at all. Pins "no new traffic where the set is
        /// empty".
        /// </summary>
        [Trait("summary", "A successful unclaimed chest pickup sends zero block and zero block entity updates.")]
        [Trait("pins", "An empty reconcile set adds no traffic.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SuccessfulPlainBlockPickup_ReconcilesNothing()
        {
            ClearStaleClaims();

            var player = await JoinPlayer("Stranger");
            await player.TeleportTo(World.Spawn.Offset(4, 0, 2));

            var chestPos = World.Spawn.Offset(4, 1, 2);
            World.SetBlock("game:chest-east", chestPos);

            await World.Ticks(8);
            player.Client.Clear();

            HandlerInvoke.PickUp(Mod, player.Player, new PickUpMessage(chestPos, CarrySlot.Hands));

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(player);

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "An unclaimed pickup must succeed");
            Assert.Empty(wire.Errors);
            Assert.True(wire.BlockUpdates.Count == 0,
                $"A successful pickup has no divergent cell, so nothing may be resynced - {wire.Describe()}");
            Assert.True(wire.BlockEntityUpdates.Count == 0,
                $"A successful pickup has no phantom block entity - {wire.Describe()}");
        }

        // ------------------------------
        //  G3 - a CAPTURED sign is not re-synced as still present
        // ------------------------------

        /// <summary>
        /// G3 - control. The owner picks up the same chest+sign cluster with the
        /// sign claim-permitted: the sign IS captured and removed server-side, so
        /// the client's optimistic removal was right and the sign cell must NOT
        /// be reconciled - resyncing it with the sign's own id would put a stale
        /// sign back on the picker's client. Guards the divergence predicate
        /// against the plausible wrong fix of reconciling every candidate sign
        /// cell unconditionally.
        /// </summary>
        [Trait("summary", "The owner picks up the same chest+sign cluster with the sign claim-permitted; both cells are air server-side and the wire stays empty.")]
        [Trait("pins", "The divergence predicate must not reconcile every candidate sign cell unconditionally.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PickedUpClusterWithPermittedSign_DoesNotResyncTheCapturedSign()
        {
            Assert.True(Mod.Config.CarryOptions.CarryAttachedWallSigns,
                "Seed did not enable wall-sign capture");
            ClearStaleClaims();

            var (owner, _) = await JoinOwnerAndStrangerAsync();
            AddClaimAroundSpawn(owner.Player);

            var chestPos = World.Spawn.Offset(9, 1, 4);
            var signCell = World.Spawn.Offset(8, 1, 4);
            World.SetBlock("game:chest-east", chestPos);
            PlaceSignFacing(signCell, "east");

            var signId = World.BlockAt(signCell).BlockId;
            Assert.NotEqual(0, signId);

            // See DeniedTrunkPickup_EngineMarkAlreadyCoversTheWholeStructure: the chest is
            // outside the claim and therefore outside reach from the join position.
            await owner.TeleportTo(World.Spawn.Offset(10, 0, 4));

            await World.Ticks(8);
            owner.Client.Clear();

            HandlerInvoke.PickUp(Mod, owner.Player, new PickUpMessage(chestPos, CarrySlot.Hands));

            await World.Ticks(12);
            var wire = VanillaClientWire.Tap(owner);

            Assert.True(Carry.IsCarrySlotOccupied(owner.Entity, CarrySlot.Hands));
            Assert.Empty(wire.Errors);
            AssertAir(chestPos);
            AssertAir(signCell);
            Assert.False(wire.HasBlockUpdate(signCell, signId),
                $"a captured sign must not be resynced as still present - {wire.Describe()}");
            Assert.True(wire.BlockUpdates.Count == 0,
                $"A fully captured cluster has no divergent cell, so nothing may be resynced - {wire.Describe()}");
            Assert.True(wire.BlockEntityUpdates.Count == 0,
                $"A fully captured cluster leaves no phantom block entity - {wire.Describe()}");
        }

        // ------------------------------
        //  Helpers
        // ------------------------------

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

        /// <summary>
        /// A claim-denied pickup shows the player exactly one toast, and it is vanilla's
        /// claim message. CarryOn adds no second error naming the same refusal: the gate
        /// reports <see cref="PickUpAlreadyReported"/> precisely so FailCarryAction stays
        /// quiet. Any other code here is a duplicate or a stray composed message.
        ///
        /// The last assertion is the one that makes this durable. This toast is
        /// client-resolved (<c>Message == null</c>), so the client looks up
        /// <c>ingameerror-noprivilege-buildbreak-landclaimed</c> and formats its <c>{0}</c>
        /// with <c>LangParams</c>. If the engine ever stopped sending the claimant, the
        /// empty args make <c>string.Format</c> throw; the engine swallows it and returns
        /// the raw entry, and the player reads a literal <c>{0}</c> on screen instead of
        /// the claim owner's name. Counting the toast is not enough - assert the arg.
        /// </summary>
        private static void AssertClaimDeniedToast(VanillaClientWire wire)
        {
            Assert.True(wire.ErrorsWithCode(PickUpAlreadyReported).Count == 0,
                "The gate already told the player; FailCarryAction must not also send " +
                "already-reported - " + wire.Describe());
            Assert.True(wire.ErrorsWithCode(VanillaClaimError).Count > 0,
                "The claim refusal must reach the player exactly once, from the engine - " + wire.Describe());
            Assert.True(wire.Errors.Count == 1,
                $"A claim-denied pickup must produce one toast, not a pair - {wire.Describe()}");

            var claimError = wire.ErrorsWithCode(VanillaClaimError)[0];
            Assert.True(claimError.IsClientResolved,
                "The claim toast should be localized by the client, not pre-resolved by the " +
                $"server - a server-text copy would forfeit per-player localization. {wire.Describe()}");
            Assert.True(claimError.LangParams.Count == 1,
                "The claim toast's {0} needs exactly one langparam (the claimant); with none, " +
                "the client renders a literal {0}. " + wire.Describe());
            Assert.True(claimError.LangParams[0].Length > 0,
                "The claimant name must not be empty, or the toast reads 'It belongs to ' - " +
                wire.Describe());
            Assert.False(claimError.LangParams[0] == "?",
                "The engine substitutes '?' for a null claimant; a '?' here means the claim " +
                "owner could not be resolved. " + wire.Describe());
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

        private void AssertTrunkAndSignIntact(BlockPos controller, BlockPos dummy, BlockPos sign, string side)
        {
            AssertTrunk(controller);
            AssertDummy(dummy, side);
            Assert.True(World.BlockAt(sign).BlockId != 0,
                $"The denied pickup must not have removed the sign at {sign}");
        }

        private void AssertTrunkOrChest(BlockPos pos)
        {
            var part = World.BlockAt(pos).Code?.FirstCodePart();
            Assert.True(part == TrunkFirstPart || part == "chest",
                $"Cell {pos} should still hold the block it started with, found {World.BlockAt(pos).Code}");
        }
    }
}
