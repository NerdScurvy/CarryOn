using System;
using System.Reflection;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Logic;
using CarryOn.Common.Network;
using CarryOn.Utility;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Client.Logic.Interaction
{
    internal sealed class CarryInteractionStateMachine : ICarryInteractionController
    {
        private readonly ICoreClientAPI api;
        private readonly ICarryManager carryManager;
        private readonly IClientNetworkChannel clientChannel;
        private readonly Action hideOverlay;
        private readonly Action<float> setOverlayProgress;
        private readonly CarryInteractionValidator validator;
        private readonly TransferLogic transferLogic;
        private readonly ClientModConfig? clientModConfig;

        public CarryInteraction Interaction { get; private set; } = new CarryInteraction();

        public Vintagestory.Client.NoObf.HudElementInteractionHelp? HudHelp { get; set; }
        private MethodInfo? composeBlockWorldInteractionHelpMethod;
        private bool helpRefreshRequested;

        public CarryInteractionStateMachine(
            ICoreClientAPI api,
            ICarryManager carryManager,
            IClientNetworkChannel clientChannel,
            Action hideOverlay,
            Action<float> setOverlayProgress,
            CarryInteractionValidator validator,
            TransferLogic transferLogic,
            ClientModConfig? clientModConfig = null)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.carryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
            this.clientChannel = clientChannel ?? throw new ArgumentNullException(nameof(clientChannel));
            this.hideOverlay = hideOverlay ?? throw new ArgumentNullException(nameof(hideOverlay));
            this.setOverlayProgress = setOverlayProgress ?? throw new ArgumentNullException(nameof(setOverlayProgress));
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
            this.transferLogic = transferLogic ?? throw new ArgumentNullException(nameof(transferLogic));
            this.clientModConfig = clientModConfig;

            composeBlockWorldInteractionHelpMethod = AccessTools.Method(typeof(Vintagestory.Client.NoObf.HudElementInteractionHelp), "ComposeBlockWorldInteractionHelp");
            if (composeBlockWorldInteractionHelpMethod == null)
            {
                this.api.Logger.Error("Failed to find method ComposeBlockWorldInteractionHelp via reflection.");
            }
            else
            {
                var parameters = composeBlockWorldInteractionHelpMethod.GetParameters();
                if (parameters.Length != 0)
                {
                    this.api.Logger.Error($"Unexpected method signature for ComposeBlockWorldInteractionHelp: expected 0 parameters, got {parameters.Length}");
                    composeBlockWorldInteractionHelpMethod = null;
                }
            }
        }

        public void TryContinueInteraction(float deltaTime)
        {
            var world = this.api.World;
            var player = world.Player;
            var input = this.api.Input;

            if (!input.InWorldMouseButton.Right) { CancelInteraction(resetTimeHeld: true); return; }

            if (Interaction.CarryAction == CarryAction.None || Interaction.CarryAction == CarryAction.Done) return;

            var requireEmpty = (Interaction.CarryAction != CarryAction.PlaceDown) || (Interaction.CarrySlot != CarrySlot.Hands);

            if (Interaction.CarryAction != CarryAction.Interact && !(Interaction.CarryAction == CarryAction.PickupEntity
                ? player.Entity.CanDoCarryAction(requireEmptyHanded: true)
                : player.Entity.CanInteract(requireEmptyHanded: requireEmpty)))
            { CancelInteraction(resetTimeHeld: true); return; }

            var carriedTarget = Interaction.CarrySlot.HasValue ? this.carryManager.GetCarried(player.Entity, Interaction.CarrySlot.Value) : null;
            var holdingAny = this.carryManager.GetCarried(player.Entity, CarrySlot.Hands);
            BlockSelection? selection = null;
            BlockBehaviorCarryable? carryBehavior = null;
            BlockBehaviorCarryableInteract? interactBehavior = null;
            EntityBehaviorAttachableCarryable? attachableCarryBehavior = null;

            switch (Interaction.CarryAction)
            {
                case CarryAction.Interact:
                case CarryAction.PickUp:
                case CarryAction.PlaceDown:

                    if (Interaction.CarryAction == CarryAction.PickUp == (holdingAny != null))
                    { CancelInteraction(); return; }

                    selection = (Interaction.CarryAction == CarryAction.PlaceDown) ? player.CurrentBlockSelection : MultiblockUtils.GetMultiblockOriginSelection(world.BlockAccessor, player.CurrentBlockSelection);

                    var position = selection?.Position;

                    if (Interaction.CarryAction == CarryAction.PlaceDown)
                    {
                        if (carriedTarget == null || carriedTarget.Block == null)
                        {
                            CancelInteraction();
                            return;
                        }

                        position = BlockUtils.GetPlacedPosition(world.BlockAccessor, player.CurrentBlockSelection, carriedTarget.Block);

                    }

                    if (Interaction.TargetBlockPos != position)
                    { CancelInteraction(); return; }

                    if (Interaction.CarryAction == CarryAction.Interact)
                    {
                        interactBehavior = selection?.Block.GetBehavior<BlockBehaviorCarryableInteract>();
                        break;
                    }
                    carryBehavior = (Interaction.CarryAction == CarryAction.PickUp)
                        ? selection?.Block?.GetBehaviorOrDefault(BlockBehaviorCarryable.Default)
                        : carriedTarget?.GetCarryableBehavior();
                    break;

                case CarryAction.SwapBack:
                    if (carriedTarget != null && !validator.BackSlotEnabled) return;

                    var carriedBack = this.carryManager.GetCarried(player.Entity, CarrySlot.Back);
                    carryBehavior = (carriedTarget != null) ? carriedTarget?.GetCarryableBehavior() : carriedBack?.GetCarryableBehavior();
                    if (carryBehavior == null)
                    {
                        this.api.Logger.Debug("Nothing carried. Player may have dropped the block from being damaged");
                        return;
                    }
                    try
                    {
                        if (Interaction.CarrySlot == null)
                        {
                            this.api.Logger.Debug("CarrySlot is null during SwapBack interaction.");
                            return;
                        }
                        var slotToCheck = Interaction.CarrySlot.Value;
                        ItemStack? itemForCheck = carriedTarget != null ? carriedTarget.ItemStack : (carriedBack != null ? carriedBack.ItemStack : null);
                        if (!carryBehavior.CanCarryInSlot(slotToCheck, itemForCheck)) return;
                    }
                    catch (Exception ex)
                    {
                        this.api.Logger.Debug("Error checking carry slot during SwapBack: " + ex);
                        return;
                    }

                    break;

                case CarryAction.Attach:
                case CarryAction.Detach:
                    attachableCarryBehavior = Interaction.TargetEntity?.GetBehavior<EntityBehaviorAttachableCarryable>();
                    break;

                case CarryAction.PickupEntity:
                    if (Interaction.TargetEntity == null || !Interaction.TargetEntity.Alive)
                    { CancelInteraction(); return; }
                    break;

                case CarryAction.Put:
                case CarryAction.Take:
                    break;

                default: return;
            }

            var requiredTime = CalculateRequiredTime(carryBehavior, interactBehavior);

            Interaction.TimeHeld += deltaTime;
            var progress = Interaction.TimeHeld / requiredTime;

            setOverlayProgress(progress);

            HudCarried.TriggerHandsHighlight();

            if (Interaction.CarryAction == CarryAction.SwapBack)
            {
                HudCarried.TriggerBackHighlight();
            }
            if (progress <= 1.0f) return;

            ExecuteAction(player, selection, carriedTarget, attachableCarryBehavior);
        }

        private void ContinueInteractAction(IPlayer player, BlockSelection? selection, IClientNetworkChannel clientChannel)
        {
            if (selection?.Block?.OnBlockInteractStart(api.World, player, selection) == true)
                clientChannel.SendPacket(new InteractMessage(selection.Position));
        }

        private void ContinuePickUpAction(IPlayer player, BlockSelection? selection, ICarryManager carryManager, IClientNetworkChannel clientChannel)
        {
            if (selection?.Position == null || !Interaction.CarrySlot.HasValue)
            {
                CancelInteraction();
                return;
            }

            string failureCode = FailureCodes.Ignore;

            var hasPickedUp = carryManager.TryPickUp(
                player.Entity,
                selection.Position,
                Interaction.CarrySlot.Value,
                ref failureCode,
                checkIsCarryable: true,
                playSound: true
            );

            if (hasPickedUp)
            {
                bool captureAttached = clientModConfig?.Config?.CaptureAttachedWallSigns ?? true;
                clientChannel.SendPacket(new PickUpMessage(selection.Position, Interaction.CarrySlot.Value)
                {
                    CaptureAttachedWallSigns = captureAttached
                });
            }
            else
            {
                CarryErrorHelper.ShowErrorWithFallback(this.api, failureCode, FailureCodes.PickUpFailed);
            }
        }

        private void ContinuePlaceDownAction(IPlayer player, BlockSelection? selection, CarriedBlock? carriedTarget, ICarryManager carryManager, IClientNetworkChannel clientChannel)
        {
            if (carriedTarget == null || selection == null || !Interaction.CarrySlot.HasValue)
            {
                CancelInteraction();
                return;
            }

            string failureCode = FailureCodes.Ignore;

            if (carryManager.TryPlaceDownAt(player, carriedTarget.Slot, selection, out var placedAt, ref failureCode) && placedAt != null)
                clientChannel.SendPacket(new PlaceDownMessage(Interaction.CarrySlot.Value, selection, placedAt));
            else
            {
                CarryErrorHelper.ShowErrorWithFallback(this.api, failureCode, FailureCodes.PlaceDownFailed);
            }
        }

        private void ContinueSwapBackAction(IPlayer player, IClientNetworkChannel clientChannel)
        {
            if (!Interaction.CarrySlot.HasValue)
            {
                CancelInteraction();
                return;
            }

            if (this.carryManager.SwapCarried(player.Entity, Interaction.CarrySlot.Value, CarrySlot.Back))
                clientChannel.SendPacket(new SwapSlotsMessage(CarrySlot.Back, Interaction.CarrySlot.Value));
        }

        private void ContinuePickupEntityAction(IPlayer player, IClientNetworkChannel clientChannel)
        {
            if (Interaction.TargetEntity == null || !Interaction.TargetEntity.Alive)
            {
                CancelInteraction();
                return;
            }

            clientChannel.SendPacket(new PickupEntityMessage(Interaction.TargetEntity.EntityId));
        }

        private void ContinueAttachAction(EntityBehaviorAttachableCarryable? attachableCarryBehavior, IClientNetworkChannel clientChannel)
        {
            if (Interaction.TargetEntity == null || attachableCarryBehavior == null || Interaction.TargetSlotIndex == null || Interaction.Slot == null) return;
            clientChannel.SendPacket(new AttachMessage(Interaction.TargetEntity.EntityId, Interaction.TargetSlotIndex.Value));
            attachableCarryBehavior.OnAttachmentToggled(true, api.World.Player.Entity, Interaction.Slot, Interaction.TargetSlotIndex.Value);
        }

        private void ContinueDetachAction(EntityBehaviorAttachableCarryable? attachableCarryBehavior, IClientNetworkChannel clientChannel)
        {
            if (Interaction.TargetEntity == null || attachableCarryBehavior == null || Interaction.TargetSlotIndex == null || Interaction.Slot == null) return;
            clientChannel.SendPacket(new DetachMessage(Interaction.TargetEntity.EntityId, Interaction.TargetSlotIndex.Value));
            attachableCarryBehavior.OnAttachmentToggled(false, api.World.Player.Entity, Interaction.Slot, Interaction.TargetSlotIndex.Value);
        }

        private void ContinuePutAction(IPlayer player, IClientNetworkChannel clientChannel)
        {
            if (Interaction.TargetBlockPos == null)
            {
                CancelInteraction();
                return;
            }

            string failureCode = FailureCodes.Ignore;
            string? onScreenErrorMessage = null;

            var putMessage = new PutMessage(blockPos: Interaction.TargetBlockPos, index: Interaction.TargetSlotIndex ?? -1);

            if (!this.transferLogic.TryPutCarryable(player, putMessage, out failureCode, out onScreenErrorMessage))
            {
                if (failureCode != FailureCodes.Continue)
                {
                    CarryErrorHelper.ShowErrorIfMessage(this.api, failureCode, onScreenErrorMessage);
                    this.api.Logger.Debug($"Failed client side: {failureCode} : {onScreenErrorMessage}");
                }
            }

            if (failureCode == FailureCodes.Stop) return;
            clientChannel.SendPacket(putMessage);
        }

        private void ContinueTakeAction(IPlayer player, IClientNetworkChannel clientChannel)
        {
            if (Interaction.TargetBlockPos == null)
            {
                CancelInteraction();
                return;
            }

            string failureCode = FailureCodes.Ignore;
            string? onScreenErrorMessage = null;

            var takeMessage = new TakeMessage(blockPos: Interaction.TargetBlockPos, index: Interaction.TargetSlotIndex ?? -1);

            if (!this.transferLogic.TryTakeCarryable(player, takeMessage, out failureCode, out onScreenErrorMessage))
            {
                if (failureCode != FailureCodes.Continue)
                {
                    CarryErrorHelper.ShowErrorIfMessage(this.api, failureCode, onScreenErrorMessage);
                    this.api.Logger.Debug($"Failed client side: {failureCode} : {onScreenErrorMessage}");
                }
            }

            if (failureCode == FailureCodes.Stop) return;
            clientChannel.SendPacket(takeMessage);
        }

        private float CalculateRequiredTime(BlockBehaviorCarryable? carryBehavior, BlockBehaviorCarryableInteract? interactBehavior)
        {
            float requiredTime;

            if (Interaction.TransferDelay.HasValue)
            {
                requiredTime = Interaction.TransferDelay.Value;
            }
            else if (Interaction.CarryAction is CarryAction.Put or CarryAction.Take)
            {
                requiredTime = carryBehavior?.TransferDelay ?? Defaults.TransferSpeed;
            }
            else if (Interaction.CarryAction == CarryAction.Interact)
            {
                if (validator.RemoveInteractDelayWhileCarrying) requiredTime = 0;
                else requiredTime = interactBehavior?.InteractDelay ?? Defaults.InteractSpeed;
            }
            else
            {

                requiredTime = carryBehavior?.InteractDelay ?? Defaults.PickUpSpeed;
                switch (Interaction.CarryAction)
                {
                    case CarryAction.PlaceDown: requiredTime *= Defaults.PlaceSpeed; break;
                    case CarryAction.SwapBack: requiredTime *= Defaults.SwapSpeed; break;
                }
            }

            requiredTime /= validator.InteractSpeedMultiplier > 0 ? validator.InteractSpeedMultiplier : 1.0f;
            return requiredTime;
        }

        private void ExecuteAction(IPlayer player, BlockSelection? selection, CarriedBlock? carriedTarget, EntityBehaviorAttachableCarryable? attachableCarryBehavior)
        {
            var clientChannel = this.clientChannel;
            var carryManager = this.carryManager;

            switch (Interaction.CarryAction)
            {
                case CarryAction.Interact:
                    ContinueInteractAction(player, selection, clientChannel);
                    break;

                case CarryAction.PickUp:
                    ContinuePickUpAction(player, selection, carryManager, clientChannel);
                    break;

                case CarryAction.PlaceDown:
                    ContinuePlaceDownAction(player, selection, carriedTarget, carryManager, clientChannel);
                    break;

                case CarryAction.SwapBack:
                    ContinueSwapBackAction(player, clientChannel);
                    break;

                case CarryAction.PickupEntity:
                    ContinuePickupEntityAction(player, clientChannel);
                    break;

                case CarryAction.Attach:
                    ContinueAttachAction(attachableCarryBehavior, clientChannel);
                    break;

                case CarryAction.Detach:
                    ContinueDetachAction(attachableCarryBehavior, clientChannel);
                    break;

                case CarryAction.Put:
                    ContinuePutAction(player, clientChannel);
                    break;

                case CarryAction.Take:
                    ContinueTakeAction(player, clientChannel);
                    break;
            }
            RefreshPlacedBlockInteractionHelp();
            CompleteInteraction();
        }

        public void CancelInteraction(bool resetTimeHeld = false)
        {
            Interaction.Clear(resetTimeHeld);
            hideOverlay();
        }

        public void CompleteInteraction()
        {
            Interaction.Complete();
            hideOverlay();
        }

        public void RefreshPlacedBlockInteractionHelp()
        {
            helpRefreshRequested = true;
        }

        public void FlushPlacedBlockInteractionHelpRefresh()
        {
            if (!helpRefreshRequested) return;
            helpRefreshRequested = false;

            if (HudHelp == null || composeBlockWorldInteractionHelpMethod == null) return;

            try
            {
                composeBlockWorldInteractionHelpMethod.Invoke(HudHelp, null);
            }
            catch (Exception e)
            {
                this.api.Logger.Error($"Failed to refresh placed block interaction help (Disabling further calls): {e}");
                HudHelp = null;
                composeBlockWorldInteractionHelpMethod = null;
            }
        }
    }
}
