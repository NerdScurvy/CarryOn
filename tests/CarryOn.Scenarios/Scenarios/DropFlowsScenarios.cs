using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.API.Event.Data;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Drop flows over the public manager seam: DropCarried placing a carried block into an
    /// open area (BlockDropped with blockPlaced=true), the DropBlockAsEntityOrItem item path
    /// (blockPlaced=false), a multi-slot drop emitting one event per carried block, a dropped
    /// chest+sign cluster re-placing the sign at the rotated offset facing its parent, and
    /// the death-drop chain (lethal damage clears both carry slots with exactly one drop
    /// event per occupied slot and concrete landings for bag and chest).
    /// </summary>
    [Trait("area", "Drop")]
    [Trait("summary", "Drop flows over the public manager seam: open-field placement, the entity-or-item fallback, one event per carried block for multi-slot drops, rotation-aware cluster placement for a chest plus attached wall sign, and the death-drop chain.")]
    public class DropFlowsScenarios : CarryOnScenarioBase
    {
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
        /// Dropping a carried chest in an open field finds a placement, places the block,
        /// clears the slot and fires BlockDropped once with blockPlaced=true.
        /// </summary>
        [Trait("summary", "Dropping a carried chest in the open places the block, clears the slot and fires exactly one BlockDropped with the placed flag, the carried code and the dropping entity.")]
        [Trait("pins", "Open-field drop places the block and the drop event contract (count, flag, code, entity) holds.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropCarried_OpenField_PlacesBlockAndFiresBlockDropped()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var pickupCode);
            Assert.True(carried != null, $"Chest pickup failed: {pickupCode}");

            using (SubscribeBlockDropped())
            {
                Actions.Drop(player.Entity, CarrySlot.Hands);
            }

            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));

            Assert.Single(Drops);
            Assert.True(Drops[0].BlockPlaced, "Open-field drop should place the block");
            Assert.Equal("game:chest-east", Drops[0].CarriedBlock.Block?.Code?.ToString());
            Assert.Same(player.Entity, Drops[0].Entity);
            Assert.Equal(1, CountBlocksWithCodePrefix(player.Entity.Pos.AsBlockPos, 4, "game:chest-"));
        }

        /// <summary>
        /// The dedicated drop-as-entity-or-item branch spawns the chest as a dropped item
        /// instead of placing it, fires BlockDropped with blockPlaced=false and clears the
        /// slot; no chest block appears in the world.
        /// </summary>
        [Trait("summary", "Driving the drop-as-entity-or-item branch directly clears the slot, fires one BlockDropped with the unplaced flag and writes no chest block.")]
        [Trait("pins", "The item fallback path reports itself as unplaced.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropBlockAsEntityOrItem_ItemPath_FiresUnplacedDrop()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var pickupCode);
            Assert.True(carried != null, $"Chest pickup failed: {pickupCode}");

            BlockPos centerBlock = player.Entity.Pos.AsBlockPos.UpCopy();

            using (SubscribeBlockDropped())
            {
                Carry.DropBlockAsEntityOrItem(carried!, centerBlock, player.Player, player.Entity);
            }

            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
            Assert.Single(Drops);
            Assert.False(Drops[0].BlockPlaced);
            Assert.Equal("game:chest-east", Drops[0].CarriedBlock.Block?.Code?.ToString());
            Assert.Equal(0, CountBlocksWithCodePrefix(player.Entity.Pos.AsBlockPos, 4, "game:chest-"));
        }

        /// <summary>
        /// Driving the drop-as-entity-or-item branch for an item-class carry (a bag with
        /// no world block) must land the item instead of throwing. Before the fix, the
        /// branch dereferenced the carried block's world <c>Block</c>, which is null for
        /// an item-class carry, so the call threw and the bag was lost.
        /// </summary>
        [Trait("summary", "Driving the drop-as-entity-or-item branch for an item-class carry lands the bag as a dropped entity, clears the hands slot and does not throw.")]
        [Trait("pins", "The drop-as-entity-or-item branch handles an item-class carry (no world block) without dereferencing a null Block.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head, where the branch read the null carried Block and threw, losing the bag.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropBlockAsEntityOrItem_ItemClassCarry_LandsItemWithoutThrowing()
        {
            BlockPos bagPos = World.Spawn.Offset(2, 1, 0);
            BlockSelection bagSelection = GroundStorageFixture.PlaceBag(
                World.Api.World, bagPos, GroundStorageFixture.BagCode);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, bagSelection, out var pickupCode);
            Assert.True(carried != null, $"Bag pickup failed: {pickupCode}");
            Assert.Null(carried!.Block);

            BlockPos centerBlock = player.Entity.Pos.AsBlockPos.UpCopy();

            var thrown = Record.Exception(() =>
                Carry.DropBlockAsEntityOrItem(carried, centerBlock, player.Player, player.Entity));

            Assert.Null(thrown);
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
            await World.Ticks(2);

            // Content must land as a dropped entity (item or carried-block), never be
            // silently lost to the null-Block dereference.
            bool landed = World.Api.World.LoadedEntities.Values.Any(entity =>
            {
                var pos = entity.Pos.AsBlockPos;
                if (Math.Abs(pos.X - centerBlock.X) > 3
                    || Math.Abs(pos.Y - centerBlock.Y) > 3
                    || Math.Abs(pos.Z - centerBlock.Z) > 3) return false;
                string? code = entity.Code?.ToString();
                return code == "game:item" || code == "carryon:carriedblock";
            });
            Assert.True(landed, "The dropped bag must land as an item or carried-block entity, not be lost");
        }

        /// <summary>
        /// Dropping two occupied slots in one call clears both and emits one BlockDropped
        /// event per carried block, each placed in the open field.
        /// </summary>
        [Trait("summary", "Dropping a Hands crate and a Back chest in one call clears both slots, fires one event per carried block and places one chest and one crate.")]
        [Trait("pins", "A multi-slot drop clears everything and emits exactly one event per block, with no duplicates or misses.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropCarried_MultipleSlots_ClearsAllAndFiresPerBlock()
        {
            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            BlockPos cratePos = World.Spawn.Offset(2, 1, 2);
            World.SetBlock("game:chest-east", chestPos);
            World.SetBlock("game:crate", cratePos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            Assert.True(Actions.PickUpIntoHands(player.Entity, cratePos, out _) != null, "Crate pickup failed");
            Assert.True(Actions.PickUpIntoSlot(player.Entity, chestPos, CarrySlot.Back, out var backCode) != null, $"Chest back pickup failed: {backCode}");

            using (SubscribeBlockDropped())
            {
                Actions.Drop(player.Entity, CarrySlot.Hands, CarrySlot.Back);
            }

            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Back));
            Assert.Equal(2, Drops.Count);
            Assert.Equal(1, CountBlocksWithCodePrefix(player.Entity.Pos.AsBlockPos, 4, "game:chest-"));
            Assert.Equal(1, CountBlocksWithCodePrefix(player.Entity.Pos.AsBlockPos, 4, "game:crate"));
        }

        /// <summary>
        /// A captured chest + wall-sign cluster dropped in the open field re-places the sign
        /// at the rotated offset of the placed chest, facing its parent (the trunk+signs
        /// drop-regression pin - place-down is covered by ClaimFootprintSign, the drop path
        /// here). The drop rotation itself is not pinned (the drop path picks any fitting
        /// facing), so the row derives the actual rotation steps from the placed chest and
        /// requires the sign to be consistent with them.
        /// </summary>
        [Trait("summary", "A captured chest plus attached wall sign dropped in the open places the chest and re-places the sign at the rotation-derived neighbor cell facing its parent.")]
        [Trait("pins", "Cluster drops re-place children rotation-aware at their rotated offsets, attached rather than floating.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DroppedCluster_SignLandsAtRotatedOffset_FacingParent()
        {
            // Sign capture is a config option (default off) - enable it for this row and
            // restore the mod state afterwards; config is mod state, not world state.
            var carryOptions = Mod.Config.CarryOptions;
            bool signsBefore = carryOptions.CarryAttachedWallSigns;
            carryOptions.CarryAttachedWallSigns = true;
            try
            {
                // Acquire the cluster: chest with a wall sign south of it, facing its parent.
                BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
                BlockPos signPos = chestPos.AddCopy(0, 0, 1);
                World.SetBlock("game:chest-east", chestPos);
                PlaceSignFacing(signPos, "north");

                ITestPlayer player = await JoinPlayer("Tester");
                await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

                var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var pickupCode);
                Assert.True(carried != null, $"Cluster pickup failed: {pickupCode}");
                Assert.True(carried!.HasAttachedBlocks, "The attached wall sign must be captured");
                Assert.True(carried.AttachedBlocks?.Count == 1, "Exactly one sign should be captured");
                var capturedSign = carried.AttachedBlocks![0];
                Assert.Equal(new BlockPos(0, 0, 1), capturedSign.RelativeOffset);
                Assert.Equal("north", capturedSign.OriginalLocalFace?.Code);

                // Open field, far from the acquisition site.
                await player.TeleportTo(World.Spawn.Offset(20, 0, 4));

                using (SubscribeBlockDropped())
                {
                    Actions.Drop(player.Entity, CarrySlot.Hands);
                }

                Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
                Assert.Single(Drops);
                Assert.True(Drops[0].BlockPlaced, "Open-field cluster drop should place the cluster");

                var center = player.Entity.Pos.AsBlockPos;
                Assert.Equal(1, CountBlocksWithCodePrefix(center, 5, "game:chest-"));
                var placedChest = FindBlockWithPrefixAround(center, 5, "game:chest-", out var placedChestPos);
                Assert.True(placedChest != null, "The dropped chest should be placed in the drop area");

                int steps = CarryRotationHelper.GetActualRotationSteps(carried, World.Api.World, placedChestPos!);
                var rotatedOffset = CarryRotationHelper.RotateOffset(new BlockPos(0, 0, 1), steps);
                BlockPos expectedSignCell = placedChestPos!.Copy().Add(rotatedOffset.X, rotatedOffset.Y, rotatedOffset.Z);
                var expectedFacing = CarryRotationHelper.RotateFacing(BlockFacing.NORTH, steps);

                var placedSign = World.BlockAt(expectedSignCell);
                Assert.True(placedSign.Code != null
                    && placedSign.Class == CarryCodes.BlockClasses.Sign
                    && placedSign.Variant != null
                    && placedSign.Variant.TryGetValue("attachment", out var attachment)
                    && attachment == "wall",
                    $"A wall sign must land at the rotated cell {expectedSignCell}, found {placedSign.Code}");

                var signFacing = CarryRotationHelper.GetBlockFacing(placedSign.Code);
                Assert.True(signFacing != null && signFacing.Code == expectedFacing.Code,
                    $"The re-placed sign must face {expectedFacing.Code}, found {signFacing?.Code}");
                Assert.True(expectedSignCell.Copy().Offset(signFacing!).Equals(placedChestPos),
                    "The re-placed sign must face its parent chest (attached, not floating)");
            }
            finally
            {
                carryOptions.CarryAttachedWallSigns = signsBefore;
            }
        }

        /// <summary>
        /// The death-drop chain: a bag in the hands slot and a chest in the back slot, then a
        /// lethal hit. Both carry slots clear, exactly one BlockDropped event fires per
        /// occupied slot (the damage drop and the death drop never double-report the same
        /// carried block), and - because vanilla claims answer PlayerDead for the dead
        /// player, refusing the drop path's world placement - the DropMode chain
        /// (EntityOnFailedPlacement) lands both carried blocks as carryon:carriedblock
        /// entities at the death site: never a world write by a dead player, never lost
        /// content. (In the harness the drops route through the death handler; a damage
        /// drop firing earlier in production yields the same one-event-per-slot outcome.)
        /// </summary>
        [Trait("summary", "A lethal hit clears both carry slots, fires exactly one unplaced drop event per occupied slot, and lands both blocks as carried-block entities because vanilla refuses a dead player's world write.")]
        [Trait("pins", "The damage drop and the death drop never double-report a carried block, and a dead player's drop never writes into the world or loses content.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task PlayerDeath_DropsCarried_OncePerOccupiedSlot_ContentLands()
        {
            // A bag (item-class carry) for the hands slot, a chest for the back slot.
            BlockPos bagPos = World.Spawn.Offset(2, 1, 0);
            BlockSelection bagSelection = GroundStorageFixture.PlaceBag(World.Api.World, bagPos, "linensack");

            BlockPos chestPos = World.Spawn.Offset(4, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(3, 0, 2));

            Assert.True(Actions.PickUpBagFromGroundStorage(player.Entity, bagSelection, out var bagCode) != null,
                $"Bag pickup failed: {bagCode}");
            Assert.True(Actions.PickUpIntoSlot(player.Entity, chestPos, CarrySlot.Back, out var backCode) != null,
                $"Chest back pickup failed: {backCode}");
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Back));

            var deathPos = player.Entity.Pos.AsBlockPos;
            using (SubscribeBlockDropped())
            {
                player.Entity.ReceiveDamage(
                    new DamageSource { Type = EnumDamageType.BluntAttack, SourcePos = player.Entity.Pos.XYZ },
                    9999f);
                await World.Ticks(4);
            }

            Assert.False(player.Entity.Alive, "The lethal hit must kill the test player");

            // Premise pin: vanilla claims answer PlayerDead for a dead player, so the
            // drop path's world placement is refused (no-permission) and the DropMode
            // chain (EntityOnFailedPlacement) must land both carried blocks as
            // carryon:carriedblock entities instead - never a world write by a dead
            // player, never lost content.
            Assert.Equal(EnumWorldAccessResponse.PlayerDead,
                World.Api.World.Claims.TestAccess(
                    player.Entity.Player, deathPos.UpCopy(), EnumBlockAccessFlags.BuildOrBreak));

            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Death must clear the hands carry");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Back),
                "Death must clear the back carry");

            // Exactly one drop event per occupied slot - the damage drop and the death
            // drop must not double-report the same carried block.
            Assert.Equal(2, Drops.Count);
            var droppedCodes = Drops
                .Select(d => d.CarriedBlock.Block?.Code?.ToString()
                    ?? d.CarriedBlock.ItemStack?.Collectible?.Code?.ToString())
                .OrderBy(code => code)
                .ToList();
            Assert.Equal(new[] { "game:chest-east", "game:linensack" }, droppedCodes);
            Assert.All(Drops, d => Assert.False(d.BlockPlaced,
                "A dead player's drop must not write into the world (vanilla PlayerDead refusal)"));

            // Concrete landing: both carried blocks survive as carried-block entities at
            // the death site (the DropMode EntityOnFailedPlacement branch).
            var carriedEntities = World.Api.World.LoadedEntities.Values
                .Where(en => en.Code.ToString() == "carryon:carriedblock")
                .ToList();
            Assert.Equal(2, carriedEntities.Count);
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

        private Block? FindBlockWithPrefixAround(BlockPos center, int radius, string prefix, out BlockPos? foundPos)
        {
            foundPos = null;
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -3; dy <= 3; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                var pos = new BlockPos(center.X + dx, center.Y + dy, center.Z + dz);
                var block = World.BlockAt(pos);
                if (block.Code?.ToString()?.StartsWith(prefix, StringComparison.Ordinal) == true)
                {
                    foundPos = pos;
                    return block;
                }
            }
            return null;
        }

        private BlockEntityGroundStorage? FindGroundStorageHolding(BlockPos center, int radius, string itemCode)
        {
            var accessor = World.Api.World.BlockAccessor;
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -3; dy <= 3; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (accessor.GetBlockEntity(new BlockPos(center.X + dx, center.Y + dy, center.Z + dz))
                    is BlockEntityGroundStorage storage
                    && storage.Inventory.Any(s => s.Itemstack?.Collectible?.Code?.ToString() == itemCode))
                {
                    return storage;
                }
            }
            return null;
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
    }
}
