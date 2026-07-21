using System;
using System.Linq;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Entities;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Logic;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Handlers.CarryHandlers
{
    internal class CarryServerHandler
    {
        private readonly ICarryManager carryManager;
        private readonly IConfigProvider configProvider;
        private ICoreServerAPI? api;
        private TransferLogic? transferLogic;

        internal bool BackSlotEnabled => configProvider.Config.CarryOptions?.BackSlotEnabled ?? false;

        internal CarryServerHandler(ICarryManager carryManager, IConfigProvider configProvider)
        {
            this.carryManager = carryManager;
            this.configProvider = configProvider;
        }

        internal void Init(ICoreServerAPI api, TransferLogic transferLogic)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.transferLogic = transferLogic ?? throw new ArgumentNullException(nameof(transferLogic));
        }

        private ICoreServerAPI RequireApi([System.Runtime.CompilerServices.CallerMemberName] string? callerName = null)
        {
            var a = api;
            if (a == null)
            {
                var msg = callerName != null
                    ? $"{callerName} requires a server context."
                    : "ServerApi is not initialized.";
                throw new InvalidOperationException(msg);
            }
            return a;
        }

        // ------------------------------
        //  Server side message handlers
        // ------------------------------

        internal void OnInteractMessage(IServerPlayer player, InteractMessage message)
        {
            var world = player.Entity.World;
            var block = world.BlockAccessor.GetBlock(message.Position);

            if (block?.HasBlockBehavior<BlockBehaviorCarryableInteract>() == true)
            {
                var behavior = block.GetBehavior<BlockBehaviorCarryableInteract>();

                if (behavior.CanInteract(player))
                {
                    var blockSelection = player.CurrentBlockSelection.Clone();
                    blockSelection.Position = message.Position;
                    blockSelection.Block = block;
                    block.OnBlockInteractStart(world, player, blockSelection);
                }
            }
        }

        internal void OnPickUpMessage(IServerPlayer player, PickUpMessage message)
        {
            if (message == null || message.Position == null)
            {
                FailCarryAction(player, message?.Position, FailureCodes.InvalidData, FailureCodes.PickUpFailed);
                return;
            }

            if (message.Slot != CarrySlot.Hands || !player.Entity.CanInteract(requireEmptyHanded: true))
            {
                FailCarryAction(player, message.Position, FailureCodes.CannotInteract, FailureCodes.PickUpFailed);
                return;
            }

            string failureCode = FailureCodes.Ignore;
            if (carryManager.TryPickUp(
                player.Entity,
                message.Position,
                message.Slot,
                ref failureCode,
                checkIsCarryable: true,
                playSound: true,
                captureAttachedSigns: message.CaptureAttachedWallSigns))
            {
                return;
            }

            FailCarryAction(player, message.Position, failureCode, FailureCodes.PickUpFailed);
        }

        internal void OnPlaceDownMessage(IServerPlayer player, PlaceDownMessage message)
        {
            if (message == null || message.Position == null || message.HitPosition == null || message.PlacedAt == null)
            {
                FailCarryAction(player, message?.Position, FailureCodes.InvalidData, FailureCodes.PlaceDownFailed);
                return;
            }

            if (!player.Entity.CanInteract(requireEmptyHanded: message.Slot != CarrySlot.Hands))
            {
                FailCarryAction(player, message.Position, FailureCodes.CannotInteract, FailureCodes.PlaceDownFailed);
                return;
            }

            string failureCode = FailureCodes.Ignore;
            if (carryManager.TryPlaceDownAt(
                player,
                message.Slot,
                message.Selection,
                out var placedAt,
                ref failureCode))
            {
                if (placedAt != message.PlacedAt)
                {
                    player.Entity.World.BlockAccessor.MarkBlockDirty(message.PlacedAt);
                }
                return;
            }

            FailCarryAction(player, message.Position, failureCode, FailureCodes.PlaceDownFailed);
        }

        internal void OnSwapSlotsMessage(IServerPlayer player, SwapSlotsMessage message)
        {
            if ((message.First != message.Second)
                && (message.First == CarrySlot.Back || message.Second == CarrySlot.Back)
                && player.Entity.CanInteract(requireEmptyHanded: true))
            {
                var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);
                if (carriedHands != null && !BackSlotEnabled)
                {
                    carryManager.TouchCarriedAttributes(player.Entity);
                    return;
                }

                if (carryManager.SwapCarried(player.Entity, message.First, message.Second))
                {
                    api?.World.PlaySoundAt(new AssetLocation(CarryCodes.SoundPaths.Throw), player.Entity);
                    carryManager.TouchCarriedAttributes(player.Entity);
                }
            }
        }

        internal void OnAttachMessage(IServerPlayer player, AttachMessage message)
        {
            RequireApi();

            string failureCode = FailureCodes.Ignore;
            if (carryManager.TryAttach(player, message.TargetEntityId, message.SlotIndex, ref failureCode))
            {
                return;
            }

            SendAttachDetachFailure(player, failureCode);
        }

        internal void OnDetachMessage(IServerPlayer player, DetachMessage message)
        {
            RequireApi();

            string failureCode = FailureCodes.Ignore;
            if (carryManager.TryDetach(player, message.TargetEntityId, message.SlotIndex, ref failureCode))
            {
                return;
            }

            SendAttachDetachFailure(player, failureCode);
        }

        internal void OnPutMessage(IServerPlayer player, PutMessage message)
        {
            var a = RequireApi();

            if (message == null)
            {
                a.Logger.Error("OnPutMessage: Received null message");
                return;
            }

            if (transferLogic == null)
            {
                a.Logger.Error("OnPutMessage: TransferLogic is not initialized");
                return;
            }

            if (!transferLogic.TryPutCarryable(player, message, out string failureCode, out string onScreenErrorMessage))
            {
                if (onScreenErrorMessage != null)
                {
                    player.SendIngameError(failureCode, onScreenErrorMessage);
                }
            }
        }

        internal void OnTakeMessage(IServerPlayer player, TakeMessage message)
        {
            var a = RequireApi();

            if (transferLogic == null)
            {
                a.Logger.Error("OnTakeMessage: TransferLogic is not initialized");
                return;
            }

            if (!transferLogic.TryTakeCarryable(player, message, out string failureCode, out string onScreenErrorMessage))
            {
                if (onScreenErrorMessage != null)
                {
                    player.SendIngameError(failureCode, onScreenErrorMessage);
                }
            }
        }

        internal void OnDismountMessage(IServerPlayer player, DismountMessage message)
        {
            player.Entity.TryUnmount();

            player.Entity.World.GetEntityById(message.EntityId)?
                .GetBehavior<EntityBehaviorCreatureCarrier>()?
                .Seats?.FirstOrDefault(s => s.SeatId == message.SeatId)?
                .Controls?.StopAllMovement();
        }

        internal void OnPickupEntityMessage(IServerPlayer player, PickupEntityMessage message)
        {
            var entity = player.Entity.World.GetEntityById(message.EntityId) as EntityCarriedBlock;
            if (entity == null) return;

            if (TryPickupFromEntity(player, entity))
                entity.Die(EnumDespawnReason.Death, null);
        }

        // ------------------------------
        //  Both side event handlers
        // ------------------------------

        internal EnumHandling OnBeforeActiveSlotChanged(EntityAgent entity)
        {
            return (carryManager.GetCarried(entity, CarrySlot.Hands) != null)
                ? EnumHandling.PreventDefault
                : EnumHandling.PassThrough;
        }

        // ------------------------------
        //  Server side event handlers
        // ------------------------------

        internal void OnServerEntitySpawn(Entity entity)
        {
            if (entity is EntityPlayer) return;

            foreach (var carried in carryManager.GetAllCarried(entity))
                carryManager.SetCarried(entity, carried, carried.Slot);
        }

        internal void OnServerPlayerNowPlaying(IServerPlayer player)
        {
            foreach (var carried in carryManager.GetAllCarried(player.Entity))
                carryManager.SetCarried(player.Entity, carried, carried.Slot);
        }

        // ------------------------------
        //  Helpers
        // ------------------------------

        private void FailCarryAction(IServerPlayer player, BlockPos? pos, string failureCode, string defaultCode)
        {
            _ = RequireApi();

            if (pos != null) player.Entity.World.BlockAccessor.MarkBlockDirty(pos);
            carryManager.TouchCarriedAttributes(player.Entity);
            player.Entity.WatchedAttributes.MarkPathDirty("stats/walkspeed");
            carryManager.LockHotbarSlots(player);

            if (!string.IsNullOrEmpty(failureCode) && failureCode != FailureCodes.Ignore)
            {
                player.SendIngameError(failureCode, LocalizationHelper.GetLang($"{defaultCode}-{failureCode}"));
            }
            else
            {
                player.SendIngameError(defaultCode, LocalizationHelper.GetLang(defaultCode));
            }
        }

        private static void SendAttachDetachFailure(IServerPlayer player, string? failureCode)
        {
            if (failureCode == null || failureCode == FailureCodes.Ignore)
            {
                return;
            }

            player.SendIngameError(failureCode, LocalizationHelper.GetLang(failureCode));
        }

        private bool TryPickupFromEntity(IServerPlayer player, EntityCarriedBlock entity)
        {
            var carriedTree = entity.CarriedBlockTree;
            if (carriedTree == null) return false;

            var entityApi = entity.Api;
            if (entityApi == null) return false;

            var carriedBlock = CarriedBlockTreeSerializer.Deserialize(carriedTree, entityApi);
            if (carriedBlock == null) return false;

            if (carryManager.GetCarried(player.Entity, CarrySlot.Hands) != null)
            {
                player.SendIngameError(FailureCodes.AlreadyCarrying, LocalizationHelper.GetLang("pick-up-already-carrying"));
                return false;
            }

            if (!CanPickupFromEntity(player, entity))
                return false;

            carryManager.SetCarried(player.Entity, carriedBlock, CarrySlot.Hands);

            var pickupSound = carriedBlock.Block?.Sounds?.Place.Location
                ?? new AssetLocation(SoundPaths.DefaultPlace);
            entityApi.World.PlaySoundAt(pickupSound, player.Entity);

            return true;
        }

        private bool CanPickupFromEntity(IServerPlayer player, EntityCarriedBlock entity)
        {
            var cfg = configProvider.Config.CarriedBlockEntity;
            if (cfg == null) return true;

            if (!CarriedBlockAccessPolicy.CanPickup(
                player.WorldData.CurrentGameMode,
                player.PlayerUID,
                entity.OwnerUid,
                cfg.PickupAccess,
                cfg.GracePeriodSeconds,
                entity.DropTimeRealTicks))
            {
                player.SendIngameError(FailureCodes.NotOwner, LocalizationHelper.GetLang("pickup-not-owner"));
                return false;
            }

            return true;
        }
    }
}
