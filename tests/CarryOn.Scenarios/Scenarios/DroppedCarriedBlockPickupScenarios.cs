using System;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Entities;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Dropped carried block entity pickup (OnPickupEntityMessage) over the real
    /// carryon:carriedblock entity: the first four rows pin the shipped
    /// PickupAccess policy - OwnerFirst with a 300s grace - through the actual
    /// CarriedBlockEntityHandler (a stranger within grace is denied with the
    /// not-owner toast and the entity is protected; past grace anyone may pick
    /// it up; the owner may always pick it up; a creative player bypasses the
    /// policy entirely, the first creative opt-in row set). The last two rows
    /// pin the present-but-unreadable tree gate: in survival the entity is
    /// protected with the carried-block-unreadable toast and the raw tree stays
    /// untouched, while a creative player may adopt the raw tree into the hands
    /// slot (SetCarriedRaw, the removed-mod recovery seam) and the entity is
    /// killed - the second creative opt-in row set. A picked-up entity is
    /// asserted through vanilla Die's synchronous marks (not alive, PickedUp
    /// despawn reason); the despawn queue's removal is server-loop machinery
    /// the headless harness does not advance. Entities are staged with trees
    /// built by the real CarriedBlockTreeSerializer and the entity's public
    /// SetCarriedBlockData, so no hand-built tree shapes are involved.
    /// Creative opt-ins follow the suite README, Decision 19: they exist only
    /// for rows whose subject is the bypass itself.
    /// </summary>
    [Trait("area", "Drop")]
    [Trait("summary", "Dropped carried-block entities staged as real spawns through the entity pickup handler assert the shipped owner-first pickup policy with its grace window, the present-but-unreadable tree gate, and the entity reach gate, with deliberate creative bypass halves pinning each opt-in.")]
    public class DroppedCarriedBlockPickupScenarios : CarryOnScenarioBase
    {
        private const int StaleBlockId = 999999;

        /// <summary>
        /// A stranger picking up a freshly dropped carried block in survival is
        /// denied while the OwnerFirst grace window is open: the not-owner toast
        /// (FailureCodes.NotOwner, with pickup-not-owner as its lang key)
        /// surfaces on the vanilla wire, no carry slot is occupied, and the
        /// entity stays alive and active in the world.
        /// </summary>
        [Trait("summary", "A stranger picking up a freshly dropped carried block in survival is denied while the owner-first grace window is open: the not-owner toast reaches the vanilla wire, no carry is granted, and the entity stays alive and active.")]
        [Trait("pins", "The survival half of the owner-first pickup policy with its grace window.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerWithinGrace_PickUpInSurvival_Denied_EntityProtected()
        {
            Assert.Equal(CarryOn.Common.Models.PickupAccess.OwnerFirst,
                Mod.Config.CarriedBlockEntity.PickupAccess);
            Assert.Equal(300f, Mod.Config.CarriedBlockEntity.GracePeriodSeconds);

            ITestPlayer owner = await JoinPlayer("Dropper");
            ITestPlayer picker = await JoinPlayer("Picker");
            await picker.TeleportTo(World.Spawn.Offset(5, 0, 4));

            var ecb = StageDroppedCarriedBlock(owner.Player.PlayerUID, DateTime.UtcNow.Ticks);

            picker.Client.Clear();
            HandlerInvoke.PickupEntity(Mod, picker.Player, new PickupEntityMessage(ecb.EntityId));

            var wire = VanillaClientWire.Tap(picker);
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.NotOwner));

            Assert.False(Carry.IsCarrySlotOccupied(picker.Entity, CarrySlot.Hands));
            Assert.True(ecb.Alive, "A refused pickup must not kill the entity");
            Assert.Equal(EnumEntityState.Active, ecb.State);
        }

        /// <summary>
        /// Once the OwnerFirst grace window has elapsed, a stranger may pick the
        /// dropped carried block up in survival: the hands slot carries the chest
        /// with its block-entity data intact and the entity despawns.
        /// </summary>
        [Trait("summary", "Once the drop is backdated past the grace window the same stranger pickup succeeds: the hands slot carries the chest with its block-entity data and the entity despawns as picked up.")]
        [Trait("pins", "The grace window is the only stranger barrier; past it anyone may pick the drop up.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerPastGrace_PickUpInSurvival_Allowed_CarriesChest()
        {
            ITestPlayer owner = await JoinPlayer("Dropper");
            ITestPlayer picker = await JoinPlayer("Picker");
            await picker.TeleportTo(World.Spawn.Offset(5, 0, 4));

            long droppedJustOverGraceAgo =
                DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(301).Ticks;
            var ecb = StageDroppedCarriedBlock(owner.Player.PlayerUID, droppedJustOverGraceAgo);

            picker.Client.Clear();
            HandlerInvoke.PickupEntity(Mod, picker.Player, new PickupEntityMessage(ecb.EntityId));

            var wire = VanillaClientWire.Tap(picker);
            Assert.Empty(wire.ErrorsWithCode(FailureCodes.NotOwner));

            var carried = Carry.GetCarried(picker.Entity, CarrySlot.Hands);
            Assert.NotNull(carried);
            Assert.Equal("game:chest-east", carried?.ItemStack?.Block?.Code?.ToString());
            Assert.NotNull(carried?.BlockEntityData);
            AssertEntityDiedAsPickedUp(ecb, "it was picked up");
        }

        /// <summary>
        /// The owner may pick their own dropped carried block up in survival even
        /// while the grace window is still open: the hands slot carries the chest
        /// and the entity despawns.
        /// </summary>
        [Trait("summary", "The owner picks their own freshly dropped carried block up in survival with the grace window still open: the chest is carried and the entity despawns as picked up.")]
        [Trait("pins", "The owner may always pick their own drop up, grace window notwithstanding.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task OwnerWithinGrace_PickUpInSurvival_Allowed_CarriesChest()
        {
            ITestPlayer owner = await JoinPlayer("Dropper");
            await owner.TeleportTo(World.Spawn.Offset(5, 0, 4));

            var ecb = StageDroppedCarriedBlock(owner.Player.PlayerUID, DateTime.UtcNow.Ticks);

            owner.Client.Clear();
            HandlerInvoke.PickupEntity(Mod, owner.Player, new PickupEntityMessage(ecb.EntityId));

            var wire = VanillaClientWire.Tap(owner);
            Assert.Empty(wire.ErrorsWithCode(FailureCodes.NotOwner));

            var carried = Carry.GetCarried(owner.Entity, CarrySlot.Hands);
            Assert.NotNull(carried);
            Assert.Equal("game:chest-east", carried?.ItemStack?.Block?.Code?.ToString());
            AssertEntityDiedAsPickedUp(ecb, "it was picked up by its owner");
        }

        /// <summary>
        /// The creative twin of the grace row: a creative stranger picks up a
        /// freshly dropped carried block that survival would deny - the creative
        /// bypass in CarriedBlockAccessPolicy.CanPickup pinned explicitly (suite
        /// README, Decision 19: creative opt-ins exist only for rows whose
        /// subject is the bypass). No toast fires, the hands slot carries the
        /// chest, and the entity despawns.
        /// </summary>
        [Trait("summary", "A creative stranger picks up the freshly dropped carried block that survival denies: no not-owner toast fires, the chest is carried in hands, and the entity despawns as picked up.")]
        [Trait("pins", "The creative bypass of the owner-first pickup policy.")]
        [Trait("role", "regression")]
        [Trait("decision", "19")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task StrangerWithinGrace_PickUpInCreative_Allowed_BypassesOwnerFirst()
        {
            ITestPlayer owner = await JoinPlayer("Dropper");
            ITestPlayer picker = await JoinPlayer("Picker", EnumGameMode.Creative);
            await picker.TeleportTo(World.Spawn.Offset(5, 0, 4));

            var ecb = StageDroppedCarriedBlock(owner.Player.PlayerUID, DateTime.UtcNow.Ticks);

            picker.Client.Clear();
            HandlerInvoke.PickupEntity(Mod, picker.Player, new PickupEntityMessage(ecb.EntityId));

            var wire = VanillaClientWire.Tap(picker);
            Assert.Empty(wire.ErrorsWithCode(FailureCodes.NotOwner));

            var carried = Carry.GetCarried(picker.Entity, CarrySlot.Hands);
            Assert.NotNull(carried);
            Assert.Equal("game:chest-east", carried?.ItemStack?.Block?.Code?.ToString());
            AssertEntityDiedAsPickedUp(ecb, "it was picked up by a creative player");
        }

        /// <summary>
        /// A dropped carried block whose stored tree is present but unreadable
        /// (the removed-mod shape: a block id that no longer resolves and no
        /// stored code to recover from) is protected in survival: the pickup
        /// refuses with the carried-block-unreadable toast, no carry slot is
        /// occupied, and the entity stays active with its raw tree untouched for
        /// later recovery. The picker is the owner, so the permission gate is
        /// not the variable this row exercises.
        /// </summary>
        [Trait("summary", "A dropped carried block whose stored tree is present but unreadable refuses pickup in survival: the carried-block-unreadable toast fires, no carry is granted, and the entity stays active with its raw tree untouched.")]
        [Trait("pins", "The survival half of the unreadable-tree gate, with the raw tree preserved for later recovery.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task UnreadableTree_PickUpInSurvival_Denied_EntityProtected()
        {
            ITestPlayer owner = await JoinPlayer("Recoverer");
            await owner.TeleportTo(World.Spawn.Offset(5, 0, 4));

            var ecb = StageDroppedCarriedBlock(
                owner.Player.PlayerUID, DateTime.UtcNow.Ticks, CorruptToUnreadable);

            owner.Client.Clear();
            HandlerInvoke.PickupEntity(Mod, owner.Player, new PickupEntityMessage(ecb.EntityId));

            var wire = VanillaClientWire.Tap(owner);
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.CarriedBlockUnreadable));

            Assert.False(Carry.IsCarrySlotOccupied(owner.Entity, CarrySlot.Hands));
            Assert.True(ecb.Alive, "A refused pickup must not kill the entity");
            Assert.Equal(EnumEntityState.Active, ecb.State);
            var keptStack = ecb.CarriedBlockTree?.GetItemstack(AttributeKeys.CarriedBlockData.Stack);
            Assert.True(keptStack?.Id == StaleBlockId && keptStack.Class == EnumItemClass.Block,
                "The refused pickup must leave the raw tree untouched on the entity");
        }

        /// <summary>
        /// The creative twin of the unreadable row: a creative player may adopt
        /// the untouched raw tree into their hands slot (the SetCarriedRaw
        /// recovery seam behind TryPickupFromEntityGeneric) so the content stays
        /// recoverable once the source mod is re-enabled. The slot reads
        /// occupied, gameplay reads return null, the protected read reports
        /// failed with the tree preserved, and the entity despawns. The picker
        /// is the owner, so the permission bypass is not the variable this row
        /// exercises - it pins the adoption branch itself (Decision 19).
        /// </summary>
        [Trait("summary", "A creative player adopts the unreadable raw tree into the hands slot: the slot reads occupied, gameplay reads return null, the protected read reports failed with the tree preserved, and the entity despawns.")]
        [Trait("pins", "The creative adoption branch keeps removed-mod content recoverable through the raw-carry seam.")]
        [Trait("role", "regression")]
        [Trait("decision", "19")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task UnreadableTree_PickUpInCreative_AdoptsRawCarry_EntityDespawns()
        {
            ITestPlayer owner = await JoinPlayer("Recoverer", EnumGameMode.Creative);
            await owner.TeleportTo(World.Spawn.Offset(5, 0, 4));

            var ecb = StageDroppedCarriedBlock(
                owner.Player.PlayerUID, DateTime.UtcNow.Ticks, CorruptToUnreadable);

            owner.Client.Clear();
            HandlerInvoke.PickupEntity(Mod, owner.Player, new PickupEntityMessage(ecb.EntityId));

            Assert.True(Carry.IsCarrySlotOccupied(owner.Entity, CarrySlot.Hands),
                "The adopted raw carry must count as an occupied slot");
            Assert.Null(Carry.GetCarried(owner.Entity, CarrySlot.Hands));

            var read = Carry.GetCarriedReadResult(owner.Entity, CarrySlot.Hands, out var storedTree);
            Assert.True(read.IsFailed,
                $"The adopted raw carry must report Failed, got {read.Status}/{read.Reason}");
            Assert.Equal("missing-block", read.Reason);
            Assert.NotNull(storedTree);
            var storedStack = storedTree!.GetItemstack(AttributeKeys.CarriedBlockData.Stack);
            Assert.True(storedStack?.Id == StaleBlockId && storedStack.Class == EnumItemClass.Block,
                "The adopted tree must be preserved untouched for later recovery");

            AssertEntityDiedAsPickedUp(ecb, "it was adopted by a creative player");
        }

        /// <summary>
        /// Phase 3 - the entity-pickup reach gate, survival half. The dropped block sits well
        /// beyond the configured reach and the picker is its <b>owner</b>, so the OwnerFirst
        /// grace window is wide open and the permission policy is not the variable: the only
        /// reason to refuse is reach. The toast is entity-out-of-reach (the target is an entity,
        /// so the code names the entity), no carry slot is occupied, and the entity stays alive.
        /// </summary>
        [Trait("summary", "A dropped carried block sitting beyond the configured reach is refused for its owner in survival: the entity-out-of-reach toast fires, no carry is granted, and the entity stays alive.")]
        [Trait("pins", "The entity-pickup reach gate keeps the entity-out-of-reach code for entity targets rather than inheriting the block code.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BeyondReach_PickUpInSurvival_Denied_EntityProtected()
        {
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;
            BlockPos far = World.Spawn.Offset(reach + 8, 1, 4);

            ITestPlayer owner = await JoinPlayer("Dropper");
            await owner.TeleportTo(World.Spawn.Offset(2, 0, 4));
            Assert.True(far.ToVec3d().DistanceTo(owner.Entity.Pos.XYZ) > reach + 2,
                "Row premise: the dropped entity must sit far enough out that either anchor rejects");

            var ecb = StageDroppedCarriedBlock(
                owner.Player.PlayerUID, DateTime.UtcNow.Ticks, at: far);

            owner.Client.Clear();
            HandlerInvoke.PickupEntity(Mod, owner.Player, new PickupEntityMessage(ecb.EntityId));

            var wire = VanillaClientWire.Tap(owner);
            Assert.NotEmpty(wire.ErrorsWithCode(FailureCodes.EntityOutOfReach));

            Assert.False(Carry.IsCarrySlotOccupied(owner.Entity, CarrySlot.Hands));
            Assert.True(ecb.Alive, "An out-of-reach pickup must not kill the entity");
            Assert.Equal(EnumEntityState.Active, ecb.State);
        }

        /// <summary>
        /// The creative twin of the reach row: a creative owner picks the same out-of-reach
        /// dropped block up, pinning the creative exemption inside the shared reach predicate
        /// (Decision 19 - a creative opt-in exists only for a row whose subject is the bypass).
        /// The survival row above is what proves the gate is real; this one proves it does not
        /// fire for creative.
        /// </summary>
        [Trait("summary", "A creative owner picks the same out-of-reach dropped block up: no reach error arrives, the chest is carried in hands, and the entity despawns as picked up.")]
        [Trait("pins", "The creative exemption on the entity-pickup reach path.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed without the production change that added the creative exemption.")]
        [Trait("decision", "19")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task BeyondReach_PickUpInCreative_Allowed_CarriesChest()
        {
            int reach = Mod.Config.CarryOptions.MaxInteractionDistance;
            BlockPos far = World.Spawn.Offset(reach + 8, 1, 4);

            ITestPlayer owner = await JoinPlayer("Dropper", EnumGameMode.Creative);
            await owner.TeleportTo(World.Spawn.Offset(2, 0, 4));
            Assert.True(far.ToVec3d().DistanceTo(owner.Entity.Pos.XYZ) > reach + 2,
                "Row premise: the dropped entity must sit far enough out that either anchor rejects");

            var ecb = StageDroppedCarriedBlock(
                owner.Player.PlayerUID, DateTime.UtcNow.Ticks, at: far);

            owner.Client.Clear();
            HandlerInvoke.PickupEntity(Mod, owner.Player, new PickupEntityMessage(ecb.EntityId));

            var wire = VanillaClientWire.Tap(owner);
            Assert.Empty(wire.ErrorsWithCode(FailureCodes.EntityOutOfReach));

            var carried = Carry.GetCarried(owner.Entity, CarrySlot.Hands);
            Assert.NotNull(carried);
            Assert.Equal("game:chest-east", carried?.ItemStack?.Block?.Code?.ToString());
            AssertEntityDiedAsPickedUp(ecb, "it was picked up by a creative player beyond reach");
        }

        /// <summary>
        /// Stages a real carryon:carriedblock entity carrying a serialized chest:
        /// the tree comes from the actual CarriedBlockTreeSerializer over the
        /// world chest, and the entity is populated through its public
        /// SetCarriedBlockData with the given owner and real-time drop ticks
        /// (backdate past the grace window to simulate an old drop). Fails fast
        /// when the entity type, the carryable behavior or its pickup handler
        /// do not resolve, mirroring the gates OnPickupEntityMessage checks.
        /// </summary>
        private EntityCarriedBlock StageDroppedCarriedBlock(
            string ownerUid, long dropTimeRealTicks, Action<ITreeAttribute>? mutateTree = null,
            BlockPos? at = null)
        {
            BlockPos chestPos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock("game:chest-east", chestPos);
            Assert.Equal("game:chest-east", World.BlockAt(chestPos).Code.ToString());

            var carried = Carry.GetCarriedFromWorld(chestPos, CarrySlot.Hands);
            Assert.NotNull(carried);
            var tree = CarriedBlockTreeSerializer.Serialize(carried!);
            Assert.NotNull(tree);
            mutateTree?.Invoke(tree!);

            var entity = World.SpawnEntity("carryon:carriedblock",
                at ?? World.Spawn.Offset(6, 1, 4));
            var ecb = entity as EntityCarriedBlock;
            Assert.True(ecb != null,
                "carryon:carriedblock must spawn as EntityCarriedBlock");
            Assert.True(ecb!.GetBehavior<EntityBehaviorCarryable>()?.IsCarryInteractable == true,
                "The spawned entity must resolve its carriedblock pickup handler");

            ecb.SetCarriedBlockData(tree!, ownerUid,
                World.Api.World.Calendar.TotalDays, dropTimeRealTicks);
            return ecb;
        }

        /// <summary>
        /// Rewrites a serialized carried tree into the removed-mod shape: a block
        /// stack whose id no longer resolves and no stored code or block-entity
        /// data left to recover from - the same shape DeserializeSafe must read
        /// as failed (LibContractScenarios pins that half).
        /// </summary>
        private static void CorruptToUnreadable(ITreeAttribute tree)
        {
            tree.SetItemstack(AttributeKeys.CarriedBlockData.Stack,
                new ItemStack { Class = EnumItemClass.Block, Id = StaleBlockId });
            tree.RemoveAttribute(AttributeKeys.CarriedBlockData.OriginalBlockCode);
            tree.RemoveAttribute(AttributeKeys.CarriedBlockData.Data);
        }

        /// <summary>
        /// A picked-up entity is killed through vanilla Entity.Die(PickedUp),
        /// which marks it synchronously: no longer alive and carrying the
        /// PickedUp despawn reason. The removal from the loaded-entities map
        /// itself is vanilla server-loop machinery (the despawn queue) that the
        /// headless harness does not advance, so it is not asserted here.
        /// </summary>
        private void AssertEntityDiedAsPickedUp(EntityCarriedBlock ecb, string because)
        {
            Assert.False(ecb.Alive,
                $"A dropped carried block must be dead because {because}");
            Assert.Equal(EnumDespawnReason.PickedUp, ecb.DespawnReason?.Reason);
        }
    }
}
