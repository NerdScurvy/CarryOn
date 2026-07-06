using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Logic;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static CarryOn.Common.Models.CarryCode;

namespace CarryOn.Common.Services
{
    internal sealed class CarryPlacementService(ICoreAPI api, ICarryManager carryManager)
    {
        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, bool dropped = false, bool playSound = true)
        {
            string failureCode = FailureCode.Ignore;
            return TryPlaceDown(entity, carriedBlock, selection, ref failureCode, dropped, playSound);
        }

        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, ref string failureCode, bool dropped = false, bool playSound = true)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(selection);

            failureCode ??= FailureCode.Ignore;

            if (carriedBlock == null)
            {
                failureCode = FailureCode.NotCarrying;
                return false;
            }

            var world = api.World;

            if (carriedBlock?.Block == null || carriedBlock.ItemStack == null) return false;
            if (!world.BlockAccessor.IsValidPos(selection.Position)) return false;

            if (carriedBlock.HasAttachedBlocks)
            {
                var estimatedSteps = CarryRotationHelper.EstimatePreflightRotation(carriedBlock, entity, selection, dropped);
                if (!PreflightAttachedBlocks(carriedBlock, world, selection.Position, estimatedSteps, ref failureCode))
                    return false;
            }

            bool placed = false;
            if (entity is EntityPlayer playerEntity)
            {
                if (!dropped)
                {
                    placed = TryPlaceDownAsPlayer(world, playerEntity, carriedBlock, selection, ref failureCode);
                }
                else
                {
                    if (!carryManager.HasPermissionAt(entity, selection.Position, showErrorMessage: false))
                    {
                        failureCode = FailureCode.NoPermission;
                    }
                    else
                    {
                        placed = TryPlaceDownDirect(world, carriedBlock, selection);
                    }
                }
            }

            if (!placed) return false;

            if (carriedBlock.HasAttachedBlocks)
            {
                var rotationSteps = CarryRotationHelper.GetActualRotationSteps(carriedBlock, world, selection.Position);
                PlaceAttachedChildren(world, carriedBlock, selection.Position, rotationSteps);
            }

            FinalizePlacedBlock(world, entity, carriedBlock, selection.Position, dropped, playSound);
            return true;
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
                    failureCode = FailureCode.AttachedBlockNoClearance;
                    return false;
                }

                var existingBlock = world.BlockAccessor.GetBlock(childPos);
                if (!existingBlock.IsReplacableBy(child.Block))
                {
                    failureCode = FailureCode.AttachedBlockNoClearance;
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
                            failureCode = FailureCode.UnsupportedAttachment;
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private void PlaceAttachedChildren(IWorldAccessor world, CarriedBlock carriedBlock, BlockPos parentPos, int rotationSteps)
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

                var childBlock = child.Block;
                if (rotatedFace != null && child.OriginalBlockCode != null)
                {
                    var wallSign = CarryRotationHelper.GetWallSignForFacing(world, child.OriginalBlockCode, rotatedFace);
                    if (wallSign != null)
                        childBlock = wallSign;
                }

                world.BlockAccessor.SetBlock(childBlock.Id, childPos, child.ItemStack);

                if (child.BlockEntityData != null && world.Side == EnumAppSide.Server)
                {
                    child.BlockEntityData.SetInt("posx", childPos.X);
                    child.BlockEntityData.SetInt("posy", childPos.Y);
                    child.BlockEntityData.SetInt("posz", childPos.Z);

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

        private bool TryPlaceDownFallback(IWorldAccessor world, CarriedBlock carriedBlock, BlockSelection selection)
        {
            if (carriedBlock?.Block == null || carriedBlock.ItemStack == null) return false;

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

            world.BlockAccessor.ExchangeBlock(droppedBlock.Id, selection.Position);

            if (droppedBlock.EntityClass != null)
            {
                world.BlockAccessor.SpawnBlockEntity(droppedBlock.EntityClass, selection.Position, carriedBlock.ItemStack);
            }

            droppedBlock.OnBlockPlaced(world, selection.Position, carriedBlock.ItemStack);

            if (meshFacing != null)
            {
                var meshAngle = -CarryRotationHelper.GetMeshAngle(meshFacing);
                carriedBlock.BlockEntityData?.SetFloat("meshAngle", meshAngle);

                var worldBE = world.BlockAccessor.GetBlockEntity(selection.Position);
                if (worldBE != null)
                {
                    var tempAttr = new TreeAttribute();
                    worldBE.ToTreeAttributes(tempAttr);
                    tempAttr.SetFloat("meshAngle", meshAngle);
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
            if (playSound)
                SoundHelper.PlaySound(api, carriedBlock.Block, position, dropped ? null : entity as EntityPlayer);

            if (dropped)
                carryManager.CarryEvents?.TriggerBlockDropped(position, entity, carriedBlock);

            if (world.Side == EnumAppSide.Server)
            {
                var entityName = entity?.GetName() ?? "Unknown Entity";
                api.World.Logger.Audit($"[{ModId}] Player {entityName}  {(dropped ? "dropped" : "placed down")}  block {carriedBlock?.Block?.Code.GetName()} at {position}");
            }
        }

        public void RestoreBlockEntityData(IWorldAccessor world, CarriedBlock carriedBlock, BlockPos pos, bool dropped = false)
        {
            if ((world.Side != EnumAppSide.Server) || (carriedBlock?.BlockEntityData == null)) return;

            var blockEntityData = carriedBlock.BlockEntityData;
            blockEntityData.SetInt("posx", pos.X);
            blockEntityData.SetInt("posy", pos.Y);
            blockEntityData.SetInt("posz", pos.Z);

            var blockEntity = world.BlockAccessor.GetBlockEntity(pos);

            carryManager.CarryEvents?.TriggerBeforeRestoreBlockEntityData(blockEntity, blockEntityData, dropped);

            blockEntity?.FromTreeAttributes(blockEntityData, world);
            blockEntity?.MarkDirty(true);
        }

        public bool TryPlaceDownAt(IPlayer player, CarrySlot carrySlot, BlockSelection selection, out BlockPos? placedAt)
        {
            string failureCode = FailureCode.Ignore;
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

            var carried = carryManager.GetCarried(entity, carrySlot);
            if (carried == null)
            {
                failureCode = FailureCode.NotCarrying;
                return false;
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

    }
}
