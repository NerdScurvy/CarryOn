using System;
using System.Linq;
using CarryOn.API.Common;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static CarryOn.CarrySystem;
using static CarryOn.API.Common.CarryCode;
using static CarryOn.Utility.Extensions;
using Vintagestory.API.Util;
using HarmonyLib;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Enums;
using CarryOn.Common.Models;

namespace CarryOn.Common.Handlers
{
    /// <summary>
    ///   Takes care of core CarryCapacity handling, such as listening to input events,
    ///   picking up, placing and swapping blocks, as well as sending and handling messages.
    /// </summary>
    public class CarryHandler : IDisposable
    {
        private ICoreAPI api;

        private long gameTickListenerId;

        private readonly CarrySystem carrySystem;

        private bool? allowSprintWhileCarrying;
        private bool? backSlotEnabled;
        private bool? removeInteractDelayWhileCarrying;
        private float? interactSpeedMultiplier;

        public bool RemoveInteractDelayWhileCarrying => removeInteractDelayWhileCarrying ??= this.carrySystem?.Config?.CarryOptions?.RemoveInteractDelayWhileCarrying ?? false;
        public bool AllowSprintWhileCarrying => allowSprintWhileCarrying ??= this.carrySystem?.Config?.CarryOptions?.AllowSprintWhileCarrying ?? false;
        public bool BackSlotEnabled => backSlotEnabled ??= this.carrySystem?.Config?.CarryOptions?.BackSlotEnabled ?? false;
        public float InteractSpeedMultiplier => interactSpeedMultiplier ??= this.carrySystem.Config.CarryOptions?.InteractSpeedMultiplier ?? 1.0f;

        private ICoreClientAPI clientApi;
        private ICoreServerAPI serverApi;
        private ICarryManager carryManager;

        public ICoreAPI Api => this.api ??= this.carrySystem.Api;
        public ICoreClientAPI ClientApi => this.clientApi ??= this.carrySystem.ClientApi;
        public ICoreServerAPI ServerApi => this.serverApi ??= this.carrySystem.ServerApi;
        public ICarryManager CarryManager => this.carryManager ??= this.carrySystem.CarryManager;

        public CarryInteraction Interaction { get; set; } = new CarryInteraction();

        public bool IsCarryOnEnabled { get; set; } = true;

        public KeyCombination CarryKeyCombination => ClientApi.Input.HotKeys.Get(HotKeyCode.Pickup)?.CurrentMapping;
        public KeyCombination CarrySwapKeyCombination => ClientApi.Input.HotKeys.Get(HotKeyCode.SwapBackModifier)?.CurrentMapping;

        public int MaxInteractionDistance { get; set; }

        public Vintagestory.Client.NoObf.HudElementInteractionHelp HudHelp { get; set; }

        public CarryHandler(CarrySystem carrySystem)
        {
            if (carrySystem == null) throw new ArgumentNullException(nameof(carrySystem));
            this.carrySystem = carrySystem;
        }

        public void InitClient(ICoreAPI api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.clientApi = api as ICoreClientAPI;

            if (ClientApi == null)
            {
                throw new InvalidOperationException("CarryHandler.InitClient can only be initialized on the client side.");
            }

            var input = ClientApi.Input;
            if (input == null)
            {
                throw new InvalidOperationException("CarryHandler.InitClient requires Input to be initialized.");
            }

            input.RegisterHotKey(HotKeyCode.Pickup, GetLang("pickup-hotkey"), PickupKeyDefault);

            input.RegisterHotKey(HotKeyCode.SwapBackModifier, GetLang("swap-back-hotkey"), SwapBackModifierDefault);

            input.RegisterHotKey(HotKeyCode.Toggle, GetLang("toggle-hotkey"), ToggleDefault, altPressed: true);
            input.RegisterHotKey(HotKeyCode.QuickDrop, GetLang("quickdrop-hotkey"), QuickDropDefault, altPressed: true, ctrlPressed: true);
            input.RegisterHotKey(HotKeyCode.ToggleDoubleTapDismount, GetLang("toggle-double-tap-dismount-hotkey"), ToggleDoubleTapDismountDefault, ctrlPressed: true);

            input.SetHotKeyHandler(HotKeyCode.Toggle, TriggerToggleKeyPressed);
            input.SetHotKeyHandler(HotKeyCode.QuickDrop, TriggerQuickDropKeyPressed);
            input.SetHotKeyHandler(HotKeyCode.ToggleDoubleTapDismount, TriggerToggleDoubleTapDismountKeyPressed);

            this.carrySystem.ClientChannel.SetMessageHandler<LockSlotsMessage>(OnLockSlotsMessage);

            input.InWorldAction += OnEntityAction;
            this.gameTickListenerId = ClientApi.Event.RegisterGameTickListener(OnGameTick, 0);

            ClientApi.Event.BeforeActiveSlotChanged +=
                (_) => OnBeforeActiveSlotChanged(ClientApi.World.Player.Entity);

            ClientApi.Event.PlayerEntitySpawn += OnPlayerEntitySpawn;

            ClientApi.Event.IsPlayerReady += OnPlayerReady;

        }

        private bool OnPlayerReady(ref EnumHandling handling)
        {
            // Check if the player is ready and the carry system is enabled
            InitTransferBehaviors(Api);

            return true;

        }

        private void InitTransferBehaviors(ICoreAPI api)
        {
            if (IsCarryOnEnabled)
            {

                var ignoreMods = new[] { "game", "creative", "survival" };

                var assemblies = api.ModLoader.Mods.Where(m => !ignoreMods.Contains(m.Info.ModID))
                                                   .Select(s => s.Systems)
                                                   .SelectMany(o => o.ToArray())
                                                   .Select(t => t.GetType().Assembly)
                                                   .Distinct();

                foreach (var assembly in assemblies)
                {
                    foreach (Type type in assembly.GetTypes().Where(t => t.GetInterfaces().Contains(typeof(ICarryableTransfer))))
                    {
                        foreach (var block in api.World.Blocks.Where(b => b.IsCarryable()))
                        {
                            if (block.HasBehavior(type))
                            {
                                try
                                {
                                    var carryableBehavior = block.GetBehavior<BlockBehaviorCarryable>();
                                    if (carryableBehavior != null)
                                    {
                                        carryableBehavior.ConfigureTransferBehavior(type, api);

                                    }
                                }
                                catch (Exception e)
                                {
                                    api.Logger.Error($"CarryOn: Failed to set TransferHandlerType for block {block.Code}: {e.Message}");
                                }
                            }
                        }
                    }
                }

            }            
        }


        private void OnPlayerEntitySpawn(IClientPlayer byPlayer)
        {
            var entityCarriedListener = new TreeModifiedListener()
            {
                path = AttributeKey.Watched.EntityCarried,
                listener = RefreshPlacedBlockInteractionHelp

            };

            byPlayer.Entity.WatchedAttributes.OnModified.Add(entityCarriedListener);
        }

        public void InitServer(ICoreServerAPI api)
        {
            this.serverApi = api ?? throw new ArgumentNullException(nameof(api));
            
            var serverEvent = api.Event;
            // TODO: Change this to a config value.
            MaxInteractionDistance = 6;

            this.carrySystem.ServerChannel
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

            serverEvent.OnEntitySpawn += OnServerEntitySpawn;
            serverEvent.PlayerNowPlaying += OnServerPlayerNowPlaying;

            serverEvent.BeforeActiveSlotChanged +=
                (player, _) => OnBeforeActiveSlotChanged(player.Entity);

            InitTransferBehaviors(api);
        }

        /// <summary>
        /// Checks if the carry key is currently pressed.
        /// Always returns false on server.
        /// </summary>
        /// <param name="checkMouse"></param>
        /// <returns></returns>
        public bool IsCarryKeyPressed(bool checkMouse = false)
        {
            if (ClientApi == null) return false;

            var input = ClientApi.Input;
            if (checkMouse && !input.InWorldMouseButton.Right) return false;

            return input.KeyboardKeyState[CarryKeyCombination.KeyCode];
        }

        /// <summary>
        /// Checks if the carry swap key is currently pressed.
        /// Always returns false on server.
        /// </summary>
        /// <returns></returns>
        public bool IsCarrySwapKeyPressed()
        {
            if (ClientApi == null) return false;
            return ClientApi.Input.KeyboardKeyState[CarrySwapKeyCombination.KeyCode];
        }


        /// <summary>
        /// Checks if entity can begin interaction with carryable item that is in the world or carried in hands slot
        /// </summary>
        /// <param name="entityAgent"></param>
        /// <param name="requireEmptyHanded">if true, requires the entity agent to have both left and right hands empty</param>
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
        /// Checks if the entity can interact with a carryable item.
        /// </summary>
        /// <param name="entityAgent"></param>
        /// <param name="requireEmptyHanded"></param>
        /// <returns></returns>
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


        /// <summary>
        /// Handles the dismount action for a player.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        private void OnDismountMessage(IServerPlayer player, DismountMessage message)
        {

            player.Entity.TryUnmount();

            player.Entity.World.GetEntityById(message.EntityId)?
                .GetBehavior<EntityBehaviorCreatureCarrier>()?
                .Seats?.FirstOrDefault(s => s.SeatId == message.SeatId)?
                .Controls?.StopAllMovement();

        }

        /// <summary>
        /// Handles player attribute updates.
        /// Currently only updates the double-tap dismount attribute which toggles the feature for the player.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        private void OnPlayerAttributeUpdateMessage(IServerPlayer player, PlayerAttributeUpdateMessage message)
        {
            var playerEntity = player.Entity;
            if (message.AttributeKey == null)
            {
                return;
            }

            if (message.AttributeKey == AttributeKey.Watched.EntityDoubleTapDismountEnabled && message.IsWatchedAttribute)
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

        /// <summary>
        /// Triggers the action to toggle client side CarryOn behavior when the specified key combination is pressed.
        /// </summary>
        /// <param name="keyCombination"></param>
        /// <returns></returns>
        public bool TriggerToggleKeyPressed(KeyCombination keyCombination)
        {
            // Using toggle key to debug the refresh
            RefreshPlacedBlockInteractionHelp();

            IsCarryOnEnabled = !IsCarryOnEnabled;
            ClientApi.ShowChatMessage(GetLang("carryon-" + (IsCarryOnEnabled ? "enabled" : "disabled")));
            return true;
        }

        /// <summary>
        /// Triggers the quick drop action when the specified key combination is pressed.
        /// </summary>
        /// <param name="keyCombination"></param>
        /// <returns></returns>
        public bool TriggerQuickDropKeyPressed(KeyCombination keyCombination)
        {
            // Send drop message even if client shows nothing being held
            this.carrySystem.ClientChannel.SendPacket(new QuickDropMessage());
            return true;
        }

        /// <summary>
        /// Triggers the double-tap dismount toggle when the specified key combination is pressed.
        /// </summary>
        /// <param name="keyCombination"></param>
        /// <returns></returns>
        private bool TriggerToggleDoubleTapDismountKeyPressed(KeyCombination keyCombination)
        {
            var playerEntity = ClientApi.World.Player.Entity;
            var isEnabled = playerEntity.WatchedAttributes.GetBool(AttributeKey.Watched.EntityDoubleTapDismountEnabled, false);

            // Toggle the opposite state 
            playerEntity.WatchedAttributes.SetBool(AttributeKey.Watched.EntityDoubleTapDismountEnabled, !isEnabled);

            this.carrySystem.ClientChannel.SendPacket(new PlayerAttributeUpdateMessage(AttributeKey.Watched.EntityDoubleTapDismountEnabled, !isEnabled, true));

            ClientApi.ShowChatMessage(GetLang("double-tap-dismount-" + (!isEnabled ? "enabled" : "disabled")));
            return true;
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

        /// <summary>
        /// Begins interaction with entity to attach or detach a carried block if conditions are met.
        /// </summary>
        /// <param name="handled"></param>
        /// <returns></returns>
        private bool BeginEntityCarryableInteraction(ref EnumHandling handled)
        {
            var world = ClientApi.World;
            var player = world.Player;

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);

            var carryAttachBehavior = player.CurrentEntitySelection?.Entity?.GetBehavior<EntityBehaviorAttachableCarryable>();

            bool isLookingAtEntity = player.CurrentEntitySelection != null;
            bool entityHasAttachable = carryAttachBehavior != null;
            bool carryKeyHeld = IsCarryKeyPressed();

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
                    ClientApi.Logger.Error("EntityBehaviorAttachable not found on entity {0}", entitySelection?.Entity?.Code);
                    ClientApi.TriggerIngameError("carryon", "attachable-not-found", GetLang("attachable-behavior-not-found"));
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
                        ClientApi.TriggerIngameError("carryon", "slot-not-empty", GetLang("slot-not-empty"));
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
                        ClientApi.TriggerIngameError("carryon", "slot-empty", GetLang("slot-empty"));
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

            var world = ClientApi.World;
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
            bool carryKeyHeld = IsCarryKeyPressed();
            bool swapKeyPressed = IsCarrySwapKeyPressed();
            bool notTargetingBlock = player.CurrentBlockSelection == null;
            bool canSwapBackFromBackSlot = !canCarryTarget && carriedBack != null && carriedHands == null;

            if (carryKeyHeld && (swapKeyPressed || notTargetingBlock || canSwapBackFromBackSlot))
            {

                if (carriedHands == null && !notTargetingBlock)
                {
                    // Don't allow swap back operation if the player is looking at a container with empty hands.
                    var hasBehavior = player.CurrentBlockSelection?.Block?.HasBehavior<BlockBehaviorContainer>() ?? false;
                    if (hasBehavior)
                    {
                        CompleteInteraction();
                        return true;
                    }
                }

                if (carriedHands != null)
                {
                    if (carriedHands.GetCarryableBehavior().Slots[CarrySlot.Back] == null)
                    {
                        ClientApi.TriggerIngameError("carryon", "cannot-swap-back", GetLang("cannot-swap-back"));
                        CompleteInteraction();
                        return true;
                    }
                }

                if (carriedHands == null && carriedBack == null)
                {
                    // If nothing is being carried, do not allow swap back.
                    ClientApi.TriggerIngameError("carryon", "nothing-carried", GetLang("nothing-carried"));
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
            var world = ClientApi.World;
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
            var world = ClientApi.World;
            var player = world.Player;

            // Escape early if carry key is not held down.
            if (!IsCarryKeyPressed())
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
                    var blockPos = BlockUtils.GetPlacedPosition(world.BlockAccessor, selection, carriedHands.Block);
                    if (blockPos == null) return true;

                    if (!player.Entity.HasPermissionToCarry(blockPos))
                    {
                        ClientApi.TriggerIngameError("carryon", "place-down-no-permission", GetLang("place-down-no-permission"));
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
            else if (CanInteract(player.Entity, true))
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
            var world = ClientApi.World;
            var player = world.Player;

            // Escape early if carry key is not held down.
            if (!IsCarryKeyPressed())
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
                if (!CanTakeCarryable(player, blockEntity, selection.SelectionBoxIndex, out transferDelay, out failureCode, out onScreenErrorMessage))
                {
                    return HandleCanTransferResponse(failureCode, onScreenErrorMessage, ref handled);
                }

                if (HandleCanTransferResponse(failureCode, null, ref handled))
                    return true;

                Interaction.CarryAction = CarryAction.Take;
            }
            else
            {
                if (!CanPutCarryable(player, blockEntity, selection.SelectionBoxIndex, out transferDelay, out failureCode, out onScreenErrorMessage))
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
                ClientApi.TriggerIngameError(CarryCode.ModId, failureCode, onScreenErrorMessage);
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

        /// <summary>
        /// Checks if the player can put a carryable item into the specified block entity.
        /// </summary>
        private bool CanPutCarryable(IPlayer player, BlockEntity blockEntity, int index, out float? transferDelay, out string failureCode, out string onScreenErrorMessage)
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
                Api.Logger.Error($"CanPutCarryable method failed: {e}");
            }

            return false;
        }


        /// <summary>
        /// Checks if the player can take a carryable item from the specified block entity.
        /// </summary>
        private bool CanTakeCarryable(IPlayer player, BlockEntity blockEntity, int index,
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
                Api.Logger.Error($"CanTakeCarryable method failed: {e}");
            }

            return false;
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
                    if (AllowSprintWhileCarrying) return;
                    isInteract = false;
                    break;
                default: return;
            }

            // If an action is currently ongoing, ignore the game's entity action.
            if (Interaction.CarryAction != CarryAction.None)
            { handled = EnumHandling.PreventDefault; return; }

            var world = ClientApi.World;
            var player = world.Player;

            // Check if player has item in active active or offhand slot
            if (!CanDoCarryAction(player.Entity, requireEmptyHanded: true))
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

        /// <summary>
        /// Client side game tick handler for player carry interactions
        /// </summary>
        /// <param name="deltaTime"></param>
        public void OnGameTick(float deltaTime)
        {
            if (!IsCarryOnEnabled) return;

            var world = ClientApi.World;
            var player = world.Player;
            var input = ClientApi.Input;

            if (!input.InWorldMouseButton.Right) { CancelInteraction(resetTimeHeld: true); return; }

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
                        ClientApi.Logger.Debug("Nothing carried. Player may have dropped the block from being damaged");
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
                requiredTime = carryBehavior?.TransferDelay ?? TransferSpeedDefault;
            }
            else if (Interaction.CarryAction == CarryAction.Interact)
            {
                if (RemoveInteractDelayWhileCarrying) requiredTime = 0;
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
                    var hasPickedUp = CarryManager.TryPickUp(player.Entity, selection.Position, Interaction.CarrySlot.Value, checkIsCarryable: true, playSound: true);
                    if (hasPickedUp)
                        this.carrySystem.ClientChannel.SendPacket(new PickUpMessage(selection.Position, Interaction.CarrySlot.Value));
                    else
                    {
                        // This else block executes when the attempt to pick up the item fails.
                        // Showing an error message here informs the player that the pick-up action was unsuccessful.
                        ClientApi.TriggerIngameError("carryon", "pick-up-failed", GetLang("pick-up-failed"));
                    }
                    break;

                case CarryAction.PlaceDown:

                    if (CarryManager.TryPlaceDownAt(player, carriedTarget, selection, out var placedAt, ref failureCode))
                        this.carrySystem.ClientChannel.SendPacket(new PlaceDownMessage(Interaction.CarrySlot.Value, selection, placedAt));
                    else
                    {
                        // Show in-game error if placing down failed.
                        if (failureCode != null && failureCode != "__ignore__")
                        {
                            ClientApi.TriggerIngameError("carryon", failureCode, GetLang("place-down-failed-" + failureCode));
                        }
                        else
                        {
                            ClientApi.TriggerIngameError("carryon", "place-down-failed", GetLang("place-down-failed"));
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
                    if (!TryPutCarryable(player, putMessage, out failureCode, out onScreenErrorMessage))
                    {
                        if (failureCode != FailureCode.Continue)
                        {
                            if (onScreenErrorMessage != null)
                            {
                                ClientApi.TriggerIngameError(ModId, failureCode, onScreenErrorMessage);
                            }
                            ClientApi.Logger.Debug($"Failed client side: {failureCode} : {onScreenErrorMessage}");
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
                    if (!TryTakeCarryable(player, takeMessage, out failureCode, out onScreenErrorMessage))
                    {
                        if (failureCode != FailureCode.Continue)
                        {
                            if (onScreenErrorMessage != null)
                            {
                                ClientApi.TriggerIngameError(ModId, failureCode, onScreenErrorMessage);
                            }

                            ClientApi.Logger.Debug($"Failed client side: {failureCode} : {onScreenErrorMessage}");
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
            var player = ClientApi.World.Player;
            var hotbar = player.InventoryManager.GetHotbarInventory();
            for (var i = 0; i < hotbar.Count; i++)
            {
                if (message.HotbarSlots?.Contains(i) == true)
                    LockedItemSlot.Lock(hotbar[i]);
                else LockedItemSlot.Restore(hotbar[i]);
            }
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
                !CanInteract(player.Entity, true))
            {
                InvalidCarry(player, message.Position);
            }

            var didPickUp = CarryManager.TryPickUp(player.Entity, message.Position, message.Slot, checkIsCarryable: true, playSound: true);
            if (!didPickUp)
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
                !CarryManager.TryPlaceDownAt(player, carried, message.Selection, out var placedAt, ref failureCode))
            {
                InvalidCarry(player, message.PlacedAt);

                if (failureCode != null && failureCode != "__ignore__")
                {
                    ServerApi.SendIngameError(player, failureCode, GetLang("place-down-failed-" + failureCode));
                }
                else
                {
                    ServerApi.SendIngameError(player, "place-down-failed", GetLang("place-down-failed"));
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
            if (!BackSlotEnabled) return;

            if ((message.First != message.Second) && (message.First == CarrySlot.Back) ||
                CanInteract(player.Entity, true))
            {
                if (player.Entity.SwapCarried(message.First, message.Second))
                {
                    Api.World.PlaySoundAt(new AssetLocation("sounds/player/throw"), player.Entity);
                    player.Entity.WatchedAttributes.MarkPathDirty(AttributeKey.Watched.EntityCarried);
                }
            }
        }

        /// <summary>
        /// Handles the attach block to entity action for a player.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        public void OnAttachMessage(IServerPlayer player, AttachMessage message)
        {
            var api = ServerApi;
            if (api == null)
            {
                throw new InvalidOperationException("ServerApi is not initialized.");
            }

            var targetEntity = api.World.GetEntityById(message.TargetEntityId);
            if (targetEntity == null)
            {
                api.SendIngameError(player, "entity-not-found", GetLang("entity-not-found"));
                api.Logger.Debug("Target entity does not exist!");
                return;
            }
            // If target entity is null or too far away, do nothing
            if (targetEntity.SidedPos?.DistanceTo(player.Entity.Pos) > MaxInteractionDistance)
            {
                api.SendIngameError(player, "entity-out-of-reach", GetLang("entity-out-of-reach"));
                api.Logger.Debug("Target entity is too far away!");
                return;
            }

            if (message.SlotIndex < 0)
            {
                api.SendIngameError(player, "slot-not-found", GetLang("slot-not-found"));
                api.Logger.Debug("Invalid target slot index!");
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
                    api.Logger.Warning("Block entity data is null, cannot attach block");
                    api.SendIngameError(player, "slot-data-missing", GetLang("slot-data-missing"));
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
                    api.SendIngameError(player, "slot-not-empty", GetLang("slot-not-empty"));
                    api.Logger.Log(EnumLogType.Debug, "Target Slot is occupied!");
                    return;
                }

                var sourceItemSlot = (ItemSlot)new DummySlot(null);
                sourceItemSlot.Itemstack = carriedBlock.ItemStack.Clone();
                TreeAttribute attr = sourceItemSlot.Itemstack.Attributes as TreeAttribute;

                if (attr == null)
                {
                    api.SendIngameError(player, "slot-data-missing", GetLang("slot-data-missing"));
                    api.Logger.Log(EnumLogType.Debug, "Source item is invalid!");
                    return;
                }

                var backupAttributes = blockEntityData.Clone();
                backupAttributes.RemoveAttribute("inventory");

                attr.SetString("type", type);

                var backpack = BlockUtils.ConvertBlockInventoryToBackpack(blockEntityData.GetTreeAttribute("inventory"));

                attr.SetAttribute("backpack", backpack);

                attr.SetAttribute("carryonbackup", backupAttributes);

                if (!targetSlot.CanTakeFrom(sourceItemSlot))
                {
                    api.SendIngameError(player, "slot-incompatible-block", GetLang("slot-incompatible-block"));
                    return;
                }
                var carryableBehavior = sourceItemSlot.Itemstack.Block.GetBehavior<BlockBehaviorCarryable>();

                if (carryableBehavior?.PreventAttaching ?? false)
                {
                    api.SendIngameError(player, "slot-prevent-attaching", GetLang("slot-prevent-attaching"));
                    return;
                }

                var iai = sourceItemSlot.Itemstack.Collectible.GetCollectibleInterface<IAttachedInteractions>();
                if (iai?.OnTryAttach(sourceItemSlot, message.SlotIndex, targetEntity) == false)
                {
                    api.SendIngameError(player, "attach-unavailable", GetLang("attach-unavailable"));
                    return;
                }

                var moved = sourceItemSlot.TryPutInto(targetEntity.World, targetSlot) > 0;
                if (moved)
                {
                    attachableBehavior.storeInv();

                    targetEntity.MarkShapeModified();
                    targetEntity.World.BlockAccessor.GetChunkAtBlockPos(targetEntity.ServerPos.AsBlockPos).MarkModified();

                    // Remove held block from player
                    CarryManager.RemoveCarried(player.Entity, CarrySlot.Hands);

                    var sound = block?.Sounds.Place ?? new AssetLocation("sounds/player/build");
                    api.World.PlaySoundAt(sound, targetEntity, null, true, 16);

                }
                else
                {
                    api.SendIngameError(player, "attach-failed", GetLang("attach-failed"));
                }
            }
        }

        /// <summary>
        /// Handles the detach block action for a player.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        public void OnDetachMessage(IServerPlayer player, DetachMessage message)
        {
            var api = ServerApi;
            if (api == null)
            {
                throw new InvalidOperationException("ServerApi is not initialized.");
            }

            var targetEntity = api.World.GetEntityById(message.TargetEntityId);
            if (targetEntity == null)
            {
                api.SendIngameError(player, "entity-not-found", GetLang("entity-not-found"));
                return;
            }

            // Validate distance
            if (targetEntity.SidedPos?.DistanceTo(player.Entity.Pos) > MaxInteractionDistance)
            {
                api.SendIngameError(player, "entity-out-of-reach", GetLang("entity-out-of-reach"));
                return;
            }

            var attachableBehavior = targetEntity.GetBehavior<EntityBehaviorAttachable>();

            if (attachableBehavior != null)
            {
                var sourceSlot = attachableBehavior.GetSlotFromSelectionBoxIndex(message.SlotIndex);
                if (sourceSlot == null || sourceSlot.Empty)
                {
                    api.SendIngameError(player, "slot-empty", GetLang("slot-empty"));
                    return;
                }
                if (!sourceSlot?.CanTake() ?? true)
                {
                    api.SendIngameError(player, "detach-unavailable", GetLang("detach-unavailable"));
                    return;
                }

                var block = sourceSlot?.Itemstack?.Block;
                if (block == null) return;

                if (!block.HasBehavior<BlockBehaviorCarryable>())
                {
                    api.SendIngameError(player, "slot-not-carryable", GetLang("slot-not-carryable"));
                    return;
                }

                // Prevent pickup/detach if other players have the inventory open
                var inventoryName = $"mountedbaginv-{message.SlotIndex}-{message.TargetEntityId}";
                var hasOpenBoatStorage = api.World.AllOnlinePlayers
                    .OfType<IServerPlayer>()
                    .Where(serverPlayer => serverPlayer.PlayerUID != player.PlayerUID)
                    .SelectMany(serverPlayer => serverPlayer.InventoryManager.OpenedInventories)
                    .Any(inv => inv.InventoryID.StartsWith(inventoryName));

                if (hasOpenBoatStorage)
                {
                    api.SendIngameError(player, "slot-inventory-open", GetLang("slot-inventory-open"));
                    return;
                }

                // Player hands must be empty - active/offhand slot and carryon hands slot
                // If active slot has an item then it will prevent block from being placed
                var carriedBlock = player?.Entity?.GetCarried(CarrySlot.Hands);
                if (carriedBlock != null) return;

                var itemstack = sourceSlot?.Itemstack;

                var sourceBackpack = itemstack?.Attributes?["backpack"] as ITreeAttribute;

                var destInventory = BlockUtils.ConvertBackpackToBlockInventory(sourceBackpack);

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
                api.World.PlaySoundAt(sound, targetEntity, null, true, 16);

                itemstack?.Collectible.GetCollectibleInterface<IAttachedListener>()?.OnDetached(sourceSlot, message.SlotIndex, targetEntity, player.Entity);

                EntityBehaviorAttachableCarryable.ClearCachedSlotStorage(api, message.SlotIndex, sourceSlot, targetEntity);
                sourceSlot.Itemstack = null;
                attachableBehavior.storeInv();

                targetEntity.MarkShapeModified();
                targetEntity.World.BlockAccessor.GetChunkAtBlockPos(targetEntity.ServerPos.AsBlockPos).MarkModified();
            }

        }

        /// <summary>
        /// Handles the Put action for a player.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        public void OnPutMessage(IServerPlayer player, PutMessage message)
        {
            var api = ServerApi;
            if (api == null)
            {
                throw new InvalidOperationException("ServerApi is not initialized.");
            }

            if (message == null)
            {
                api.Logger.Error("OnPutMessage: Received null message");
                return;
            }

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

        /// <summary>
        /// Try to put a carryable item into the targeted block's slot
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        /// <param name="failureCode"></param>
        /// <param name="onScreenErrorMessage"></param>
        /// <returns></returns>
        private bool TryPutCarryable(IPlayer player, PutMessage message, out string failureCode, out string onScreenErrorMessage)
        {
            const string methodName = "TryPutCarryable";

            failureCode = null;
            onScreenErrorMessage = null;

            var api = Api;
            if (api == null)
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
                    CarryManager.RemoveCarried(player?.Entity, CarrySlot.Hands);
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

        /// <summary>
        /// Tries to take a carryable block from the specified block entity slot.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        /// <param name="failureCode"></param>
        /// <param name="onScreenErrorMessage"></param>
        /// <returns></returns>
        private bool TryTakeCarryable(IPlayer player, TakeMessage message, out string failureCode, out string onScreenErrorMessage)
        {

            const string methodName = "TryTakeCarryable";

            failureCode = null;
            onScreenErrorMessage = null;

            var api = Api;
            if (api == null)
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
                    CarryManager.SetCarried(player?.Entity, new CarriedBlock(CarrySlot.Hands, itemStack, blockEntityData));
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

        /// <summary>
        /// Handles the quick drop action for a player.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        public void OnQuickDropMessage(IServerPlayer player, QuickDropMessage message)
        {
            CarrySlot[] fromHands = [CarrySlot.Hands, CarrySlot.Shoulder];

            CarryManager.DropCarried(player.Entity, fromHands, 2);

        }

        public void RefreshPlacedBlockInteractionHelp()
        {
            if (HudHelp == null) return;
            try
            {
                var method = AccessTools.Method(typeof(Vintagestory.Client.NoObf.HudElementInteractionHelp), "ComposeBlockWorldInteractionHelp");
                if(method == null)
                {
                    Api.Logger.Error("Failed to find method ComposeBlockWorldInteractionHelp via reflection.");
                    HudHelp = null;
                    return;
                }

                method.Invoke(HudHelp, null);
            }
            catch (Exception e)
            {
                Api.Logger.Error($"Failed to refresh placed block interaction help (Disabling further calls): {e}");
                HudHelp = null;
            }
        }


        /// <summary> 
        /// Called when a player picks up or places down an invalid block,
        /// requiring it to get notified about the action being rejected. 
        /// </summary>
        private void InvalidCarry(IServerPlayer player, BlockPos pos)
        {
            player.Entity.World.BlockAccessor.MarkBlockDirty(pos);
            player.Entity.WatchedAttributes.MarkPathDirty(AttributeKey.Watched.EntityCarried);
            player.Entity.WatchedAttributes.MarkPathDirty("stats/walkspeed");
            CarryManager.LockHotbarSlots(player);
        }

        public void Dispose()
        {

            if (Api.Side == EnumAppSide.Client)
            {
                var api = Api as ICoreClientAPI;
                api.Input.InWorldAction -= OnEntityAction;
                api.Event.UnregisterGameTickListener(this.gameTickListenerId);

                api.Event.BeforeActiveSlotChanged -=
                    (_) => OnBeforeActiveSlotChanged(api.World.Player.Entity);

                api.Event.PlayerEntitySpawn -= OnPlayerEntitySpawn;
            }
            else
            {
                var api = Api as ICoreServerAPI;

                api.Event.OnEntitySpawn -= OnServerEntitySpawn;
                api.Event.PlayerNowPlaying -= OnServerPlayerNowPlaying;

                api.Event.BeforeActiveSlotChanged -=
                    (player, _) => OnBeforeActiveSlotChanged(player.Entity);
            }

        }
    }
}
