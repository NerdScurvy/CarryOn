using System;
using System.Reflection;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Pickup-open guard rows: a container must
    /// reject being picked while another player has it open (`SlotInventoryOpen`),
    /// and the acting player's own open GUI must never block the action. Three
    /// pickup surfaces each get a reject/allow pair - ground-storage bags
    /// (bag-content and container GUIs), a world-block chest, and a mounted boat
    /// cargo slot. Written red-first in Phase 1; the Phase-2 (ground-storage bag)
    /// and Phase-3 (world-block parity) server guards turned rows 1/1b/3 green.
    /// M4 added row 8 (the decompile pin) and M3 added rows 9 and 10 (the dummy
    /// cell the guard resolves), so all ten rows are green today.
    ///
    /// "Another player has it open" is driven headless by seeding the server-side
    /// `OpenedInventories` seam - `IPlayerInventoryManager.OpenInventory`, the same
    /// call vanilla makes when a GUI opens on the server - with an inventory whose
    /// `InventoryID` is exactly the ID the guard matches:
    ///
    /// - bag-content GUI: the real `BlockEntityContainedBagWorkspace` wrapper, driven
    ///   through vanilla's own `TryLoadBagInv` so its `className`/`instanceID` come
    ///   from VSSurvivalMod rather than from a literal copied here. Row 8 compares
    ///   that real ID against the guard's helper and is what keeps the two honest.
    /// - ground-storage container GUI: the storage's own `beg.Inventory`.
    /// - world-block chest GUI: the chest block entity's own inventory, and for a
    ///   multiblock the **controller's** - rows 3 and 10 pin that the guard asks the
    ///   cell whose GUI a player could actually have open, not the clicked dummy.
    /// - mounted cargo slot: `MountedBagInventoryPrefix-{slotIndex}-{entityId}`.
    ///
    /// The mounted rows drive the guard with a **chest** cargo slot, not a bag: the
    /// guard runs in `TrySlotAction` before any stack-class logic, and a bag
    /// (item-class stack) cannot be attached to the boat in the first place
    /// (`slot-incompatible-block`, pinned by VehicleAttachScenarios) - so the
    /// chest-attach round trip is the honest way to reach the identical wire.
    /// </summary>
    [Trait("area", "Pickup/Place-down")]
    [Trait("summary", "Pickup-open guard rows: a container rejects being picked while another player has it open with slot-inventory-open, while the acting player's own open GUI never blocks the action, across ground-storage bags, a world-block chest and a mounted boat cargo slot.")]
    public class PickupOpenGuardScenarios : CarryOnScenarioBase
    {
        /// <summary>
        /// Row 1 - the targeted bag's own content GUI is open on another player:
        /// the bag pickup must be rejected with `slot-inventory-open`, the bag must
        /// stay in the storage, and the actor's hands must stay free. Green - the
        /// Phase-2 ground-storage guard blocks it.
        /// </summary>
        [Trait("summary", "With the bag's real content GUI open on another player, the bag pickup rejects slot-inventory-open, the bag stays in the storage and the actor's hands stay free.")]
        [Trait("pins", "The ground-storage guard blocks a bag-content GUI open on another player.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task GroundStorageBag_OtherPlayerHasBagOpen_PickupRejected()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            BlockSelection selection = GroundStorageFixture.PlaceBag(World.Api.World, pos, GroundStorageFixture.BagCode);

            var beg = World.BlockEntityAt<BlockEntityGroundStorage>(pos);
            Assert.NotNull(beg);
            var bagSlot = beg!.GetSlotAt(selection);
            int slotId = beg.Inventory.GetSlotId(bagSlot);

            ITestPlayer actor = await JoinPlayer("Tester");
            ITestPlayer opener = await JoinPlayer("Opener");
            await actor.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await opener.TeleportTo(World.Spawn.Offset(4, 0, 2));

            OpenFor(opener, OpenRealContainedBagWrapper(World.Api, beg, slotId, bagSlot!));

            var carried = Actions.PickUpBagFromGroundStorage(actor.Entity, selection, out var failureCode);

            Assert.Null(carried);
            Assert.Equal(FailureCodes.SlotInventoryOpen, failureCode);
            Assert.False(Carry.IsCarrySlotOccupied(actor.Entity, CarrySlot.Hands));

            var storage = World.BlockEntityAt<BlockEntityGroundStorage>(pos);
            Assert.NotNull(storage);
            Assert.Equal("game:" + GroundStorageFixture.BagCode,
                storage!.Inventory[GetSlotId(storage, selection)].Itemstack?.Collectible?.Code?.ToString());
        }

        /// <summary>
        /// Row 1b - the ground-storage **container** GUI is open on another player
        /// (seeded with the storage's own inventory object, the exact ID the
        /// container GUI binds to): the bag yank must be rejected the same way.
        /// Green - the Phase-2 guard matches the `beg.Inventory` ID too.
        /// </summary>
        [Trait("summary", "With the ground-storage container GUI open on another player, the bag pickup rejects slot-inventory-open and the storage keeps its bag.")]
        [Trait("pins", "The ground-storage guard also matches the storage container's own inventory id.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task GroundStorageBag_OtherPlayerHasContainerOpen_PickupRejected()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            BlockSelection selection = GroundStorageFixture.PlaceBag(World.Api.World, pos, GroundStorageFixture.BagCode);

            var beg = World.BlockEntityAt<BlockEntityGroundStorage>(pos);
            Assert.NotNull(beg);

            ITestPlayer actor = await JoinPlayer("Tester");
            ITestPlayer opener = await JoinPlayer("Opener");
            await actor.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await opener.TeleportTo(World.Spawn.Offset(4, 0, 2));

            OpenFor(opener, beg!.Inventory);

            var carried = Actions.PickUpBagFromGroundStorage(actor.Entity, selection, out var failureCode);

            Assert.Null(carried);
            Assert.Equal(FailureCodes.SlotInventoryOpen, failureCode);
            Assert.False(Carry.IsCarrySlotOccupied(actor.Entity, CarrySlot.Hands));

            var storage = World.BlockEntityAt<BlockEntityGroundStorage>(pos);
            Assert.NotNull(storage);
            Assert.Equal("game:" + GroundStorageFixture.BagCode,
                storage!.Inventory[GetSlotId(storage, selection)].Itemstack?.Collectible?.Code?.ToString());
        }

        /// <summary>
        /// Row 2 - the acting player has the bag-content GUI open on their own
        /// client: by design the guard skips the actor's UID, so the pickup must
        /// still succeed and carry the bag. Green - pins the self-skip rule.
        /// </summary>
        [Trait("summary", "With the acting player's own bag-content GUI open, the pickup still succeeds and carries the bag.")]
        [Trait("pins", "The guard skips the acting player's own UID.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task GroundStorageBag_ActorOwnOpen_PickupAllowed()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            BlockSelection selection = GroundStorageFixture.PlaceBag(World.Api.World, pos, GroundStorageFixture.BagCode);

            var beg = World.BlockEntityAt<BlockEntityGroundStorage>(pos);
            Assert.NotNull(beg);
            var bagSlot = beg!.GetSlotAt(selection);
            int slotId = beg.Inventory.GetSlotId(bagSlot);

            ITestPlayer actor = await JoinPlayer("Tester");
            await actor.TeleportTo(World.Spawn.Offset(2, 0, 2));

            OpenFor(actor, OpenRealContainedBagWrapper(World.Api, beg, slotId, bagSlot!));

            var carried = Actions.PickUpBagFromGroundStorage(actor.Entity, selection, out var failureCode);

            Assert.True(carried != null, $"Bag pickup failed: {failureCode}");
            Assert.Equal(CarrySlot.Hands, carried?.Slot);
            Assert.True(Carry.IsCarrySlotOccupied(actor.Entity, CarrySlot.Hands));
            Assert.Equal(0, World.BlockAt(pos).Id);
        }

        /// <summary>
        /// Row 3 - another player has the world-block chest's inventory open: the
        /// chest pickup must be rejected with `slot-inventory-open`, the block must
        /// stay in the world, and the actor's hands must stay free. Green - the
        /// Phase-3 world-block parity guard blocks it.
        /// </summary>
        [Trait("summary", "With the world-block chest's inventory open on another player, the chest pickup rejects slot-inventory-open, the block stays and the hands stay free.")]
        [Trait("pins", "The world-block parity guard blocks a chest inventory open on another player.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WorldBlock_OtherPlayerHasInventoryOpen_PickupRejected()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            var chest = World.BlockEntityAt<BlockEntityGenericTypedContainer>(pos);
            Assert.NotNull(chest);

            ITestPlayer actor = await JoinPlayer("Tester");
            ITestPlayer opener = await JoinPlayer("Opener");
            await actor.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await opener.TeleportTo(World.Spawn.Offset(4, 0, 2));

            OpenFor(opener, chest!.Inventory);

            var carried = Actions.PickUpIntoHands(actor.Entity, pos, out var failureCode);

            Assert.Null(carried);
            Assert.Equal(FailureCodes.SlotInventoryOpen, failureCode);
            Assert.False(Carry.IsCarrySlotOccupied(actor.Entity, CarrySlot.Hands));
            Assert.Equal("game:chest-east", World.BlockAt(pos).Code?.ToString());
        }

        /// <summary>
        /// Row 4 - the acting player has the chest inventory open on their own
        /// client: the pickup must succeed (the guard skips the actor's UID).
        /// Green - pins the self-skip rule.
        /// </summary>
        [Trait("summary", "With the acting player's own chest inventory open, the pickup succeeds and carries the chest.")]
        [Trait("pins", "The world-block guard skips the acting player's own UID.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WorldBlock_ActorOwnOpen_PickupAllowed()
        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", pos);

            var chest = World.BlockEntityAt<BlockEntityGenericTypedContainer>(pos);
            Assert.NotNull(chest);

            ITestPlayer actor = await JoinPlayer("Tester");
            await actor.TeleportTo(World.Spawn.Offset(2, 0, 2));

            OpenFor(actor, chest!.Inventory);

            var carried = Actions.PickUpIntoHands(actor.Entity, pos, out var failureCode);

            Assert.True(carried != null, $"Chest pickup failed: {failureCode}");
            Assert.Equal(CarrySlot.Hands, carried?.Slot);
            Assert.True(Carry.IsCarrySlotOccupied(actor.Entity, CarrySlot.Hands));
            Assert.Equal(0, World.BlockAt(pos).Id);
        }

        /// <summary>
        /// Row 5 - another player has the mounted-slot bag inventory open: the
        /// existing server guard must keep rejecting the detach (`slot-inventory-open`
        /// toast on the vanilla wire), the attached stack must stay in the boat
        /// slot, and the actor's hands must stay free. Green - the anchor row
        /// proving the `OpenedInventories` seeding trips the real, already-shipped
        /// mounted guard.
        /// </summary>
        [Trait("summary", "With the mounted cargo slot's inventory open on another player, the detach is rejected with a slot-inventory-open wire error and the attached stack stays in the boat slot.")]
        [Trait("pins", "The shipped mounted-slot guard rejects a detach while another player has that inventory open.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MountedBag_OtherPlayerHasBagOpen_DetachRejected()
        {
            var (boat, actor, slot) = await SpawnBoatWithAttachedChest();

            ITestPlayer opener = await JoinPlayer("Opener");
            await opener.TeleportTo(World.Spawn.Offset(3, 0, 4));

            OpenFor(opener, NewMountedBagWrapper(World.Api, boat.EntityId, slot));

            actor.Client.Clear();
            HandlerInvoke.Detach(Mod, actor.Player, new DetachMessage(boat.EntityId, slot));

            var wire = VanillaClientWire.Tap(actor);
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.SlotInventoryOpen));

            var attachable = boat.GetBehavior<EntityBehaviorAttachable>()!;
            Assert.Equal("game:chest-east", attachable.Inventory[slot].Itemstack?.Collectible?.Code?.ToString());
            Assert.False(Carry.IsCarrySlotOccupied(actor.Entity, CarrySlot.Hands));
        }

        /// <summary>
        /// Row 6 - the acting player has the mounted-slot bag inventory open on
        /// their own client: the existing guard skips the actor's UID, so the detach
        /// must still succeed and return the stack to hands. Green - pins the
        /// self-skip half of the mounted behavior being generalized.
        /// </summary>
        [Trait("summary", "With the acting player's own mounted-slot inventory open, the detach succeeds and returns the stack to Hands.")]
        [Trait("pins", "The mounted guard skips the acting player's own UID.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task MountedBag_ActorOwnOpen_DetachAllowed()
        {
            var (boat, actor, slot) = await SpawnBoatWithAttachedChest();

            OpenFor(actor, NewMountedBagWrapper(World.Api, boat.EntityId, slot));

            HandlerInvoke.Detach(Mod, actor.Player, new DetachMessage(boat.EntityId, slot));

            var attachable = boat.GetBehavior<EntityBehaviorAttachable>()!;
            Assert.Null(attachable.Inventory[slot].Itemstack);
            Assert.True(Carry.IsCarrySlotOccupied(actor.Entity, CarrySlot.Hands));
            Assert.Equal("game:chest-east",
                Carry.GetCarried(actor.Entity, CarrySlot.Hands)?.ItemStack?.Collectible?.Code?.ToString());
        }

        /// <summary>
        /// The **real** vanilla bag-content wrapper inventory for the bag in
        /// <paramref name="slotId"/> of the storage at <paramref name="pos"/>, built by
        /// vanilla's own code path rather than by re-stating its naming.
        /// <para>
        /// <c>BEBehaviorContainedBagInventory.Initialize</c> gives the container one
        /// <c>BlockEntityContainedBagWorkspace</c> per inventory index; calling the
        /// workspace's public <c>TryLoadBagInv</c> is what makes it
        /// <c>wrapperInv.Init</c> with <c>"blockcontainedbaginv"</c> /
        /// <c>"blockcontainedbag-slot{slotId}-{pos}"</c>. Reading that private
        /// <c>wrapperInv</c> field is the only way to get the instance the game itself
        /// would open - there is no public accessor for it, and re-typing the two
        /// literals here is exactly the duplication row 8 exists to delete.
        /// </para>
        /// </summary>
        private static InventoryGeneric OpenRealContainedBagWrapper(
            ICoreAPI api, BlockEntityGroundStorage beg, int slotId, ItemSlot bagSlot)
        {
            var behavior = beg.GetBehavior<BEBehaviorContainedBagInventory>()
                ?? throw new InvalidOperationException(
                    "BEBehaviorContainedBagInventory is not on the ground-storage block entity - "
                    + "vanilla's contained-bag GUI cannot be reproduced, so the guard's bag-content "
                    + "arm cannot be exercised honestly.");

            var workspace = behavior.BagInventories[slotId]
                ?? throw new InvalidOperationException(
                    $"No BlockEntityContainedBagWorkspace for slot {slotId}");

            var bagStack = bagSlot.Itemstack!;
            var bagBehavior = bagStack.Collectible?.GetBehavior<CollectibleBehaviorGroundStoredHeldBag>()
                ?? throw new InvalidOperationException(
                    $"Bag stack {bagStack.Collectible?.Code} is not a CollectibleBehaviorGroundStoredHeldBag");

            Assert.True(
                workspace.TryLoadBagInv(bagSlot, bagBehavior),
                "vanilla refused to load the bag-content wrapper for this slot");

            var field = typeof(BlockEntityContainedBagWorkspace).GetField(
                "wrapperInv", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "BlockEntityContainedBagWorkspace.wrapperInv is gone - vanilla renamed or "
                    + "reshaped the field; update the PickupOpenGuardScenarios seam.");

            return (InventoryGeneric)field.GetValue(workspace)!;
        }

        /// <summary>
        /// Row 8 - <b>premise, green before and after.</b> The single anti-vacuity check
        /// in the finding: the <see cref="IInventory.InventoryID"/> vanilla actually puts
        /// in a server player's <c>OpenedInventories</c> when a bag-content GUI opens
        /// <b>equals</b> what <c>CarryInventoryGuard.BagContentInventoryId</c> computes.
        /// <para>
        /// Every other row here seeds the premise by *stating* the expected ID, so they
        /// would all stay green if VSSurvivalMod renamed
        /// <c>blockcontainedbaginv</c> / <c>blockcontainedbag-slot</c> - the guard's
        /// prefix arm would then match nothing and the bag-content reject would
        /// silently fail open, with every green row still green. This row builds the
        /// ID through vanilla's own <c>TryLoadBagInv</c>, opens it on a real player, and
        /// compares, so a rename turns this row red instead. It is the automated
        /// version of the review's "grep the current VS install for the contained-bag
        /// inventory name".
        /// </para>
        /// <para>
        /// Deliberately asserts nothing about the pickup outcome: the guard's behaviour
        /// is rows 1 and 2. This row only pins that the guard and vanilla agree on the
        /// name.
        /// </para>
        /// </summary>
        [Trait("summary", "Building the bag-content wrapper through vanilla's own loader and opening it on a real player, the resulting inventory id equals the guard's computed bag-content id.")]
        [Trait("pins", "The guard and vanilla agree on the bag-content inventory name, so the guard's bag-content arm cannot silently match nothing.")]
        [Trait("role", "premise")]
        [Trait("caveat", "Decompile-pinned to vanilla contained-bag workspace naming; a vanilla rename makes this row a real finding rather than a flake.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task GroundStorageBag_RealVanillaWrapperId_MatchesGuardHelper()        {
            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            BlockSelection selection = GroundStorageFixture.PlaceBag(World.Api.World, pos, GroundStorageFixture.BagCode);

            var beg = World.BlockEntityAt<BlockEntityGroundStorage>(pos);
            Assert.NotNull(beg);
            var bagSlot = beg!.GetSlotAt(selection);
            Assert.NotNull(bagSlot);
            int slotId = beg.Inventory.GetSlotId(bagSlot!);

            InventoryGeneric realWrapper =
                OpenRealContainedBagWrapper(World.Api, beg, slotId, bagSlot!);

            ITestPlayer opener = await JoinPlayer("Opener");
            await opener.TeleportTo(World.Spawn.Offset(4, 0, 2));

            OpenFor(opener, realWrapper);

            var opened = opener.Player.InventoryManager.OpenedInventories;
            Assert.NotEmpty(opened);

            string actual = opened[opened.Count - 1].InventoryID ?? string.Empty;
            string expected = BagContentInventoryIdViaSeam(beg, bagSlot!);

            Assert.True(
                string.Equals(expected, actual, StringComparison.Ordinal),
                "The guard's bag-content id no longer matches the one vanilla opens. Either "
                + "VSSurvivalMod renamed its contained-bag workspace naming, or "
                + "CarryInventoryGuard.BagContentInventoryId drifted. Until this is "
                + "reconciled the guard's bag-content arm matches nothing and a bag-content "
                + $"GUI being open no longer blocks a pickup (silent fail-open). Expected "
                + $"'{expected}', vanilla opened '{actual}'.");
        }

        private const string TrunkSouth = "game:trunk-south";
        private const string TrunkDummySouthWest = "game:multiblock-monolithic-n1-0-0";

        /// <summary>
        /// Row 9 (M3) - <b>premise, green before and after.</b> Why the
        /// dummy-target window is closed on shipped content, so the M3 hardening is
        /// not mistaken for the fix of a live bug.
        /// <para>
        /// <c>TryPickUp</c> ordered the two gates carryable-first, so a pickup aimed at
        /// a multiblock's <b>dummy</b> cell died at <c>NotCarryable</c> and the
        /// inventory-open guard was never reached. That is the only reason the guard's
        /// use of the raw clicked <c>pos</c> has never been observed to matter: no
        /// vanilla dummy is carryable.
        /// </para>
        /// <para>
        /// The row pins both halves of that premise rather than just the failure code.
        /// It asserts the targeted cell really is a dummy (<c>BlockMultiblock</c> whose
        /// <c>OffsetInv</c> resolves to the controller), that the dummy cell holds
        /// <b>no</b> <see cref="BlockEntityContainer"/> - so a guard reading
        /// <c>GetBlockEntity(pos)</c> there finds nothing to match and could only
        /// fail open - and that the pickup is in fact rejected <c>NotCarryable</c>.
        /// </para>
        /// <para>
        /// Reachability is unchanged by the fix on <b>shipped</b> content: a mod that
        /// makes a dummy carryable still needs a mod-owned multiblock blocktype to do
        /// it (the Q3 finding), so there is deliberately <b>no</b> red row here. What M3
        /// changes is that if such a mod appears, the guard checks the controller's
        /// container instead of nothing.
        /// </para>
        /// <para>
        /// That mod is now hypothetical rather than hypothetical: see
        /// <c>MultiblockDummyCarryableGuardScenarios</c> in the ExampleMods suite,
        /// which patches the vanilla dummy family carryable and reaches the same
        /// window through the production <c>Actions.PickUpIntoHands</c> path with no
        /// <c>checkIsCarryable: false</c> seam anywhere. This row stays the
        /// shipped-content statement and stays green, because the patch is
        /// ExampleMods-only and never reaches this suite's class-world.
        /// </para>
        /// </summary>
        [Trait("summary", "A pickup aimed at a multiblock dummy cell is rejected not-carryable before the inventory-open guard is reached, and the dummy cell exposes no container.")]
        [Trait("pins", "On shipped content a dummy-targeted pickup dies at the carryable gate, which is why the guard's container resolution was never observable there.")]
        [Trait("role", "premise")]
        [Trait("caveat", "Reachable on untouched vanilla content only because no vanilla dummy is carryable; the guard path itself needs a mod-owned multiblock with carryable dummies.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WorldBlock_DummyTargeted_NotCarryableBeforeTheGuard_R9()
        {
            var controller = World.Spawn.Offset(6, 1, 0);
            World.SetBlock(TrunkSouth, controller);
            Assert.Equal(TrunkSouth, World.BlockAt(controller).Code?.ToString());

            // trunk-south writes exactly one dummy, one cell west of the controller.
            var dummy = controller.Copy().Add(-1, 0, 0);
            Assert.Equal(TrunkDummySouthWest, World.BlockAt(dummy).Code?.ToString());

            // The targeted cell is the dummy, not the controller, and it points back.
            var dummyBlock = World.BlockAt(dummy);
            var dummyMulti = dummyBlock as BlockMultiblock;
            Assert.True(dummyMulti != null,
                $"The trunk's west cell is a {dummyBlock?.Code}, not a BlockMultiblock dummy; "
                + "this row's premise (a dummy-targeted pickup) no longer holds.");
            Assert.Equal(controller, dummy.Copy().Add(dummyMulti!.OffsetInv));

            // The guard resolves `GetBlockEntity(pos) as BlockEntityContainer`. At a dummy
            // cell that is null, so the guard has nothing to match even if it ran.
            Assert.True(
                World.BlockEntityAt<BlockEntityContainer>(dummy) == null,
                $"The trunk's dummy cell now exposes a BlockEntityContainer "
                + $"({World.BlockAt(dummy).Code}); the guard at the clicked cell would no "
                + "longer be a guaranteed no-op, and this premise row's claim needs review.");

            ITestPlayer actor = await JoinPlayer("Tester");
            await actor.TeleportTo(World.Spawn.Offset(8, 0, 2));

            var carried = Actions.PickUpIntoHands(actor.Entity, dummy, out var failureCode);

            Assert.Null(carried);
            Assert.Equal(FailureCodes.NotCarryable, failureCode);
            Assert.False(Carry.IsCarrySlotOccupied(actor.Entity, CarrySlot.Hands));
            Assert.Equal(TrunkSouth, World.BlockAt(controller).Code?.ToString());
            Assert.Equal(TrunkDummySouthWest, World.BlockAt(dummy).Code?.ToString());
        }

        /// <summary>
        /// Row 10 (M3) - <b>RED before the fix, green after.</b> Which container the
        /// guard inspects when the pickup targets a multiblock <b>dummy</b> cell.
        /// <para>
        /// The fix's whole content is the resolution: pre-fix the guard read
        /// <c>GetBlockEntity(pos)</c>, and at a dummy that is null, so the guard had no
        /// container to match and the pickup went through with the controller's GUI open
        /// on another player. Post-fix it resolves the controller first and blocks.
        /// </para>
        /// <para>
        /// <b>Honest label - how this row reaches the window.</b> It passes
        /// <c>checkIsCarryable: false</c> to the public manager seam because that is the
        /// only way to get past the carryable gate on a dummy cell, and **no production
        /// caller does that** (verified: nothing in <c>src</c> passes
        /// <c>checkIsCarryable: false</c>). Row 9 is the shipped-content behaviour;
        /// this row exists purely to observe which container the guard chooses once the
        /// gate is out of the way, so the M3 change is not an unevidenced refactor.
        /// <para>
        /// The seam is <b>no longer the only way in</b>.
        /// <c>MultiblockDummyCarryableGuardScenarios</c> (ExampleMods suite) makes a
        /// vanilla dummy genuinely carryable and reaches this same resolution through
        /// the production <c>Actions.PickUpIntoHands</c> path - its row D1 drives the
        /// real pickup, gets the real <c>SlotInventoryOpen</c>, and goes red on the
        /// minimal mutation of this file's line 150 below. <b>This seam stays</b>:
        /// the fixture patch is staged only by the ExampleMods suite, so this suite
        /// still cannot reach the window without it, and removing the seam would
        /// delete the only evidence that runs in a world where vanilla content is
        /// untouched. Keep both; they are not redundant.
        /// </para>
        /// </summary>
        [Trait("summary", "With a player's GUI open on the multiblock controller, a dummy-targeted pickup is blocked with slot-inventory-open because the guard resolves the controller's container.")]
        [Trait("pins", "The guard resolves the controller's container when the clicked cell is a multiblock dummy, instead of failing open.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before the fix, when the guard read the dummy cell, resolved no container, and let the pickup through while the controller's GUI was open.")]
        [Trait("caveat", "Reaches the guard only by passing the non-carryable-check seam, which no production caller uses; the shipped-content statement is the dummy premise row.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task WorldBlock_DummyTargeted_GuardChecksTheControllersContainer_R10()
        {
            var controller = World.Spawn.Offset(6, 1, 0);
            World.SetBlock(TrunkSouth, controller);
            Assert.Equal(TrunkSouth, World.BlockAt(controller).Code?.ToString());

            var dummy = controller.Copy().Add(-1, 0, 0);
            Assert.Equal(TrunkDummySouthWest, World.BlockAt(dummy).Code?.ToString());

            // The premise of the fix: the clicked cell exposes no container, the
            // controller does. If a future VS release gave dummy cells an inventory the
            // two would converge and this row's whole argument would change.
            Assert.True(
                World.BlockEntityAt<BlockEntityContainer>(dummy) == null,
                "The trunk's dummy cell now exposes a BlockEntityContainer; the pre-fix guard "
                + "would no longer have been a guaranteed no-op at a dummy.");
            var controllerEntity = World.BlockEntityAt<BlockEntityContainer>(controller);
            Assert.True(
                controllerEntity != null,
                $"The trunk controller exposes no BlockEntityContainer "
                + $"({World.BlockAt(controller).Code}), so there is no controller-side GUI to "
                + "bind and this row cannot test the guard's resolution. Pick a multiblock "
                + "whose controller is a container.");

            ITestPlayer actor = await JoinPlayer("Tester");
            ITestPlayer opener = await JoinPlayer("Opener");
            await actor.TeleportTo(World.Spawn.Offset(8, 0, 2));
            await opener.TeleportTo(World.Spawn.Offset(10, 0, 2));

            // Another player has the *controller's* GUI open - the only GUI a player
            // interacting with this structure could have open.
            OpenFor(opener, controllerEntity!.Inventory);

            string failureCode = "";
            bool picked = Carry.TryPickUp(
                actor.Entity, dummy, CarrySlot.Hands, ref failureCode, checkIsCarryable: false);

            Assert.False(picked,
                "The dummy-targeted pickup succeeded while the controller's inventory was "
                + "open on another player - the guard resolved no container at a dummy cell "
                + "(silent fail-open).");
            Assert.Equal(FailureCodes.SlotInventoryOpen, failureCode);
            Assert.False(Carry.IsCarrySlotOccupied(actor.Entity, CarrySlot.Hands));
            Assert.Equal(TrunkSouth, World.BlockAt(controller).Code?.ToString());
            Assert.Equal(TrunkDummySouthWest, World.BlockAt(dummy).Code?.ToString());
        }

        /// <summary>
        /// Calls the internal <c>CarryInventoryGuard.BagContentInventoryId</c> through
        /// reflection, off the <b>runtime</b> mod assembly (<c>Mod.GetType().Assembly</c>)
        /// rather than the copy this test compiled against - the same seam pattern as
        /// <c>MultiblockFootprintScenarios</c> / <c>HandlerInvoke</c>. Row 8 is only
        /// meaningful if it compares against the string production actually matches on,
        /// so a rename of the helper fails fast here instead of silently comparing a
        /// test-local copy.
        /// </summary>
        private string BagContentInventoryIdViaSeam(BlockEntityGroundStorage beg, ItemSlot bagSlot)
        {
            var guardType = Mod.GetType().Assembly.GetType("CarryOn.Common.Logic.CarryInventoryGuard")
                ?? throw new InvalidOperationException(
                    "CarryOn.Common.Logic.CarryInventoryGuard is not in the runtime CarryOn "
                    + "assembly - the type moved or was renamed; update the "
                    + "PickupOpenGuardScenarios seam.");

            var method = guardType.GetMethod("BagContentInventoryId",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    "CarryInventoryGuard.BagContentInventoryId(BlockEntityGroundStorage, ItemSlot) "
                    + "is not in the runtime CarryOn assembly - the method moved or was renamed; "
                    + "update the PickupOpenGuardScenarios seam.");

            return (string)method.Invoke(null, [beg, bagSlot])!;
        }

        /// <summary>
        /// The CarryOn mounted-bag identity for slot <paramref name="slotIndex"/>
        /// on entity <paramref name="entityId"/> - matches the prefix the existing
        /// <c>HasOpenMountedBagInventory</c> guard scans for.
        /// </summary>
        private static InventoryGeneric NewMountedBagWrapper(ICoreAPI api, long entityId, int slotIndex)
        {
            return new InventoryGeneric(
                1,
                MountedBagInventoryPrefix,
                $"{slotIndex}-{entityId}",
                api,
                null);
        }

        private static int GetSlotId(BlockEntityGroundStorage beg, BlockSelection selection)
        {
            var slot = beg.GetSlotAt(selection);
            return slot == null ? -1 : beg.Inventory.GetSlotId(slot);
        }

        /// <summary>
        /// The real attach round trip the mounted rows detach from: spawns the
        /// sailed cargo boat, places a chest seeded with a stick, joins the actor
        /// nearby, picks the chest into hands and attaches it into the boat's first
        /// chest-capable cargo slot through the real attach handler.
        /// </summary>
        private async Task<(Entity Boat, ITestPlayer Player, int Slot)> SpawnBoatWithAttachedChest()
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

            ITestPlayer player = await JoinPlayer("Tester");
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