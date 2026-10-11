using System;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Handler-path place-down rows for the phantom-reconciliation design: the same
    /// entry point a real client's PlaceDownMessage (with its WrittenCells claim)
    /// drives on the server - validation, reconciliation and failure bookkeeping -
    /// asserted through world truth, carry state and client-visible packets.
    /// Covers the place-down reconciliation design's verification matrix, including
    /// its forced-fail rows.
    /// </summary>
    [Trait("area", "Handler path")]
    [Trait("summary", "Handler-path place-down rows driving the real place-down message entry point with its written-cells claim, covering normal placement, success reconciliation, forced failures, and invalid-data guards through world truth, carry state, and client packets.")]
    public class HandlerPathPlaceDownScenarios : CarryOnScenarioBase
    {
        [Trait("summary", "An honest one-cell claim places the chest at the target and clears the hands carry, with exactly one chest in the area.")]
        [Trait("pins", "The normal place-down round-trip.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_NormalClaim_PlacesAndClearsCarry()
        {
            BlockPos pickup = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pickup);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, pickup, out var pf) != null, $"Pickup failed: {pf}");

            BlockPos target = World.Spawn.Offset(3, 1, 0);
            var selection = CarryActions.AimDownAt(target);
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, target, new[] { target });

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            Assert.True(IsChest(World.BlockAt(target)), "Chest should be placed at the target cell");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Equal(1, CountBlocksByCodePrefix(World.Spawn, 5, "game:chest-"));
        }

        [Trait("summary", "The client claims placement at one cell while the server resolves to another; the chest lands at the server's cell and the falsely-claimed cell stays air with the carry cleared.")]
        [Trait("pins", "Predicted-but-wrong cells revert to server truth on the success path.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_PlacedAtMismatch_ReconcilesClaimedCellsToServerTruth()
        {
            BlockPos pickup = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pickup);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, pickup, out var pf) != null, $"Pickup failed: {pf}");

            // The client claims placement at `claimed` but the server resolves the
            // selection to `target` - success with placedAt != message.PlacedAt must
            // reconcile every claimed cell back to server truth.
            BlockPos target = World.Spawn.Offset(3, 1, 0);
            BlockPos claimed = World.Spawn.Offset(5, 1, 0);
            var selection = CarryActions.AimDownAt(target);
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, claimed, new[] { target, claimed });

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            Assert.True(IsChest(World.BlockAt(target)), "Server truth: chest at the actually-placed cell");
            Assert.True(World.BlockAt(claimed).BlockId == 0, "Server truth: falsely-claimed cell stays air");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
        }

        [Trait("summary", "A place-down aimed at an occupied cell with a blocker above is rejected, retaining the carry and leaving exactly the two fixture chests with no ghost anywhere.")]
        [Trait("pins", "A failed place-down leaves no ghost blocks and no item loss.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_ForcedFailOccupied_LeavesNoGhostAndKeepsCarry()
        {
            BlockPos pickup = World.Spawn.Offset(2, 1, 0);
            BlockPos occupiedTarget = World.Spawn.Offset(3, 1, 0);
            World.SetBlock("game:chest-east", pickup);
            World.SetBlock("game:chest-north", occupiedTarget);
            // CarryOn offsets the selection by its face when the aimed cell is not
            // replaceable (CarryPlacementService.cs:444), so both the aimed cell and the
            // face-adjacent cell must be occupied to force a genuine placement failure.
            World.SetBlock("game:chest-east", occupiedTarget.UpCopy());

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, pickup, out var pf) != null, $"Pickup failed: {pf}");
            int revisionBefore = Carry.GetCarriedRevision(player.Entity);

            var selection = CarryActions.AimDownAt(occupiedTarget);
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, occupiedTarget, new[] { occupiedTarget });

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands), "Failed place-down must retain the carry");
            Assert.Equal("game:chest-east",
                Carry.GetCarried(player.Entity, CarrySlot.Hands)?.OriginalBlockCode?.ToString());
            Assert.True(World.BlockAt(occupiedTarget).Code?.ToString() == "game:chest-north",
                "Occupied cell keeps its original block");
            Assert.True(CountBlocksByCodePrefix(World.Spawn, 5, "game:chest-") == 2,
                "Only the two fixture chests remain - no ghost placement anywhere");
            Assert.True(Carry.GetCarriedRevision(player.Entity) > revisionBefore,
                "Rejected place-down should still touch carried attributes");
            Assert.NotEmpty(player.Client.Packets<LockSlotsMessage>(Channel));
        }

        [Trait("summary", "The same failed place-down with an empty claim is a clean no-op beyond the failure bookkeeping: the carry is retained and the world is unchanged.")]
        [Trait("pins", "With no prediction there is nothing to reconcile.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_ForcedFailWithoutPrediction_IsNoOpBeyondDirtyMarks()
        {
            BlockPos pickup = World.Spawn.Offset(2, 1, 0);
            BlockPos occupiedTarget = World.Spawn.Offset(3, 1, 0);
            World.SetBlock("game:chest-east", pickup);
            World.SetBlock("game:chest-north", occupiedTarget);
            World.SetBlock("game:chest-east", occupiedTarget.UpCopy());

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, pickup, out var pf) != null, $"Pickup failed: {pf}");
            int revisionBefore = Carry.GetCarriedRevision(player.Entity);

            // No prediction was performed (matrix row: unreadable-carry drop, :329) -
            // empty WrittenCells must be a clean no-op on failure.
            var selection = CarryActions.AimDownAt(occupiedTarget);
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, occupiedTarget);

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.True(World.BlockAt(occupiedTarget).Code?.ToString() == "game:chest-north");
            Assert.True(CountBlocksByCodePrefix(World.Spawn, 5, "game:chest-") == 2);
            Assert.True(Carry.GetCarriedRevision(player.Entity) > revisionBefore);
        }

        [Trait("summary", "A 65-cell claim over the 64-cell limit is rejected as invalid data, retaining the carry and leaving the target air with no chest placed.")]
        [Trait("pins", "The written-cells count guard.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_OversizedWrittenCells_RejectedAsInvalidData()
        {
            var (player, target) = await PlayerCarryingChestAtAsync();

            var selection = CarryActions.AimDownAt(target);
            var oversized = new BlockPos[65];
            for (int i = 0; i < oversized.Length; i++) oversized[i] = target.Copy();
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, target, oversized);

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            AssertRejectedWithoutWorldChange(player, target);
        }

        [Trait("summary", "A claim cell beyond the leash limit from the message position is rejected as invalid data, retaining the carry and leaving the target air.")]
        [Trait("pins", "The written-cells leash guard.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_CellBeyondLeash_RejectedAsInvalidData()
        {
            var (player, target) = await PlayerCarryingChestAtAsync();

            var selection = CarryActions.AimDownAt(target);
            // Manhattan distance 13 from message.Position, beyond WrittenCellsMaxLeash (12).
            var distant = target.Offset(13, 0, 0);
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, target, new[] { target, distant });

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            AssertRejectedWithoutWorldChange(player, target);
        }

        /// <summary>
        /// R4 - RED before the fix. The actor anchor, isolated.
        /// <para>
        /// The client names both placement anchors 20 blocks from itself and asks for 64
        /// dirty marks right next to them, so both client-supplied anchors agree with each
        /// other and are far from the actor. Pre-fix the leash was measured from
        /// <c>message.Position</c> - itself client-supplied - so the claim was accepted and
        /// the placement proceeded at the fake target. Post-fix the actor anchor rejects it
        /// as invalid data.
        /// </para>
        /// <para>
        /// 20 is the smallest distance that reliably clears <c>WrittenCellsMaxLeash</c>
        /// (12) while staying in the chunks the fixture has already loaded; the exploit does
        /// not need 200 blocks to be reachable, and keeping it small keeps the row about the
        /// leash rather than about chunk loading.
        /// </para>
        /// <para>
        /// This row fails if only the <c>PlacedAt</c> anchor is implemented: here
        /// <c>PlacedAt == Position == far</c>, so a <c>PlacedAt</c>-only check passes this
        /// claim. R4b covers the other half.
        /// </para>
        /// </summary>
        [Trait("summary", "A claim whose two client-supplied anchors both sit far from the actor and whose dirty cells cluster beside them is rejected as invalid data.")]
        [Trait("pins", "The dirty set is anchored to the actor, not to a client-chosen origin.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while the leash was measured from the client-supplied position and the far claim was accepted.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_CellsNearFakePlacementOrigin_FarFromActor_RejectedAsInvalidData()
        {
            var (player, target) = await PlayerCarryingChestAtAsync();

            var far = target.Offset(20, 0, 0);
            var selection = CarryActions.AimDownAt(far);
            var cells = new BlockPos[64];
            for (int i = 0; i < cells.Length; i++) cells[i] = far.Offset(1, 0, 0);

            // PlacedAt == Position == far: the two client anchors agree, so nothing here
            // contradicts itself. Only the actor anchor can catch it.
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, far, cells);

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            AssertRejectedWithoutWorldChange(player, target);
        }

        /// <summary>
        /// R4b - RED before the fix. The <c>PlacedAt</c> anchor, isolated, and the mirror of
        /// R4.
        /// <para>
        /// Here the geometry is the one a real client sends: the selection and the dirty
        /// cells are both next to the actor, and only <c>PlacedAt</c> is nonsense - 20
        /// blocks away. Pre-fix the leash was measured from <c>message.Position</c>, so this
        /// claim was accepted and the placement ran at the near cell, reconciling 64 dirty
        /// marks the attacker chose.
        /// </para>
        /// <para>
        /// This is the row that fails if only the <b>actor</b> anchor is implemented, which
        /// is why it exists: every cell here is comfortably within reach, so an
        /// actor-anchored-only check waves it straight through. (The plan describes this row
        /// as "near <c>message.PlacedAt</c> but far from the actor" - that is R4's geometry,
        /// and it cannot fail against an actor-only implementation, so the stated purpose and
        /// the stated geometry disagreed and the purpose won.)
        /// </para>
        /// </summary>
        [Trait("summary", "A claim whose target and dirty cells are next to the actor but whose claimed placement cell is far away is rejected as invalid data.")]
        [Trait("pins", "A far claimed placement cell cannot launder a near target.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed while a far claimed placement cell was accepted and the near cell was written.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_CellsNearActor_ButFarFromPlacedAt_RejectedAsInvalidData()
        {
            var (player, target) = await PlayerCarryingChestAtAsync();

            var selection = CarryActions.AimDownAt(target);
            var cells = new BlockPos[64];
            for (int i = 0; i < cells.Length; i++) cells[i] = target.Copy();

            var farPlacedAt = target.Offset(20, 0, 0);
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, farPlacedAt, cells);

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            AssertRejectedWithoutWorldChange(player, target);
        }

        [Trait("summary", "A claim containing a null cell is rejected as invalid data, retaining the carry and leaving the target air.")]
        [Trait("pins", "The null written-cell guard.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_NullWrittenCell_RejectedAsInvalidData()
        {
            var (player, target) = await PlayerCarryingChestAtAsync();

            var selection = CarryActions.AimDownAt(target);
            var withNull = new[] { target, null! };
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, target, withNull);

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            AssertRejectedWithoutWorldChange(player, target);
        }

        [Trait("summary", "After a failed place-down the claimed cell still holds the fixture chest and is pickable once the carry is dropped, so claimed cells never hold phantom state.")]
        [Trait("pins", "Post-failure world consistency at the claimed cell.")]
        [Trait("role", "coverage")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_ForcedFail_ClaimedCellResolvesToServerTruthAfterwards()
        {
            BlockPos pickup = World.Spawn.Offset(2, 1, 0);
            BlockPos occupiedTarget = World.Spawn.Offset(3, 1, 0);
            World.SetBlock("game:chest-east", pickup);
            World.SetBlock("game:chest-north", occupiedTarget);
            World.SetBlock("game:chest-east", occupiedTarget.UpCopy());

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, pickup, out var pf) != null, $"Pickup failed: {pf}");

            var selection = CarryActions.AimDownAt(occupiedTarget);
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, occupiedTarget, new[] { occupiedTarget });
            HandlerInvoke.PlaceDown(Mod, player.Player, message);
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands), "Setup: place-down failed as intended");

            // The claimed cell must resolve against server truth afterwards: it still
            // holds the fixture chest, and once the carry is dropped it is pickable.
            Actions.Drop(player.Entity, CarrySlot.Hands);
            var picked = Actions.PickUpIntoHands(player.Entity, occupiedTarget, out var failure);

            Assert.True(picked != null, $"Pickup at the claimed cell should resolve to server truth: {failure}");
            Assert.True(World.BlockAt(occupiedTarget).BlockId == 0, "Claimed cell should now be air after real pickup");
        }

        [Trait("summary", "On a placed-at mismatch with a populated chest, exactly one chest holds exactly the original contents, the falsely-claimed cell holds no container, and the hands clear.")]
        [Trait("pins", "Inventory conservation across a predicted-versus-server placement split.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_PlacedAtMismatch_NoDuplicateContents()
        {
            // The reported dupe vector: a container carrying inventory is
            // placed so the client's predicted cell disagrees with the server's true
            // placement. The success-divergence reconciliation must leave exactly one
            // instance of the container holding exactly the original contents - a second
            // chest anywhere (the "dupe") or a falsely-claimed cell holding anything is
            // a failure.
            BlockPos pickup = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pickup);
            SeedChestWithSticks(pickup, 3);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, pickup, out var pf) != null, $"Pickup failed: {pf}");

            // The client claims placement at `claimed` (where its optimistic prediction
            // wrote a fully-populated container) but the server resolves the selection
            // to `target`. Both cells are claimed so the server reconciles the whole
            // prediction footprint.
            BlockPos target = World.Spawn.Offset(3, 1, 0);
            BlockPos claimed = World.Spawn.Offset(5, 1, 0);
            var selection = CarryActions.AimDownAt(target);
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, claimed, new[] { target, claimed });

            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            Assert.True(IsChest(World.BlockAt(target)), "Server truth: chest at the actually-placed cell");
            Assert.True(World.BlockAt(claimed).BlockId == 0, "Server truth: falsely-claimed cell stays air");
            Assert.True(World.BlockEntityAt<BlockEntityGenericTypedContainer>(claimed) == null,
                "No phantom container block entity may exist at the falsely-claimed cell");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Successful placement consumes the carry");
            Assert.True(CountBlocksByCodePrefix(World.Spawn, 5, "game:chest-") == 1,
                "Exactly one chest exists in the world - no dupe container anywhere");
            Assert.True(CountItemInChests(World.Spawn, 5, "stick") == 3,
                "Exactly the original contents exist - not duplicated, not lost");
        }

        [Trait("summary", "A rejected place-down with a populated chest retains the carry and its contents, materialises no duplicate, and re-placing it restores exactly the original sticks.")]
        [Trait("pins", "Failure keeps the carry's inventory intact and never materialises a second copy.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task HandlerPlaceDown_ForcedFail_NoDuplicateContents()
        {
            // The other dupe vector: placement rejected while the client has already
            // optimistically predicted a populated container. The failure branch must
            // leave the world exactly as it was (no ghost, no dupe), retain the carry
            // with its contents intact, and a later place of that retained carry must
            // restore exactly the original contents.
            BlockPos pickup = World.Spawn.Offset(2, 1, 0);
            BlockPos occupiedTarget = World.Spawn.Offset(3, 1, 0);
            World.SetBlock("game:chest-east", pickup);
            World.SetBlock("game:chest-north", occupiedTarget);
            World.SetBlock("game:chest-east", occupiedTarget.UpCopy());
            SeedChestWithSticks(pickup, 3);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, pickup, out var pf) != null, $"Pickup failed: {pf}");

            var selection = CarryActions.AimDownAt(occupiedTarget);
            var message = new PlaceDownMessage(CarrySlot.Hands, selection, occupiedTarget, new[] { occupiedTarget });
            HandlerInvoke.PlaceDown(Mod, player.Player, message);

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Failed place-down must retain the carry");
            Assert.True(World.BlockAt(occupiedTarget).Code?.ToString() == "game:chest-north",
                "Occupied cell keeps its original block");
            Assert.True(CountBlocksByCodePrefix(World.Spawn, 5, "game:chest-") == 2,
                "Only the two fixture chests remain - no ghost placement, no dupe container");
            Assert.True(CountItemInChests(World.Spawn, 5, "stick") == 0,
                "No duplicate contents materialised into any world block");

            // The retained carry must still hold the original contents: re-placing it at
            // a free cell restores exactly the seeded sticks and nothing more.
            BlockPos free = World.Spawn.Offset(4, 1, 0);
            var placedAt = Actions.PlaceDown(player.Player, CarrySlot.Hands,
                CarryActions.AimDownAt(free), out var placeFailure);
            Assert.True(placedAt != null, $"Re-place of retained carry failed: {placeFailure}");
            Assert.True(CountBlocksByCodePrefix(World.Spawn, 5, "game:chest-") == 3,
                "The two fixtures plus the re-placed chest - nothing extra");
            Assert.True(CountItemInChests(World.Spawn, 5, "stick") == 3,
                "Exactly the original contents exist after re-place - conserved, not duplicated");
        }

        // ------------------------------
        //  Helpers
        // ------------------------------

        private void SeedChestWithSticks(BlockPos pos, int count)
        {
            var chestEntity = World.BlockEntityAt<BlockEntityGenericTypedContainer>(pos);
            Assert.NotNull(chestEntity);
            var stack = new ItemStack(World.Api.World.GetItem(new AssetLocation("stick")));
            stack.StackSize = count;
            chestEntity!.Inventory[0].Itemstack = stack;
            chestEntity.Inventory[0].MarkDirty();
            chestEntity.MarkDirty(true);
        }

        /// <summary>
        /// Totals the stack sizes of <paramref name="itemCode"/> held in <c>game:chest-*</c>
        /// block entities within <paramref name="radius"/> of <paramref name="center"/>.
        /// Any chest anywhere holding that item counts - so a dupe that materialises a
        /// second populated container is caught as a doubled total.
        /// </summary>
        private int CountItemInChests(BlockPos center, int radius, string itemCode)
        {
            int total = 0;
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        var cell = center.Offset(dx, dy, dz);
                        if (World.BlockAt(cell).Code?.FirstCodePart() != "chest") continue;
                        if (World.BlockEntityAt<BlockEntityGenericTypedContainer>(cell) is { } be)
                        {
                            foreach (var slot in be.Inventory)
                            {
                                if (slot.Itemstack?.Collectible?.Code?.ToString() == "game:" + itemCode)
                                    total += slot.Itemstack.StackSize;
                            }
                        }
                    }
            return total;
        }

        private async Task<(ITestPlayer Player, BlockPos Target)> PlayerCarryingChestAtAsync()
        {
            BlockPos pickup = World.Spawn.Offset(2, 1, 0);
            BlockPos target = World.Spawn.Offset(3, 1, 0);
            World.SetBlock("game:chest-east", pickup);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 0));
            Assert.True(Actions.PickUpIntoHands(player.Entity, pickup, out var pf) != null, $"Pickup failed: {pf}");
            return (player, target);
        }

        private void AssertRejectedWithoutWorldChange(ITestPlayer player, BlockPos target)
        {
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Invalid claim must retain the carry");
            Assert.True(World.BlockAt(target).BlockId == 0, "Target cell must stay air (no ghost placement)");
            Assert.True(CountBlocksByCodePrefix(World.Spawn, 5, "game:chest-") == 0,
                "No chest anywhere in the world - the carried one is not placed on an invalid claim");
            Assert.True(player.Client.Packets<LockSlotsMessage>(Channel).Count > 0,
                "Rejected message should notify the client");
        }

        private BlockPos[] MeasureFootprint(BlockPos origin, string firstCodePart)
        {
            var cells = new System.Collections.Generic.List<BlockPos> { origin.Copy() };
            foreach (var face in BlockFacing.ALLFACES)
            {
                var neighbor = origin.Copy().Offset(face);
                if (World.BlockAt(neighbor).Code?.FirstCodePart() == firstCodePart)
                    cells.Add(neighbor);
            }
            return cells.ToArray();
        }

        private static bool IsChest(Block? block)
        {
            return block != null
                && block.BlockId != 0
                && block.Code != null
                && block.Code.FirstCodePart() == "chest";
        }

        private int CountBlocksByCodePrefix(BlockPos center, int radius, string codePrefix)
        {
            int count = 0;
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        var code = World.BlockAt(center.Offset(dx, dy, dz)).Code?.ToString();
                        if (code != null && code.StartsWith(codePrefix, StringComparison.Ordinal)) count++;
                    }
            return count;
        }
    }
}
