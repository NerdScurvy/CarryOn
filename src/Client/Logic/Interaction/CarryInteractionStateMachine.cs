using System;
using System.Reflection;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;
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
        private readonly Action triggerHandsHighlight;
        private readonly Action triggerBackHighlight;
        private readonly CarryInteractionValidator validator;
        private readonly ClientModConfig? clientModConfig;

        public CarryInteraction Interaction { get; private set; } = new CarryInteraction();

        internal Vintagestory.Client.NoObf.HudElementInteractionHelp? HudHelp { get; set; }
        private MethodInfo? composeBlockWorldInteractionHelpMethod;
        private bool helpRefreshRequested;

        private CarriedBlock? cachedCarriedTarget;
        private bool cachedCarriedHandsPresent;
        private CarriedBlock? cachedCarriedBack;
        private CarriedBlockReadResult? cachedPlaceDownRead;
        private ICarryableEntitySlotHandler? cachedSlotHandler;
        private ICarryableEntityPickupHandler? cachedEntityPickupHandler;

        public CarryInteractionStateMachine(
            ICoreClientAPI api,
            ICarryManager carryManager,
            IClientNetworkChannel clientChannel,
            Action hideOverlay,
            Action<float> setOverlayProgress,
            Action triggerHandsHighlight,
            Action triggerBackHighlight,
            CarryInteractionValidator validator,
            ClientModConfig? clientModConfig = null)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.carryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
            this.clientChannel = clientChannel ?? throw new ArgumentNullException(nameof(clientChannel));
            this.hideOverlay = hideOverlay ?? throw new ArgumentNullException(nameof(hideOverlay));
            this.setOverlayProgress = setOverlayProgress ?? throw new ArgumentNullException(nameof(setOverlayProgress));
            this.triggerHandsHighlight = triggerHandsHighlight ?? throw new ArgumentNullException(nameof(triggerHandsHighlight));
            this.triggerBackHighlight = triggerBackHighlight ?? throw new ArgumentNullException(nameof(triggerBackHighlight));
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
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

        public void BeginInteraction(CarryInteraction? interaction)
        {
            Interaction = interaction ?? new CarryInteraction();
            RefreshInteractionCache();
        }

        private void RefreshInteractionCache()
        {
            var player = this.api.World.Player;
            var carryable = Interaction.TargetEntity?.GetBehavior<EntityBehaviorCarryable>();

            cachedCarriedTarget = Interaction.Slot.HasValue ? this.carryManager.GetCarried(player.Entity, Interaction.Slot.Value) : null;
            cachedCarriedHandsPresent = this.carryManager.GetCarried(player.Entity, CarrySlot.Hands) != null
                || this.carryManager.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands);
            cachedCarriedBack = this.carryManager.GetCarried(player.Entity, CarrySlot.Back);
            cachedSlotHandler = carryable?.SlotHandler;
            cachedEntityPickupHandler = carryable?.Handler;
        }

        private void ClearInteractionCache()
        {
            cachedCarriedTarget = null;
            cachedCarriedHandsPresent = false;
            cachedCarriedBack = null;
            cachedPlaceDownRead = null;
            cachedSlotHandler = null;
            cachedEntityPickupHandler = null;
        }

        public void TryContinueInteraction(float deltaTime)
        {
            var world = this.api.World;
            var player = world.Player;
            var input = this.api.Input;

            if (!input.InWorldMouseButton.Right) { CancelInteraction(resetTimeHeld: true); return; }

            if (Interaction.Action == CarryAction.None || Interaction.Action == CarryAction.Done) return;

            var requireEmpty = (Interaction.Action != CarryAction.PlaceDown) || (Interaction.Slot != CarrySlot.Hands);

            if (Interaction.Action != CarryAction.Interact && !(Interaction.Action == CarryAction.PickupEntity
                ? player.Entity.CanDoCarryAction(requireEmptyHanded: true)
                : player.Entity.CanInteract(requireEmptyHanded: requireEmpty)))
            { CancelInteraction(resetTimeHeld: true); return; }

            switch (Interaction.Action)
            {
                case CarryAction.SwapBack:
                {
                    var swapInspectHands = this.carryManager.GetCarriedReadResult(player.Entity, CarrySlot.Hands, out _);
                    var swapInspectBack = this.carryManager.GetCarriedReadResult(player.Entity, CarrySlot.Back, out _);
                    cachedCarriedTarget = swapInspectHands.Carried;
                    cachedCarriedBack = swapInspectBack.Carried;
                    if (!(swapInspectHands.IsOk || swapInspectHands.IsFailed)
                        && !(swapInspectBack.IsOk || swapInspectBack.IsFailed))
                    {
                        this.api.Logger.Debug("Nothing carried. Player may have dropped the block from being damaged");
                        CancelInteraction();
                        return;
                    }
                    break;
                }

                case CarryAction.PlaceDown:
                    if (Interaction.Slot.HasValue)
                    {
                        var placeRead = this.carryManager.GetCarriedReadResult(player.Entity, Interaction.Slot.Value, out _);
                        cachedPlaceDownRead = placeRead;
                        cachedCarriedTarget = placeRead.Carried;
                    }
                    break;
            }

            var carriedTarget = cachedCarriedTarget;
            BlockSelection? selection = null;
            BlockBehaviorCarryable? carryBehavior = null;
            BlockBehaviorCarryableInteract? interactBehavior = null;
            ICarryableEntitySlotHandler? slotHandler = null;

            switch (Interaction.Action)
            {
                case CarryAction.Interact:
                case CarryAction.PickUp:
                case CarryAction.PickUpGroundStorageBag:
                case CarryAction.PlaceDown:

                    bool pickUpLikeAction = Interaction.Action is CarryAction.PickUp or CarryAction.PickUpGroundStorageBag;
                    if (pickUpLikeAction == cachedCarriedHandsPresent)
                    { CancelInteraction(); return; }

                    selection = (Interaction.Action == CarryAction.PlaceDown) ? player.CurrentBlockSelection : MultiblockUtils.GetMultiblockOriginSelection(world.BlockAccessor, player.CurrentBlockSelection);

                    var position = selection?.Position;

                    if (Interaction.Action == CarryAction.PlaceDown)
                    {
                        var placingUnreadable = cachedPlaceDownRead is { IsFailed: true } placeFailure
                            && placeFailure.Reason == FailureCodes.MissingBlock;
                        if (placingUnreadable)
                        {
                            // Present-but-unreadable carry: its block code is unknown so there is
                            // nothing to predict against; the server handles the drop.
                            position = selection?.Position;
                        }
                        else
                        {
                            if (carriedTarget == null || carriedTarget.ItemStack == null)
                            {
                                CancelInteraction();
                                return;
                            }

                            // Block carries predict against the block's placement cell; item
                            // carries (e.g. a bag from ground storage, Block == null) predict
                            // against the ground-storage cell the stack would occupy.
                            position = carriedTarget.Block != null
                                ? BlockUtils.GetPlacedPosition(world.BlockAccessor, player.CurrentBlockSelection, carriedTarget.Block)
                                : BlockUtils.GetGroundStoragePlacementPosition(world.BlockAccessor, player.CurrentBlockSelection);
                        }

                    }

                    if (Interaction.TargetBlockPos != position)
                    { CancelInteraction(); return; }

                    if (Interaction.Action == CarryAction.Interact)
                    {
                        // Re-validate a ground-storage bag target every frame during the hold.
                        // The begin check is done once, but the player can move the cursor to a
                        // different slot before the progress circle completes; re-checking here
                        // prevents the vanilla OnBlockInteractStart from falling through to an
                        // unwanted pickup/stack interaction on a non-bag slot.
                        if (selection?.Block is Vintagestory.GameContent.BlockGroundStorage
                            && !validator.IsValidGroundStorageBagTarget(selection))
                        { CancelInteraction(); return; }

                        interactBehavior = selection?.Block.GetBehavior<BlockBehaviorCarryableInteract>();
                        break;
                    }

                    if (Interaction.Action == CarryAction.PickUpGroundStorageBag)
                    {
                        // Same re-validation for the bag pickup: the player may have moved the
                        // cursor off the bag slot before the hold completes, in which case the
                        // position check above already cancels, and this guards against a bag
                        // that was taken by another player or removed from ground storage.
                        if (selection?.Block is not Vintagestory.GameContent.BlockGroundStorage
                            || !validator.IsValidGroundStorageBagPickupTarget(selection))
                        { CancelInteraction(); return; }
                        break;
                    }

                    carryBehavior = (Interaction.Action == CarryAction.PickUp)
                        ? selection?.Block?.GetBehaviorOrDefault(BlockBehaviorCarryable.Default)
                        : carriedTarget?.GetCarryableBehavior();
                    break;

                case CarryAction.SwapBack:
                    carryBehavior = (cachedCarriedTarget != null) ? cachedCarriedTarget?.GetCarryableBehavior() : cachedCarriedBack?.GetCarryableBehavior();
                    break;

                case CarryAction.Attach:
                case CarryAction.Detach:
                    slotHandler = cachedSlotHandler;
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

            triggerHandsHighlight();

            if (Interaction.Action == CarryAction.SwapBack)
            {
                triggerBackHighlight();
            }
            if (progress <= 1.0f) return;

            ExecuteAction(player, selection, carriedTarget, slotHandler);
        }

        private void ContinueInteractAction(IPlayer player, BlockSelection? selection, IClientNetworkChannel clientChannel)
        {
            if (selection?.Block?.OnBlockInteractStart(api.World, player, selection) == true)
                clientChannel.SendPacket(new InteractMessage(selection));
        }

        private void ContinuePickUpAction(IPlayer player, BlockSelection? selection, ICarryManager carryManager, IClientNetworkChannel clientChannel)
        {
            if (selection?.Position == null || !Interaction.Slot.HasValue)
            {
                CancelInteraction();
                return;
            }

            string failureCode = FailureCodes.Ignore;

            var hasPickedUp = carryManager.TryPickUp(
                player.Entity,
                selection.Position,
                Interaction.Slot.Value,
                ref failureCode,
                checkIsCarryable: true,
                playSound: true
            );

            if (hasPickedUp)
            {
                bool captureAttached = clientModConfig?.Config?.CaptureAttachedWallSigns ?? true;
                clientChannel.SendPacket(new PickUpMessage(selection.Position, Interaction.Slot.Value)
                {
                    CaptureAttachedWallSigns = captureAttached
                });
            }
            else
            {
                ShowErrorWithFallback(failureCode, FailureCodes.PickUpFailed);
            }
        }

        private void ContinuePlaceDownAction(IPlayer player, BlockSelection? selection, CarriedBlock? carriedTarget, ICarryManager carryManager, IClientNetworkChannel clientChannel)
        {
            if (Interaction.Slot.HasValue
                && cachedPlaceDownRead is { IsFailed: true } unreadablePlace
                && unreadablePlace.Reason == FailureCodes.MissingBlock)
            {
                if (selection == null)
                {
                    CancelInteraction();
                    return;
                }

                // Present-but-unreadable carry (block from a removed/disabled mod): no client-side
                // prediction is possible. Ask the server to drop it as a carried block entity; the
                // outcome is reported back by message.
                clientChannel.SendPacket(new PlaceDownMessage(Interaction.Slot.Value, selection, selection.Position));
                return;
            }

            if (carriedTarget == null || selection == null || !Interaction.Slot.HasValue)
            {
                CancelInteraction();
                return;
            }

            string failureCode = FailureCodes.Ignore;

            var writeTracker = new PlacementWriteTracker(api.World, carriedTarget, selection.Position);

            if (carryManager.TryPlaceDownAt(player, carriedTarget.Slot, selection, out var placedAt, ref failureCode))
            {
                if (placedAt != null)
                {
                    var writtenCells = writeTracker.GetWrittenCells(placedAt);
                    clientChannel.SendPacket(new PlaceDownMessage(Interaction.Slot.Value, selection, placedAt, writtenCells));
                }
                // placedAt == null means entity was spawned - no server message needed.
            }
            else
            {
                ShowErrorWithFallback(failureCode, FailureCodes.PlaceDownFailed);
            }
        }

        private void ShowErrorWithFallback(string failureCode, string defaultCode)
        {
            if (failureCode == FailureCodes.Ignore)
            {
                CarryErrorHelper.ShowError(this.api, defaultCode, defaultCode);
            }
            else
            {
                CarryErrorHelper.ShowError(this.api, failureCode, $"{defaultCode}-{failureCode}");
            }
        }

        private void ContinueSwapBackAction(IPlayer player, IClientNetworkChannel clientChannel)
        {
            if (!Interaction.Slot.HasValue)
            {
                CancelInteraction();
                return;
            }

            // A bag carried in hands (picked up from ground storage) is stowed into the
            // vanilla backpack instead of the back slot - gated on vanilla's worn-bag slot
            // rule (storage flags), so held-only bags like the linen sack stow too.
            // Server-authoritative, no client-side prediction.
            if (Interaction.Slot.Value == CarrySlot.Hands
                && this.carryManager.GetCarried(player.Entity, CarrySlot.Hands)?.ItemStack.IsBackpackStowable() == true)
            {
                clientChannel.SendPacket(new BagToBackpackMessage());
                return;
            }

            if (this.carryManager.SwapCarried(player.Entity, Interaction.Slot.Value, CarrySlot.Back))
                clientChannel.SendPacket(new SwapSlotsMessage(CarrySlot.Back, Interaction.Slot.Value));
        }

        private void ContinuePickUpGroundStorageBagAction(IPlayer player, BlockSelection? selection, IClientNetworkChannel clientChannel)
        {
            if (selection?.Position == null || !Interaction.Slot.HasValue)
            {
                CancelInteraction();
                return;
            }

            if (!validator.IsValidGroundStorageBagPickupTarget(selection))
            {
                CancelInteraction();
                return;
            }

            // No client-side prediction: the bag is removed from the ground storage inventory and
            // carried on the server, which syncs the carry via the entity's watched attributes.
            clientChannel.SendPacket(new PickUpGroundStorageBagMessage(selection));
        }

        private void ContinuePickupEntityAction(IClientNetworkChannel clientChannel)
        {
            if (Interaction.TargetEntity == null || !Interaction.TargetEntity.Alive)
            {
                CancelInteraction();
                return;
            }

            clientChannel.SendPacket(new PickupEntityMessage(Interaction.TargetEntity.EntityId));
        }

        private void ContinueAttachAction(IPlayer player, IClientNetworkChannel clientChannel, ICarryableEntitySlotHandler? slotHandler)
        {
            if (Interaction.TargetEntity == null || Interaction.TargetSlotIndex == null) return;

            var targetEntity = Interaction.TargetEntity;
            var slotIndex = Interaction.TargetSlotIndex.Value;

            clientChannel.SendPacket(new AttachMessage(targetEntity.EntityId, slotIndex));
            slotHandler?.OnClientAttachmentToggled(targetEntity, slotIndex, player, isAttached: true);
        }

        private void ContinueDetachAction(IPlayer player, IClientNetworkChannel clientChannel, ICarryableEntitySlotHandler? slotHandler)
        {
            if (Interaction.TargetEntity == null || Interaction.TargetSlotIndex == null) return;

            var targetEntity = Interaction.TargetEntity;
            var slotIndex = Interaction.TargetSlotIndex.Value;

            clientChannel.SendPacket(new DetachMessage(targetEntity.EntityId, slotIndex));
            slotHandler?.OnClientAttachmentToggled(targetEntity, slotIndex, player, isAttached: false);
        }

        private void ContinuePutAction(IPlayer player, IClientNetworkChannel clientChannel)
        {
            if (Interaction.TargetBlockPos == null || Interaction.TargetSlotIndex == null)
            {
                CancelInteraction();
                return;
            }

            var slotIndex = Interaction.TargetSlotIndex.Value;
            var putMessage = new PutMessage(blockPos: Interaction.TargetBlockPos, index: slotIndex);

            clientChannel.SendPacket(putMessage);
            ResolveTransferHandler()?.OnClientTransferToggled(player, slotIndex, isPut: true);
        }

        private void ContinueTakeAction(IPlayer player, IClientNetworkChannel clientChannel)
        {
            if (Interaction.TargetBlockPos == null || Interaction.TargetSlotIndex == null)
            {
                CancelInteraction();
                return;
            }

            var slotIndex = Interaction.TargetSlotIndex.Value;
            var takeMessage = new TakeMessage(blockPos: Interaction.TargetBlockPos, index: slotIndex);

            clientChannel.SendPacket(takeMessage);
            ResolveTransferHandler()?.OnClientTransferToggled(player, slotIndex, isPut: false);
        }

        private ICarryableBlockSlotHandler? ResolveTransferHandler()
        {
            if (Interaction.TargetBlockPos == null) return null;

            var blockEntity = this.api.World.BlockAccessor.GetBlockEntity(Interaction.TargetBlockPos);
            return blockEntity?.Block?.GetBehavior<BlockBehaviorCarryable>()?.TransferHandler;
        }

        private float CalculateRequiredTime(BlockBehaviorCarryable? carryBehavior, BlockBehaviorCarryableInteract? interactBehavior)
        {
            float requiredTime;

            if (!float.IsNaN(Interaction.TransferDelay))
            {
                requiredTime = Interaction.TransferDelay;
            }
            else if (Interaction.Action is CarryAction.Put or CarryAction.Take)
            {
                requiredTime = carryBehavior?.TransferDelay ?? Defaults.TransferSpeed;
            }
            else if (Interaction.Action == CarryAction.Interact)
            {
                if (validator.RemoveInteractDelayWhileCarrying) requiredTime = 0;
                else requiredTime = interactBehavior?.InteractDelay ?? Defaults.InteractSpeed;
            }
            else if (Interaction.Action is CarryAction.Attach or CarryAction.Detach)
            {
                var targetEntity = Interaction.TargetEntity;
                var slotHandler = cachedSlotHandler;
                var slotIndex = Interaction.TargetSlotIndex ?? 0;
                requiredTime = slotHandler != null && targetEntity != null
                    ? slotHandler.GetInteractDelay(targetEntity, this.api, slotIndex)
                    : Defaults.PickUpSpeed;
            }
            else if (Interaction.Action == CarryAction.PickupEntity)
            {
                var targetEntity = Interaction.TargetEntity;
                var handler = cachedEntityPickupHandler;
                requiredTime = handler != null && targetEntity != null
                    ? handler.GetInteractDelay(targetEntity, this.api)
                    : carryBehavior?.InteractDelay ?? Defaults.PickUpSpeed;
            }
            else
            {

                requiredTime = carryBehavior?.InteractDelay ?? Defaults.PickUpSpeed;
                switch (Interaction.Action)
                {
                    case CarryAction.PlaceDown: requiredTime *= Defaults.PlaceSpeed; break;
                    case CarryAction.SwapBack: requiredTime *= Defaults.SwapSpeed; break;
                }
            }

            requiredTime /= validator.InteractSpeedMultiplier > 0 ? validator.InteractSpeedMultiplier : 1.0f;
            return requiredTime;
        }

        private void ExecuteAction(IPlayer player, BlockSelection? selection, CarriedBlock? carriedTarget, ICarryableEntitySlotHandler? slotHandler)
        {
            var clientChannel = this.clientChannel;
            var carryManager = this.carryManager;

            switch (Interaction.Action)
            {
                case CarryAction.Interact:
                    ContinueInteractAction(player, selection, clientChannel);
                    break;

                case CarryAction.PickUp:
                    ContinuePickUpAction(player, selection, carryManager, clientChannel);
                    break;

                case CarryAction.PickUpGroundStorageBag:
                    ContinuePickUpGroundStorageBagAction(player, selection, clientChannel);
                    break;

                case CarryAction.PlaceDown:
                    ContinuePlaceDownAction(player, selection, carriedTarget, carryManager, clientChannel);
                    break;

                case CarryAction.SwapBack:
                    ContinueSwapBackAction(player, clientChannel);
                    break;

                case CarryAction.PickupEntity:
                    ContinuePickupEntityAction(clientChannel);
                    break;

                case CarryAction.Attach:
                    ContinueAttachAction(player, clientChannel, slotHandler);
                    break;

                case CarryAction.Detach:
                    ContinueDetachAction(player, clientChannel, slotHandler);
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
            ClearInteractionCache();
            hideOverlay();
        }

        public void CompleteInteraction()
        {
            Interaction.Complete();
            ClearInteractionCache();
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
