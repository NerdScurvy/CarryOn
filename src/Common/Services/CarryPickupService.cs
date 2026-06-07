using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Logic;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Services
{
    internal sealed class CarryPickupService(ICoreAPI api, ICarryManager carryManager)
    {
        private const int MultiblockScanRadius = 5;
        public bool HasPermissionToCarry(Entity entity, BlockPos pos)
        {
            var isReinforced = entity.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.IsReinforced(pos) ?? false;
            if (entity is EntityPlayer playerEntity)
            {
                var result = entity.World.GetCarryEvents()?.TriggerCheckPermissionToCarry(playerEntity, pos, isReinforced);
                if (result.HasValue) return result.Value;

                var isCreative = playerEntity.Player.WorldData.CurrentGameMode == EnumGameMode.Creative;
                if (!isCreative && isReinforced) return false;

                return entity.World.Claims.TryAccess(playerEntity.Player, pos, EnumBlockAccessFlags.BuildOrBreak);
            }
            else
            {
                return !isReinforced;
            }
        }

        private bool CanPickUp(Entity entity, BlockPos pos, CarrySlot slot, CarriedBlock carried, ref string failureCode)
        {
            return carryManager.CarryEvents?.TriggerBeforePickUpBlock(entity, pos, slot, carried, ref failureCode) ?? true;
        }

        public CarriedBlock? GetCarriedFromWorld(BlockPos pos, CarrySlot slot, bool checkIsCarryable = false)
        {
            string failureCode = FailureCode.Ignore;
            return GetCarriedFromWorld(null, pos, slot, ref failureCode, checkIsCarryable);
        }

        public CarriedBlock? GetCarriedFromWorld(Entity? entity, BlockPos pos, CarrySlot slot, ref string failureCode, bool checkIsCarryable = false)
        {
            var world = api.World;
            var carried = BlockUtils.CreateCarriedFromBlockPos(world, pos, slot);
            if (carried == null) return null;

            if (checkIsCarryable && !carryManager.IsCarryable(carried.Block, slot)) return null;

            if (entity != null && !CanPickUp(entity, pos, slot, carried, ref failureCode)) return null;

            if (carryManager.Config?.CarryOptions?.CarryAttachedWallSigns == true)
            {
                var attachedBlocks = CaptureAttachedSigns(world, pos, carried.Block);
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
            bool playSound = true)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(pos);

            failureCode ??= FailureCode.Ignore;

            if (entity.GetCarried(slot) != null)
            {
                failureCode = FailureCode.AlreadyCarrying;
                return false;
            }

            if (entity.Api.Side == EnumAppSide.Server && !HasPermissionToCarry(entity, pos))
            {
                failureCode = FailureCode.NoPermission;
                return false;
            }

            var block = entity.World.BlockAccessor.GetBlock(pos);
            if (checkIsCarryable && !carryManager.IsCarryable(block, slot))
            {
                failureCode = FailureCode.NotCarryable;
                return false;
            }

            var carryBehavior = block.GetBehavior<BlockBehaviorCarryable>();
            if (entity.Api.Side == EnumAppSide.Client && carryBehavior != null && !carryBehavior.OptimisticPickup)
                return true;

            var optimisticPickup = carryBehavior?.OptimisticPickup ?? true;
            var carried = GetCarriedFromWorld(entity, pos, slot, ref failureCode, checkIsCarryable);
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

        public bool TryPickUp(Entity entity, BlockPos pos, CarrySlot slot, bool checkIsCarryable = true, bool playSound = true)
        {
            string failureCode = FailureCode.Ignore;
            return TryPickUp(entity, pos, slot, ref failureCode, checkIsCarryable, playSound);
        }

        private List<AttachedCarriedBlock>? CaptureAttachedSigns(IWorldAccessor world, BlockPos pos, Block parentBlock)
        {
            var footprint = GetBlockFootprint(world, pos, parentBlock);
            if (footprint.Count == 0) return null;

            var attached = new List<AttachedCarriedBlock>();
            var capturedPositions = new HashSet<BlockPos>();

            foreach (var footPos in footprint)
            {
                foreach (var face in BlockFacing.HORIZONTALS)
                {
                    var candidatePos = footPos.Copy();
                    candidatePos.Offset(face);
                    if (footprint.Contains(candidatePos) || capturedPositions.Contains(candidatePos))
                        continue;

                    var candidateBlock = world.BlockAccessor.GetBlock(candidatePos);
                    if (candidateBlock.Id == 0) continue;
                    if (candidateBlock.Class != "BlockSign") continue;

                    if (!IsWallSign(candidateBlock)) continue;

                    var signFacing = GetWallSignFacing(candidateBlock);
                    if (signFacing == null) continue;

                    var attachedToPos = candidatePos.Copy();
                    attachedToPos.Offset(signFacing);
                    if (!footprint.Contains(attachedToPos)) continue;

                    var originalCode = candidateBlock.Code;
                    var childStack = candidateBlock.OnPickBlock(world, candidatePos) ?? new ItemStack(candidateBlock);

                    ITreeAttribute? childData = null;
                    var childEntity = world.BlockAccessor.GetBlockEntity(candidatePos);
                    if (childEntity != null)
                    {
                        childData = new TreeAttribute();
                        childEntity.ToTreeAttributes(childData);
                        childData = (ITreeAttribute)childData.Clone();
                        childData.RemoveAttribute("posx");
                        childData.RemoveAttribute("posy");
                        childData.RemoveAttribute("posz");
                        childData.RemoveAttribute("meshAngle");
                    }

                    var relativeOffset = new BlockPos(
                        candidatePos.X - pos.X,
                        candidatePos.Y - pos.Y,
                        candidatePos.Z - pos.Z
                    );

                    var childCarried = new CarriedBlock(CarrySlot.Attached, childStack, childData, null, originalCode);
                    attached.Add(new AttachedCarriedBlock(relativeOffset, childCarried, signFacing));
                    capturedPositions.Add(candidatePos);
                }
            }

            return attached.Count > 0 ? attached : null;
        }

        private static HashSet<BlockPos> GetBlockFootprint(IWorldAccessor world, BlockPos pos, Block parentBlock)
        {
            var footprint = new HashSet<BlockPos>();

            BlockPos? origin = parentBlock is BlockMultiblock parentMulti
                ? pos.Copy().Add(parentMulti.OffsetInv)
                : parentBlock.HasBehavior<BlockBehaviorMultiblock>()
                    ? pos
                    : null;

            if (origin != null)
            {
                for (int dx = -MultiblockScanRadius; dx <= MultiblockScanRadius; dx++)
                for (int dy = -MultiblockScanRadius; dy <= MultiblockScanRadius; dy++)
                for (int dz = -MultiblockScanRadius; dz <= MultiblockScanRadius; dz++)
                {
                    var checkPos = new BlockPos(origin.X + dx, origin.Y + dy, origin.Z + dz);
                    var checkBlock = world.BlockAccessor.GetBlock(checkPos);
                    if (checkBlock is BlockMultiblock checkMulti)
                    {
                        var checkOrigin = checkPos.Copy();
                        checkOrigin.Add(checkMulti.OffsetInv);
                        if (checkOrigin.Equals(origin))
                        {
                            footprint.Add(checkPos.Copy());
                        }
                    }
                }
            }

            footprint.Add(pos.Copy());
            return footprint;
        }

        private static bool IsWallSign(Block block)
        {
            if (block.Variant == null) return false;
            return block.Variant.TryGetValue("attachment", out var attachment)
                && attachment == "wall";
        }

        private static BlockFacing? GetWallSignFacing(Block block)
        {
            var path = block.Code.Path;
            var parts = path.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            var lastPart = parts[parts.Length - 1];
            return BlockFacing.FromCode(lastPart);
        }

    }
}
