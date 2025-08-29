using System;
using CarryOn.API.Common;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Common;
using static CarryOn.CarrySystem;
using static CarryOn.API.Common.CarryCode;
using CarryOn.Utility;
using CarryOn.Common.Network;
using Vintagestory.API.Datastructures;

namespace CarryOn.Common.Logic
{
    public class TransferProcessor
    {
        private ICoreAPI api;
        private CarrySystem carrySystem;

        public TransferProcessor(ICoreAPI api, CarrySystem carrySystem)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.carrySystem = carrySystem ?? throw new ArgumentNullException(nameof(carrySystem));
        }

        /// <summary>
        /// Checks if the player can take a carryable item from the specified block entity.
        /// </summary>
        public bool CanTakeCarryable(IPlayer player, BlockEntity blockEntity, int index,
            out float? transferDelay, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = null;
            onScreenErrorMessage = null;
            transferDelay = null;

            if (blockEntity == null) return false;

            var carryableBehavior = blockEntity.Block?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null || !carryableBehavior.TransferEnabled) return false;

            var transferHandler = carryableBehavior.TransferHandler;
            if (transferHandler == null) return false;

            try
            {
                return transferHandler.CanTakeCarryable(player, blockEntity, index,
                   out transferDelay, out failureCode, out onScreenErrorMessage);
            }
            catch (Exception e)
            {
                failureCode = FailureCode.Internal;
                onScreenErrorMessage = GetLang("unknown-error");
                this.api.Logger.Error($"CanTakeCarryable method failed: {e}");
            }

            return false;
        }

        /// <summary>
        /// Checks if the player can put a carryable item into the specified block entity.
        /// </summary>
        public bool CanPutCarryable(IPlayer player, BlockEntity blockEntity, int index, out float? transferDelay, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = null;
            onScreenErrorMessage = null;
            transferDelay = null;

            if (blockEntity == null) return false;

            var carryableBehavior = blockEntity.Block?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null || !carryableBehavior.TransferEnabled) return false;

            var transferHandler = carryableBehavior.TransferHandler;
            if (transferHandler == null) return false;


            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            try
            {
                return transferHandler.CanPutCarryable(player, blockEntity, index, carriedHands?.ItemStack, carriedHands?.BlockEntityData,
                    out transferDelay, out failureCode, out onScreenErrorMessage);
            }
            catch (Exception e)
            {
                failureCode = FailureCode.Internal;
                onScreenErrorMessage = GetLang("unknown-error");
                this.api.Logger.Error($"CanPutCarryable method failed: {e}");
            }

            return false;
        }        

        /// <summary>
        /// Try to put a carryable item into the targeted block's slot
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        /// <param name="failureCode"></param>
        /// <param name="onScreenErrorMessage"></param>
        /// <returns></returns>
        public bool TryPutCarryable(IPlayer player, PutMessage message, out string failureCode, out string onScreenErrorMessage)
        {
            const string methodName = "TryPutCarryable";

            failureCode = null;
            onScreenErrorMessage = null;

            if (this.api == null)
            {
                throw new InvalidOperationException("Api is not initialized.");
            }

            if (message == null)
            {
                api.Logger.Error($"{methodName}: Received null message");
                failureCode = FailureCode.Internal;
                return false;
            }

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);
            if (carriedHands == null)
            {
                api.Logger.Error($"{methodName}: Player hands are empty");
                return false;
            }

            if (message.BlockPos == null)
            {
                api.Logger.Error($"{methodName}: No BlockPos in message");
                failureCode = FailureCode.Internal;
                return false;
            }

            var blockEntity = api.World.BlockAccessor.GetBlockEntity(message.BlockPos);
            if (blockEntity == null)
            {
                api.Logger.Error($"{methodName}: No block entity found at position");
                return false;
            }

            var carryableBehavior = blockEntity.Block?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null)
            {
                api.Logger.Error($"{methodName}: No Carryable behavior found");
                failureCode = FailureCode.Internal;
                return false;
            }

            if (!carryableBehavior.TransferEnabled) return false;

            var transferHandler = carryableBehavior.TransferHandler;
            if (transferHandler == null) return false;

            try
            {
                var success = transferHandler.TryPutCarryable(player, blockEntity, message.Index, carriedHands?.ItemStack, carriedHands?.BlockEntityData,
                    out failureCode, out onScreenErrorMessage);

                if (success)
                {
                    // If the transfer was successful, we can remove the carried block from the player's hands.
                    this.carrySystem.CarryManager.RemoveCarried(player?.Entity, CarrySlot.Hands);
                    return true;
                }

            }
            catch (Exception e)
            {
                api.Logger.Error($"Call to {methodName} failed: {e}");
                failureCode = FailureCode.Internal;
            }
            return false;
        }

        /// <summary>
        /// Tries to take a carryable block from the specified block entity slot.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        /// <param name="failureCode"></param>
        /// <param name="onScreenErrorMessage"></param>
        /// <returns></returns>
        public bool TryTakeCarryable(IPlayer player, TakeMessage message, out string failureCode, out string onScreenErrorMessage)
        {

            const string methodName = "TryTakeCarryable";

            failureCode = null;
            onScreenErrorMessage = null;

            if (this.api == null)
            {
                throw new InvalidOperationException("Api is not initialized.");
            }

            if (message == null)
            {
                api.Logger.Error($"{methodName}: Received null message");
                failureCode = FailureCode.Internal;
                return false;
            }

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);
            if (carriedHands != null)
            {
                api.Logger.Error($"{methodName}: Player hands are not empty");
                return false;
            }

            if (message.BlockPos == null)
            {
                api.Logger.Error($"{methodName}: No BlockPos in message");
                failureCode = FailureCode.Internal;
                return false;
            }

            var blockEntity = api.World.BlockAccessor.GetBlockEntity(message.BlockPos);
            if (blockEntity == null)
            {
                api.Logger.Error($"{methodName}: No block entity found at position");
                return false;
            }

            var carryableBehavior = blockEntity.Block?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null)
            {
                api.Logger.Error($"{methodName}: No Carryable behavior found");
                failureCode = FailureCode.Internal;
                return false;
            }

            if (!carryableBehavior.TransferEnabled) return false;

            var transferHandler = carryableBehavior.TransferHandler;
            if (transferHandler == null) return false;

            ItemStack itemStack;
            ITreeAttribute blockEntityData;
            try
            {
                var success = transferHandler.TryTakeCarryable(player, blockEntity, message.Index, out itemStack, out blockEntityData,
                    out failureCode, out onScreenErrorMessage);

                if (success)
                {
                    // If the transfer was successful, we can put the block in the player's hands.
                    this.carrySystem.CarryManager.SetCarried(player?.Entity, new CarriedBlock(CarrySlot.Hands, itemStack, blockEntityData));
                    // var carriedBlock = new CarriedBlockExtended(CarrySlot.Hands, itemStack, blockEntityData);
                    // carriedBlock.Set(player.Entity);
                    return true;
                }

            }
            catch (Exception e)
            {
                api.Logger.Error($"Call to {methodName} failed: {e}");
                failureCode = FailureCode.Internal;
            }
            return false;
        }     

    }
}