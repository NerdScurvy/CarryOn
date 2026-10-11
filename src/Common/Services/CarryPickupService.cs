using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Logic;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Services
{
    internal sealed class CarryPickupService(ICoreAPI api, ICarryManager carryManager, CarryPermissionService permission, IConfigProvider configProvider)
    {

        private bool CanPickUp(Entity entity, BlockPos pos, CarrySlot slot, CarriedBlock carried, ref string failureCode)
        {
            return carryManager.CarryEvents?.TriggerBeforePickUpBlock(entity, pos, slot, carried, ref failureCode) ?? true;
        }

        private IServerPlayer? ResolveActingPlayer(Entity entity)
        {
            if (entity.Api.Side != EnumAppSide.Server)
            {
                return null;
            }

            return api.World.AllOnlinePlayers
                .OfType<IServerPlayer>()
                .FirstOrDefault(serverPlayer => serverPlayer.Entity?.EntityId == entity.EntityId);
        }

        public CarriedBlock? GetCarriedFromWorld(BlockPos pos, CarrySlot slot, bool checkIsCarryable = false)
        {
            string failureCode = FailureCodes.Ignore;
            return GetCarriedFromWorld(null, pos, slot, ref failureCode, checkIsCarryable);
        }

        public CarriedBlock? GetCarriedFromWorld(Entity? entity, BlockPos pos, CarrySlot slot, ref string failureCode, bool checkIsCarryable = false, bool? captureAttachedSigns = null, HashSet<BlockPos>? footprint = null)
        {
            failureCode ??= FailureCodes.Ignore;

            var world = api.World;
            var carried = BlockUtils.CreateCarriedFromBlockPos(world, pos, slot);
            if (carried == null) return null;

            if (checkIsCarryable && !carryManager.IsCarryable(carried.Block, slot)) return null;

            if (entity != null && !CanPickUp(entity, pos, slot, carried, ref failureCode)) return null;

            bool canCapture = configProvider?.Config?.CarryOptions?.CarryAttachedWallSigns == true;
            bool shouldCapture = canCapture && (captureAttachedSigns ?? true);
            if (shouldCapture)
            {
                var attachedBlocks = CaptureAttachedSigns(world, pos, carried.Block, entity, footprint);
                if (attachedBlocks != null)
                    carried = new CarriedBlock(carried.Slot, carried.ItemStack, carried.BlockEntityData, attachedBlocks, carried.OriginalBlockCode, carried.OriginalMeshAngle);
            }

            carryManager.CarryEvents?.TriggerBeforeRemoveBlockFromWorld(carried, pos);

            if (carried.HasAttachedBlocks)
            {
                foreach (var child in carried.AttachedBlocks ?? [])
                {
                    var childPos = pos.Copy();
                    childPos.Add(child.RelativeOffset);
                    world.BlockAccessor.SetBlock(0, childPos);
                    world.BlockAccessor.TriggerNeighbourBlockUpdate(childPos);
                }
            }

            world.BlockAccessor.SetBlock(0, pos);
            world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.ClearReinforcement(pos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
            return carried;
        }

        public bool TryPickUp(
            Entity entity,
            BlockPos pos,
            CarrySlot slot,
            ref string failureCode,
            bool checkIsCarryable = true,
            bool playSound = true,
            bool? captureAttachedSigns = null)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(pos);

            failureCode ??= FailureCodes.Ignore;

            if (carryManager.GetCarried(entity, slot) != null)
            {
                failureCode = FailureCodes.AlreadyCarrying;
                return false;
            }

            var block = entity.World.BlockAccessor.GetBlock(pos);

            // Whole-footprint permission (decided rule 2): the main/multiblock footprint
            // must be permitted at pickup time - current claim state governs per cell, no
            // grandfathering. The silent scan applies every per-cell gate; one standard
            // claim message is emitted on the first denied cell. Permission still precedes
            // the carryable check, so a dummy-targeted pickup of a claim-straddling trunk
            // fails NoPermission (its footprint includes the claim-blocked controller)
            // rather than NotCarryable.
            //
            // Kept in scope (not a local of this block): the inventory-open guard below
            // resolves the same controller, and the attached-sign sweep reuses this set
            // instead of recomputing ~125 block reads per pickup.
            HashSet<BlockPos>? footprint = null;
            if (entity.Api.Side == EnumAppSide.Server)
            {
                footprint = CarryFootprint.GetFootprint(entity.World, pos, block);
                if (permission.TryGetFirstDeniedCell(entity, footprint, out var deniedCell, out var denialReason))
                {
                    // Re-invoke on the denied cell only, with messages on, so the refusal is
                    // reported once for the whole footprint. What that re-invoke actually
                    // sends depends on the reason, which is why the reason is carried out
                    // rather than flattened to NoPermission here: a claim denial gets
                    // vanilla's own claim message from this call, so the code below tells
                    // the handler to stay quiet instead of adding a second toast, while a
                    // reinforced cell gets no message from this call at all and needs
                    // CarryOn's own.
                    permission.HasPermissionAt(entity, deniedCell!, out _, showErrorMessage: true);
                    failureCode = denialReason.ToFailureCode();
                    return false;
                }
            }

            // World-block parity: if another player has this
            // block's inventory open, reject before the world is touched - their GUI stays
            // bound to an inventory that is about to move.
            //
            // Ahead of the carryable gate: the gate used to run first, so a pickup
            // aimed at a multiblock dummy died NotCarryable and the guard was never
            // reached. That ordering is what hid the bug below - see
            // WorldBlock_DummyTargeted_NotCarryableBeforeTheGuard_R9.
            //
            // The container checked is always the one whose GUI the player could have
            // open. For a multiblock that is the *controller's*, not the clicked cell: a
            // dummy carries no BlockEntityContainer, so resolving the raw pos could only
            // ever find nothing and fail open. Resolved through the footprint's own
            // controller anchor (CarryFootprint.ResolveController) so this and the
            // placement write-set resolve the controller identically; for a plain block
            // it returns pos with no world read at all.
            var actingPlayer = ResolveActingPlayer(entity);
            if (actingPlayer != null)
            {
                var controllerPos = CarryFootprint.ResolveController(entity.World, pos, block, out _);
                var controllerEntity = entity.World.BlockAccessor.GetBlockEntity(controllerPos) as BlockEntityContainer;
                if (controllerEntity?.Inventory?.InventoryID is string inventoryId
                    && CarryInventoryGuard.HasOpenInventory(api, actingPlayer, id => id == inventoryId))
                {
                    failureCode = FailureCodes.SlotInventoryOpen;
                    return false;
                }
            }

            if (checkIsCarryable && !carryManager.IsCarryable(block, slot))
            {
                failureCode = FailureCodes.NotCarryable;
                return false;
            }

            var carryBehavior = block.GetBehavior<BlockBehaviorCarryable>();

            // Optimistic pickup: on the client side, when OptimisticPickup is disabled,
            // skip the actual pickup and return true immediately. The server will handle
            // the real pickup and sync the result. This avoids client-side desync when
            // claims or permissions prevent the pickup.
            if (entity.Api.Side == EnumAppSide.Client && carryBehavior != null && !carryBehavior.OptimisticPickup)
                return true;

            var optimisticPickup = carryBehavior?.OptimisticPickup ?? true;
            var carried = GetCarriedFromWorld(entity, pos, slot, ref failureCode, checkIsCarryable, captureAttachedSigns, footprint);
            if (carried == null) return false;

            carryManager.SetCarried(entity, carried);

            if (playSound)
                SoundHelper.PlaySound(api, carried.Block, pos, entity as EntityPlayer, dualCall: optimisticPickup);

            if (entity.Api.Side == EnumAppSide.Server)
            {
                var entityName = entity.GetName() ?? "Unknown Entity";
                entity.World.Logger.Audit($"[{ModId}] {entityName} picked up block {carried.Block.Code.GetName()} at {pos}");
            }

            return true;
        }

        public bool TryPickUp(Entity entity, BlockPos pos, CarrySlot slot, bool checkIsCarryable = true, bool playSound = true, bool? captureAttachedSigns = null)
        {
            string failureCode = FailureCodes.Ignore;
            return TryPickUp(entity, pos, slot, ref failureCode, checkIsCarryable, playSound, captureAttachedSigns);
        }

        /// <summary>
        /// Picks a single bag item (backpack, hand basket, sack, quiver, ...) out of the ground
        /// storage slot targeted by <paramref name="selection"/> and carries it in the hands slot.
        /// The ground storage <b>block</b> itself is never picked up. Server-authoritative: after
        /// removing the stack, <c>CheckInventoryClearedMidTick()</c> lets the vanilla self-delete
        /// path remove the storage block when it became empty.
        /// </summary>
        /// <param name="entity">The entity picking up the bag.</param>
        /// <param name="selection">Client-resolved selection of the ground storage slot containing the bag.</param>
        /// <param name="failureCode">Receives the failure code on rejection, <see cref="FailureCodes.Ignore"/> otherwise.</param>
        /// <param name="maxInteractionDistance">Optional distance limit; defaults to the configured carry interaction distance.</param>
        /// <param name="playSound">Whether to play the ground storage remove sound.</param>
        public bool TryPickUpGroundStorageBag(
            Entity entity,
            BlockSelection selection,
            ref string failureCode,
            int? maxInteractionDistance = null,
            bool playSound = true)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(selection);

            failureCode ??= FailureCodes.Ignore;

            if (carryManager.GetCarried(entity, CarrySlot.Hands) != null)
            {
                failureCode = FailureCodes.AlreadyCarrying;
                return false;
            }

            if (entity.Api.Side == EnumAppSide.Server && !carryManager.HasPermissionAt(entity, selection.Position))
            {
                failureCode = FailureCodes.NoPermission;
                return false;
            }

            double reach = maxInteractionDistance
                ?? configProvider?.Config?.CarryOptions?.MaxInteractionDistance
                ?? Defaults.MaxInteractionDistance;
            // CarryReach supplies the eye anchor, the creative exemption and the cell-centre target
            // (this used to measure to the cell corner, which biased the boundary toward whichever
            // side the player approached from). The code becomes BlockOutOfReach because the target
            // is a block cell; nothing pinned the old CannotInteract here.
            if (!CarryReach.IsWithinReach(entity, selection.Position, reach))
            {
                failureCode = FailureCodes.BlockOutOfReach;
                return false;
            }

            var block = entity.World.BlockAccessor.GetBlock(selection.Position);
            if (block is not BlockGroundStorage)
            {
                failureCode = FailureCodes.NotCarryable;
                return false;
            }

            var beg = entity.World.BlockAccessor.GetBlockEntity(selection.Position) as BlockEntityGroundStorage;
            if (!beg.IsGroundStorageBagTargeted(selection))
            {
                failureCode = FailureCodes.NotCarryable;
                return false;
            }

            var bagSlot = beg!.GetSlotAt(selection);
            if (bagSlot?.Itemstack == null)
            {
                failureCode = FailureCodes.NotCarryable;
                return false;
            }

            // Another player having the bag's content GUI or the storage's own container GUI
            // open keeps the targeted stack bound to their client - yanking it mid-open is a
            // duplicate/ghost-item window. The acting player's own open GUI
            // never blocks the action.
            var acting = ResolveActingPlayer(entity);

            if (CarryInventoryGuard.HasOpenInventory(
                    api,
                    acting,
                    id => id == beg.Inventory.InventoryID
                        || id.StartsWith(CarryInventoryGuard.BagContentInventoryId(beg, bagSlot!))))
            {
                failureCode = FailureCodes.SlotInventoryOpen;
                return false;
            }

            // TakeOutWhole zeroes the slot's original stack object in place and returns a
            // clone - the carried stack must be that clone, since a reference saved before
            // the call would observe StackSize == 0 afterwards.
            var bagStack = bagSlot!.TakeOutWhole();
            if (bagStack == null)
            {
                failureCode = FailureCodes.NotCarryable;
                return false;
            }

            bagSlot.MarkDirty();
            beg.MarkDirty(true);
            beg.CheckInventoryClearedMidTick();

            carryManager.SetCarried(entity, new CarriedBlock(CarrySlot.Hands, bagStack, null));

            if (playSound)
                SoundHelper.PlaySound(api, beg.StorageProps?.PlaceRemoveSound, selection.Position, entity as EntityPlayer, dualCall: false);

            if (entity.Api.Side == EnumAppSide.Server)
            {
                var entityName = entity.GetName() ?? "Unknown Entity";
                entity.World.Logger.Audit($"[{ModId}] {entityName} picked up bag {bagStack.Collectible?.Code} from ground storage at {selection.Position}");
            }

            return true;
        }

        /// <summary>
        /// Stows the bag currently carried in the hands slot (picked up from ground storage) into
        /// the first empty slot of the player's vanilla backpack inventory. Never touches the back
        /// carry slot - this is the swap-back companion of <see cref="TryPickUpGroundStorageBag"/>.
        /// </summary>
        /// <param name="entity">The entity stowing the carried bag.</param>
        /// <param name="failureCode">Receives the failure code on rejection, <see cref="FailureCodes.Ignore"/> otherwise.</param>
        /// <param name="playSound">Whether to play the item's place sound.</param>
        public bool TryStoreBagInBackpack(Entity entity, ref string failureCode, bool playSound = true)
        {
            ArgumentNullException.ThrowIfNull(entity);

            failureCode ??= FailureCodes.Ignore;

            var carried = carryManager.GetCarried(entity, CarrySlot.Hands);
            var stack = carried?.ItemStack;
            if (carried == null || stack == null)
            {
                failureCode = FailureCodes.NothingCarried;
                return false;
            }

            if (!stack.IsBackpackStowable())
            {
                failureCode = FailureCodes.CannotSwapBack;
                return false;
            }

            if (entity is not EntityPlayer { Player: { } entityPlayer })
            {
                failureCode = FailureCodes.CannotInteract;
                return false;
            }

            // Only the four worn-bag slots: the "backpack" inventory also exposes the content
            // slots of every bag being worn (InventoryPlayerBackpacks.bagInv), and a carried
            // bag must never be stowed inside another bag's inventory.
            var backpack = entityPlayer.InventoryManager.GetOwnInventory("backpack");
            var emptySlot = backpack?.FirstOrDefault(s => s is ItemSlotBackpack && s.Empty);
            if (emptySlot == null)
            {
                failureCode = FailureCodes.BackpackSlotsFull;
                return false;
            }

            emptySlot.Itemstack = stack;
            emptySlot.MarkDirty();

            carryManager.RemoveCarried(entity, CarrySlot.Hands);

            if (playSound)
                SoundHelper.PlaySound(api, new AssetLocation(SoundPaths.DefaultPlace), entity.Pos.AsBlockPos, entityPlayer.Entity, dualCall: false);

            if (entity.Api.Side == EnumAppSide.Server)
            {
                var entityName = entity.GetName() ?? "Unknown Entity";
                entity.World.Logger.Audit($"[{ModId}] {entityName} stowed bag {stack.Collectible?.Code} into backpack");
            }

            return true;
        }

        /// <summary>
        /// Captures the wall signs attached to the parent block's footprint as attached
        /// carried blocks, so they are picked up alongside the parent.
        /// </summary>
        /// <param name="world">The world accessor.</param>
        /// <param name="pos">The position of the parent block being picked up.</param>
        /// <param name="parentBlock">The parent block being picked up.</param>
        /// <param name="entity">
        /// The acting entity. On the server, a sign whose own cell the entity lacks permission for is
        /// skipped - left behind in the world, not captured, not removed (decided rule 3: signs never
        /// deny the parent pickup). Null (programmatic pickup) and client-side entities stay unfiltered;
        /// the server is authoritative and resyncs the client (see CarryServerHandler's pickup
        /// reconciliation, which uses the same scan to find the cells the client got wrong).
        /// </param>
        /// <param name="footprint">
        /// The caller's already-computed footprint for the same parent, if it has one.
        /// Null recomputes it.
        /// </param>
        /// <returns>A list of attached wall signs, or null if none were found.</returns>
        private List<AttachedCarriedBlock>? CaptureAttachedSigns(IWorldAccessor world, BlockPos pos, Block parentBlock, Entity? entity, HashSet<BlockPos>? footprint)
        {
            var candidates = CarrySignScan.Scan(world, pos, parentBlock, footprint);
            if (candidates.Count == 0) return null;

            var attached = new List<AttachedCarriedBlock>();

            foreach (var candidate in candidates)
            {
                var candidatePos = candidate.SignPos;

                // Claim filter (decided rule 3): a sign whose own cell the acting
                // entity lacks permission for is left behind - not captured, not
                // removed. Only the main/multiblock footprint can deny the pickup.
                if (entity != null
                    && entity.Api.Side == EnumAppSide.Server
                    && !permission.HasPermissionAt(entity, candidatePos, showErrorMessage: false))
                    continue;

                var candidateBlock = world.BlockAccessor.GetBlock(candidatePos);
                var originalCode = candidateBlock.Code;
                var childStack = candidateBlock.SafeOnPickBlock(world, candidatePos) ?? new ItemStack(candidateBlock);

                ITreeAttribute? childData = null;
                var childEntity = world.BlockAccessor.GetBlockEntity(candidatePos);
                if (childEntity != null)
                {
                    childData = new TreeAttribute();
                    childEntity.ToTreeAttributes(childData);
                    childData = (ITreeAttribute)childData.Clone();
                    childData.RemoveAttribute(AttributeKeys.BlockEntityData.PosX);
                    childData.RemoveAttribute(AttributeKeys.BlockEntityData.PosY);
                    childData.RemoveAttribute(AttributeKeys.BlockEntityData.PosZ);
                    childData.RemoveAttribute(AttributeKeys.BlockEntityData.MeshAngle);
                }

                var relativeOffset = new BlockPos(
                    candidatePos.X - pos.X,
                    candidatePos.Y - pos.Y,
                    candidatePos.Z - pos.Z
                );

                var childCarried = new CarriedBlock(CarrySlot.Attached, childStack, childData, null, originalCode);
                attached.Add(new AttachedCarriedBlock(relativeOffset, childCarried, candidate.Facing));
            }

            return attached.Count > 0 ? attached : null;
        }
    }
}
