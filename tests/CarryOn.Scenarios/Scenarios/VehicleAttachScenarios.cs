using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Vehicle attach rows over the real server message handlers: attaching a carried
    /// chest to a vanilla sailed cargo boat (OnAttachMessage), the vanilla entity-detach
    /// anti-yank guard (the Harmony prefix on EntityBehaviorAttachable.TryRemoveAttachment
    /// invoked through the private vanilla method itself), CarryOn's own detach
    /// (OnDetachMessage) with contents intact through the attach round trip, and the
    /// attach failure-code distinction for a carried item-class stack (bag). The last
    /// two rows pin the unresolvable-attached-stack gate: a stack whose block id went
    /// stale (removed/disabled mod, re-resolved to vanilla's missing-block placeholder)
    /// is not detachable in survival (slot-not-carryable) but is recoverable by a
    /// creative player - the creative bypass pinned explicitly (suite README,
    /// Decision 19). The slot-entity-not-attachable half needs a custom entity-pickup
    /// handler, so it lives in the sibling example-mods project's
    /// EntityCarryAttachScenarios (CarryEntityDemo's demo-cube as the in-kind stand-in).
    /// </summary>
    [Trait("area", "Pickup/Place-down")]
    [Trait("summary", "Attach and detach rows over the real server message handlers and the guarded vanilla TryRemoveAttachment on a sailed cargo boat: the marked-stack attach round trip, the anti-yank block, the failure-code distinction, the unresolvable-stack gate with its creative recovery, and the attach reach gate.")]
    public class VehicleAttachScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// A carried chest attaches into the boat's chest-capable cargo slot: the slot then
        /// holds the chest stack marked with the carryonbackup attribute, the hands slot
        /// clears, and the boat stays active.
        /// </summary>
        [Trait("summary", "A carried chest attaches into the boat's chest-capable cargo slot: the slot holds the stack marked carryonbackup, the hands slot clears, and the boat stays active.")]
        [Trait("pins", "The attach path encodes the carried block with its block-entity data into the vehicle slot.")]
        [Trait("role", "smoke")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task AttachChestToCargoBoat_SlotHoldsMarkedStack_HandsClear()
        {
            var (boat, player, slot) = await SpawnCargoBoatWithCarriedChest();

            var attachable = boat.GetBehavior<EntityBehaviorAttachable>()!;

            var targetSlot = attachable.Inventory[slot];
            Assert.Equal("game:chest-east", targetSlot.Itemstack?.Collectible?.Code?.ToString());
            Assert.True(AttachableCarryConversion.IsCarryAttached(targetSlot.Itemstack),
                "The attached chest must carry the carryonbackup marker");
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));
            Assert.Equal(EnumEntityState.Active, boat.State);
        }

        /// <summary>
        /// The vanilla entity-detach path (the exact method the Harmony prefix guards,
        /// invoked server-side through reflection) cannot yank the CarryOn-attached chest
        /// out of the cargo slot: the call is blocked, the marked stack stays in the slot,
        /// and the player's own inventories stay clean.
        /// </summary>
        [Trait("summary", "Invoking the private vanilla TryRemoveAttachment on the marked stack is blocked by the anti-yank prefix: the stack stays in the slot and no player inventory receives the chest.")]
        [Trait("pins", "The anti-yank Harmony prefix guards the real vanilla entity-attach detach method.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task VanillaDetach_YankBlocked_StackStaysInSlot()
        {
            var (boat, player, slot) = await SpawnCargoBoatWithCarriedChest();

            var attachable = boat.GetBehavior<EntityBehaviorAttachable>()!;
            int boxIndex = BoatFixture.FindBoxIndexForSlot(boat, slot);
            Assert.True(boxIndex >= 0, "No selection box maps to the chest cargo slot");

            var method = typeof(EntityBehaviorAttachable).GetMethod(
                "TryRemoveAttachment", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            bool removed = (bool)method!.Invoke(attachable, new object[] { player.Entity, boxIndex })!;

            Assert.False(removed, "Vanilla TryRemoveAttachment must be blocked on a marked stack");
            var targetSlot = attachable.Inventory[slot];
            Assert.Equal("game:chest-east", targetSlot.Itemstack?.Collectible?.Code?.ToString());
            Assert.True(AttachableCarryConversion.IsCarryAttached(targetSlot.Itemstack));

            foreach (var name in new[] { "hotbar", "backpack", "cursor" })
            {
                var inventory = player.Player.InventoryManager.GetOwnInventory(name);
                if (inventory == null) continue;
                Assert.False(inventory.Any(s =>
                        s.Itemstack?.Collectible?.Code?.ToString() == "game:chest-east"),
                    $"The yank must not deposit the chest into the {name} inventory");
            }
        }

        /// <summary>
        /// CarryOn's own detach returns the chest to the player's hands with its contents
        /// intact through the attach round trip: the carried block carries its block-entity
        /// data, the slot empties, and placing it back down materializes a chest whose
        /// inventory still holds the seeded stick.
        /// </summary>
        [Trait("summary", "CarryOn's own detach returns the chest to hands with its contents intact, empties the slot, and a place-down materializes a chest whose inventory still holds the seeded stick.")]
        [Trait("pins", "Block-entity data survives the attach-detach round trip.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task CarryOnDetach_ChestReturnsToHandsWithContents()
        {
            var (boat, player, slot) = await SpawnCargoBoatWithCarriedChest();

            HandlerInvoke.Detach(Mod, player.Player, new DetachMessage(boat.EntityId, slot));

            var carried = Carry.GetCarried(player.Entity, CarrySlot.Hands);
            Assert.NotNull(carried);
            Assert.Equal("game:chest-east", carried?.ItemStack?.Collectible?.Code?.ToString());
            Assert.NotNull(carried?.BlockEntityData);

            var attachable = boat.GetBehavior<EntityBehaviorAttachable>()!;
            Assert.Null(attachable.Inventory[slot].Itemstack);
            Assert.Equal(EnumEntityState.Active, boat.State);

            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));
            BlockPos placedPos = World.Spawn.Offset(4, 1, 0);
            BlockPos? placedAt = Actions.PlaceDown(player.Player, CarrySlot.Hands,
                CarryActions.AimDownAt(placedPos), out var failureCode);
            Assert.True(placedAt != null, $"Chest place-down failed: {failureCode}");

            var chestEntity = World.BlockEntityAt<BlockEntityGenericTypedContainer>(placedPos);
            Assert.NotNull(chestEntity);
            Assert.True(chestEntity!.Inventory.Any(s =>
                    s.Itemstack?.Collectible?.Code?.ToString() == "game:stick"),
                "The seeded stick must survive the attach/detach round trip");
        }

        /// <summary>
        /// Attaching a carried item-class stack (a quiver picked up from ground storage -
        /// no block-entity data to encode) rejects with the slot-incompatible-block
        /// failure code, surfaces the IngameError toast on the vanilla wire, and keeps the
        /// bag carried and the slot empty.
        /// </summary>
        [Trait("summary", "Attaching a carried item-class stack (a quiver) rejects with slot-incompatible-block, surfaces the toast on the vanilla wire, and keeps the bag carried with the slot empty.")]
        [Trait("pins", "The attach failure-code distinction for item-class carries that carry no block-entity data.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task AttachCarriedBag_RejectsWithSlotIncompatibleBlock()
        {
            BlockPos bagPos = World.Spawn.Offset(2, 1, 0);
            BlockSelection bagSelection = GroundStorageFixture.PlaceBag(World.Api.World, bagPos, "quiver");

            Entity boat = World.SpawnEntity(
                BoatFixture.ResolveCargoBoatCode(World.Api.World), World.Spawn.Offset(6, 1, 4));

            ITestPlayer player = await JoinPlayer("Tester");
            await player.TeleportTo(World.Spawn.Offset(5, 0, 4));

            var carried = Actions.PickUpBagFromGroundStorage(player.Entity, bagSelection, out var pickupFailure);
            Assert.True(carried != null, $"Quiver pickup failed: {pickupFailure}");

            var chestStack = new ItemStack(World.Api.World.GetBlock(new AssetLocation("chest-east")));
            int slot = BoatFixture.FindChestCargoSlot(boat, chestStack);
            Assert.True(slot >= 0, "The cargo boat has no chest-capable cargo slot");

            player.Client.Clear();
            HandlerInvoke.Attach(Mod, player.Player, new AttachMessage(boat.EntityId, slot));

            var wire = VanillaClientWire.Tap(player);
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.SlotIncompatibleBlock));

            var attachable = boat.GetBehavior<EntityBehaviorAttachable>()!;
            Assert.Null(attachable.Inventory[slot].Itemstack);
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
            Assert.Equal("game:quiver",
                Carry.GetCarried(player.Entity, CarrySlot.Hands)?.ItemStack?.Collectible?.Code?.ToString());
        }

        /// <summary>
        /// An attached cargo stack whose block id went stale (the mod that provided the
        /// block was removed or disabled after a world reload) re-resolves to vanilla's
        /// missing-block placeholder, not to the original block. In survival such a slot
        /// is not detachable at all: CanDetach answers slot-not-carryable (the placeholder
        /// has no carryable behavior), the toast surfaces on the vanilla wire, and the
        /// corrupt stack stays in the slot.
        /// </summary>
        [Trait("summary", "An attached stack re-resolved to the missing-block placeholder is not detachable in survival: the slot-not-carryable toast fires on the vanilla wire, the corrupt stack stays in the slot, and no carry is granted.")]
        [Trait("pins", "The survival half of the unresolvable-attached-stack gate.")]
        [Trait("role", "regression")]
        [Trait("decision", "20")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task UnresolvableCargoStack_DetachInSurvival_Denied_KeepsSlot()
        {
            var (boat, player, slot) = await SpawnCargoBoatWithCarriedChest();
            CorruptAttachedStackToPlaceholder(boat, slot, StaleBlockId);

            var attachable = boat.GetBehavior<EntityBehaviorAttachable>()!;
            var corrupt = attachable.Inventory[slot].Itemstack;
            Assert.True(corrupt?.Block != null && corrupt.Block.IsMissing,
                "Premise: the stale id must re-resolve to the missing-block placeholder");

            player.Client.Clear();
            HandlerInvoke.Detach(Mod, player.Player, new DetachMessage(boat.EntityId, slot));

            var wire = VanillaClientWire.Tap(player);
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.SlotNotCarryable));

            Assert.Equal(StaleBlockId, attachable.Inventory[slot].Itemstack?.Id);
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// The creative twin: a creative player detaches the same stale stack - the
        /// creative recovery the survival row pins against (Decision 19). The stack is
        /// recovered into hands as a raw unreadable carry (missing-block), so gameplay
        /// reads return null while the protected read reports Failed with the tree
        /// preserved verbatim (stale id and block-entity data) for recovery once the
        /// source mod returns; the slot empties and the entity stays active.
        /// The recovery used to crash - ItemStackToCarriedBlock NREd on the placeholder's
        /// null Code, and the SetCarriedRaw branch required a null Block which vanilla's
        /// placeholder substitution never produces.
        /// </summary>
        [Trait("summary", "A creative player detaches the same stale stack: it recovers into hands as a raw unreadable carry whose protected read reports Failed with the tree preserved verbatim, and the slot empties.")]
        [Trait("pins", "The known-red creative recovery of a placeholder stack must preserve the stale stack for later recovery instead of crashing.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the current head with an NRE while dissolving the stale stack (null Code on the missing-block placeholder).")]
        [Trait("decision", "20")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task UnresolvableCargoStack_DetachInCreative_RecoversStackToHands()
        {
            var (boat, player, slot) = await SpawnCargoBoatWithCarriedChest(EnumGameMode.Creative);
            CorruptAttachedStackToPlaceholder(boat, slot, StaleBlockId);

            var attachable = boat.GetBehavior<EntityBehaviorAttachable>()!;
            var corrupt = attachable.Inventory[slot].Itemstack;
            Assert.True(corrupt?.Block != null && corrupt.Block.IsMissing,
                "Premise: the stale id must re-resolve to the missing-block placeholder");

            HandlerInvoke.Detach(Mod, player.Player, new DetachMessage(boat.EntityId, slot));

            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "Creative detach must recover the stale stack into hands as a raw carry");
            Assert.Null(Carry.GetCarried(player.Entity, CarrySlot.Hands));

            var read = Carry.GetCarriedReadResult(player.Entity, CarrySlot.Hands, out var storedTree);
            Assert.True(read.IsFailed,
                $"The recovered raw carry must report Failed, got {read.Status}/{read.Reason}");
            Assert.Equal("missing-block", read.Reason);
            Assert.NotNull(storedTree);
            var storedStack = storedTree!.GetItemstack(AttributeKeys.CarriedBlockData.Stack);
            Assert.True(storedStack?.Id == StaleBlockId && storedStack.Class == EnumItemClass.Block,
                "The recovered raw carry must preserve the stale stack verbatim for later recovery");
            Assert.True(storedTree?.GetTreeAttribute(AttributeKeys.CarriedBlockData.Data) != null,
                "The recovered raw carry must keep its block-entity data so contents survive");

            Assert.Null(attachable.Inventory[slot].Itemstack);
            Assert.Equal(EnumEntityState.Active, boat.State);
        }

        /// <summary>
        /// Phase 3 - the attach reach gate, survival half. The boat sits well beyond the
        /// configured reach while the player stands at the chest holding the carried stack, so
        /// the only reason to refuse is reach: no claim, no slot-type problem and an
        /// in-reach source block. The toast is entity-out-of-reach (the target is an entity),
        /// the cargo slot stays empty, and the chest stays carried so the player is not
        /// punished for a refused attach with the stack nowhere to go.
        /// </summary>
        [Trait("summary", "With the boat far beyond reach, a survival attach is refused with entity-out-of-reach, the cargo slot stays empty, and the chest stays carried in hands.")]
        [Trait("pins", "The attach reach gate still answers entity-out-of-reach after being retargeted onto the shared reach predicate.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task AttachBeyondReach_Survival_RejectsWithEntityOutOfReach()
        {
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;
            var (boat, player, slot) = await SpawnFarCargoBoatWithCarriedChest(
                World.Spawn.Offset(reach + 8, 1, 4), EnumGameMode.Survival);

            player.Client.Clear();
            HandlerInvoke.Attach(Mod, player.Player, new AttachMessage(boat.EntityId, slot));

            var wire = VanillaClientWire.Tap(player);
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.EntityOutOfReach));

            Assert.Null(boat.GetBehavior<EntityBehaviorAttachable>()!.Inventory[slot].Itemstack);
            Assert.True(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands),
                "A refused attach must leave the stack in hands");
            Assert.Equal("game:chest-east",
                Carry.GetCarried(player.Entity, CarrySlot.Hands)?.ItemStack?.Collectible?.Code?.ToString());
        }

        /// <summary>
        /// The creative twin of the attach reach row: a creative player attaches the same
        /// carried chest into the same distant boat's cargo slot, pinning the creative
        /// exemption inside the shared reach predicate on the attach path (Decision 19).
        /// </summary>
        [Trait("summary", "A creative player attaches the same carried chest into the distant boat's cargo slot, bypassing the attach reach gate.")]
        [Trait("pins", "The creative exemption inside the shared reach predicate on the attach path.")]
        [Trait("role", "coverage")]
        [Trait("gamemode", "creative")]
        [Trait("decision", "19")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task AttachBeyondReach_Creative_Allowed()
        {
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;
            var (boat, player, slot) = await SpawnFarCargoBoatWithCarriedChest(
                World.Spawn.Offset(reach + 8, 1, 4), EnumGameMode.Creative);

            HandlerInvoke.Attach(Mod, player.Player, new AttachMessage(boat.EntityId, slot));

            var targetSlot = boat.GetBehavior<EntityBehaviorAttachable>()!.Inventory[slot];
            Assert.True(AttachableCarryConversion.IsCarryAttached(targetSlot.Itemstack),
                $"Creative must attach beyond reach - boat slot holds {targetSlot.Itemstack}");
            Assert.Equal("game:chest-east", targetSlot.Itemstack?.Collectible?.Code?.ToString());
            Assert.False(Carry.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// Spawns the cargo boat at a caller-chosen position (so a row can put it out of
        /// reach), places and picks up the chest with the player, resolves the chest-capable
        /// cargo slot, and hands back <b>without attaching</b> - the reach rows need the attach
        /// itself to be the thing under test.
        /// </summary>
        private async Task<(Entity Boat, ITestPlayer Player, int Slot)> SpawnFarCargoBoatWithCarriedChest(
            BlockPos boatPos, EnumGameMode gameMode)
        {
            Entity boat = World.SpawnEntity(
                BoatFixture.ResolveCargoBoatCode(World.Api.World), boatPos);

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            ITestPlayer player = await JoinPlayer("Tester", gameMode);
            await player.TeleportTo(World.Spawn.Offset(3, 0, 0));

            var chestStack = new ItemStack(World.Api.World.GetBlock(new AssetLocation("chest-east")));
            int slot = BoatFixture.FindChestCargoSlot(boat, chestStack);
            Assert.True(slot >= 0, "The cargo boat has no chest-capable cargo slot");

            Assert.True(boatPos.ToVec3d().DistanceTo(player.Entity.Pos.XYZ)
                    > Mod.Config.CarryOptions.MaxInteractionDistance + 2,
                "Row premise: the boat must sit far enough out that either anchor rejects");

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var pickupFailure);
            Assert.True(carried != null, $"Chest pickup failed: {pickupFailure}");

            return (boat, player, slot);
        }

        /// <summary>
        /// A block id no registered block answers to - the state a slot stack is left in
        /// when the mod that provided its block is removed or disabled before a reload.
        /// </summary>
        private const int StaleBlockId = 9_999_999;

        /// <summary>
        /// Corrupts an attached stack the way a removed/disabled mod does after a world
        /// reload: the stack keeps a stale block id and is re-resolved through the same
        /// ItemStack.ResolveBlockOrItem call deserialization uses, which caches vanilla's
        /// missing-block placeholder (IsMissing) in place of the original block.
        /// </summary>
        private static void CorruptAttachedStackToPlaceholder(Entity boat, int slot, int staleId)
        {
            var attachable = boat.GetBehavior<EntityBehaviorAttachable>()!;
            var stack = attachable.Inventory[slot].Itemstack;
            Assert.NotNull(stack);
            stack!.Id = staleId;
            Assert.True(stack.ResolveBlockOrItem(boat.World),
                "The stale id must resolve (to the placeholder) rather than fail outright");
            attachable.Inventory[slot].MarkDirty();
        }

        /// <summary>
        /// Spawns the cargo boat, places a chest seeded with a stick, joins the player
        /// nearby, picks the chest up into hands and attaches it into the boat's first
        /// chest-capable cargo slot through the real attach handler.
        /// </summary>
        private async Task<(Entity Boat, ITestPlayer Player, int Slot)> SpawnCargoBoatWithCarriedChest(
            EnumGameMode gameMode = EnumGameMode.Survival)
        {
            Entity boat = World.SpawnEntity(
                BoatFixture.ResolveCargoBoatCode(World.Api.World), World.Spawn.Offset(6, 1, 4));

            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);

            var chestEntity = World.BlockEntityAt<BlockEntityGenericTypedContainer>(chestPos);
            Assert.NotNull(chestEntity);
            chestEntity!.Inventory[0].Itemstack =
                new ItemStack(World.Api.World.GetItem(new AssetLocation("stick")));
            chestEntity.Inventory[0].MarkDirty();
            chestEntity.MarkDirty(true);

            ITestPlayer player = await JoinPlayer("Tester", gameMode);
            await player.TeleportTo(World.Spawn.Offset(5, 0, 4));

            var chestStack = new ItemStack(World.Api.World.GetBlock(new AssetLocation("chest-east")));
            int slot = BoatFixture.FindChestCargoSlot(boat, chestStack);
            Assert.True(slot >= 0, "The cargo boat has no chest-capable cargo slot");

            var carried = Actions.PickUpIntoHands(player.Entity, chestPos, out var pickupFailure);
            Assert.True(carried != null, $"Chest pickup failed: {pickupFailure}");

            HandlerInvoke.Attach(Mod, player.Player, new AttachMessage(boat.EntityId, slot));

            return (boat, player, slot);
        }
    }
}
