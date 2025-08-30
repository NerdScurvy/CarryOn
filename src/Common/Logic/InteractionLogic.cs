using System;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Enums;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using static CarryOn.CarrySystem;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using HarmonyLib;
using CarryOn.API.Common.Models;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Logic
{
    public class InteractionLogic
    {
        private ICoreClientAPI api;
        private CarrySystem carrySystem;

        public CarryInteraction Interaction { get; private set; } = new CarryInteraction();

        public Vintagestory.Client.NoObf.HudElementInteractionHelp HudHelp { get; set; }

        private bool? allowSprintWhileCarrying;
        private bool? backSlotEnabled;
        private bool? removeInteractDelayWhileCarrying;
        private float? interactSpeedMultiplier;

        private TransferLogic transferLogic;

        public bool RemoveInteractDelayWhileCarrying => removeInteractDelayWhileCarrying ??= this.carrySystem?.Config?.CarryOptions?.RemoveInteractDelayWhileCarrying ?? false;
        public bool AllowSprintWhileCarrying => allowSprintWhileCarrying ??= this.carrySystem?.Config?.CarryOptions?.AllowSprintWhileCarrying ?? false;
        public bool BackSlotEnabled => backSlotEnabled ??= this.carrySystem?.Config?.CarryOptions?.BackSlotEnabled ?? false;
        public float InteractSpeedMultiplier => interactSpeedMultiplier ??= this.carrySystem.Config.CarryOptions?.InteractSpeedMultiplier ?? 1.0f;


        public InteractionLogic(ICoreClientAPI api, CarrySystem carrySystem)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.carrySystem = carrySystem ?? throw new ArgumentNullException(nameof(carrySystem));
            this.transferLogic = new TransferLogic(api, carrySystem);
        }

        public void TryBeginInteraction(bool isInteracting, ref EnumHandling handled)
        {
            // If an action is currently ongoing, ignore the game's entity action.
            if (Interaction.CarryAction != CarryAction.None)
            { handled = EnumHandling.PreventDefault; return; }

            var world = this.api.World;
            var player = world.Player;

            // Check if player has item in active active or offhand slot
            if (!player.Entity.CanDoCarryAction(requireEmptyHanded: true))
            {
                // Prevent further carry interaction checks
                return;
            }

            if (isInteracting)
            {
                if (BeginEntityCarryableInteraction(ref handled)) return;
                if (BeginSwapBackInteraction(ref handled)) return;
                if (BeginBlockEntityInteraction(ref handled)) return;
                if (BeginTransferInteraction(ref handled)) return;
                if (BeginBlockCarryableInteraction(ref handled)) return;
            }

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            // If player is carrying something in their hands or an interaction is in progress then prevent default interactions
            if ((carriedHands != null) || (isInteracting && (Interaction.TimeHeld > 0.0F)))
                handled = EnumHandling.PreventDefault;
        }

        /// <summary>
        /// Client side game tick handler for player carry interactions
        /// </summary>
        /// <param name="deltaTime"></param>
        public void TryContinueInteraction(float deltaTime)
        {

            var world = this.api.World;
            var player = world.Player;
            var input = this.api.Input;

            if (!input.InWorldMouseButton.Right) { CancelInteraction(resetTimeHeld: true); return; }

            if (Interaction.CarryAction == CarryAction.None || Interaction.CarryAction == CarryAction.Done) return;

            var requireEmpty = (Interaction.CarryAction != CarryAction.PlaceDown) || (Interaction.CarrySlot != CarrySlot.Hands);

            if (Interaction.CarryAction != CarryAction.Interact && !player.Entity.CanInteract(requireEmptyHanded: requireEmpty))
            { CancelInteraction(resetTimeHeld: true); return; }

            var carriedTarget = Interaction.CarrySlot.HasValue ? player.Entity.GetCarried(Interaction.CarrySlot.Value) : null;
            var holdingAny = player.Entity.GetCarried(CarrySlot.Hands)
                             ?? player.Entity.GetCarried(CarrySlot.Shoulder);
            BlockSelection selection = null;
            BlockBehaviorCarryable carryBehavior = null;
            BlockBehaviorCarryableInteract interactBehavior = null;
            EntityBehaviorAttachableCarryable attachableCarryBehavior = null;

            switch (Interaction.CarryAction)
            {
                case CarryAction.Interact:
                case CarryAction.PickUp:
                case CarryAction.PlaceDown:

                    // Ensure the player hasn't in the meantime
                    // picked up / placed down something somehow.
                    if (Interaction.CarryAction == CarryAction.PickUp == (holdingAny != null))
                    { CancelInteraction(); return; }

                    selection = (Interaction.CarryAction == CarryAction.PlaceDown) ? player.CurrentBlockSelection : BlockUtils.GetMultiblockOriginSelection(world.BlockAccessor, player.CurrentBlockSelection);

                    var position = (Interaction.CarryAction == CarryAction.PlaceDown)
                        ? BlockUtils.GetPlacedPosition(world.BlockAccessor, player?.CurrentBlockSelection, carriedTarget.Block)
                        : selection?.Position;

                    // Make sure the player is still looking at the same block.
                    if (Interaction.TargetBlockPos != position)
                    { CancelInteraction(); return; }

                    if (Interaction.CarryAction == CarryAction.Interact)
                    {
                        interactBehavior = selection?.Block.GetBehavior<BlockBehaviorCarryableInteract>();
                        break;
                    }
                    // Get the block behavior from either the block
                    // to be picked up or the currently carried block.
                    carryBehavior = (Interaction.CarryAction == CarryAction.PickUp)
                        ? selection?.Block?.GetBehaviorOrDefault(BlockBehaviorCarryable.Default)
                        : carriedTarget?.GetCarryableBehavior();
                    break;

                case CarryAction.SwapBack:
                    if (!BackSlotEnabled) return;

                    var carriedBack = player.Entity.GetCarried(CarrySlot.Back);
                    // Get the carry behavior from from hands slot unless null, then from back slot.
                    carryBehavior = (carriedTarget != null) ? carriedTarget?.GetCarryableBehavior() : carriedBack?.GetCarryableBehavior();
                    if (carryBehavior == null)
                    {
                        this.api.Logger.Debug("Nothing carried. Player may have dropped the block from being damaged");
                        return;
                    }
                    // Make sure the block to swap can still be put in that slot. TODO: check code - this returns if block behaviour has no allowed slots
                    if (carryBehavior.Slots[Interaction.CarrySlot.Value] == null) return;

                    break;

                case CarryAction.Attach:
                case CarryAction.Detach:
                    attachableCarryBehavior = Interaction.TargetEntity?.GetBehavior<EntityBehaviorAttachableCarryable>();
                    break;

                case CarryAction.Put:
                case CarryAction.Take:
                    break;

                default: return;
            }

            float requiredTime;

            if (Interaction.TransferDelay.HasValue)
            {
                requiredTime = Interaction.TransferDelay.Value;
            }
            else if (Interaction.CarryAction is CarryAction.Put or CarryAction.Take)
            {
                requiredTime = carryBehavior?.TransferDelay ?? Default.TransferSpeed;
            }
            else if (Interaction.CarryAction == CarryAction.Interact)
            {
                if (RemoveInteractDelayWhileCarrying) requiredTime = 0;
                else requiredTime = interactBehavior?.InteractDelay ?? Default.InteractSpeed;
            }
            else
            {

                requiredTime = carryBehavior?.InteractDelay ?? Default.PickUpSpeed;
                switch (Interaction.CarryAction)
                {
                    case CarryAction.PlaceDown: requiredTime *= Default.PlaceSpeed; break;
                    case CarryAction.SwapBack: requiredTime *= Default.SwapSpeed; break;
                }
            }

            requiredTime /= InteractSpeedMultiplier > 0 ? InteractSpeedMultiplier : 1.0f;

            Interaction.TimeHeld += deltaTime;
            var progress = Interaction.TimeHeld / requiredTime;
            this.carrySystem.HudOverlayRenderer.CircleProgress = progress;
            if (progress <= 1.0F) return;

            string failureCode = null;
            string onScreenErrorMessage = null;

            switch (Interaction.CarryAction)
            {
                case CarryAction.Interact:
                    if (selection?.Block?.OnBlockInteractStart(world, player, selection) == true)
                        this.carrySystem.ClientChannel.SendPacket(new InteractMessage(selection.Position));
                    break;

                case CarryAction.PickUp:
                    var hasPickedUp = this.carrySystem.CarryManager.TryPickUp(player.Entity, selection.Position, Interaction.CarrySlot.Value, checkIsCarryable: true, playSound: true);
                    if (hasPickedUp)
                        this.carrySystem.ClientChannel.SendPacket(new PickUpMessage(selection.Position, Interaction.CarrySlot.Value));
                    else
                    {
                        // This else block executes when the attempt to pick up the item fails.
                        // Showing an error message here informs the player that the pick-up action was unsuccessful.
                        this.api.TriggerIngameError("carryon", "pick-up-failed", GetLang("pick-up-failed"));
                    }
                    break;

                case CarryAction.PlaceDown:
                    if (this.carrySystem.CarryManager.TryPlaceDownAt(player, carriedTarget, selection, out var placedAt, ref failureCode))
                        this.carrySystem.ClientChannel.SendPacket(new PlaceDownMessage(Interaction.CarrySlot.Value, selection, placedAt));
                    else
                    {
                        // Show in-game error if placing down failed.
                        if (failureCode != null && failureCode != FailureCode.Ignore)
                        {
                            this.api.TriggerIngameError("carryon", failureCode, GetLang("place-down-failed-" + failureCode));
                        }
                        else
                        {
                            this.api.TriggerIngameError("carryon", "place-down-failed", GetLang("place-down-failed"));
                        }
                    }
                    break;

                case CarryAction.SwapBack:
                    if (player.Entity.SwapCarried(Interaction.CarrySlot.Value, CarrySlot.Back))
                        this.carrySystem.ClientChannel.SendPacket(new SwapSlotsMessage(CarrySlot.Back, Interaction.CarrySlot.Value));
                    break;

                case CarryAction.Attach:
                    if (Interaction.TargetEntity == null) break;
                    this.carrySystem.ClientChannel.SendPacket(new AttachMessage(Interaction.TargetEntity.EntityId, Interaction.TargetSlotIndex.Value));
                    attachableCarryBehavior.OnAttachmentToggled(true, player.Entity, Interaction.Slot, Interaction.TargetSlotIndex.Value);
                    break;

                case CarryAction.Detach:
                    if (Interaction.TargetEntity == null) break;
                    this.carrySystem.ClientChannel.SendPacket(new DetachMessage(Interaction.TargetEntity.EntityId, Interaction.TargetSlotIndex.Value));
                    attachableCarryBehavior.OnAttachmentToggled(false, player.Entity, Interaction.Slot, Interaction.TargetSlotIndex.Value);
                    break;

                case CarryAction.Put:
                    var putMessage = new PutMessage()
                    {
                        BlockPos = Interaction.TargetBlockPos,
                        Index = Interaction.TargetSlotIndex ?? -1
                    };

                    // Call Client side
                    if (!this.transferLogic.TryPutCarryable(player, putMessage, out failureCode, out onScreenErrorMessage))
                    {
                        if (failureCode != FailureCode.Continue)
                        {
                            if (onScreenErrorMessage != null)
                            {
                                this.api.TriggerIngameError(ModId, failureCode, onScreenErrorMessage);
                            }
                            this.api.Logger.Debug($"Failed client side: {failureCode} : {onScreenErrorMessage}");
                            break;
                        }
                    }

                    if (failureCode == FailureCode.Stop) break;
                    // Call Server side
                    this.carrySystem.ClientChannel.SendPacket(putMessage);
                    break;

                case CarryAction.Take:
                    var takeMessage = new TakeMessage()
                    {
                        BlockPos = Interaction.TargetBlockPos,
                        Index = Interaction.TargetSlotIndex ?? -1
                    };

                    // Call Client side
                    if (!this.transferLogic.TryTakeCarryable(player, takeMessage, out failureCode, out onScreenErrorMessage))
                    {
                        if (failureCode != FailureCode.Continue)
                        {
                            if (onScreenErrorMessage != null)
                            {
                                this.api.TriggerIngameError(ModId, failureCode, onScreenErrorMessage);
                            }

                            this.api.Logger.Debug($"Failed client side: {failureCode} : {onScreenErrorMessage}");
                            break;
                        }
                    }

                    if (failureCode == FailureCode.Stop) break;
                    // Call Server side
                    this.carrySystem.ClientChannel.SendPacket(takeMessage);
                    break;
            }
            RefreshPlacedBlockInteractionHelp();
            CompleteInteraction();
        }


        // Cancels the current interaction and resets the interaction state ready for next interaction.
        public void CancelInteraction(bool resetTimeHeld = false)
        {
            Interaction.Clear(resetTimeHeld);
            this.carrySystem.HudOverlayRenderer.CircleVisible = false;
        }

        // Completes the current interaction and resets the interaction state but does not allow for a new interaction until mouse button is released.
        public void CompleteInteraction()
        {
            Interaction.Complete();
            this.carrySystem.HudOverlayRenderer.CircleVisible = false;
        }

        /// <summary>
        /// Triggers a refresh of the block interaction help.
        /// </summary>
        public void RefreshPlacedBlockInteractionHelp()
        {
            if (HudHelp == null) return;
            try
            {
                var method = AccessTools.Method(typeof(Vintagestory.Client.NoObf.HudElementInteractionHelp), "ComposeBlockWorldInteractionHelp");
                if (method == null)
                {
                    this.api.Logger.Error("Failed to find method ComposeBlockWorldInteractionHelp via reflection.");
                    HudHelp = null;
                    return;
                }

                // Validate method signature
                var parameters = method.GetParameters();
                if (parameters.Length != 0)
                {
                    this.api.Logger.Error($"Unexpected method signature for ComposeBlockWorldInteractionHelp: expected 0 parameters, got {parameters.Length}");
                    HudHelp = null;
                    return;
                }

                method.Invoke(HudHelp, null);
            }
            catch (Exception e)
            {
                this.api.Logger.Error($"Failed to refresh placed block interaction help (Disabling further calls): {e}");
                HudHelp = null;
            }
        }



        /// <summary> Returns the first "action" slot (either Hands or Shoulder)
        ///           that satisfies the specified function, or null if none. </summary>
        private static CarrySlot? FindActionSlot(System.Func<CarrySlot, bool> func)
        {
            if (func(CarrySlot.Hands)) return CarrySlot.Hands;
            if (func(CarrySlot.Shoulder)) return CarrySlot.Shoulder;
            return null;
        }

        /// <summary>
        /// Begins interaction with entity to attach or detach a carried block if conditions are met.
        /// </summary>
        /// <param name="handled"></param>
        /// <returns></returns>
        private bool BeginEntityCarryableInteraction(ref EnumHandling handled)
        {
            var world = this.api.World;
            var player = world.Player;

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            var carryAttachBehavior = player.CurrentEntitySelection?.Entity?.GetBehavior<EntityBehaviorAttachableCarryable>();

            bool isLookingAtEntity = player.CurrentEntitySelection != null;
            bool entityHasAttachable = carryAttachBehavior != null;
            bool carryKeyHeld = api.Input.IsCarryKeyPressed();

            bool shouldPreventInteraction = (isLookingAtEntity && !entityHasAttachable) || (entityHasAttachable && !carryKeyHeld);
            if (shouldPreventInteraction) return true;

            if (entityHasAttachable)
            {
                var entitySelection = player.CurrentEntitySelection;

                int selBoxIndex = entitySelection?.SelectionBoxIndex ?? -1;
                int slotIndex = carryAttachBehavior.GetSlotIndex(selBoxIndex);


                var behaviorAttachable = entitySelection?.Entity.GetBehavior<EntityBehaviorAttachable>();

                if (behaviorAttachable == null)
                {
                    this.api.Logger.Error("EntityBehaviorAttachable not found on entity {0}", entitySelection?.Entity?.Code);
                    this.api.TriggerIngameError("carryon", "attachable-not-found", GetLang("attachable-behavior-not-found"));
                    return true;
                }

                Interaction.TargetSlotIndex = behaviorAttachable.GetSlotIndexFromSelectionBoxIndex(selBoxIndex - 1);
                Interaction.TargetEntity = entitySelection?.Entity;
                Interaction.Slot = carryAttachBehavior.GetItemSlot(slotIndex);

                if (Interaction.Slot == null)
                {
                    // This is probably a seat interaction. Not showng error
                    CompleteInteraction();
                    return true;
                }
                if (carriedHands != null)
                {
                    if (!Interaction.Slot.Empty)
                    {
                        this.api.TriggerIngameError("carryon", "slot-not-empty", GetLang("slot-not-empty"));
                        CompleteInteraction();
                        handled = EnumHandling.PreventDefault;
                        return true;
                    }
                    Interaction.CarryAction = CarryAction.Attach;
                }
                else
                {
                    if (Interaction.Slot.Empty)
                    {
                        this.api.TriggerIngameError("carryon", "slot-empty", GetLang("slot-empty"));
                        CompleteInteraction();
                        return true;
                    }

                    if (Interaction.Slot?.Itemstack?.Block?.GetBehavior<BlockBehaviorCarryable>() == null)
                    {
                        // Item in slot is not carryable by CarryOn - e.g. Oar or Lantern
                        // Let default interaction handle it - required for when players have different keybinds
                        CompleteInteraction();
                        return true;
                    }
                    Interaction.CarryAction = CarryAction.Detach;
                }
                // Prevent default action. Don't want to interact with blocks/entities.
                handled = EnumHandling.PreventDefault;
                return true;
            }

            // Entity action not handled
            return false;
        }

        /// <summary>
        /// Begins the interaction to swap the carried item from hands to back slot if conditions are met.
        /// </summary>
        /// <param name="handled"></param>
        /// <returns></returns>
        private bool BeginSwapBackInteraction(ref EnumHandling handled)
        {
            var backSlotEnabled = this.carrySystem?.Config?.CarryOptions?.BackSlotEnabled ?? false;

            if (!backSlotEnabled) return false;

            var world = this.api.World;
            var player = world.Player;
            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);
            var carriedBack = player.Entity.GetCarried(CarrySlot.Back);

            if (!player.Entity.CanInteract(requireEmptyHanded: true))
            {
                return false;
            }

            // Can player carry target block
            bool canCarryTarget = player.CurrentBlockSelection?.Block?.IsCarryable(CarrySlot.Hands) == true;

            // Swap back conditions: When carry key is held down and one of the following is true:
            // 1. The carry swap key is pressed
            // 2. The player is not targeting a block
            // 3. The player has empty hands but has something in back slot and the target block is not carryable
            bool carryKeyHeld = api.Input.IsCarryKeyPressed();
            bool swapKeyPressed = api.Input.IsCarrySwapBackKeyPressed();
            bool notTargetingBlock = player.CurrentBlockSelection == null;
            bool canSwapBackFromBackSlot = !canCarryTarget && carriedBack != null && carriedHands == null;

            if (carryKeyHeld && (swapKeyPressed || notTargetingBlock || canSwapBackFromBackSlot))
            {

                if (carriedHands == null && !notTargetingBlock)
                {
                    // Don't allow swap back operation if the player is looking at a container or ground storage with empty hands.
                    var isContainer = player.CurrentBlockSelection?.Block?.HasBehavior<BlockBehaviorContainer>() ?? false;
                    var isGroundStorage = player.CurrentBlockSelection?.Block?.Code == "groundstorage";

                    if (isContainer || isGroundStorage)
                    {
                        CompleteInteraction();
                        return true;
                    }
                }

                if (carriedHands != null)
                {
                    if (carriedHands.GetCarryableBehavior().Slots[CarrySlot.Back] == null)
                    {
                        this.api.TriggerIngameError("carryon", "cannot-swap-back", GetLang("cannot-swap-back"));
                        CompleteInteraction();
                        return true;
                    }
                }

                if (carriedHands == null && carriedBack == null)
                {
                    // If nothing is being carried, do not allow swap back.
                    this.api.TriggerIngameError("carryon", "nothing-carried", GetLang("nothing-carried"));
                    CompleteInteraction();
                    return true;
                }

                Interaction.CarryAction = CarryAction.SwapBack;
                // This is always Hands. If Shoulder is ever implemented, this will need to change.
                Interaction.CarrySlot = CarrySlot.Hands;
                handled = EnumHandling.PreventDefault;

                return true;
            }

            return false;
        }


        /// <summary>
        /// Begins an interaction with a block entity, such as opening a door or chest, if the conditions are met.
        /// </summary>
        /// <param name="handled"></param>
        /// <returns></returns>
        private bool BeginBlockEntityInteraction(ref EnumHandling handled)
        {
            var world = this.api.World;
            var player = world.Player;
            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            // Escape if player is not holding anything in hands.
            if (carriedHands == null)
            {
                return false;
            }

            if (!player.Entity.CanInteract(requireEmptyHanded: carriedHands == null))
            {
                var selection = player.CurrentBlockSelection;
                selection = BlockUtils.GetMultiblockOriginSelection(world.BlockAccessor, selection);

                // Cannot pick up or put down - check for interact behavior such as open door or chest.
                if (selection?.Block?.HasBehavior<BlockBehaviorCarryableInteract>() == true)
                {
                    var interactBehavior = selection?.Block.GetBehavior<BlockBehaviorCarryableInteract>();
                    if (interactBehavior.CanInteract(player))
                    {
                        Interaction.CarryAction = CarryAction.Interact;
                        Interaction.TargetBlockPos = selection.Position;
                        handled = EnumHandling.PreventDefault;
                        return true;
                    }
                }

            }
            return false;
        }

        /// <summary>
        /// Begins a block carryable interaction if the conditions are met.
        /// </summary>
        /// <param name="handled"></param>
        /// <returns></returns>
        private bool BeginBlockCarryableInteraction(ref EnumHandling handled)
        {
            var world = this.api.World;
            var player = world.Player;

            // Escape early if carry key is not held down.
            if (!api.Input.IsCarryKeyPressed())
            {
                return false;
            }

            var selection = player.CurrentBlockSelection;
            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            // If something's being held..
            if (carriedHands != null)
            {
                // ..and aiming at block, try to place it.
                if (selection != null)
                {
                    // If carrying something in-hand, don't require empty hands.
                    // This shouldn't occur since nothing is supposed to go into
                    // an active slot that is locked. This is
                    // just in case, so a carried block can still be placed down.
                    if (!player.Entity.CanInteract(requireEmptyHanded: carriedHands == null))
                    {
                        handled = EnumHandling.PreventDefault;
                        return true;
                    }
                    var blockPos = BlockUtils.GetPlacedPosition(world.BlockAccessor, selection, carriedHands.Block);
                    if (blockPos == null) return true;

                    if (!player.Entity.HasPermissionToCarry(blockPos))
                    {
                        this.api.TriggerIngameError(ModId, "place-down-no-permission", GetLang("place-down-no-permission"));
                        handled = EnumHandling.PreventDefault;
                        return false;
                    }


                    Interaction.TargetBlockPos = blockPos;
                    Interaction.CarryAction = CarryAction.PlaceDown;
                    Interaction.CarrySlot = carriedHands.Slot;
                    handled = EnumHandling.PreventDefault;
                    return true;
                }
            }
            // If nothing's being held..
            else if (player.Entity.CanInteract(requireEmptyHanded: true))
            {
                if (selection != null) selection = BlockUtils.GetMultiblockOriginSelection(world.BlockAccessor, selection);

                if ((selection?.Block != null) && (Interaction.CarrySlot = FindActionSlot(slot => selection.Block.IsCarryable(slot))) != null)
                {
                    Interaction.CarryAction = CarryAction.PickUp;
                    Interaction.TargetBlockPos = selection.Position?.Copy();
                    handled = EnumHandling.PreventDefault;
                    return true;
                }

            }
            return false;
        }

        /// <summary>
        /// Begins the transfer interaction between the player and a block entity if the conditions are met.
        /// </summary>
        /// <param name="handled"></param>
        /// <returns></returns>
        private bool BeginTransferInteraction(ref EnumHandling handled)
        {
            var world = this.api.World;
            var player = world.Player;

            // Escape early if carry key is not held down.
            if (!api.Input.IsCarryKeyPressed())
            {
                return false;
            }

            var selection = player.CurrentBlockSelection;
            if (selection == null)
            {
                // Not pointing at a block, cannot transfer
                return false;
            }

            var blockEntity = world.BlockAccessor.GetBlockEntity(selection.Position);
            if (blockEntity?.Block == null)
            {
                return false;
            }

            var carryableBehavior = blockEntity.Block.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null || !carryableBehavior.TransferEnabled)
            {
                return false;
            }

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            string failureCode;
            string onScreenErrorMessage;
            float? transferDelay;

            if (carriedHands == null)
            {
                if (!this.transferLogic.CanTakeCarryable(player, blockEntity, selection.SelectionBoxIndex, out transferDelay, out failureCode, out onScreenErrorMessage))
                {
                    return HandleCanTransferResponse(failureCode, onScreenErrorMessage, ref handled);
                }

                if (HandleCanTransferResponse(failureCode, null, ref handled))
                    return true;

                Interaction.CarryAction = CarryAction.Take;
            }
            else
            {
                if (!this.transferLogic.CanPutCarryable(player, blockEntity, selection.SelectionBoxIndex, out transferDelay, out failureCode, out onScreenErrorMessage))
                {
                    return HandleCanTransferResponse(failureCode, onScreenErrorMessage, ref handled);
                }

                if (HandleCanTransferResponse(failureCode, null, ref handled))
                    return true;

                Interaction.CarryAction = CarryAction.Put;
            }
            Interaction.TransferDelay = transferDelay;
            Interaction.TargetBlockPos = selection.Position;
            Interaction.TargetSlotIndex = selection.SelectionBoxIndex;
            handled = EnumHandling.PreventDefault;
            return true;
        }

        /// <summary>
        /// Helper for transfer interaction error handling
        /// </summary>
        /// <param name="failureCode"></param>
        /// <param name="onScreenErrorMessage"></param>
        /// <param name="handled"></param>
        /// <returns></returns>
        private bool HandleCanTransferResponse(string failureCode, string onScreenErrorMessage, ref EnumHandling handled)
        {
            if (onScreenErrorMessage != null)
            {
                this.api.TriggerIngameError(CarryCode.ModId, failureCode, onScreenErrorMessage);
                CompleteInteraction();
                handled = EnumHandling.PreventDefault;
                return true;
            }

            if (failureCode == FailureCode.Default)
            {
                CompleteInteraction();
                return true;
            }

            if (failureCode == FailureCode.Stop)
            {
                handled = EnumHandling.PreventDefault;
                CompleteInteraction();
                return true;
            }

            return false;
        }
    }
}