using System;
using System.Collections.Generic;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Common;
using CarryOn.Utility;
using CarryOn.Common.Network;
using Vintagestory.API.Datastructures;
using CarryOn.API.Common.Models;
using Vintagestory.API.MathTools;
using CarryOn.API.Common.Interfaces;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Logic
{
    public class TransferLogic(ICoreAPI api, ICarryManager carryManager)
    {

        /// <summary>
        /// Checks if the player can take a carryable item from the specified block entity.
        /// </summary>
        public bool CanTakeCarryable(IPlayer player, BlockEntity blockEntity, int index,
            out string failureCode)
        {
            failureCode = default!;

            if (!TryResolveCarryableTransfer(blockEntity, "CanTakeCarryable", out var transferHandler))
                return false;

            try
            {
                return transferHandler!.CanTake(player, blockEntity, index, api, out failureCode);
            }
            catch (Exception e)
            {
                failureCode = FailureCodes.Internal;
                api.Logger.Error($"CanTakeCarryable method failed: {e}", e);
            }

            return false;
        }

        /// <summary>
        /// Checks if the player can put a carryable item into the specified block entity.
        /// </summary>
        public bool CanPutCarryable(IPlayer player, BlockEntity blockEntity, int index, out string failureCode)
        {
            failureCode = default!;

            if (!TryResolveCarryableTransfer(blockEntity, "CanPutCarryable", out var transferHandler))
                return false;


            var carriedHands = player?.Entity != null ? carryManager.GetCarried(player.Entity, CarrySlot.Hands) : null;
            if (carriedHands == null) { api.Logger.Notification("CanPutCarryable: carriedHands is null"); return false; }

            try
            {
                return transferHandler!.CanPut(player!, blockEntity, index, carriedHands.ItemStack, api, out failureCode);
            }
            catch (Exception e)
            {
                failureCode = FailureCodes.Internal;
                api.Logger.Error($"CanPutCarryable method failed: {e}", e);
            }

            return false;
        }

        /// <summary>
        /// Gets the hold-to-interact duration in seconds for a transfer at the specified
        /// slot index. Returns <see cref="float.NaN"/> when no transfer handler applies.
        /// </summary>
        public float GetInteractDelay(IPlayer player, BlockEntity blockEntity, int index)
        {
            if (!TryResolveCarryableTransfer(blockEntity, "GetInteractDelay", out var transferHandler))
                return float.NaN;

            try
            {
                return transferHandler!.GetInteractDelay(blockEntity, api, index);
            }
            catch (Exception e)
            {
                api.Logger.Error($"GetInteractDelay method failed: {e}", e);
                return float.NaN;
            }
        }

        /// <summary>
        /// Resolves the carryable behavior and transfer handler for a direct block entity.
        /// </summary>
        private bool TryResolveCarryableTransfer(
            BlockEntity blockEntity,
            string methodName,
            out ICarryableBlockSlotHandler? transferHandler)
        {
            transferHandler = null;

            if (blockEntity == null) { api.Logger.Notification($"{methodName}: blockEntity is null"); return false; }

            var carryableBehavior = blockEntity.Block?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null || !carryableBehavior.TransferEnabled)
            {
                api.Logger.Notification($"{methodName}: carryableBehavior null={carryableBehavior == null}, TransferEnabled={carryableBehavior?.TransferEnabled}");
                return false;
            }

            transferHandler = carryableBehavior.TransferHandler;
            if (transferHandler == null) { api.Logger.Notification($"{methodName}: TransferHandler is null"); return false; }

            return true;
        }

        /// <summary>
        /// Resolves the block entity, carryable behavior, and transfer handler for a transfer message.
        /// </summary>
        private bool TryResolveTransferTarget(
            BlockPos blockPos,
            string methodName,
            out BlockEntity blockEntity,
            out BlockBehaviorCarryable? carryableBehavior,
            out ICarryableBlockSlotHandler? transferHandler,
            out string failureCode)
        {
            failureCode = default!;
            blockEntity = default!;
            carryableBehavior = default;
            transferHandler = default;

            blockEntity = api.World.BlockAccessor.GetBlockEntity(blockPos);
            if (blockEntity == null)
            {
                api.Logger.Error($"{methodName}: No block entity found at position");
                return false;
            }

            carryableBehavior = blockEntity.Block?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null)
            {
                api.Logger.Error($"{methodName}: No Carryable behavior found");
                failureCode = FailureCodes.Internal;
                return false;
            }

            if (!carryableBehavior.TransferEnabled) return false;

            transferHandler = carryableBehavior.TransferHandler;
            if (transferHandler == null) return false;

            return true;
        }

        /// <summary>
        /// Validates common preconditions for a transfer operation (player not null, message not null, API initialized, BlockPos present).
        /// </summary>
        private bool ValidateTransferPreconditions<T>(
            IPlayer player,
            T message,
            string methodName,
            out BlockPos blockPos,
            out string failureCode) where T : class
        {
            failureCode = default!;
            blockPos = default!;

            if (api == null)
            {
                throw new InvalidOperationException("Api is not initialized.");
            }

            if (message == null)
            {
                api.Logger.Error($"{methodName}: Received null message");
                failureCode = FailureCodes.Internal;
                return false;
            }

            blockPos = message switch
            {
                PutMessage put => put.BlockPos!,
                TakeMessage take => take.BlockPos!,
                _ => null!
            };

            if (blockPos == null)
            {
                api.Logger.Error($"{methodName}: No BlockPos in message");
                failureCode = FailureCodes.Internal;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Try to put a carryable item into the targeted block's slot
        /// </summary>
        public bool TryPutCarryable(IPlayer player, PutMessage message, out string failureCode)
        {
            player = player ?? throw new ArgumentNullException(nameof(player));

            const string methodName = "TryPutCarryable";

            failureCode = default!;

            if (!ValidateTransferPreconditions(player, message, methodName, out var blockPos, out failureCode))
                return false;

            var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);
            if (carriedHands == null)
            {
                api.Logger.Error($"{methodName}: Player hands are empty");
                return false;
            }

            if (!TryResolveTransferTarget(blockPos, methodName, out var blockEntity, out _, out var transferHandler, out failureCode))
                return false;

            if (!transferHandler!.IsValidSlot(blockEntity, message.Index))
            {
                api.Logger.Notification($"{methodName}: Invalid slot index {message.Index}");
                failureCode = FailureCodes.Internal;
                return false;
            }

            try
            {
                var success = transferHandler.TryPut(player, blockEntity, message.Index, carriedHands.ItemStack, carriedHands.BlockEntityData,
                    api, out failureCode);

                if (success)
                {
                    carryManager.RemoveCarried(player.Entity, CarrySlot.Hands);
                    return true;
                }
            }
            catch (Exception e)
            {
                api.Logger.Error($"Call to {methodName} failed: {e}");
                failureCode = FailureCodes.Internal;
            }
            return false;
        }

        /// <summary>
        /// Tries to take a carryable block from the specified block entity slot.
        /// </summary>
        public bool TryTakeCarryable(IPlayer player, TakeMessage message, out string failureCode)
        {
            player = player ?? throw new ArgumentNullException(nameof(player));

            const string methodName = "TryTakeCarryable";

            failureCode = default!;

            if (!ValidateTransferPreconditions(player, message, methodName, out var blockPos, out failureCode))
                return false;

            var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);
            if (carriedHands != null)
            {
                api.Logger.Error($"{methodName}: Player hands are not empty");
                return false;
            }

            if (!TryResolveTransferTarget(blockPos, methodName, out var blockEntity, out _, out var transferHandler, out failureCode))
                return false;

            if (!transferHandler!.IsValidSlot(blockEntity, message.Index))
            {
                api.Logger.Notification($"{methodName}: Invalid slot index {message.Index}");
                failureCode = FailureCodes.Internal;
                return false;
            }

            ItemStack itemStack;
            ITreeAttribute? blockEntityData;
            try
            {
                var success = transferHandler.TryTake(player, blockEntity, message.Index, api,
                    out itemStack, out blockEntityData, out failureCode);

                if (success)
                {
                    carryManager.SetCarried(player.Entity, new CarriedBlock(CarrySlot.Hands, itemStack, blockEntityData));
                    return true;
                }
            }
            catch (Exception e)
            {
                api.Logger.Error($"Call to {methodName} failed: {e}");
                failureCode = FailureCodes.Internal;
            }
            return false;
        }

    }
}
