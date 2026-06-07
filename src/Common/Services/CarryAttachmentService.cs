using System;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Services
{
    internal sealed class CarryAttachmentService(ICoreAPI api, CarryOnConfig config, ICarryManager carryManager)
    {
        private const string MountedBagInventoryPrefix = "mountedbaginv";

        /// <summary>
        /// Attempts to attach the carried block in hands to a target entity slot.
        /// </summary>
        /// <param name="player">Acting server player.</param>
        /// <param name="targetEntityId">Target entity id.</param>
        /// <param name="slotIndex">Attachable selection-box slot index.</param>
        /// <param name="playSound">Whether to play attach audio on success.</param>
        /// <returns>True when attach succeeds; otherwise false.</returns>
        public bool TryAttach(IServerPlayer player, long targetEntityId, int slotIndex, bool playSound = true)
        {
            string failureCode = FailureCode.Ignore;
            return TryAttach(player, targetEntityId, slotIndex, ref failureCode, playSound);
        }

        /// <summary>
        /// Attempts to attach the carried block in hands to a target entity slot.
        /// </summary>
        /// <param name="player">Acting server player.</param>
        /// <param name="targetEntityId">Target entity id.</param>
        /// <param name="slotIndex">Attachable selection-box slot index.</param>
        /// <param name="failureCode">Failure code output when attach fails.</param>
        /// <param name="playSound">Whether to play attach audio on success.</param>
        /// <returns>True when attach succeeds; otherwise false.</returns>
        public bool TryAttach(IServerPlayer player, long targetEntityId, int slotIndex, ref string failureCode, bool playSound = true)
        {
            if (slotIndex < 0)
            {
                failureCode = FailureCode.SlotNotFound;
                return false;
            }

            var context = ResolveAttachDetachContext(player, targetEntityId, ref failureCode);
            if (context == null) return false;

            var (world, targetEntity, attachableBehavior) = context;
            var carriedBlock = player.Entity.GetCarried(CarrySlot.Hands);
            if (carriedBlock == null)
            {
                return false;
            }

            var blockEntityData = carriedBlock.BlockEntityData;
            if (blockEntityData == null)
            {
                failureCode = FailureCode.SlotDataMissing;
                return false;
            }

            var targetSlot = attachableBehavior.GetSlotFromSelectionBoxIndex(slotIndex);
            var apname = targetEntity.GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes?[slotIndex]?.AttachPoint?.Code;

            var seatableBehavior = targetEntity.GetBehavior<EntityBehaviorSeatable>();
            bool isOccupied = false;
            if (seatableBehavior != null)
            {
                var seatId = seatableBehavior.SeatConfigs.FirstOrDefault(s => s.APName == apname)?.SeatId;
                isOccupied = seatableBehavior.Seats.FirstOrDefault(s => s.SeatId == seatId)?.Passenger != null;
            }

            if (targetSlot == null || !targetSlot.Empty || isOccupied)
            {
                failureCode = FailureCode.SlotNotEmpty;
                return false;
            }

            var sourceItemSlot = (ItemSlot)new DummySlot(null);
            sourceItemSlot.Itemstack = carriedBlock.ItemStack.Clone();
            if (sourceItemSlot.Itemstack?.Attributes is not TreeAttribute attr)
            {
                failureCode = FailureCode.SlotDataMissing;
                return false;
            }

            var backupAttributes = blockEntityData.Clone();
            backupAttributes.RemoveAttribute("inventory");

            attr.SetString("type", blockEntityData.GetString("type"));
            var backpack = BlockUtils.ConvertBlockInventoryToBackpack(blockEntityData.GetTreeAttribute("inventory"));
            attr.SetAttribute("backpack", backpack);
            attr.SetAttribute("carryonbackup", backupAttributes);

            if (!targetSlot.CanTakeFrom(sourceItemSlot))
            {
                failureCode = FailureCode.SlotIncompatibleBlock;
                return false;
            }

            var carryableBehavior = sourceItemSlot.Itemstack.Block.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior?.PreventAttaching ?? false)
            {
                failureCode = FailureCode.SlotPreventAttaching;
                return false;
            }

            var iai = sourceItemSlot.Itemstack.Collectible.GetCollectibleInterface<IAttachedInteractions>();
            if (iai?.OnTryAttach(sourceItemSlot, slotIndex, targetEntity) == false)
            {
                failureCode = FailureCode.AttachUnavailable;
                return false;
            }

            var moved = sourceItemSlot.TryPutInto(targetEntity.World, targetSlot) > 0;
            if (!moved)
            {
                failureCode = FailureCode.AttachFailed;
                return false;
            }

            attachableBehavior.storeInv();
            targetEntity.MarkShapeModified();
            targetEntity.World.BlockAccessor.GetChunkAtBlockPos(targetEntity.Pos.AsBlockPos).MarkModified();

            carryManager.RemoveCarried(player.Entity, CarrySlot.Hands);

            if (playSound)
            {
                PlayPlaceSoundAt(carriedBlock.Block, targetEntity);
            }

            world.Logger.Audit($"[{ModId}] Player {player?.PlayerName} attached block {carriedBlock.Block.Code} to entity {targetEntity.EntityId} {targetEntity.GetName()} slot {slotIndex} at position {targetEntity.Pos.AsBlockPos}");
            return true;
        }

        /// <summary>
        /// Attempts to detach a carryable block from a target entity slot to player hands.
        /// </summary>
        /// <param name="player">Acting server player.</param>
        /// <param name="targetEntityId">Target entity id.</param>
        /// <param name="slotIndex">Attachable selection-box slot index.</param>
        /// <param name="playSound">Whether to play detach audio on success.</param>
        /// <returns>True when detach succeeds; otherwise false.</returns>
        public bool TryDetach(IServerPlayer player, long targetEntityId, int slotIndex, bool playSound = true)
        {
            string failureCode = FailureCode.Ignore;
            return TryDetach(player, targetEntityId, slotIndex, ref failureCode, playSound);
        }

        /// <summary>
        /// Attempts to detach a carryable block from a target entity slot to player hands.
        /// </summary>
        /// <param name="player">Acting server player.</param>
        /// <param name="targetEntityId">Target entity id.</param>
        /// <param name="slotIndex">Attachable selection-box slot index.</param>
        /// <param name="failureCode">Failure code output when detach fails.</param>
        /// <param name="playSound">Whether to play detach audio on success.</param>
        /// <returns>True when detach succeeds; otherwise false.</returns>
        public bool TryDetach(IServerPlayer player, long targetEntityId, int slotIndex, ref string failureCode, bool playSound = true)
        {
            var context = ResolveAttachDetachContext(player, targetEntityId, ref failureCode);
            if (context == null) return false;

            var (world, targetEntity, attachableBehavior) = context;
            var sourceSlot = attachableBehavior.GetSlotFromSelectionBoxIndex(slotIndex);
            if (sourceSlot == null || sourceSlot.Empty)
            {
                failureCode = FailureCode.SlotEmpty;
                return false;
            }

            if (!sourceSlot.CanTake())
            {
                failureCode = FailureCode.DetachUnavailable;
                return false;
            }

            var block = sourceSlot.Itemstack?.Block;
            if (block == null)
            {
                return false;
            }

            if (!block.HasBehavior<BlockBehaviorCarryable>())
            {
                failureCode = FailureCode.SlotNotCarryable;
                return false;
            }

            var inventoryName = $"{MountedBagInventoryPrefix}-{slotIndex}-{targetEntityId}";
            var hasOpenBoatStorage = world.AllOnlinePlayers
                .OfType<IServerPlayer>()
                .Where(serverPlayer => serverPlayer.PlayerUID != player.PlayerUID)
                .SelectMany(serverPlayer => serverPlayer.InventoryManager.OpenedInventories)
                .Any(inv => inv.InventoryID.StartsWith(inventoryName));

            if (hasOpenBoatStorage)
            {
                failureCode = FailureCode.SlotInventoryOpen;
                return false;
            }

            var carriedInHands = player.Entity.GetCarried(CarrySlot.Hands);
            if (carriedInHands != null)
            {
                return false;
            }

            var itemstack = sourceSlot.Itemstack;
            if (itemstack == null)
            {
                return false;
            }

            var sourceBackpack = itemstack.Attributes?["backpack"] as ITreeAttribute;
            var destInventory = BlockUtils.ConvertBackpackToBlockInventory(sourceBackpack);

            TreeAttribute blockEntityData;
            if (itemstack.Attributes?["carryonbackup"] is not TreeAttribute backupAttributes)
            {
                blockEntityData = new TreeAttribute();
            }
            else
            {
                blockEntityData = backupAttributes;
            }

            blockEntityData.SetString("blockCode", block.Code.ToShortString());
            blockEntityData.SetAttribute("inventory", destInventory);
            blockEntityData.SetString("forBlockCode", block.Code.ToShortString());
            var itemAttributes = itemstack.Attributes;
            var itemType = itemAttributes != null ? itemAttributes.GetString("type") : string.Empty;
            blockEntityData.SetString("type", itemType ?? string.Empty);

            var itemstackCopy = itemstack.Clone();
            itemstackCopy.Attributes.Remove("backpack");

            var carriedBlock = new CarriedBlock(CarrySlot.Hands, itemstackCopy, blockEntityData);
            carryManager.SetCarried(player.Entity, carriedBlock);

            if (playSound)
            {
                PlayPlaceSoundAt(block, targetEntity);
            }

            itemstack.Collectible.GetCollectibleInterface<IAttachedListener>()?.OnDetached(sourceSlot, slotIndex, targetEntity, player.Entity);

            EntityBehaviorAttachableCarryable.ClearCachedSlotStorage(api, slotIndex, sourceSlot, targetEntity);
            sourceSlot.Itemstack = null;
            attachableBehavior.storeInv();

            targetEntity.MarkShapeModified();
            targetEntity.World.BlockAccessor.GetChunkAtBlockPos(targetEntity.Pos.AsBlockPos).MarkModified();
            world.Logger.Audit($"[{ModId}] Player {player?.PlayerName} detached block {block.Code} from entity {targetEntity.EntityId} {targetEntity.GetName()} slot {slotIndex} at position {targetEntity.Pos.AsBlockPos}");

            return true;
        }

        private sealed record AttachDetachContext(IWorldAccessor World, Entity TargetEntity, EntityBehaviorAttachable AttachableBehavior);

        private AttachDetachContext? ResolveAttachDetachContext(IServerPlayer player, long targetEntityId, ref string failureCode)
        {
            ArgumentNullException.ThrowIfNull(player);

            failureCode ??= FailureCode.Ignore;

            var validation = ValidateAttachDetachTarget(player, targetEntityId);
            if (!validation.IsValid)
            {
                if (validation.FailureCode != null)
                {
                    failureCode = validation.FailureCode;
                }
                return null;
            }

            var targetEntity = validation.TargetEntity;
            var attachableBehavior = validation.AttachableBehavior;

            if (targetEntity == null || attachableBehavior == null)
            {
                return null;
            }

            return new AttachDetachContext(api.World, targetEntity, attachableBehavior);
        }

        private sealed class AttachTargetValidationResult
        {
            public bool IsValid { get; }
            public string? FailureCode { get; }
            public Entity? TargetEntity { get; }
            public EntityBehaviorAttachable? AttachableBehavior { get; }

            private AttachTargetValidationResult(bool isValid, string? failureCode, Entity? targetEntity, EntityBehaviorAttachable? attachableBehavior)
            {
                IsValid = isValid;
                FailureCode = failureCode;
                TargetEntity = targetEntity;
                AttachableBehavior = attachableBehavior;
            }

            public static AttachTargetValidationResult Fail(string? failureCode = null)
                => new(false, failureCode, null, null);

            public static AttachTargetValidationResult Success(Entity targetEntity, EntityBehaviorAttachable attachableBehavior)
                => new(true, null, targetEntity, attachableBehavior);
        }

        private AttachTargetValidationResult ValidateAttachDetachTarget(IServerPlayer player, long targetEntityId)
        {
            var world = api.World;
            var targetEntity = world.GetEntityById(targetEntityId);
            if (targetEntity == null)
            {
                return AttachTargetValidationResult.Fail(FailureCode.EntityNotFound);
            }

            if (targetEntity.Pos?.DistanceTo(player.Entity.Pos) > GetMaxInteractionDistance())
            {
                return AttachTargetValidationResult.Fail(FailureCode.EntityOutOfReach);
            }

            var attachableBehavior = targetEntity.GetBehavior<EntityBehaviorAttachable>();
            if (attachableBehavior == null)
            {
                // Preserve existing behavior: silently fail when target has no attachable behavior.
                return AttachTargetValidationResult.Fail();
            }

            var ownableBehavior = targetEntity.GetBehavior<EntityBehaviorOwnable>();
            if (ownableBehavior != null && !ownableBehavior.IsOwner(player.Entity))
            {
                return AttachTargetValidationResult.Fail(FailureCode.RequiresOwnership);
            }

            return AttachTargetValidationResult.Success(targetEntity, attachableBehavior);
        }

        private void PlayPlaceSoundAt(Block? block, Entity targetEntity)
        {
            var sound = block?.Sounds?.Place.Location ?? new AssetLocation("sounds/player/build");
            api.World.PlaySoundAt(sound, targetEntity, null, true, 16);
        }

        private int GetMaxInteractionDistance()
            => config?.CarryOptions?.MaxInteractionDistance ?? Default.MaxInteractionDistance;
    }
}