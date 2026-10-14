using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Logic;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Services
{
    internal sealed class CarryPlacementService(ICoreAPI api, ICarryManager carryManager, CarryPermissionService permission, IConfigProvider configProvider, GroundStorageItemPlacement groundStorage)
    {
        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, bool dropped = false, bool playSound = true)
        {
            string failureCode = FailureCodes.Ignore;
            return TryPlaceDown(entity, carriedBlock, selection, ref failureCode, dropped, playSound);
        }

        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, ref string failureCode, bool dropped = false, bool playSound = true)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(selection);

            failureCode ??= FailureCodes.Ignore;

            if (carriedBlock == null)
            {
                failureCode = FailureCodes.NotCarrying;
                return false;
            }

            var world = api.World;

            if (carriedBlock.ItemStack == null) return false;
            if (!world.BlockAccessor.IsValidPos(selection.Position)) return false;

            // Entity spawn is handled by TryPlaceDownAt (player path).
            // For drops (dropped=true), check here so CarryDropService gets entity spawning.
            // Gated on the signal flag: custom entity blocks re-spawn as entities, normal blocks
            // proceed to block placement (TryPlaceAtTarget) under EntityOnFailedPlacement.
            if (dropped
                && CarryEntityPlacementPolicy.ShouldSpawnAsEntityOnPlacement(carriedBlock, configProvider.Config.CarriedBlockEntity)
                && TrySpawnAsEntity(entity, carriedBlock, selection, dropped))
                return true;

            // Item-class carry (e.g. a bag picked up from ground storage): placement deposits
            // into an existing ground storage or creates a new one and finalizes entirely
            // inside TryPlaceDownAsPlayer - block-class finalize (attached children, block
            // sounds, block entity restore) does not apply, and the anchor cell is the
            // storage cell rather than the clicked cell. Dropped items never reach this
            // point: CarryDropService routes them through its own item pipeline.
            if (carriedBlock.Block == null)
            {
                if (dropped) return false;
                return TryPlaceAtTarget(entity, carriedBlock, selection, dropped: false, ref failureCode);
            }

            // Whole-footprint claim preflight (decided rule 1): every cell this placement
            // writes - the target cell, all multiblock footprint cells of the variant being
            // placed, all attached-sign cells at their estimated rotated offsets - must be
            // unclaimed or claim-permitted. One gate covers the player, dropped and fallback
            // paths; claim denial takes precedence over clearance/support denial. Deliberately
            // stricter than vanilla, which claim-checks the target cell only (vanilla allows
            // multiblock part cells to intrude into claims). Server-authoritative: the client
            // predicts optimistically and resyncs on rejection, as with the single-cell gate
            // this replaces.
            if (api.Side == EnumAppSide.Server)
            {
                var footprint = CarryFootprint.GetPlacementFootprint(world, entity, carriedBlock, selection, dropped);
                if (permission.TryGetFirstDeniedCell(entity, footprint, out var deniedCell, out var denialReason))
                {
                    // One message for the whole write footprint, and which message depends on
                    // the reason - see the matching comment in CarryPickupService.
                    permission.HasPermissionAt(entity, deniedCell!, out _, showErrorMessage: true);
                    failureCode = denialReason.ToFailureCode();
                    return false;
                }
            }

            // Preflight rotation (client-side prediction parity): the client predicts its
            // children at these offsets, so the runtime spill branch below needs them to
            // reconcile the cell the client actually wrote.
            int estimatedSteps = 0;
            if (carriedBlock.HasAttachedBlocks)
            {
                estimatedSteps = CarryRotationHelper.EstimatePreflightRotation(carriedBlock, entity, selection, dropped);
                if (!PreflightAttachedBlocks(carriedBlock, world, selection.Position, estimatedSteps, ref failureCode))
                    return false;
            }

            if (!TryPlaceAtTarget(entity, carriedBlock, selection, dropped, ref failureCode)) return false;

            if (carriedBlock.HasAttachedBlocks)
            {
                var rotationSteps = CarryRotationHelper.GetActualRotationSteps(carriedBlock, world, selection.Position);
                PlaceAttachedChildren(world, entity, carriedBlock, selection.Position, rotationSteps, estimatedSteps);
            }

            FinalizePlacedBlock(world, entity, carriedBlock, selection.Position, dropped, playSound);
            return true;
        }

        private bool TryPlaceAtTarget(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, bool dropped, ref string failureCode)
        {
            if (entity is not EntityPlayer playerEntity) return false;

            if (!dropped)
                return TryPlaceDownAsPlayer(api.World, playerEntity, carriedBlock, selection, ref failureCode);

            // Dropped-path claims are gated by the whole-footprint preflight in
            // TryPlaceDown before any write; the direct placement proceeds unguarded.
            return TryPlaceDownDirect(api.World, carriedBlock, selection);
        }

        private bool PreflightAttachedBlocks(CarriedBlock carriedBlock, IWorldAccessor world, BlockPos parentPos, int rotationSteps, ref string failureCode)
        {
            if (carriedBlock.AttachedBlocks == null) return true;

            var clusterPositions = new HashSet<BlockPos> { parentPos.Copy() };

            for (int i = 0; i < carriedBlock.AttachedBlocks.Count; i++)
            {
                var rotatedOffset = CarryRotationHelper.RotateOffset(carriedBlock.AttachedBlocks[i].RelativeOffset, rotationSteps);
                var cPos = parentPos.Copy();
                cPos.Add(rotatedOffset);
                clusterPositions.Add(cPos);
            }

            foreach (var child in carriedBlock.AttachedBlocks)
            {
                if (child.OriginalLocalFace == null) continue;
                var rotatedOffset = CarryRotationHelper.RotateOffset(child.RelativeOffset, rotationSteps);
                var rotatedFace = CarryRotationHelper.RotateFacing(child.OriginalLocalFace, rotationSteps);
                var childPos = parentPos.Copy();
                childPos.Add(rotatedOffset);
                var supportPos = childPos.Copy();
                supportPos.Offset(rotatedFace);
                clusterPositions.Add(supportPos);
            }

            foreach (var child in carriedBlock.AttachedBlocks)
            {
                var rotatedOffset = CarryRotationHelper.RotateOffset(child.RelativeOffset, rotationSteps);
                var rotatedFace = child.OriginalLocalFace != null
                    ? CarryRotationHelper.RotateFacing(child.OriginalLocalFace, rotationSteps)
                    : null;

                var childPos = parentPos.Copy();
                childPos.Add(rotatedOffset);

                if (!world.BlockAccessor.IsValidPos(childPos))
                {
                    failureCode = FailureCodes.AttachedBlockNoClearance;
                    return false;
                }

                var existingBlock = world.BlockAccessor.GetBlock(childPos);
                if (!existingBlock.IsReplacableBy(child.Block))
                {
                    failureCode = FailureCodes.AttachedBlockNoClearance;
                    return false;
                }

                if (rotatedFace != null)
                {
                    var supportPos = childPos.Copy();
                    supportPos.Offset(rotatedFace);

                    if (!clusterPositions.Contains(supportPos))
                    {
                        var supportBlock = world.BlockAccessor.GetBlock(supportPos);
                        if (supportBlock.Id == 0 || supportBlock.IsReplacableBy(child.Block))
                        {
                            failureCode = FailureCodes.UnsupportedAttachment;
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private void PlaceAttachedChildren(
            IWorldAccessor world,
            Entity? entity,
            CarriedBlock carriedBlock,
            BlockPos parentPos,
            int rotationSteps,
            int estimatedSteps)
        {
            if (carriedBlock.AttachedBlocks == null) return;

            foreach (var child in carriedBlock.AttachedBlocks)
            {
                var rotatedOffset = CarryRotationHelper.RotateOffset(child.RelativeOffset, rotationSteps);
                var rotatedFace = child.OriginalLocalFace != null
                    ? CarryRotationHelper.RotateFacing(child.OriginalLocalFace, rotationSteps)
                    : null;

                var childPos = parentPos.Copy();
                childPos.Add(rotatedOffset);

                // Actual-rotation claim safety: the pre-gate checks children at the
                // estimated rotation; the placement's actual rotation (mesh angle or
                // facing suffix of the placed parent) can diverge. Never silently write
                // a child into a cell the player lacks permission for - skip the child
                // and spill it as an item entity at the parent cell, with an audit trace.
                if (world.Side == EnumAppSide.Server
                    && entity != null
                    && !permission.HasPermissionAt(entity, childPos, showErrorMessage: false))
                {
                    var dropVec3d = new Vec3d(parentPos.X + 0.5, parentPos.Y + 0.5, parentPos.Z + 0.5);
                    world.SpawnItemEntity(child.ItemStack, dropVec3d);
                    world.Logger.Audit(
                        $"[{ModId}] Player {entity.GetName()} placed down {carriedBlock.Block?.Code.GetName()} at {parentPos} "
                        + $"but lacked permission for attached child {child.Block.Code.GetName()} at {childPos}; child dropped as item");

                    // The client's prediction wrote this child at the ESTIMATED offset, and
                    // the parent placement succeeded, so the handler's WrittenCells reconcile
                    // never runs (placedAt == message.PlacedAt). Dirty the predicted cell -
                    // not childPos, which the server never wrote and the client never
                    // predicted - on both channels, so the phantom block and the phantom
                    // block entity the client created there are evicted.
                    if (world.Side == EnumAppSide.Server)
                    {
                        var predictedOffset = CarryRotationHelper.RotateOffset(child.RelativeOffset, estimatedSteps);
                        var predictedPos = parentPos.Copy();
                        predictedPos.Add(predictedOffset);
                        world.BlockAccessor.MarkBlockDirty(predictedPos);
                        world.BlockAccessor.MarkBlockEntityDirty(predictedPos);
                    }
                    continue;
                }

                var childBlock = child.Block;
                if (rotatedFace != null && child.OriginalBlockCode != null)
                {
                    var wallSign = CarryRotationHelper.GetRotatedVariantBlock(world, child.OriginalBlockCode, rotatedFace);
                    if (wallSign != null)
                        childBlock = wallSign;
                }

                world.BlockAccessor.SetBlock(childBlock.Id, childPos, child.ItemStack);

                if (child.BlockEntityData != null && world.Side == EnumAppSide.Server)
                {
                    child.BlockEntityData.SetInt(AttributeKeys.BlockEntityData.PosX, childPos.X);
                    child.BlockEntityData.SetInt(AttributeKeys.BlockEntityData.PosY, childPos.Y);
                    child.BlockEntityData.SetInt(AttributeKeys.BlockEntityData.PosZ, childPos.Z);

                    var blockEntity = world.BlockAccessor.GetBlockEntity(childPos);
                    blockEntity?.FromTreeAttributes(child.BlockEntityData, world);
                    blockEntity?.MarkDirty(true);
                }

                world.BlockAccessor.MarkBlockDirty(childPos);
            }
        }

        private bool TryPlaceDownAsPlayer(IWorldAccessor world, EntityPlayer playerEntity, CarriedBlock carriedBlock, BlockSelection selection, ref string failureCode)
        {
            var player = world.PlayerByUid(playerEntity.PlayerUID);
            var activeHotbarSlot = player?.InventoryManager?.ActiveHotbarSlot;
            if (player == null || activeHotbarSlot == null)
            {
                world.Logger.Error($"CarryOn: Failed to resolve player inventory while placing carried block at {selection.Position}. Falling back to direct placement.");
                return TryPlaceDownFallback(world, carriedBlock, selection);
            }

            // Item-class carry (e.g. a bag picked up from ground storage): deposits into an
            // existing ground storage or creates a new one through the shared placement
            // service, with all vanilla validation (layout, capacity, wall support).
            if (carriedBlock.Block == null)
                return TryPlaceDownGroundStorableItem(playerEntity, carriedBlock, selection);

            var shift = playerEntity.Controls.ShiftKey;
            var ctrl = playerEntity.Controls.CtrlKey;

            try
            {
                activeHotbarSlot.Itemstack = carriedBlock.ItemStack;

                playerEntity.Controls.ShiftKey = true;
                playerEntity.Controls.CtrlKey = false;

                return carriedBlock.Block.TryPlaceBlock(world, player, carriedBlock.ItemStack, selection, ref failureCode);
            }
            finally
            {
                playerEntity.Controls.ShiftKey = shift;
                playerEntity.Controls.CtrlKey = ctrl;
                activeHotbarSlot.Itemstack = null;
            }
        }

        /// <summary>
        /// Places an item-class carried stack (e.g. a bag picked up from ground storage) into
        /// the ground storage targeted by the selection - depositing into an existing storage
        /// or creating a new one via <see cref="GroundStorageItemPlacement"/> - and finalizes
        /// the carry against the storage cell: dirty mark, carry removal and audit. The
        /// storage's own place/remove sound is played by the placement itself.
        /// </summary>
        private bool TryPlaceDownGroundStorableItem(EntityPlayer playerEntity, CarriedBlock carriedBlock, BlockSelection selection)
        {
            if (!groundStorage.TryPlaceAtSelection(playerEntity, carriedBlock, selection, out var placedAt))
                return false;

            var world = api.World;

            if (placedAt != null)
            {
                world.BlockAccessor.MarkBlockDirty(placedAt);
                world.BlockAccessor.TriggerNeighbourBlockUpdate(placedAt);
            }

            carryManager.RemoveCarried(playerEntity, carriedBlock.Slot);

            if (world.Side == EnumAppSide.Server)
            {
                var entityName = playerEntity.GetName() ?? "Unknown Entity";
                var carriedName = CarryCodes.DisplayName(carriedBlock.ItemStack?.Collectible?.Code?.ToString());
                api.World.Logger.Audit($"[{ModId}] Player {entityName} placed down block {carriedName} at {placedAt}");
            }

            return true;
        }

        private bool TryPlaceDownFallback(IWorldAccessor world, CarriedBlock carriedBlock, BlockSelection selection)
        {
            if (carriedBlock.Block == null || carriedBlock.ItemStack == null) return false;

            world.BlockAccessor.SetBlock(carriedBlock.Block.Id, selection.Position, carriedBlock.ItemStack);
            return true;
        }

        private bool TryPlaceDownDirect(IWorldAccessor world, CarriedBlock carriedBlock, BlockSelection selection)
        {
            var meshFacing = selection.Face;
            var droppedBlock = carriedBlock.Block;

            if (meshFacing != null)
            {
                var assetLocation = carriedBlock.Block.Code.Clone();
                var baseCode = assetLocation.FirstCodePart();
                assetLocation.Path = $"{baseCode}-{meshFacing.Code}";
                droppedBlock = world.GetBlock(assetLocation) ?? carriedBlock.Block;
            }

            // SetBlock (not ExchangeBlock) so the synchronous behavior sweep runs -
            // OnBlockRemoved for the replaced replacable block and OnBlockPlaced for the
            // placed one, which places the multiblock dummy cells exactly like vanilla
            // item placement. The claim preflight in TryPlaceDown gates exactly this
            // write set; a dropped trunk that wrote only its controller cell would both
            // break the trunk and make the footprint check lie.
            world.BlockAccessor.SetBlock(droppedBlock.Id, selection.Position, carriedBlock.ItemStack);

            if (droppedBlock.EntityClass != null)
            {
                world.BlockAccessor.SpawnBlockEntity(droppedBlock.EntityClass, selection.Position, carriedBlock.ItemStack);
            }

            droppedBlock.OnBlockPlaced(world, selection.Position, carriedBlock.ItemStack);

            if (meshFacing != null)
            {
                var meshAngle = -CarryRotationHelper.GetMeshAngle(meshFacing);
                carriedBlock.BlockEntityData?.SetFloat(AttributeKeys.BlockEntityData.MeshAngle, meshAngle);

                var worldBE = world.BlockAccessor.GetBlockEntity(selection.Position);
                if (worldBE != null)
                {
                    var tempAttr = new TreeAttribute();
                    worldBE.ToTreeAttributes(tempAttr);
                    tempAttr.SetFloat(AttributeKeys.BlockEntityData.MeshAngle, meshAngle);
                    worldBE.FromTreeAttributes(tempAttr, world);
                    worldBE.MarkDirty(true);
                }
            }

            return true;
        }

        private void FinalizePlacedBlock(IWorldAccessor world, Entity entity, CarriedBlock carriedBlock, BlockPos position, bool dropped, bool playSound)
        {
            RestoreBlockEntityData(world, carriedBlock, position, dropped: dropped);

            world.BlockAccessor.MarkBlockDirty(position);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(position);

            carryManager.RemoveCarried(entity, carriedBlock.Slot);

            // Item-class carries (e.g. bags) never reach this finalize - they finalize inside
            // TryPlaceDownGroundStorableItem against the storage cell, where the storage's
            // own place/remove sound also plays. Block carries play the carried block's
            // place sound here; the null-Block guard stays as defense.
            if (playSound && carriedBlock.Block != null)
                SoundHelper.PlaySound(api, carriedBlock.Block, position, dropped ? null : entity as EntityPlayer);

            if (dropped)
                carryManager.CarryEvents?.TriggerBlockDropped(position, entity, carriedBlock);

            if (world.Side == EnumAppSide.Server)
            {
                var entityName = entity?.GetName() ?? "Unknown Entity";
                var carriedName = carriedBlock.Block != null
                    ? carriedBlock.Block.Code.GetName()
                    : CarryCodes.DisplayName(carriedBlock.ItemStack?.Collectible?.Code?.ToString());
                api.World.Logger.Audit($"[{ModId}] Player {entityName}  {(dropped ? "dropped" : "placed down")}  block {carriedName} at {position}");
            }
        }

        public void RestoreBlockEntityData(IWorldAccessor world, CarriedBlock carriedBlock, BlockPos pos, bool dropped = false)
        {
            if ((world.Side != EnumAppSide.Server) || (carriedBlock?.BlockEntityData == null)) return;

            var blockEntityData = carriedBlock.BlockEntityData;
            blockEntityData.SetInt(AttributeKeys.BlockEntityData.PosX, pos.X);
            blockEntityData.SetInt(AttributeKeys.BlockEntityData.PosY, pos.Y);
            blockEntityData.SetInt(AttributeKeys.BlockEntityData.PosZ, pos.Z);

            var blockEntity = world.BlockAccessor.GetBlockEntity(pos);

            carryManager.CarryEvents?.TriggerBeforeRestoreBlockEntityData(blockEntity, blockEntityData, dropped);

            blockEntity?.FromTreeAttributes(blockEntityData, world);
            blockEntity?.MarkDirty(true);
        }

        public bool TryPlaceDownAt(IPlayer player, CarrySlot carrySlot, BlockSelection selection, out BlockPos? placedAt)
        {
            string failureCode = FailureCodes.Ignore;
            return TryPlaceDownAt(player, carrySlot, selection, out placedAt, ref failureCode);
        }

        public bool TryPlaceDownAt(IPlayer player, CarrySlot carrySlot, BlockSelection selection, out BlockPos? placedAt, ref string failureCode)
        {
            placedAt = null;
            var entity = player.Entity;
            if (entity == null) return false;

            var blockSelection = selection.Clone();
            var selectedBlock = entity.World?.BlockAccessor?.GetBlock(blockSelection.Position);

            if (selectedBlock == null) return false;

            var readResult = carryManager.GetCarriedReadResult(entity, carrySlot, out var rawTree);
            if (readResult.IsFailed && readResult.Reason == FailureCodes.MissingBlock)
            {
                // The carried block's code is no longer registered (its mod was removed or
                // disabled). It must never be placed as a missing-texture block: on the server,
                // drop the carried block as a CarriedBlockEntity (the untouched raw tree preserves
                // stack + block entity data + attached children) so it can be recovered after the
                // mod is re-enabled, then clear the slot. The client mirrors the failure so no
                // placement is attempted prediction-side either.
                if (entity.World is { Side: EnumAppSide.Server } && rawTree != null)
                {
                    UnusableCarriedBlockDrop.TryDrop(api, carryManager, entity, carrySlot, rawTree, entity.Pos.XYZ);
                }

                failureCode = FailureCodes.CarriedBlockMissing;
                return false;
            }

            var carried = readResult.Carried;
            if (carried == null)
            {
                failureCode = FailureCodes.NotCarrying;
                return false;
            }

            // Check entity spawn BEFORE modifying the selection.
            // The original selection has the player's exact crosshair target.
            if (TrySpawnAsEntity(entity, carried, blockSelection, dropped: false))
            {
                placedAt = blockSelection.Position;
                return true;
            }

            // Item-class carry (e.g. a bag picked up from ground storage): no Block to place.
            // The anchor cell mirrors the vanilla ground-storable decision (existing storage
            // at the clicked cell or above it, else a new storage above an upward click) and
            // the selection stays as the player aimed it. Server-authoritative: the client
            // computes only the anchor cell - the storage mutation (inventory transfer,
            // storage block) happens exclusively on the server, so no client-side ghost
            // storage can appear, and the carry state clears through the watched-attribute
            // sync once the server removes it.
            if (carried.Block == null)
            {
                placedAt = BlockUtils.GetGroundStoragePlacementPosition(entity.World?.BlockAccessor, blockSelection);
                if (placedAt == null) return false;
                if (api.Side == EnumAppSide.Client) return true;
                return TryPlaceDown(entity, carried, blockSelection, ref failureCode);
            }

            if (selectedBlock.IsReplacableBy(carried.Block))
            {
                blockSelection.Face = BlockFacing.UP;
                blockSelection.HitPosition.Y = 0.5;
            }
            else
            {
                blockSelection.Position.Offset(blockSelection.Face);
                blockSelection.DidOffset = true;
            }

            placedAt = blockSelection.Position;
            return TryPlaceDown(entity, carried, blockSelection, ref failureCode);
        }

        private bool TrySpawnAsEntity(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, bool dropped)
        {
            var hitPos = selection.HitPosition ?? new Vec3d(0.5, 0.5, 0.5);
            var position = new Vec3d(
                selection.Position.X + hitPos.X,
                selection.Position.Y + hitPos.Y,
                selection.Position.Z + hitPos.Z);

            var context = new EntitySpawnContext
            {
                CarriedBlock = carriedBlock,
                OwnerUid = (entity as EntityPlayer)?.Player?.PlayerUID ?? "unknown",
                Position = position,
                BlockPosition = selection.Position,
                HitPosition = selection.HitPosition,
                Face = selection.Face,
                Dropped = dropped,
                Api = api,
            };

            if (EntityHandlerSpawner.TrySpawn(carryManager, entity, context))
            {
                if (dropped)
                    carryManager.CarryEvents?.TriggerBlockDropped(selection.Position, entity, carriedBlock);
                return true;
            }

            // When the factory is applicable (ShouldSpawnAsEntity == true)
            // but the actual spawn failed - e.g. not enough space - prevent
            // block placement on both sides. The carry is retained because
            // RemoveCarried is only called on a successful spawn. On the
            // client, TrySpawn always returns false (it can't spawn), so
            // this guard is what stops the client from placing a block.
            if (EntityHandlerSpawner.IsEntitySpawnApplicable(api, carryManager, carriedBlock, dropped))
            {
                return true;
            }

            return false;
        }

    }
}
