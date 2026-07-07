using System;
using System.Linq;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.API.Util;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Common.Logic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Client.Logic.Interaction;
using CarryOn.Common.Interfaces;
using CarryOn.Client.Models;
using CarryOn.Common.Entities;
using static CarryOn.Common.Models.CarryCode;

namespace CarryOn.Common.Handlers
{
    /// <summary>
    ///   Takes care of core CarryCapacity handling, such as listening to input events,
    ///   picking up, placing and swapping blocks, as well as sending and handling messages.
    /// </summary>
    public class CarryHandler : IDisposable
    {
        private static readonly Type[] CarryMessageTypes =
        [
            typeof(InteractMessage),
            typeof(LockSlotsMessage),
            typeof(PickUpMessage),
            typeof(PlaceDownMessage),
            typeof(SwapSlotsMessage),
            typeof(AttachMessage),
            typeof(DetachMessage),
            typeof(PutMessage),
            typeof(TakeMessage),
            typeof(DismountMessage),
            typeof(ConfigSyncMessage),
            typeof(PickupEntityMessage)
        ];


        private long gameTickListenerId;

        private readonly Func<bool> getIsCarryOnEnabled;

        private readonly ICarryManager carryManager;
        private readonly IConfigProvider configProvider;

        public bool IsCarryOnEnabled => this.getIsCarryOnEnabled();

        private bool lastCanInteractState = true;

        public bool BackSlotEnabled => configProvider.Config.CarryOptions?.BackSlotEnabled ?? false;

        private ICoreAPI? api;

        public ICoreAPI Api => this.api!;
        public ICoreClientAPI? ClientApi => this.api as ICoreClientAPI;
        public ICoreServerAPI? ServerApi => this.api as ICoreServerAPI;

        // Clientside
        private CarryInteractionController interactionLogic { get; set; } = null!;
        private TreeModifiedListener? entityCarriedListener;
        private Entity? watchedClientPlayerEntity;
        private Vintagestory.API.Common.Func<ActiveSlotChangeEventArgs, EnumHandling>? clientBeforeActiveSlotChangedDelegate;
        private Vintagestory.API.Common.Func<IServerPlayer, ActiveSlotChangeEventArgs, EnumHandling>? serverBeforeActiveSlotChangedDelegate;

        // Serverside
        private TransferLogic? TransferLogic { get; set; } = null;


        private bool CanSprintWhileCarrying(EntityPlayer player)
        {
            var cfg = this.configProvider.Config.CarryWalkSpeed;
            if (cfg == null) return true;

            var handsCarried = this.carryManager.GetCarried(player, CarrySlot.Hands);
            var backCarried = this.carryManager.GetCarried(player, CarrySlot.Back);

            if (handsCarried != null && !cfg.HandsAllowSprint) return false;
            if (backCarried != null && !cfg.BackAllowSprint) return false;

            return true;
        }

        /// <summary>
        /// Sets the HUD element for interaction help, so that it can be updated when carrying interactable blocks.
        /// </summary>
        /// <param name="hudHelp"> The HUD element for interaction help. </param>
        public void SetHudHelp(Vintagestory.Client.NoObf.HudElementInteractionHelp? hudHelp)
        {
            if (this.interactionLogic == null)
            {
                throw new InvalidOperationException("SetHudHelp can only be called after InitClient has been called.");
            }

            this.interactionLogic.HudHelp = hudHelp;
        }

        public void RefreshConfigCache()
        {
            interactionLogic?.RefreshConfigCache();
        }

        /// <summary>
        /// Initializes a new instance of the CarryHandler class.
        /// </summary>
        /// <param name="isCarryOnEnabled"> Function returning the current CarryOn enabled state (client-side). </param>
        public CarryHandler(ICarryManager carryManager, Func<bool> isCarryOnEnabled)
        {
            ArgumentNullException.ThrowIfNull(carryManager);
            ArgumentNullException.ThrowIfNull(isCarryOnEnabled);
            
            this.carryManager = carryManager;
            this.configProvider = (IConfigProvider)carryManager ?? throw new ArgumentException("carryManager must implement IConfigProvider", nameof(carryManager));
            this.getIsCarryOnEnabled = isCarryOnEnabled;
        }

        /// <summary>
        /// Initializes the carry handler on the client side, setting up message handlers, hotkeys and event listeners.
        /// </summary>
        /// <param name="api"> The client API instance. </param>
        /// <param name="clientChannel"> The client network channel. </param>
        /// <param name="hideOverlay"> Action to hide the overlay. </param>
        /// <param name="setOverlayProgress"> Action to set the overlay progress. </param>
        /// <exception cref="ArgumentNullException"> Thrown if the provided API instance is null. </exception>
        /// <exception cref="InvalidOperationException"> Thrown if the method is called on the server side or if Input is not initialized. </exception>
        public void InitClient(ICoreAPI api, IClientNetworkChannel clientChannel, Action hideOverlay, Action<float> setOverlayProgress, ClientModConfig? clientModConfig = null)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));

            if (ClientApi == null)
            {
                throw new InvalidOperationException("CarryHandler.InitClient can only be initialized on the client side.");
            }

            if (ClientApi.Input == null)
            {
                throw new InvalidOperationException("CarryHandler.InitClient requires Input to be initialized.");
            }

            ArgumentNullException.ThrowIfNull(clientChannel);
            ArgumentNullException.ThrowIfNull(hideOverlay);
            ArgumentNullException.ThrowIfNull(setOverlayProgress);

            this.interactionLogic = new CarryInteractionController(ClientApi, this.carryManager, clientChannel, hideOverlay, setOverlayProgress, clientModConfig);

            RegisterCarryMessageTypes(clientChannel)
                .SetMessageHandler<LockSlotsMessage>(OnLockSlotsMessage);

            ClientApi.Input.RegisterHotKey(HotKeyCode.Pickup, LocalizationHelper.GetLang("pickup-hotkey"), Default.PickupKeybind);
            ClientApi.Input.RegisterHotKey(HotKeyCode.SwapBackModifier, LocalizationHelper.GetLang("swap-back-hotkey"), Default.SwapBackModifierKeybind);

            ClientApi.Input.InWorldAction += OnEntityAction;
            this.gameTickListenerId = ClientApi.Event.RegisterGameTickListener(OnGameTick, 0);

            this.clientBeforeActiveSlotChangedDelegate = (_entity) => OnBeforeActiveSlotChanged(ClientApi.World.Player.Entity);
            ClientApi.Event.BeforeActiveSlotChanged += this.clientBeforeActiveSlotChangedDelegate;

            ClientApi.Event.PlayerEntitySpawn += OnPlayerEntitySpawn;

            ClientApi.Event.IsPlayerReady += OnPlayerReady;
        }

        /// <summary>
        /// Initializes the carry handler on the server side, setting up message handlers and event listeners.
        /// </summary>
        /// <param name="api"> The server API instance. </param>
        /// <param name="serverChannel"> The server network channel. </param>
        /// <exception cref="ArgumentNullException"> Thrown if the provided API instance is null. </exception>
        public void InitServer(ICoreServerAPI api, IServerNetworkChannel serverChannel)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            ArgumentNullException.ThrowIfNull(serverChannel);

            var serverEvent = api.Event;

            this.TransferLogic = new TransferLogic(api, this.carryManager);

            RegisterCarryMessageTypes(serverChannel)
                .SetMessageHandler<InteractMessage>(OnInteractMessage)
                .SetMessageHandler<PickUpMessage>(OnPickUpMessage)
                .SetMessageHandler<PlaceDownMessage>(OnPlaceDownMessage)
                .SetMessageHandler<SwapSlotsMessage>(OnSwapSlotsMessage)
                .SetMessageHandler<AttachMessage>(OnAttachMessage)
                .SetMessageHandler<DetachMessage>(OnDetachMessage)
                .SetMessageHandler<PutMessage>(OnPutMessage)
                .SetMessageHandler<TakeMessage>(OnTakeMessage)
                .SetMessageHandler<DismountMessage>(OnDismountMessage)
                .SetMessageHandler<PickupEntityMessage>(OnPickupEntityMessage);

            serverEvent.OnEntitySpawn += OnServerEntitySpawn;
            serverEvent.PlayerNowPlaying += OnServerPlayerNowPlaying;

            this.serverBeforeActiveSlotChangedDelegate = (player, _) => OnBeforeActiveSlotChanged(player.Entity);
            serverEvent.BeforeActiveSlotChanged += this.serverBeforeActiveSlotChangedDelegate;

            if (this.IsCarryOnEnabled)
                TransferLogic.InitTransferBehaviors(api);
        }

        private static TChannel RegisterCarryMessageTypes<TChannel>(TChannel channel) where TChannel : class
        {
            ArgumentNullException.ThrowIfNull(channel);

            var registerMessageTypeMethod = typeof(TChannel)
                .GetMethods()
                .FirstOrDefault(m => m.Name == nameof(IClientNetworkChannel.RegisterMessageType)
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 0);

            if (registerMessageTypeMethod == null)
            {
                throw new InvalidOperationException($"Could not find RegisterMessageType<T>() on channel type {typeof(TChannel).Name}.");
            }

            var current = channel;
            foreach (var messageType in CarryMessageTypes)
            {
                var result = registerMessageTypeMethod.MakeGenericMethod(messageType).Invoke(current, null);

                if (result is not TChannel typedChannel)
                {
                    throw new InvalidOperationException(
                        $"RegisterMessageType<{messageType.Name}> returned null or unexpected type on {typeof(TChannel).Name}.");
                }

                current = typedChannel;
            }
            return current ?? throw new InvalidOperationException($"RegisterMessageType chain returned null on {typeof(TChannel).Name}.");
        }


        // ------------------------------
        //  Client side message handlers
        // ------------------------------

        /// <summary>
        /// Handles the locking and unlocking of hotbar slots on the client side.
        /// </summary>
        /// <param name="message"> The message containing the hotbar slot information. </param>
        public void OnLockSlotsMessage(LockSlotsMessage message)
        {
            if (ClientApi == null)
            {
                throw new InvalidOperationException("OnLockSlotsMessage can only be handled on the client side.");
            }

            var player = ClientApi.World.Player;
            var hotbar = player.InventoryManager.GetHotbarInventory();
            for (var i = 0; i < hotbar.Count; i++)
            {
                if (message.HotbarSlots?.Contains(i) == true)
                    LockedItemSlot.Lock(hotbar[i]);
                else LockedItemSlot.Restore(hotbar[i]);
            }
        }


        // ------------------------------
        //  Server side message handlers
        // ------------------------------

        /// <summary>
        /// Handles the interact action for a player.
        /// </summary>
        /// <param name="player"> The player performing the interact action. </param>
        /// <param name="message"> The message containing the interact action details. </param>
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
                    // Consider: add CarryEvent hook here before calling OnBlockInteractStart
                    block.OnBlockInteractStart(world, player, blockSelection);
                }
            }
        }

        /// <summary>
        /// Called when PickUp message received.
        /// </summary>
        /// <param name="player"> The player performing the pick-up action. </param>
        /// <param name="message"> The message containing the pick-up action details. </param>
        public void OnPickUpMessage(IServerPlayer player, PickUpMessage message)
        {

            if (message == null || message.Position == null)
            {
                FailCarryAction(player, message?.Position, FailureCode.InvalidData, FailureCode.PickUpFailed);
                return;
            }

            if (message.Slot != CarrySlot.Hands || !player.Entity.CanInteract(requireEmptyHanded: true))
            {
                FailCarryAction(player, message?.Position, FailureCode.CannotInteract, FailureCode.PickUpFailed);
                return;
            }

            string failureCode = FailureCode.Ignore;
            if (this.carryManager.TryPickUp(
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

            FailCarryAction(player, message.Position, failureCode, FailureCode.PickUpFailed);
        }

        /// <summary>
        /// Called when PlaceDown message received.
        /// </summary>
        /// <param name="player"> The player performing the place-down action. </param>
        /// <param name="message"> The message containing the place-down action details. </param>
        public void OnPlaceDownMessage(IServerPlayer player, PlaceDownMessage message)
        {
            if (message == null || message.Position == null || message.HitPosition == null || message.PlacedAt == null)
            {
                FailCarryAction(player, message?.Position, FailureCode.InvalidData, FailureCode.PlaceDownFailed);
                return;
            }

            if (!player.Entity.CanInteract(requireEmptyHanded: message.Slot != CarrySlot.Hands))
            {
                FailCarryAction(player, message?.Position, FailureCode.CannotInteract, FailureCode.PlaceDownFailed);
                return;
            }

            string failureCode = FailureCode.Ignore;
            if (this.carryManager.TryPlaceDownAt(
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

            FailCarryAction(player, message.Position, failureCode, FailureCode.PlaceDownFailed);
        }


        private void FailCarryAction(IServerPlayer player, BlockPos? pos, string failureCode, string defaultCode)
        {
            if (ServerApi == null)
            {
                throw new InvalidOperationException("FailCarryAction can only be called on the server side.");
            }

            if (pos != null) player.Entity.World.BlockAccessor.MarkBlockDirty(pos);
            this.carryManager.TouchCarriedAttributes(player.Entity);
            player.Entity.WatchedAttributes.MarkPathDirty("stats/walkspeed");
            this.carryManager.LockHotbarSlots(player);

            if (!string.IsNullOrEmpty(failureCode) && failureCode != FailureCode.Ignore)
            {
                player.SendIngameError(failureCode, LocalizationHelper.GetLang($"{defaultCode}-{failureCode}"));
            }
            else
            {
                player.SendIngameError(defaultCode, LocalizationHelper.GetLang(defaultCode));
            }
        }

        /// <summary>
        /// Called when the player swaps slots.
        /// </summary>
        /// <param name="player"> The player performing the slot swap action. </param>
        /// <param name="message"> The message containing the slot swap details. </param>
        public void OnSwapSlotsMessage(IServerPlayer player, SwapSlotsMessage message)
        {
            if ((message.First != message.Second)
                && (message.First == CarrySlot.Back || message.Second == CarrySlot.Back)
                && player.Entity.CanInteract(requireEmptyHanded: true))
            {
                var carriedHands = this.carryManager.GetCarried(player.Entity, CarrySlot.Hands);
                if (carriedHands != null && !this.BackSlotEnabled)
                {
                    this.carryManager.TouchCarriedAttributes(player.Entity);
                    return;
                }

                if (this.carryManager.SwapCarried(player.Entity, message.First, message.Second))
                {
                    Api.World.PlaySoundAt(new AssetLocation(CarryCode.SoundPath.Throw), player.Entity);
                    this.carryManager.TouchCarriedAttributes(player.Entity);
                }
            }
        }

        /// <summary>
        /// Handles the attach block to entity action for a player.
        /// </summary>
        /// <param name="player"> The player performing the action. </param>
        /// <param name="message"> The attach message containing details of the action. </param>
        public void OnAttachMessage(IServerPlayer player, AttachMessage message)
        {
            var api = ServerApi;
            if (api == null)
            {
                throw new InvalidOperationException("ServerApi is not initialized.");
            }

            string failureCode = FailureCode.Ignore;
            if (this.carryManager.TryAttach(player, message.TargetEntityId, message.SlotIndex, ref failureCode))
            {
                return;
            }

            SendAttachDetachFailure(player, failureCode);
        }

        /// <summary>
        /// Handles the detach block action for a player.
        /// </summary>
        /// <param name="player"> The player performing the detach action. </param>
        /// <param name="message"> The message containing the detach action details. </param>
        public void OnDetachMessage(IServerPlayer player, DetachMessage message)
        {
            var api = ServerApi;
            if (api == null)
            {
                throw new InvalidOperationException("ServerApi is not initialized.");
            }

            string failureCode = FailureCode.Ignore;
            if (this.carryManager.TryDetach(player, message.TargetEntityId, message.SlotIndex, ref failureCode))
            {
                return;
            }

            SendAttachDetachFailure(player, failureCode);

        }

        private void SendAttachDetachFailure(IServerPlayer player, string? failureCode)
        {
            if (failureCode == null || failureCode == FailureCode.Ignore)
            {
                return;
            }

            player.SendIngameError(failureCode, LocalizationHelper.GetLang(failureCode));
        }

        /// <summary>
        /// Handles the Put action for a player.
        /// </summary>
        /// <param name="player"> The player performing the put action. </param>
        /// <param name="message"> The message containing the put action details. </param>
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

            if(this.TransferLogic == null)
            {
                api.Logger.Error("OnPutMessage: TransferLogic is not initialized");
                return;
            }

            if (!this.TransferLogic.TryPutCarryable(player, message, out failureCode, out onScreenErrorMessage))
            {
                if (onScreenErrorMessage != null)
                {
                    player.SendIngameError(failureCode, onScreenErrorMessage);
                }
            }
        }

        /// <summary>
        /// Handles the Take action for a player.
        /// </summary>
        /// <param name="player"> The player performing the take action. </param>
        /// <param name="message"> The message containing the take action details. </param>
        public void OnTakeMessage(IServerPlayer player, TakeMessage message)
        {

            var api = ServerApi;
            if (api == null)
            {
                throw new InvalidOperationException("ServerApi is not initialized.");
            }
            
            if (this.TransferLogic == null)
            {
                api.Logger.Error("OnTakeMessage: TransferLogic is not initialized");
                return;
            }

            if (!this.TransferLogic.TryTakeCarryable(player, message, out string failureCode, out string onScreenErrorMessage))
            {
                if (onScreenErrorMessage != null)
                {
                    player.SendIngameError(failureCode, onScreenErrorMessage);
                }
            }
        }

        /// <summary>
        /// Handles the dismount action for a player.
        /// </summary>
        /// <param name="player"> The player performing the dismount action. </param>
        /// <param name="message"> The message containing the dismount action details. </param>
        private void OnDismountMessage(IServerPlayer player, DismountMessage message)
        {
            player.Entity.TryUnmount();

            player.Entity.World.GetEntityById(message.EntityId)?
                .GetBehavior<EntityBehaviorCreatureCarrier>()?
                .Seats?.FirstOrDefault(s => s.SeatId == message.SeatId)?
                .Controls?.StopAllMovement();
        }


        /// <summary>
        /// Handles the pickup entity action for a player.
        /// </summary>
        /// <param name="player"> The player performing the pickup. </param>
        /// <param name="message"> The message containing the entity ID. </param>
        private void OnPickupEntityMessage(IServerPlayer player, PickupEntityMessage message)
        {
            var entity = player.Entity.World.GetEntityById(message.EntityId) as EntityCarriedBlock;
            if (entity == null) return;

            if (TryPickupFromEntity(player, entity))
                entity.Die(EnumDespawnReason.Death, null);
        }

        /// <summary>
        /// Attempts to pick up a CarriedBlock from an EntityCarriedBlock into the player's Hands slot.
        /// </summary>
        private bool TryPickupFromEntity(IServerPlayer player, EntityCarriedBlock entity)
        {
            var carriedTree = entity.CarriedBlockTree;
            if (carriedTree == null) return false;

            var api = entity.Api;
            if (api == null) return false;

            var carriedBlock = CarriedBlockTreeSerializer.Deserialize(carriedTree, api);
            if (carriedBlock == null) return false;

            if (this.carryManager.GetCarried(player.Entity, CarrySlot.Hands) != null)
            {
                player.SendIngameError(FailureCode.AlreadyCarrying, LocalizationHelper.GetLang("pick-up-already-carrying"));
                return false;
            }

            if (!CanPickupFromEntity(player, entity))
                return false;

            this.carryManager.SetCarried(player.Entity, carriedBlock, CarrySlot.Hands);

            var pickupSound = carriedBlock.Block?.Sounds?.Place.Location
                ?? new AssetLocation(SoundPath.DefaultPlace);
            api.World.PlaySoundAt(pickupSound, player.Entity);

            return true;
        }

        private bool CanPickupFromEntity(IServerPlayer player, EntityCarriedBlock entity)
        {
            var cfg = this.configProvider.Config.CarriedBlockEntity;
            if (cfg == null) return true;

            if (!CarriedBlockAccessPolicy.CanPickup(
                player.WorldData.CurrentGameMode,
                player.PlayerUID,
                entity.OwnerUid,
                cfg.PickupAccess,
                cfg.GracePeriodSeconds,
                entity.DropTimeRealTicks))
            {
                player.SendIngameError(FailureCode.NotOwner, LocalizationHelper.GetLang("pickup-not-owner"));
                return false;
            }

            return true;
        }

        // ------------------------------
        //  Both side event handlers
        // ------------------------------

        /// <summary>
        /// Called when the active hotbar slot is about to change.
        /// </summary>
        /// <param name="entity"> The entity whose active hotbar slot is changing. </param>
        /// <returns> An EnumHandling value indicating whether the action should be prevented or allowed. </returns>
        public EnumHandling OnBeforeActiveSlotChanged(EntityAgent entity)
        {
            // If the player is carrying something in their hands,
            // prevent them from changing their active hotbar slot.
            return (this.carryManager.GetCarried(entity, CarrySlot.Hands) != null)
                ? EnumHandling.PreventDefault
                : EnumHandling.PassThrough;
        }


        // ------------------------------
        //  Client side event handlers
        // ------------------------------


        /// <summary>
        /// Called when the player is ready.
        /// </summary>
        /// <param name="handling"> An EnumHandling value indicating whether the action should be prevented or allowed. </param>
        /// <returns> True if the player is ready and the carry system is enabled, otherwise false. </returns>
        private bool OnPlayerReady(ref EnumHandling handling)
        {
            // Check if the player is ready and the carry system is enabled
            if (!this.IsCarryOnEnabled) return true;
            TransferLogic.InitTransferBehaviors(Api);

            return true;
        }

        /// <summary>
        /// Called when a player entity spawns client side.
        /// Configures the entity's attributes and listeners.
        /// Note: In VS, PlayerEntitySpawn fires for all players, but the guard
        /// on watchedClientPlayerEntity ensures only the local player is tracked.
        /// </summary>
        /// <param name="byPlayer"> The player whose entity has spawned. </param>
        private void OnPlayerEntitySpawn(IClientPlayer byPlayer)
        {
            // Guard: Only add listener if entity changed or not already registered
            if (this.watchedClientPlayerEntity == byPlayer.Entity && this.entityCarriedListener != null)
            {
                // Already registered for this entity, do nothing
                return;
            }

            // Cleanup previous listener if present
            if (this.watchedClientPlayerEntity?.WatchedAttributes?.OnModified != null && this.entityCarriedListener != null)
            {
                this.watchedClientPlayerEntity.WatchedAttributes.OnModified.Remove(this.entityCarriedListener);
            }

            // Assign new listener and entity
            this.entityCarriedListener = new TreeModifiedListener()
            {
                path = AttributeKey.Watched.EntityCarried,
                listener = this.interactionLogic.RefreshPlacedBlockInteractionHelp
            };

            this.watchedClientPlayerEntity = byPlayer.Entity;
            byPlayer.Entity.WatchedAttributes.OnModified.Add(this.entityCarriedListener);
        }

        /// <summary>
        /// Called when an entity action is triggered.
        /// </summary>
        /// <param name="action"> The action being triggered. </param>
        /// <param name="on"> A boolean indicating whether the action is being activated or deactivated. </param>
        /// <param name="handled"> An EnumHandling value indicating whether the action should be prevented or allowed. </param>
        public void OnEntityAction(EnumEntityAction action, bool on, ref EnumHandling handled)
        {

            if (!on && action == EnumEntityAction.InWorldRightMouseDown)
            {
                this.interactionLogic.CancelInteraction(resetTimeHeld: true);
                return;
            }

            // Only handle action if it's being activated rather than deactivated.
            if (!on || !this.IsCarryOnEnabled) return;

            bool isInteracting;
            switch (action)
            {
                // Right click (interact action) starts carry's pickup and place handling.
                case EnumEntityAction.InWorldRightMouseDown:
                    isInteracting = true; break;
                // Other actions, which are prevented while holding something.
                case EnumEntityAction.InWorldLeftMouseDown:
                    isInteracting = false;
                    break;
                case EnumEntityAction.Sprint:
                {
                    var player = this.ClientApi?.World.Player?.Entity;
                    if (player != null && CanSprintWhileCarrying(player)) return;
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                default: return;
            }

            this.interactionLogic.TryBeginInteraction(isInteracting, ref handled);

        }

        /// <summary>
        /// Client side game tick handler for player carry interactions
        /// </summary>
        /// <param name="deltaTime"> The time elapsed since the last game tick. </param>
        public void OnGameTick(float deltaTime)
        {
            if (!this.IsCarryOnEnabled) return;

            var entity = ClientApi?.World?.Player?.Entity;

            if (entity != null)
            {
                // Check if the interaction state has changed
                bool canInteractNow = entity.CanDoCarryAction(requireEmptyHanded: true);
                if (canInteractNow != this.lastCanInteractState)
                {
                    this.lastCanInteractState = canInteractNow;
                    this.interactionLogic.RefreshPlacedBlockInteractionHelp();
                }
            }

            this.interactionLogic.TryContinueInteraction(deltaTime);
            this.interactionLogic.FlushPlacedBlockInteractionHelpRefresh();

        }


        // ------------------------------
        //  Server side event handlers
        // ------------------------------


        /// <summary>
        /// Handles the spawning of a server entity.
        /// </summary>
        /// <param name="entity"> The entity that has spawned. </param>
        public void OnServerEntitySpawn(Entity entity)
        {
            // We handle player "spawning" in OnServerPlayerJoin.
            // If we send a LockSlotsMessage at this point, the client's player is still null.
            if (entity is EntityPlayer) return;

            // Set this again so walk speed modifiers and animations can be applied.
            foreach (var carried in this.carryManager.GetAllCarried(entity))
                this.carryManager.SetCarried(entity, carried, carried.Slot);
        }

        /// <summary>
        /// Handles the player now playing event on the server side, ensuring that any carried entities are properly initialized when a player starts playing.
        /// </summary>
        /// <param name="player"> The player who has started playing. </param>
        public void OnServerPlayerNowPlaying(IServerPlayer player)
        {
            foreach (var carried in this.carryManager.GetAllCarried(player.Entity))
                this.carryManager.SetCarried(player.Entity, carried, carried.Slot);
        }

        /// <summary>
        /// Disposes of the carry handler, unregistering all event listeners and message handlers to prevent memory leaks and unintended behavior when the handler is no longer needed.
        /// </summary>
        public void Dispose()
        {

            if (ClientApi != null)
            {
                var api = ClientApi;
                api.Input.InWorldAction -= OnEntityAction;
                api.Event.UnregisterGameTickListener(this.gameTickListenerId);
                if (this.watchedClientPlayerEntity?.WatchedAttributes?.OnModified != null && this.entityCarriedListener != null)
                {
                    this.watchedClientPlayerEntity.WatchedAttributes.OnModified.Remove(this.entityCarriedListener);
                    this.entityCarriedListener = null;
                    this.watchedClientPlayerEntity = null;
                }
                if (this.clientBeforeActiveSlotChangedDelegate != null)
                {
                    api.Event.BeforeActiveSlotChanged -= this.clientBeforeActiveSlotChangedDelegate;
                    this.clientBeforeActiveSlotChangedDelegate = null;
                }

                api.Event.PlayerEntitySpawn -= OnPlayerEntitySpawn;

                // Unsubscribe player ready and level finalize handlers (may throw if event was never subscribed)
                try { api.Event.IsPlayerReady -= OnPlayerReady; } catch (Exception ex) { api.Logger.Debug($"CarryOn: Could not unsubscribe IsPlayerReady during dispose: {ex.Message}"); }
            }

            if (this.ServerApi != null)
            {
                var api = this.ServerApi;
                api.Event.OnEntitySpawn -= OnServerEntitySpawn;
                api.Event.PlayerNowPlaying -= OnServerPlayerNowPlaying;

                if (this.serverBeforeActiveSlotChangedDelegate != null)
                {
                    api.Event.BeforeActiveSlotChanged -= this.serverBeforeActiveSlotChangedDelegate;
                    this.serverBeforeActiveSlotChangedDelegate = null;
                }
            }

        }
    }
}
