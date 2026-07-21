using System;
using System.Collections.Generic;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Common;
using CarryOn.Utility;
using CarryOn.Common.Network;
using Vintagestory.API.Datastructures;
using CarryOn.API.Common.Models;
using System.Linq;
using Vintagestory.API.MathTools;
using CarryOn.API.Common.Interfaces;
using static CarryOn.Common.Models.CarryCodes;
 
namespace CarryOn.Common.Logic
{
    public class TransferLogic(ICoreAPI api, ICarryManager carryManager)
    {

        /// <summary>
        /// Initializes the transfer behaviors for carryable blocks.
        /// For each registered behavior code, resolves the Type and wires
        /// matching carryable blocks.
        /// </summary>
        /// <param name="api"></param>
        /// <param name="carryManager"></param>
        public static void InitTransferBehaviors(ICoreAPI api, ICarryManager carryManager)
        {
            var codes = carryManager.EnumerateTransferBehaviors().ToList();
            api.Logger.Notification($"CarryOn: InitTransferBehaviors called. Registered transfer codes: [{string.Join(", ", codes)}]");

            foreach (var code in codes)
            {
                var type = api.ClassRegistry.GetBlockBehaviorClass(code);
                if (type == null)
                {
                    api.Logger.Warning($"CarryOn: Transfer behavior '{code}' not registered with the API");
                    continue;
                }

                api.Logger.Notification($"CarryOn: Resolved transfer behavior '{code}' to type {type.Name}. Scanning carryable blocks...");

                foreach (var block in api.World.Blocks.Where(b => b.IsCarryable()))
                {
                    if (!block.HasBehavior(type))
                        continue;

                    var carryableBehavior = block.GetBehavior<BlockBehaviorCarryable>();
                    if (carryableBehavior == null)
                        continue;

                    var transferHandler = block.GetBehavior(type) as ICarryableTransfer;
                    if (transferHandler == null)
                        continue;

                    api.Logger.Notification($"CarryOn: Wired transfer handler '{code}' to block {block.Code}");
                    carryableBehavior.ConfigureTransferBehavior(transferHandler, api);
                }
            }
        }

        /// <summary>
        /// Checks if the player can take a carryable item from the specified block entity.
        /// </summary>
        public bool CanTakeCarryable(IPlayer player, BlockEntity blockEntity, int index,
            out float? transferDelay, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = default!;
            onScreenErrorMessage = default!;
            transferDelay = null;

            if (blockEntity == null) { api.Logger.Notification("CanTakeCarryable: blockEntity is null"); return false; }

            var carryableBehavior = blockEntity.Block?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null || !carryableBehavior.TransferEnabled)
            {
                api.Logger.Notification($"CanTakeCarryable: carryableBehavior null={carryableBehavior == null}, TransferEnabled={carryableBehavior?.TransferEnabled}");
                return false;
            }

            var transferHandler = carryableBehavior.TransferHandler;
            if (transferHandler == null) { api.Logger.Notification("CanTakeCarryable: TransferHandler is null"); return false; }

            try
            {
                return transferHandler.CanTakeCarryable(player, blockEntity, index,
                   out transferDelay, out failureCode, out onScreenErrorMessage);
            }
            catch (Exception e)
            {
                failureCode = FailureCodes.Internal;
                onScreenErrorMessage = LocalizationHelper.GetLang("unknown-error");
                api.Logger.Error($"CanTakeCarryable method failed: {e}", e);
            }

            return false;
        }

        /// <summary>
        /// Checks if the player can put a carryable item into the specified block entity.
        /// </summary>
        public bool CanPutCarryable(IPlayer player, BlockEntity blockEntity, int index, out float? transferDelay, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = default!;
            onScreenErrorMessage = default!;
            transferDelay = null;

            if (blockEntity == null) { api.Logger.Notification("CanPutCarryable: blockEntity is null"); return false; }

            var carryableBehavior = blockEntity.Block?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null || !carryableBehavior.TransferEnabled)
            {
                api.Logger.Notification($"CanPutCarryable: carryableBehavior null={carryableBehavior == null}, TransferEnabled={carryableBehavior?.TransferEnabled}");
                return false;
            }

            var transferHandler = carryableBehavior.TransferHandler;
            if (transferHandler == null) { api.Logger.Notification("CanPutCarryable: TransferHandler is null"); return false; }


            var carriedHands = player?.Entity != null ? carryManager.GetCarried(player.Entity, CarrySlot.Hands) : null;
            if (carriedHands == null) { api.Logger.Notification("CanPutCarryable: carriedHands is null"); return false; }

            try
            {
                return transferHandler.CanPutCarryable(player!, blockEntity, index, carriedHands.ItemStack, carriedHands.BlockEntityData,
                    out transferDelay, out failureCode, out onScreenErrorMessage);
            }
            catch (Exception e)
            {
                failureCode = FailureCodes.Internal;
                onScreenErrorMessage = LocalizationHelper.GetLang("unknown-error");
                api.Logger.Error($"CanPutCarryable method failed: {e}");
            }

            return false;
        }

        /// <summary>
        /// Resolves the block entity, carryable behavior, and transfer handler for a transfer message.
        /// </summary>
        private bool TryResolveTransferTarget(
            BlockPos blockPos,
            string methodName,
            out BlockEntity blockEntity,
            out BlockBehaviorCarryable? carryableBehavior,
            out ICarryableTransfer? transferHandler,
            out string failureCode,
            out string onScreenErrorMessage)
        {
            failureCode = default!;
            onScreenErrorMessage = default!;
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
            out string failureCode,
            out string onScreenErrorMessage) where T : class
        {
            failureCode = default!;
            onScreenErrorMessage = default!;
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
        public bool TryPutCarryable(IPlayer player, PutMessage message, out string failureCode, out string onScreenErrorMessage)
        {
            player = player ?? throw new ArgumentNullException(nameof(player));

            const string methodName = "TryPutCarryable";

            failureCode = default!;
            onScreenErrorMessage = default!;

            if (!ValidateTransferPreconditions(player, message, methodName, out var blockPos, out failureCode, out onScreenErrorMessage))
                return false;

            var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);
            if (carriedHands == null)
            {
                api.Logger.Error($"{methodName}: Player hands are empty");
                return false;
            }

            if (!TryResolveTransferTarget(blockPos, methodName, out var blockEntity, out _, out var transferHandler, out failureCode, out onScreenErrorMessage))
                return false;

            try
            {
                var success = transferHandler!.TryPutCarryable(player, blockEntity, message.Index, carriedHands.ItemStack, carriedHands.BlockEntityData,
                    out failureCode, out onScreenErrorMessage);

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
        public bool TryTakeCarryable(IPlayer player, TakeMessage message, out string failureCode, out string onScreenErrorMessage)
        {
            player = player ?? throw new ArgumentNullException(nameof(player));

            const string methodName = "TryTakeCarryable";

            failureCode = default!;
            onScreenErrorMessage = default!;

            if (!ValidateTransferPreconditions(player, message, methodName, out var blockPos, out failureCode, out onScreenErrorMessage))
                return false;

            var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);
            if (carriedHands != null)
            {
                api.Logger.Error($"{methodName}: Player hands are not empty");
                return false;
            }

            if (!TryResolveTransferTarget(blockPos, methodName, out var blockEntity, out _, out var transferHandler, out failureCode, out onScreenErrorMessage))
                return false;

            ItemStack itemStack;
            ITreeAttribute? blockEntityData;
            try
            {
                var success = transferHandler!.TryTakeCarryable(player, blockEntity, message.Index, out itemStack, out blockEntityData,
                    out failureCode, out onScreenErrorMessage);

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