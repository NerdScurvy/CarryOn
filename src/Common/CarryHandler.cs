using System;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using CarryOn.API.Common;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static CarryOn.CarrySystem;

namespace CarryOn.Common
{
    /// <summary>
    ///   Takes care of core CarryCapacity handling, such as listening to input events,
    ///   picking up, placing and swapping blocks, as well as sending and handling messages.
    /// </summary>
    public class CarryHandler
    {
        public CarryInteraction Interaction { get; set; } = new CarryInteraction();

        public bool IsCarryOnEnabled { get; set; } = true;

        private KeyCombination CarryKeyCombination { get { return CarrySystem.ClientAPI.Input.HotKeys[PickupKeyCode]?.CurrentMapping; } }

        private KeyCombination CarrySwapKeyCombination { get { return CarrySystem.ClientAPI.Input.HotKeys[SwapBackModifierKeyCode]?.CurrentMapping; } }

        private CarrySystem CarrySystem { get; }

        public CarryHandler(CarrySystem carrySystem)
            => CarrySystem = carrySystem;

        public int MaxInteractionDistance { get; set; }

        public void InitClient()
        {
            var cApi = CarrySystem.ClientAPI;
            var input = cApi.Input;

            input.RegisterHotKey(PickupKeyCode, Lang.Get(ModId + ":pickup-hotkey"), PickupKeyDefault);

            input.RegisterHotKey(SwapBackModifierKeyCode, Lang.Get(ModId + ":swap-back-hotkey"), SwapBackModifierDefault);

            input.RegisterHotKey(ToggleKeyCode, Lang.Get(ModId + ":toggle-hotkey"), ToggleDefault, altPressed: true);
            input.RegisterHotKey(QuickDropKeyCode, Lang.Get(ModId + ":quickdrop-hotkey"), QuickDropDefault, altPressed: true, ctrlPressed: true);
            input.RegisterHotKey(ToggleDoubleTapDismountKeyCode, Lang.Get(ModId + ":toggle-double-tap-dismount-hotkey"), ToggleDoubleTapDismountDefault, ctrlPressed: true);

            input.SetHotKeyHandler(ToggleKeyCode, TriggerToggleKeyPressed);
            input.SetHotKeyHandler(QuickDropKeyCode, TriggerQuickDropKeyPressed);
            input.SetHotKeyHandler(ToggleDoubleTapDismountKeyCode, TriggerToggleDoubleTapDismountKeyPressed);

            CarrySystem.ClientChannel.SetMessageHandler<LockSlotsMessage>(OnLockSlotsMessage);

            cApi.Input.InWorldAction += OnEntityAction;
            cApi.Event.RegisterGameTickListener(OnGameTick, 0);

            cApi.Event.BeforeActiveSlotChanged +=
                (_) => OnBeforeActiveSlotChanged(CarrySystem.ClientAPI.World.Player.Entity);
        }



        public void InitServer()
        {
            // TODO: Change this to a config value.
            MaxInteractionDistance = 6;

            CarrySystem.ServerChannel
                .SetMessageHandler<InteractMessage>(OnInteractMessage)
                .SetMessageHandler<PickUpMessage>(OnPickUpMessage)
                .SetMessageHandler<PlaceDownMessage>(OnPlaceDownMessage)
                .SetMessageHandler<SwapSlotsMessage>(OnSwapSlotsMessage)
                .SetMessageHandler<AttachMessage>(OnAttachMessage)
                .SetMessageHandler<DetachMessage>(OnDetachMessage)
                .SetMessageHandler<PutMessage>(OnPutMessage)
                .SetMessageHandler<TakeMessage>(OnTakeMessage)
                .SetMessageHandler<QuickDropMessage>(OnQuickDropMessage)
                .SetMessageHandler<DismountMessage>(OnDismountMessage)
                .SetMessageHandler<PlayerAttributeUpdateMessage>(OnPlayerAttributeUpdateMessage);

            CarrySystem.ServerAPI.Event.OnEntitySpawn += OnServerEntitySpawn;
            CarrySystem.ServerAPI.Event.PlayerNowPlaying += OnServerPlayerNowPlaying;

            CarrySystem.ServerAPI.Event.BeforeActiveSlotChanged +=
                (player, _) => OnBeforeActiveSlotChanged(player.Entity);
        }

        private void OnDismountMessage(IServerPlayer player, DismountMessage message)
        {

            player.Entity.TryUnmount();

            player.Entity.World.GetEntityById(message.EntityId)?
                .GetBehavior<EntityBehaviorCreatureCarrier>()?
                .Seats?.FirstOrDefault(s => s.SeatId == message.SeatId)?
                .Controls?.StopAllMovement();

        }

        private void OnPlayerAttributeUpdateMessage(IServerPlayer player, PlayerAttributeUpdateMessage message)
        {
            var playerEntity = player.Entity;
            if (message.AttributeKey == null)
            {
                return;
            }

            if (message.AttributeKey == DoubleTapDismountEnabledAttributeKey && message.IsWatchedAttribute)
            {
                if (message.BoolValue.HasValue)
                {
                    playerEntity.WatchedAttributes.SetBool(message.AttributeKey, message.BoolValue.Value);
                }
                else
                {
                    playerEntity.WatchedAttributes.RemoveAttribute(message.AttributeKey);
                }

                return;
            }

            playerEntity.Api.Logger.Warning($"Received PlayerAttributeUpdateMessage with unknown attribute key: {message.AttributeKey}");
        }


        public bool TriggerToggleKeyPressed(KeyCombination keyCombination)
        {

            IsCarryOnEnabled = !IsCarryOnEnabled;
            CarrySystem.ClientAPI.ShowChatMessage(Lang.Get(ModId + ":carryon-" + (IsCarryOnEnabled ? "enabled" : "disabled")));
            return true;
        }

        public bool TriggerQuickDropKeyPressed(KeyCombination keyCombination)
        {
            // Send drop message even if client shows nothing being held
            CarrySystem.ClientChannel.SendPacket(new QuickDropMessage());
            return true;
        }

        private bool TriggerToggleDoubleTapDismountKeyPressed(KeyCombination keyCombination)
        {
            var playerEntity = CarrySystem.ClientAPI.World.Player.Entity;
            var isEnabled = playerEntity.WatchedAttributes.GetBool(DoubleTapDismountEnabledAttributeKey, false);

            // Toggle the opposite state 
            playerEntity.WatchedAttributes.SetBool(DoubleTapDismountEnabledAttributeKey, !isEnabled);

            CarrySystem.ClientChannel.SendPacket(new PlayerAttributeUpdateMessage(DoubleTapDismountEnabledAttributeKey, !isEnabled, true));

            CarrySystem.ClientAPI.ShowChatMessage(Lang.Get(ModId + ":double-tap-dismount-" + (!isEnabled ? "enabled" : "disabled")));
            return true;
        }

        public bool IsCarryKeyPressed(bool checkMouse = false)
        {
            var input = CarrySystem.ClientAPI.Input;
            if (checkMouse && !input.InWorldMouseButton.Right) return false;

            return input.KeyboardKeyState[CarryKeyCombination.KeyCode];
        }

        public bool IsCarrySwapKeyPressed()
        {
            return CarrySystem.ClientAPI.Input.KeyboardKeyState[CarrySwapKeyCombination.KeyCode];
        }

        public void OnServerEntitySpawn(Entity entity)
        {
            // We handle player "spawning" in OnServerPlayerJoin.
            // If we send a LockSlotsMessage at this point, the client's player is still null.
            if (entity is EntityPlayer) return;

            // Set this again so walk speed modifiers and animations can be applied.
            foreach (var carried in entity.GetCarried())
                carried.Set(entity, carried.Slot);
        }

        public void OnServerPlayerNowPlaying(IServerPlayer player)
        {
            foreach (var carried in player.Entity.GetCarried())
                carried.Set(player.Entity, carried.Slot);
        }

        /// <summary> Returns the first "action" slot (either Hands or Shoulder)
        ///           that satisfies the specified function, or null if none. </summary>
        private static CarrySlot? FindActionSlot(System.Func<CarrySlot, bool> func)
        {
            if (func(CarrySlot.Hands)) return CarrySlot.Hands;
            if (func(CarrySlot.Shoulder)) return CarrySlot.Shoulder;
            return null;
        }

        /// Begins interaction with entity to attach or detach a carried block if conditions are met.
        private bool BeginEntityCarryableInteraction(ref EnumHandling handled)
        {
            var world = CarrySystem.ClientAPI.World;
            var player = world.Player;

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            var carryAttachBehavior = player.CurrentEntitySelection?.Entity?.GetBehavior<EntityBehaviorAttachableCarryable>();

            bool isLookingAtEntity = player.CurrentEntitySelection != null;
            bool entityHasAttachable = carryAttachBehavior != null;
            bool carryKeyHeld = player.Entity.IsCarryKeyHeld();

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
                    CarrySystem.ClientAPI.Logger.Error("EntityBehaviorAttachable not found on entity {0}", entitySelection?.Entity?.Code);
                    CarrySystem.ClientAPI.TriggerIngameError("carryon", "attachable-not-found", Lang.Get(ModId + ":attachable-behavior-not-found"));
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
                        CarrySystem.ClientAPI.TriggerIngameError("carryon", "slot-not-empty", Lang.Get(ModId + ":slot-not-empty"));
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
                        CarrySystem.ClientAPI.TriggerIngameError("carryon", "slot-empty", Lang.Get(ModId + ":slot-empty"));
                        CompleteInteraction();
                        return true;
                    }

                    if (Interaction.Slot?.Itemstack?.Block?.GetBehavior<BlockBehaviorCarryable>() == null)
                    {
                        CarrySystem.ClientAPI.TriggerIngameError("carryon", "slot-not-carryable", Lang.Get(ModId + ":slot-not-carryable"));
                        CompleteInteraction();
                        handled = EnumHandling.PreventDefault;
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

        // Begins the interaction to swap the carried item from hands to back slot if conditions are met.
        private bool BeginSwapBackInteraction(ref EnumHandling handled)
        {
            if (!ModConfig.BackSlotEnabled) return false;

            var world = CarrySystem.ClientAPI.World;
            var player = world.Player;
            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);
            var carriedBack = player.Entity.GetCarried(CarrySlot.Back);

            if (!CanInteract(player.Entity, true))
            {
                return false;
            }

            // Can player carry target block
            bool canCarryTarget = player.CurrentBlockSelection?.Block?.IsCarryable(CarrySlot.Hands) == true;

            // Swap back conditions: When carry key is held down and one of the following is true:
            // 1. The carry swap key is pressed
            // 2. The player is not targeting a block
            // 3. The player has empty hands but has something in back slot and the target block is not carryable
            bool carryKeyHeld = player.Entity.IsCarryKeyHeld();
            bool swapKeyPressed = IsCarrySwapKeyPressed();
            bool notTargetingBlock = player.CurrentBlockSelection == null;
            bool canSwapBackFromBackSlot = !canCarryTarget && carriedBack != null && carriedHands == null;

            if (carryKeyHeld && (swapKeyPressed || notTargetingBlock || canSwapBackFromBackSlot))
            {

                if (carriedHands != null)
                {
                    if (carriedHands.Behavior.Slots[CarrySlot.Back] == null)
                    {
                        CarrySystem.ClientAPI.TriggerIngameError("carryon", "cannot-swap-back", Lang.Get(ModId + ":cannot-swap-back"));
                        CompleteInteraction();
                        return true;
                    }
                }

                if (carriedHands == null && carriedBack == null)
                {
                    // If nothing is being carried, do not allow swap back.
                    CarrySystem.ClientAPI.TriggerIngameError("carryon", "nothing-carried", Lang.Get(ModId + ":nothing-carried"));
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

        // Begins an interaction with a block entity, such as opening a door or chest, if the conditions are met.
        private bool BeginBlockEntityInteraction(ref EnumHandling handled)
        {
            var world = CarrySystem.ClientAPI.World;
            var player = world.Player;
            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            // Escape if player is not holding anything in hands.
            if (carriedHands == null)
            {
                return false;
            }

            if (!CanInteract(player.Entity, carriedHands == null))
            {
                var selection = player.CurrentBlockSelection;
                selection = GetMultiblockOriginSelection(selection);

                // Cannot pick up or put down - check for interact behavior such as open door or chest.
                if (selection?.Block?.HasBehavior<BlockBehaviorCarryableInteract>() == true)
                {
                    var interactBehavior = selection?.Block.GetBehavior<BlockBehaviorCarryableInteract>();
                    if (interactBehavior.CanInteract(player))
                    {
                        Interaction.CarryAction = CarryAction.Interact;
                        Interaction.SelectedBlockPos = selection.Position;
                        handled = EnumHandling.PreventDefault;
                        return true;
                    }
                }

            }
            return false;
        }

        private bool BeginBlockCarryableInteraction(ref EnumHandling handled)
        {
            var world = CarrySystem.ClientAPI.World;
            var player = world.Player;

            // Escape early if carry key is not held down.
            if (!player.Entity.IsCarryKeyHeld())
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
                    if (!CanInteract(player.Entity, carriedHands == null))
                    {
                        handled = EnumHandling.PreventDefault;
                        return true;
                    }
                    Interaction.SelectedBlockPos = GetPlacedPosition(world, selection, carriedHands.Block);
                    if (Interaction.SelectedBlockPos == null) return true;

                    Interaction.CarryAction = CarryAction.PlaceDown;
                    Interaction.CarrySlot = carriedHands.Slot;
                    handled = EnumHandling.PreventDefault;
                    return true;
                }
            }
            // If nothing's being held..
            else if (CanInteract(player.Entity, true))
            {
                if (selection != null) selection = GetMultiblockOriginSelection(selection);
                // ..and aiming at carryable block, try to pick it up.
                if ((selection?.Block != null) && (Interaction.CarrySlot = FindActionSlot(slot => selection.Block.IsCarryable(slot))) != null)
                {
                    Interaction.CarryAction = CarryAction.PickUp;
                    Interaction.SelectedBlockPos = selection.Position;
                    handled = EnumHandling.PreventDefault;
                    return true;
                }

            }
            return false;
        }

        private bool CanTakeCarryable(IPlayer player, BlockEntity blockEntity, int? index, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = null;
            onScreenErrorMessage = null;

            if (blockEntity == null)
            {
                return false;
            }

            const int failureCodeParam = 3;
            const int onScreenErrorMessageParam = 4;

            const string transferMethod = "CanTakeCarryable";

            bool result;

            var world = player.Entity.World;
            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);


            var carryableBehavior = blockEntity.Block?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior != null)
            {
                if (carryableBehavior.TransferHandlerBehavior == null)
                {
                    // No transfer handler - tell caller to continue with next interaction
                    failureCode = "continue";
                    return false;
                }
                else
                {
                    var transferBehavior = blockEntity.Block?.BlockBehaviors.FirstOrDefault(b => b.GetType().Name == carryableBehavior.TransferHandlerBehavior);
                    if (transferBehavior != null)
                    {
                        try
                        {
                            var method = transferBehavior.GetType().GetMethod(transferMethod);
                            if (method == null)
                            {
                                CarrySystem.Api.Logger.Warning($"CanTakeCarryable method not found on {carryableBehavior.TransferHandlerBehavior}");
                                return false;
                            }

                            object[] parameters = [player, blockEntity, index, null, null];
                            result = method.Invoke(transferBehavior, parameters) as bool? ?? false;

                            failureCode = parameters[failureCodeParam] as string;
                            onScreenErrorMessage = parameters[onScreenErrorMessageParam] as string;

                        }
                        catch (Exception e)
                        {
                            CarrySystem.Api.Logger.Error($"Call to {transferMethod} failed", e);
                            failureCode = "__failure__";
                            onScreenErrorMessage = Lang.Get(CarrySystem.ModId + ":unknown-error");
                            return false;
                        }
                        return result;

                    }
                }
            }
            return false;

        }

        private bool CanPutCarryable(IPlayer player, BlockEntity blockEntity, int? index, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = null;
            onScreenErrorMessage = null;

            if (blockEntity == null)
            {
                return false;
            }

            const int failureCodeParam = 5;
            const int onScreenErrorMessageParam = 6;

            const string transferMethod = "CanPutCarryable";

            bool result;

            var carryableBehavior = blockEntity.Block?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior != null)
            {
                if (carryableBehavior.TransferHandlerBehavior == null)
                {
                    CarrySystem.Api.Logger.Error($"No transferHandlerBehavior defined");
                    failureCode = "__failure__";
                    onScreenErrorMessage = Lang.Get(CarrySystem.ModId + ":unknown-error");
                    return false;
                }
                else
                {
                    var transferBehavior = blockEntity.Block?.BlockBehaviors.FirstOrDefault(b => b.GetType().Name == carryableBehavior.TransferHandlerBehavior);

                    if (transferBehavior == null)
                    {
                        CarrySystem.Api.Logger.Warning($"TransferBehavior {carryableBehavior.TransferHandlerBehavior} not found");
                        return false;
                    }
                    try
                    {
                        var method = transferBehavior.GetType().GetMethod(transferMethod);
                        if (method == null)
                        {
                            CarrySystem.Api.Logger.Warning($"CanPutCarryable method not found on {carryableBehavior.TransferHandlerBehavior}");
                            return false;
                        }

                        var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);
                        // BlockEntity Data will likely be null client side
                        object[] parameters = [player, blockEntity, index, carriedHands?.ItemStack, carriedHands?.BlockEntityData, null, null];
                        result = method?.Invoke(transferBehavior, parameters) as bool? ?? false;

                        failureCode = parameters[failureCodeParam] as string;
                        onScreenErrorMessage = parameters[onScreenErrorMessageParam] as string;

                    }
                    catch (Exception e)
                    {
                        CarrySystem.Api.Logger.Error($"Call to {transferMethod} failed", e);
                        failureCode = "__failure__";
                        onScreenErrorMessage = Lang.Get(CarrySystem.ModId + ":unknown-error");
                        return false;
                    }
                    return result;
                }

            }
            return false;
        }

        private bool BeginTransferInteraction(ref EnumHandling handled)
        {
            var world = CarrySystem.ClientAPI.World;
            var player = world.Player;

            // Escape early if carry key is not held down.
            if (!player.Entity.IsCarryKeyHeld())
            {
                return false;
            }

            string failureCode;
            string onScreenErrorMessage;

            var selection = player.CurrentBlockSelection;
            if (selection == null)
            {
                // Not pointing at a block, cannot transfer
                return false;
            }

            var blockEntity = world.BlockAccessor.GetBlockEntity(selection.Position);
            if (blockEntity == null)
            {
                return false;
            }

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);


            if (carriedHands == null)
            {
                if (!CanTakeCarryable(player, blockEntity, selection.SelectionBoxIndex, out failureCode, out onScreenErrorMessage))
                {
                    if (onScreenErrorMessage != null)
                    {
                        // If error message then send to display then stop all other actions
                        CarrySystem.ClientAPI.TriggerIngameError(ModId, failureCode, onScreenErrorMessage);
                        CompleteInteraction();
                        handled = EnumHandling.PreventDefault;
                        return true;
                    }

                    // Skip further CarryOn interactions and perform default handling
                    if (failureCode is "__default__")
                    {
                        CompleteInteraction();
                        return true;
                    }

                    if (failureCode is "__stop__")
                    {
                        // Skip further CarryOn interactions and prevent default handling
                        handled = EnumHandling.PreventDefault;
                        CompleteInteraction();
                        return true;
                    }

                    // Allow next level of carry behavior interaction
                    return false;
                }

                if (failureCode is "__default__")
                {
                    CompleteInteraction();
                    return true;
                }

                if (failureCode is "__stop__")
                {
                    CompleteInteraction();
                    handled = EnumHandling.PreventDefault;
                    return true;
                }

                CarrySystem.Api.Logger.Debug($"CanTakeCarryable returned true");
                Interaction.CarryAction = CarryAction.Take;
                Interaction.SelectedBlockPos = selection.Position;
                Interaction.TargetSlotIndex = selection.SelectionBoxIndex;
                handled = EnumHandling.PreventDefault;

            }
            else
            {
                if (!CanPutCarryable(player, blockEntity, selection.SelectionBoxIndex, out failureCode, out onScreenErrorMessage))
                {
                    if (onScreenErrorMessage != null)
                    {
                        // If error message then send to display then stop all other actions
                        CarrySystem.ClientAPI.TriggerIngameError(ModId, failureCode, onScreenErrorMessage);
                        CompleteInteraction();
                        handled = EnumHandling.PreventDefault;
                        return true;
                    }

                    // Skip further CarryOn interactions and perform default handling
                    if (failureCode is "__default__")
                    {
                        CompleteInteraction();
                        return true;
                    }

                    if (failureCode is "__stop__")
                    {
                        // Skip further CarryOn interactions and prevent default handling
                        handled = EnumHandling.PreventDefault;
                        CompleteInteraction();
                        return true;
                    }

                    // Allow next level of carry behavior interaction
                    return false;
                }

                if (failureCode is "__default__")
                {
                    CompleteInteraction();
                    return true;
                }

                if (failureCode is "__stop__")
                {
                    CompleteInteraction();
                    handled = EnumHandling.PreventDefault;
                    return true;
                }

                CarrySystem.Api.Logger.Debug($"CanPutCarryable returned true");
                Interaction.CarryAction = CarryAction.Put;
                Interaction.SelectedBlockPos = selection.Position;
                Interaction.TargetSlotIndex = selection.SelectionBoxIndex;
                handled = EnumHandling.PreventDefault;

            }
            return true;
        }

        public void OnEntityAction(EnumEntityAction action, bool on, ref EnumHandling handled)
        {

            if (!on && action == EnumEntityAction.InWorldRightMouseDown)
            {
                Interaction.CarryAction = CarryAction.None;
                return;
            }

            // Only handle action if it's being activated rather than deactivated.
            if (!on || !IsCarryOnEnabled) return;

            bool isInteract;
            switch (action)
            {
                // Right click (interact action) starts carry's pickup and place handling.
                case EnumEntityAction.InWorldRightMouseDown:
                    isInteract = true; break;
                // Other actions, which are prevented while holding something.
                case EnumEntityAction.InWorldLeftMouseDown:
                    isInteract = false;
                    break;
                case EnumEntityAction.Sprint:
                    if (ModConfig.AllowSprintWhileCarrying) return;
                    isInteract = false;
                    break;
                default: return;
            }

            // If an action is currently ongoing, ignore the game's entity action.
            if (Interaction.CarryAction != CarryAction.None)
            { handled = EnumHandling.PreventDefault; return; }

            var world = CarrySystem.ClientAPI.World;
            var player = world.Player;

            // Check if player has item in active active or offhand slot
            if (!player.Entity.CanDoCarryAction(requireEmptyHanded: true))
            {
                // Prevent further carry interaction checks
                return;
            }

            if (isInteract)
            {

                if (BeginEntityCarryableInteraction(ref handled)) return;

                if (BeginSwapBackInteraction(ref handled)) return;

                if (BeginBlockEntityInteraction(ref handled)) return;

                if (BeginTransferInteraction(ref handled)) return;

                if (BeginBlockCarryableInteraction(ref handled)) return;
            }


            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            // If something is being carried in-hand, prevent RMB, LMB and sprint.
            // If still holding RMB after an action completed, prevent the default action as well.
            if ((carriedHands != null) || (isInteract && (Interaction.TimeHeld > 0.0F)))
                handled = EnumHandling.PreventDefault;
        }

        public void OnGameTick(float deltaTime)
        {
            if (!IsCarryOnEnabled) return;

            var world = CarrySystem.ClientAPI.World;
            var player = world.Player;
            var input = CarrySystem.ClientAPI.Input;

            if (!input.InWorldMouseButton.Right) { CancelInteraction(resetTimeHeld: true); return; }

            player.Entity.SetCarryKeyHeld(input.IsHotKeyPressed(PickupKeyCode));

            if (Interaction.CarryAction == CarryAction.None || Interaction.CarryAction == CarryAction.Done) return;


            // TODO: Only allow close blocks to be picked up.
            // TODO: Don't allow the block underneath to change?

            if (Interaction.CarryAction != CarryAction.Interact && !CanInteract(player.Entity, (Interaction.CarryAction != CarryAction.PlaceDown) || (Interaction.CarrySlot != CarrySlot.Hands)))
            { CancelInteraction(); return; }

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

                    selection = (Interaction.CarryAction == CarryAction.PlaceDown) ? player.CurrentBlockSelection : GetMultiblockOriginSelection(player.CurrentBlockSelection);

                    var position = (Interaction.CarryAction == CarryAction.PlaceDown)
                        ? GetPlacedPosition(world, player?.CurrentBlockSelection, carriedTarget.Block)
                        : selection?.Position;

                    // Make sure the player is still looking at the same block.
                    if (Interaction.SelectedBlockPos != position)
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
                        : carriedTarget?.Behavior;
                    break;

                case CarryAction.SwapBack:
                    if (!ModConfig.BackSlotEnabled) return;

                    var carriedBack = player.Entity.GetCarried(CarrySlot.Back);
                    // Get the carry behavior from from hands slot unless null, then from back slot.
                    carryBehavior = (carriedTarget != null) ? carriedTarget?.Behavior : carriedBack?.Behavior;
                    if (carryBehavior == null)
                    {
                        CarrySystem.Api.Logger.Debug("Nothing carried. Player may have dropped the block from being damaged");
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
            if (Interaction.CarryAction == CarryAction.Interact)
            {
                if (ModConfig.RemoveInteractDelayWhileCarrying) requiredTime = 0;
                else requiredTime = interactBehavior?.InteractDelay ?? InteractSpeedDefault;
            }
            else
            {
                requiredTime = carryBehavior?.InteractDelay ?? PickUpSpeedDefault;
                switch (Interaction.CarryAction)
                {
                    case CarryAction.PlaceDown: requiredTime *= PlaceSpeedDefault; break;
                    case CarryAction.SwapBack: requiredTime *= SwapSpeedDefault; break;
                }
            }

            requiredTime /= ModConfig.InteractSpeedMultiplier > 0 ? ModConfig.InteractSpeedMultiplier : 1.0f;

            Interaction.TimeHeld += deltaTime;
            var progress = Interaction.TimeHeld / requiredTime;
            CarrySystem.HudOverlayRenderer.CircleProgress = progress;
            if (progress <= 1.0F) return;

            string failureCode = null;
            string onScreenErrorMessage = null;

            switch (Interaction.CarryAction)
            {
                case CarryAction.Interact:
                    if (selection?.Block?.OnBlockInteractStart(world, player, selection) == true)
                        CarrySystem.ClientChannel.SendPacket(new InteractMessage(selection.Position));
                    break;

                case CarryAction.PickUp:
                    if (player.Entity.Carry(selection.Position, Interaction.CarrySlot.Value))
                        CarrySystem.ClientChannel.SendPacket(new PickUpMessage(selection.Position, Interaction.CarrySlot.Value));
                    break;

                case CarryAction.PlaceDown:

                    if (PlaceDown(player, carriedTarget, selection, out var placedAt, ref failureCode))
                        CarrySystem.ClientChannel.SendPacket(new PlaceDownMessage(Interaction.CarrySlot.Value, selection, placedAt));
                    else
                    {
                        // Show in-game error if placing down failed.
                        if (failureCode != null && failureCode != "__ignore__")
                        {
                            CarrySystem.ClientAPI.TriggerIngameError("carryon", failureCode, Lang.Get(ModId + ":place-down-failed-" + failureCode));
                        }
                        else
                        {
                            CarrySystem.ClientAPI.TriggerIngameError("carryon", "place-down-failed", Lang.Get(ModId + ":place-down-failed"));
                        }
                    }
                    break;

                case CarryAction.SwapBack:
                    if (player.Entity.Swap(Interaction.CarrySlot.Value, CarrySlot.Back))
                        CarrySystem.ClientChannel.SendPacket(new SwapSlotsMessage(CarrySlot.Back, Interaction.CarrySlot.Value));
                    break;

                case CarryAction.Attach:
                    if (Interaction.TargetEntity == null) break;
                    CarrySystem.ClientChannel.SendPacket(new AttachMessage(Interaction.TargetEntity.EntityId, Interaction.TargetSlotIndex.Value));
                    attachableCarryBehavior.OnAttachmentToggled(true, player.Entity, Interaction.Slot, Interaction.TargetSlotIndex.Value);
                    break;

                case CarryAction.Detach:
                    if (Interaction.TargetEntity == null) break;
                    CarrySystem.ClientChannel.SendPacket(new DetachMessage(Interaction.TargetEntity.EntityId, Interaction.TargetSlotIndex.Value));
                    attachableCarryBehavior.OnAttachmentToggled(false, player.Entity, Interaction.Slot, Interaction.TargetSlotIndex.Value);
                    break;

                case CarryAction.Put:
                    var putMessage = new PutMessage()
                    {
                        BlockPos = Interaction.SelectedBlockPos,
                        Index = Interaction?.TargetSlotIndex
                    };

                    // Call Client side
                    if (!TryPutCarryable(player, putMessage, out failureCode, out onScreenErrorMessage))
                    {
                        if (failureCode != "__continue__")
                        {
                            if (onScreenErrorMessage != null)
                            {
                                CarrySystem.ClientAPI.TriggerIngameError(ModId, failureCode, onScreenErrorMessage);
                            }
                            CarrySystem.Api.Logger.Debug($"Failed client side: {failureCode} : {onScreenErrorMessage}");
                            break;
                        }
                    }

                    if (failureCode == "__stop__") break;
                    // Call Server side
                    CarrySystem.ClientChannel.SendPacket(putMessage);
                    break;

                case CarryAction.Take:
                    var takeMessage = new TakeMessage()
                    {
                        BlockPos = Interaction.SelectedBlockPos,
                        Index = Interaction?.TargetSlotIndex
                    };

                    // Call Client side
                    if (!TryTakeCarryable(player, takeMessage, out failureCode, out onScreenErrorMessage))
                    {
                        if (failureCode != "__continue__")
                        {
                            if (onScreenErrorMessage != null)
                            {
                                CarrySystem.ClientAPI.TriggerIngameError(ModId, failureCode, onScreenErrorMessage);
                            }
                                
                            CarrySystem.Api.Logger.Debug($"Failed client side: {failureCode} : {onScreenErrorMessage}");
                            break;
                        }
                    }

                    if (failureCode == "__stop__") break;
                    // Call Server side
                    CarrySystem.ClientChannel.SendPacket(takeMessage);
                    break;
            }

            CompleteInteraction();
        }

        // Cancels the current interaction and resets the interaction state ready for next interaction.
        public void CancelInteraction(bool resetTimeHeld = false)
        {
            Interaction.Clear(resetTimeHeld);
            CarrySystem.HudOverlayRenderer.CircleVisible = false;
        }

        // Completes the current interaction and resets the interaction state but does not allow for a new interaction until mouse button is released.
        public void CompleteInteraction()
        {
            Interaction.Complete();
            CarrySystem.HudOverlayRenderer.CircleVisible = false;
        }

        public EnumHandling OnBeforeActiveSlotChanged(EntityAgent entity)
        {
            // If the player is carrying something in their hands,
            // prevent them from changing their active hotbar slot.
            return (entity.GetCarried(CarrySlot.Hands) != null)
                ? EnumHandling.PreventDefault
                : EnumHandling.PassThrough;
        }

        public void OnLockSlotsMessage(LockSlotsMessage message)
        {
            var player = CarrySystem.ClientAPI.World.Player;
            var hotbar = player.InventoryManager.GetHotbarInventory();
            for (var i = 0; i < hotbar.Count; i++)
            {
                if (message.HotbarSlots?.Contains(i) == true)
                    LockedItemSlot.Lock(hotbar[i]);
                else LockedItemSlot.Restore(hotbar[i]);
            }
        }

        public void SendLockSlotsMessage(IServerPlayer player)
        {
            var hotbar = player.InventoryManager.GetHotbarInventory();
            var slots = Enumerable.Range(0, hotbar.Count).Where(i => hotbar[i] is LockedItemSlot).ToList();
            CarrySystem.ServerChannel.SendPacket(new LockSlotsMessage(slots), player);
        }
        public static void SendLockSlotsMessage(EntityPlayer player)
        {
            if ((player == null) || (player.World.PlayerByUid(player.PlayerUID) is not IServerPlayer serverPlayer)) return;
            var system = player.World.Api.ModLoader.GetModSystem<CarrySystem>();
            system.CarryHandler.SendLockSlotsMessage(serverPlayer);
        }

        private void OnInteractMessage(IServerPlayer player, InteractMessage message)
        {
            var world = player.Entity.World;
            var block = world.BlockAccessor.GetBlock(message.Position);

            // Check block has interact behavior serverside
            if (block?.HasBlockBehavior<BlockBehaviorCarryableInteract>() == true)
            {
                var behavior = block.GetBehavior<BlockBehaviorCarryableInteract>();

                if (behavior.CanInteract(player))
                {
                    var blockSelection = player.CurrentBlockSelection.Clone();
                    blockSelection.Position = message.Position;
                    blockSelection.Block = block;
                    // TODO: add event hook here
                    block?.OnBlockInteractStart(world, player, blockSelection);
                }
            }
        }

        public void OnPickUpMessage(IServerPlayer player, PickUpMessage message)
        {
            // FIXME: Do at least some validation of this data.

            var carried = player.Entity.GetCarried(message.Slot);
            if ((message.Slot == CarrySlot.Back) || (carried != null) ||
                !CanInteract(player.Entity, true) ||
                !player.Entity.Carry(message.Position, message.Slot))
            {
                InvalidCarry(player, message.Position);
            }
        }

        public void OnPlaceDownMessage(IServerPlayer player, PlaceDownMessage message)
        {
            // FIXME: Do at least some validation of this data.
            string failureCode = null;
            var carried = player.Entity.GetCarried(message.Slot);
            if ((message.Slot == CarrySlot.Back) || (carried == null) ||
                !CanInteract(player.Entity, message.Slot != CarrySlot.Hands) ||
                !PlaceDown(player, carried, message.Selection, out var placedAt, ref failureCode))
            {
                InvalidCarry(player, message.PlacedAt);

                if (failureCode != null && failureCode != "__ignore__")
                {
                    CarrySystem.ServerAPI.SendIngameError(player, failureCode, Lang.Get(ModId + ":place-down-failed-" + failureCode));
                }
                else
                {
                    CarrySystem.ServerAPI.SendIngameError(player, "place-down-failed", Lang.Get(ModId + ":place-down-failed"));
                }

            }
            // If succeeded, but by chance the client's projected placement isn't
            // the same as the server's, re-sync the block at the client's position.
            else if (placedAt != message.PlacedAt)
            {
                player.Entity.World.BlockAccessor.MarkBlockDirty(message.PlacedAt);
            }
        }

        public void OnSwapSlotsMessage(IServerPlayer player, SwapSlotsMessage message)
        {
            if (!ModConfig.BackSlotEnabled) return;

            if ((message.First != message.Second) && (message.First == CarrySlot.Back) ||
                CanInteract(player.Entity, true))
            {
                if (player.Entity.Swap(message.First, message.Second))
                {
                    CarrySystem.Api.World.PlaySoundAt(new AssetLocation("sounds/player/throw"), player.Entity);
                    player.Entity.WatchedAttributes.MarkPathDirty(CarriedBlock.AttributeId);
                }
            }
        }

        public void OnAttachMessage(IServerPlayer player, AttachMessage message)
        {

            var targetEntity = CarrySystem.Api.World.GetEntityById(message.TargetEntityId);
            if (targetEntity == null)
            {
                CarrySystem.ServerAPI.SendIngameError(player, "entity-not-found", Lang.Get(ModId + ":entity-not-found"));
                CarrySystem.Api.Logger.Debug("Target entity does not exist!");
                return;
            }
            // If target entity is null or too far away, do nothing
            if (targetEntity.SidedPos?.DistanceTo(player.Entity.Pos) > MaxInteractionDistance)
            {
                CarrySystem.ServerAPI.SendIngameError(player, "entity-out-of-reach", Lang.Get(ModId + ":entity-out-of-reach"));
                CarrySystem.Api.Logger.Debug("Target entity is too far away!");
                return;
            }

            if (message.SlotIndex < 0)
            {
                CarrySystem.ServerAPI.SendIngameError(player, "slot-not-found", Lang.Get(ModId + ":slot-not-found"));
                CarrySystem.Api.Logger.Debug("Invalid target slot index!");
                return;
            }


            var attachableBehavior = targetEntity.GetBehavior<EntityBehaviorAttachable>();

            if (attachableBehavior != null)
            {

                // Check player is carrying block
                var carriedBlock = player.Entity.GetCarried(CarrySlot.Hands);

                if (carriedBlock == null) return;

                var blockEntityData = carriedBlock?.BlockEntityData;

                if (blockEntityData == null)
                {
                    CarrySystem.Api.Logger.Warning("Block entity data is null, cannot attach block");
                    CarrySystem.ServerAPI.SendIngameError(player, "slot-data-missing", Lang.Get(ModId + ":slot-data-missing"));
                    return;
                }
                var type = blockEntityData.GetString("type");
                var sourceInventory = blockEntityData.GetTreeAttribute("inventory");
                var block = carriedBlock?.Block;

                var targetSlot = attachableBehavior.GetSlotFromSelectionBoxIndex(message.SlotIndex);
                var apname = targetEntity.GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes[message.SlotIndex]?.AttachPoint?.Code;

                var seatableBehavior = targetEntity.GetBehavior<EntityBehaviorSeatable>();
                bool isOccupied = false;
                if (seatableBehavior != null)
                {
                    var seatId = seatableBehavior.SeatConfigs.Where(s => s.APName == apname).FirstOrDefault()?.SeatId;
                    isOccupied = seatableBehavior.Seats.Where(s => s.SeatId == seatId).FirstOrDefault()?.Passenger != null;
                }


                if (targetSlot == null || !targetSlot.Empty || isOccupied)
                {
                    CarrySystem.ServerAPI.SendIngameError(player, "slot-not-empty", Lang.Get(ModId + ":slot-not-empty"));
                    CarrySystem.Api.Logger.Log(EnumLogType.Debug, "Target Slot is occupied!");
                    return;
                }

                var sourceItemSlot = (ItemSlot)new DummySlot(null);
                sourceItemSlot.Itemstack = carriedBlock.ItemStack.Clone();
                TreeAttribute attr = sourceItemSlot.Itemstack.Attributes as TreeAttribute;

                if (attr == null)
                {
                    CarrySystem.ServerAPI.SendIngameError(player, "slot-data-missing", Lang.Get(ModId + ":slot-data-missing"));
                    CarrySystem.Api.Logger.Log(EnumLogType.Debug, "Source item is invalid!");
                    return;
                }

                var backupAttributes = blockEntityData.Clone();
                backupAttributes.RemoveAttribute("inventory");

                attr.SetString("type", type);

                var backpack = ConvertBlockInventoryToBackpack(blockEntityData.GetTreeAttribute("inventory"));

                attr.SetAttribute("backpack", backpack);

                attr.SetAttribute("carryonbackup", backupAttributes);

                if (!targetSlot.CanTakeFrom(sourceItemSlot))
                {
                    CarrySystem.ServerAPI.SendIngameError(player, "slot-incompatible-block", Lang.Get(ModId + ":slot-incompatible-block"));
                    return;
                }
                var carryableBehavior = sourceItemSlot.Itemstack.Block.GetBehavior<BlockBehaviorCarryable>();

                if (carryableBehavior?.PreventAttaching ?? false)
                {
                    CarrySystem.ServerAPI.SendIngameError(player, "slot-prevent-attaching", Lang.Get(ModId + ":slot-incompatible-block"));
                    return;
                }

                var iai = sourceItemSlot.Itemstack.Collectible.GetCollectibleInterface<IAttachedInteractions>();
                if (iai?.OnTryAttach(sourceItemSlot, message.SlotIndex, targetEntity) == false)
                {
                    CarrySystem.ServerAPI.SendIngameError(player, "attach-unavailable", Lang.Get(ModId + ":attach-unavailable"));
                    return;
                }

                var moved = sourceItemSlot.TryPutInto(targetEntity.World, targetSlot) > 0;
                if (moved)
                {
                    attachableBehavior.storeInv();

                    targetEntity.MarkShapeModified();
                    targetEntity.World.BlockAccessor.GetChunkAtBlockPos(targetEntity.ServerPos.AsBlockPos).MarkModified();

                    // Remove held block from player
                    CarriedBlock.Remove(player.Entity, CarrySlot.Hands);

                    var sound = block?.Sounds.Place ?? new AssetLocation("sounds/player/build");
                    CarrySystem.Api.World.PlaySoundAt(sound, targetEntity, null, true, 16);

                }
                else
                {
                    CarrySystem.ServerAPI.SendIngameError(player, "attach-failed", Lang.Get(ModId + ":attach-failed"));
                }
            }
        }


        public static ITreeAttribute ConvertBlockInventoryToBackpack(ITreeAttribute blockInventory)
        {
            if (blockInventory == null) return new TreeAttribute(); // graceful fallback

            var backpack = new TreeAttribute();
            var slotCount = blockInventory.GetAsInt("qslots");
            var slots = blockInventory.GetTreeAttribute("slots");

            // create backpack slots and copy items
            var backpackSlots = new TreeAttribute();
            for (int i = 0; i < slotCount; i++)
            {
                var slotKey = "slot-" + i;

                var itemstack = slots.GetItemstack(i.ToString());
                backpackSlots.SetItemstack(slotKey, itemstack);

            }

            backpack.SetAttribute("slots", backpackSlots);
            return backpack;
        }

        public static IAttribute ConvertBackpackToBlockInventory(ITreeAttribute backpack)
        {
            var inventory = new TreeAttribute();
            if (backpack != null)
            {
                // If no backpack slots, return empty TreeAttribute
                if (backpack.Count == 0) return inventory;

                var backpackSlots = backpack["slots"] as ITreeAttribute;
                var count = backpackSlots.Count();

                inventory.SetInt("qslots", count);
                var slotsAttribute = new TreeAttribute();

                for (var i = 0; i < count; i++)
                {
                    var value = backpackSlots.Values[i];
                    if (value?.GetValue() == null) continue;
                    slotsAttribute.SetAttribute(i.ToString(), value.Clone());
                }

                inventory.SetAttribute("slots", slotsAttribute);
            }

            return inventory;
        }

        public void OnDetachMessage(IServerPlayer player, DetachMessage message)
        {

            var targetEntity = CarrySystem.Api.World.GetEntityById(message.TargetEntityId);
            if (targetEntity == null)
            {
                CarrySystem.ServerAPI.SendIngameError(player, "entity-not-found", Lang.Get(ModId + ":entity-not-found"));
                return;
            }

            // Validate distance
            if (targetEntity.SidedPos?.DistanceTo(player.Entity.Pos) > MaxInteractionDistance)
            {
                CarrySystem.ServerAPI.SendIngameError(player, "entity-out-of-reach", Lang.Get(ModId + ":entity-out-of-reach"));
                return;
            }

            var attachableBehavior = targetEntity.GetBehavior<EntityBehaviorAttachable>();

            if (attachableBehavior != null)
            {
                var sourceSlot = attachableBehavior.GetSlotFromSelectionBoxIndex(message.SlotIndex);
                if (sourceSlot == null || sourceSlot.Empty)
                {
                    CarrySystem.ServerAPI.SendIngameError(player, "slot-empty", Lang.Get(ModId + ":slot-empty"));
                    return;
                }
                if (!sourceSlot?.CanTake() ?? true)
                {
                    CarrySystem.ServerAPI.SendIngameError(player, "detach-unavailable", Lang.Get(ModId + ":detach-unavailable"));
                    return;
                }

                var block = sourceSlot?.Itemstack?.Block;
                if (block == null) return;

                if (!block.HasBehavior<BlockBehaviorCarryable>())
                {
                    CarrySystem.ServerAPI.SendIngameError(player, "slot-not-carryable", Lang.Get(ModId + ":slot-not-carryable"));
                    return;
                }

                // Prevent pickup/detach if other players have the inventory open
                var inventoryName = $"mountedbaginv-{message.SlotIndex}-{message.TargetEntityId}";
                var hasOpenBoatStorage = CarrySystem.Api.World.AllOnlinePlayers
                    .OfType<IServerPlayer>()
                    .Where(serverPlayer => serverPlayer.PlayerUID != player.PlayerUID)
                    .SelectMany(serverPlayer => serverPlayer.InventoryManager.OpenedInventories)
                    .Any(inv => inv.InventoryID.StartsWith(inventoryName));

                if (hasOpenBoatStorage)
                {
                    CarrySystem.ServerAPI.SendIngameError(player, "slot-inventory-open", Lang.Get(ModId + ":slot-inventory-open"));
                    return;
                }

                // Player hands must be empty - active/offhand slot and carryon hands slot
                // If active slot has an item then it will prevent block from being placed
                var carriedBlock = player?.Entity?.GetCarried(CarrySlot.Hands);
                if (carriedBlock != null) return;

                var itemstack = sourceSlot?.Itemstack;

                var sourceBackpack = itemstack?.Attributes?["backpack"] as ITreeAttribute;

                var destInventory = ConvertBackpackToBlockInventory(sourceBackpack);

                TreeAttribute blockEntityData;
                if (itemstack?.Attributes?["carryonbackup"] is not TreeAttribute backupAttributes)
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
                blockEntityData.SetString("type", itemstack.Attributes.GetString("type"));

                // Clone itemstack and remove inventory attribute since it will be stored as blockdata
                var itemstackCopy = itemstack.Clone();
                itemstackCopy.Attributes.Remove("backpack");

                carriedBlock = new CarriedBlock(CarrySlot.Hands, itemstackCopy, blockEntityData);
                carriedBlock.Set(player.Entity, CarrySlot.Hands);

                var sound = block?.Sounds.Place ?? new AssetLocation("sounds/player/build");
                CarrySystem.Api.World.PlaySoundAt(sound, targetEntity, null, true, 16);

                itemstack?.Collectible.GetCollectibleInterface<IAttachedListener>()?.OnDetached(sourceSlot, message.SlotIndex, targetEntity, player.Entity);

                EntityBehaviorAttachableCarryable.ClearCachedSlotStorage(CarrySystem.Api, message.SlotIndex, sourceSlot, targetEntity);
                sourceSlot.Itemstack = null;
                attachableBehavior.storeInv();

                targetEntity.MarkShapeModified();
                targetEntity.World.BlockAccessor.GetChunkAtBlockPos(targetEntity.ServerPos.AsBlockPos).MarkModified();
            }

        }

        public void OnPutMessage(IServerPlayer player, PutMessage message)
        {
            string failureCode;
            string onScreenErrorMessage;

            if (!TryPutCarryable(player, message, out failureCode, out onScreenErrorMessage))
            {
                if (onScreenErrorMessage != null)
                {
                    player.SendIngameError(failureCode, onScreenErrorMessage);
                }
            }

        }

        private bool TryPutCarryable(IPlayer player, PutMessage message, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = null;
            onScreenErrorMessage = null;

            bool result;

            const int failureCodeParam = 5;
            const int onScreenErrorMessageParam = 6;

            const string transferMethod = "TryPutCarryable";

            if (message.BlockPos == null)
            {
                CarrySystem.Api.Logger.Error("TryPutCarryable: No block selected");
                return false;
            }

            var targetBlock = CarrySystem.Api.World.BlockAccessor.GetBlock(message.BlockPos);
            if (targetBlock == null)
            {
                CarrySystem.Api.Logger.Error("TryPutCarryable: No block found at position");
                return false;
            }
            var carryableBehavior = targetBlock?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null)
            {
                CarrySystem.Api.Logger.Error("TryPutCarryable: No Carryable behavior found");
                return false;
            }

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            if (carriedHands == null)
            {
                CarrySystem.Api.Logger.Error("TryPutCarryable: Player hands are empty");
                return false;
            }

            var transferBehavior = targetBlock?.BlockBehaviors?.FirstOrDefault(b => b.GetType().Name == carryableBehavior.TransferHandlerBehavior);
            if (transferBehavior == null)
            {
                return false;
            }

            try
            {
                var method = transferBehavior.GetType().GetMethod(transferMethod);
                if (method == null)
                {
                    CarrySystem.Api.Logger.Warning($"{transferMethod} method not found on {carryableBehavior.TransferHandlerBehavior}");
                    return false;
                }

                var blockEntity = CarrySystem.Api.World.BlockAccessor.GetBlockEntity(message.BlockPos);

                object[] parameters = [player, blockEntity, message.Index, carriedHands.ItemStack, carriedHands.BlockEntityData, null, null];

                result = method?.Invoke(transferBehavior, parameters) as bool? ?? false;

                failureCode = parameters[failureCodeParam] as string;
                onScreenErrorMessage = parameters[onScreenErrorMessageParam] as string;

            }
            catch (Exception e)
            {
                CarrySystem.Api.Logger.Error($"Call to {transferMethod} failed", e);
                failureCode = "__failure__";
                return false;
            }

            if (result)
            {
                CarrySystem.Api.Logger.Debug($"{transferMethod} returned true");
                CarriedBlock.Remove(player.Entity, CarrySlot.Hands);

                return true;
            }
            return false;
        }

        public void OnTakeMessage(IServerPlayer player, TakeMessage message)
        {
            string failureCode;
            string onScreenErrorMessage;

            if (!TryTakeCarryable(player, message, out failureCode, out onScreenErrorMessage))
            {
                if (onScreenErrorMessage != null)
                {
                    player.SendIngameError(failureCode, onScreenErrorMessage);
                }
            }
        }

        private bool TryTakeCarryable(IPlayer player, TakeMessage message, out string failureCode, out string onScreenErrorMessage)
        {
            failureCode = null;
            onScreenErrorMessage = null;

            bool result;

            const int itemStackParam = 3;
            const int blockEntityDataParam = 4;
            const int failureCodeParam = 5;
            const int onScreenErrorMessageParam = 6;

            const string transferMethod = "TryTakeCarryable";

            if (message.BlockPos == null)
            {
                CarrySystem.Api.Logger.Error("TryTakeCarryable: No block selected");
                return false;
            }

            var block = CarrySystem.Api.World.BlockAccessor.GetBlock(message.BlockPos);
            if (block == null)
            {
                CarrySystem.Api.Logger.Error("TryTakeCarryable: No block found at position");
                return false;
            }
            var carryableBehavior = block?.GetBehavior<BlockBehaviorCarryable>();
            if (carryableBehavior == null)
            {
                CarrySystem.Api.Logger.Error("TryTakeCarryable: No Carryable behavior found");
                return false;
            }

            var transferBehavior = block?.BlockBehaviors?.FirstOrDefault(b => b.GetType().Name == carryableBehavior.TransferHandlerBehavior);
            if (transferBehavior == null)
            {
                return false;
            }

            var blockEntity = CarrySystem.Api.World.BlockAccessor.GetBlockEntity(message.BlockPos);
            if (blockEntity == null)
            {
                CarrySystem.Api.Logger.Error("TryTakeCarryable: No block entity found at position");
                return false;
            }

            ItemStack itemStack = null;
            ITreeAttribute blockEntityData = null;

            try
            {
                var method = transferBehavior.GetType().GetMethod(transferMethod);
                if (method == null)
                {
                    CarrySystem.Api.Logger.Warning($"{transferMethod} method not found on {carryableBehavior.TransferHandlerBehavior}");
                    return false;
                }

                object[] parameters = [player, blockEntity, message.Index, null, null, null, null];

                result = method?.Invoke(transferBehavior, parameters) as bool? ?? false;

                itemStack = parameters[itemStackParam] as ItemStack;
                blockEntityData = parameters[blockEntityDataParam] as ITreeAttribute;
                failureCode = parameters[failureCodeParam] as string;
                onScreenErrorMessage = parameters[onScreenErrorMessageParam] as string;

            }
            catch (Exception e)
            {
                CarrySystem.Api.Logger.Error($"Call to {transferMethod} failed", e);
                failureCode = "__failure__";
                return false;
            }

            if (result)
            {
                CarrySystem.Api.Logger.Debug($"{transferMethod} returned true");
                var carriedBlock = new CarriedBlock(CarrySlot.Hands, itemStack, blockEntityData);
                carriedBlock.Set(player.Entity);

                return true;

            }
            return false;
        }

        public void OnQuickDropMessage(IServerPlayer player, QuickDropMessage message)
        {
            CarrySlot[] fromHands = new[] { CarrySlot.Hands, CarrySlot.Shoulder };

            player.Entity.DropCarried(fromHands, 1, 2);

        }

        /// <summary>
        /// Checks if entity can begin interaction with carryable item that is in the world or in hand slot
        /// Their left and right hands be empty.
        /// </summary>
        /// <param name="entityAgent"></param>
        /// <param name="requireEmptyHanded"></param>
        /// <returns></returns>
        public bool CanDoCarryAction(EntityAgent entityAgent, bool requireEmptyHanded)
        {
            var isEmptyHanded = entityAgent.RightHandItemSlot.Empty && entityAgent.LeftHandItemSlot.Empty;
            if (!isEmptyHanded && requireEmptyHanded) return false;

            if (entityAgent is not EntityPlayer entityPlayer) return true;

            // Active slot must be main hotbar (This excludes the backpack slots)
            var activeHotbarSlot = entityPlayer.Player.InventoryManager.ActiveHotbarSlotNumber;
            return (activeHotbarSlot >= 0) && (activeHotbarSlot < 10);
        }

        /// <summary>
        ///   Returns whether the specified entity has the required prerequisites
        ///   to interact using CarryOn: Must be sneaking with an empty hand.
        ///   Also tests for whether a valid hotbar slot is currently selected.
        /// </summary>
        public bool CanInteract(EntityAgent entityAgent, bool requireEmptyHanded)
        {
            if (entityAgent.Api.Side == EnumAppSide.Client)
            {
                if (!IsCarryKeyPressed(true))
                {
                    return false;
                }
            }

            return CanDoCarryAction(entityAgent, requireEmptyHanded);
        }

        public bool PlaceDown(IPlayer player, CarriedBlock carried,
                                     BlockSelection selection, out BlockPos placedAt, ref string failureCode)
        {
            var clickedBlock = player.Entity.World.BlockAccessor.GetBlock(selection.Position);

            // Clone the selection, because we don't
            // want to affect what is sent to the server.
            selection = selection.Clone();

            if (clickedBlock.IsReplacableBy(carried.Block))
            {
                selection.Face = BlockFacing.UP;
                selection.HitPosition.Y = 0.5;
            }
            else
            {
                selection.Position.Offset(selection.Face);
                selection.DidOffset = true;
            }

            placedAt = selection.Position;
            return player.PlaceCarried(selection, carried.Slot, ref failureCode);
        }

        /// <summary> Called when a player picks up or places down an invalid block,
        ///           requiring it to get notified about the action being rejected. </summary>
        private void InvalidCarry(IServerPlayer player, BlockPos pos)
        {
            player.Entity.World.BlockAccessor.MarkBlockDirty(pos);
            player.Entity.WatchedAttributes.MarkPathDirty(CarriedBlock.AttributeId);
            player.Entity.WatchedAttributes.MarkPathDirty("stats/walkspeed");
            SendLockSlotsMessage(player);
        }

        /// <summary> Returns the position that the specified block would
        ///           be placed at for the specified block selection. </summary>
        private static BlockPos GetPlacedPosition(
            IWorldAccessor world, BlockSelection selection, Block block)
        {
            if (selection == null) return null;
            var position = selection.Position.Copy();
            var clickedBlock = world.BlockAccessor.GetBlock(position);
            if (!clickedBlock.IsReplacableBy(block))
            {
                position.Offset(selection.Face);
                var replacedBlock = world.BlockAccessor.GetBlock(position);
                if (!replacedBlock.IsReplacableBy(block)) return null;
            }
            return position;
        }

        /// <summary>Get the block position for the main block within for a multiblock structure</summary>
        private BlockPos GetMultiblockOrigin(BlockPos position, BlockMultiblock multiblock)
        {
            if (position == null) return null;

            if (multiblock != null)
            {
                var multiPosition = position.Copy();
                multiPosition.Add(multiblock.OffsetInv);
                return multiPosition;
            }
            return position;
        }

        /// <summary>Create a new block selection pointing to the main block within a multiblock structure</summary>
        private BlockSelection GetMultiblockOriginSelection(BlockSelection blockSelection)
        {
            if (blockSelection?.Block is BlockMultiblock multiblock)
            {
                var world = CarrySystem.Api.World;
                var position = GetMultiblockOrigin(blockSelection.Position, multiblock);
                var block = world.BlockAccessor.GetBlock(position);
                var selection = blockSelection.Clone();
                selection.Position = position;
                selection.Block = block;

                return selection;
            }
            return blockSelection;
        }
    }
}
