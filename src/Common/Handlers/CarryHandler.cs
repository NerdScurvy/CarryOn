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
using static CarryOn.CarrySystem;
using Vintagestory.API.Util;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Common.Logic;
using static CarryOn.API.Common.Models.CarryCode;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using Vintagestory.API.Config;

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
            typeof(DismountMessage)
        ];


        private long gameTickListenerId;

        private readonly CarrySystem carrySystem;

        public bool IsCarryOnEnabled => this.carrySystem.CarryOnEnabled;

        private bool lastCanInteractState = true;

        public bool BackSlotEnabled { get; private set; }

        private ICarryManager carryManager;

        private ICoreAPI api;

        public ICoreAPI Api => this.api;
        public ICoreClientAPI ClientApi => this.api as ICoreClientAPI;
        public ICoreServerAPI ServerApi => this.api as ICoreServerAPI;
        public ICarryManager CarryManager => this.carryManager ??= this.carrySystem.CarryManager;

        // Clientside
        private InteractionLogic interactionLogic { get; set; }
        private TreeModifiedListener entityCarriedListener;
        private Entity watchedClientPlayerEntity;
        private Vintagestory.API.Common.Func<ActiveSlotChangeEventArgs, EnumHandling> clientBeforeActiveSlotChangedDelegate;
        private Vintagestory.API.Common.Func<IServerPlayer, ActiveSlotChangeEventArgs, EnumHandling> serverBeforeActiveSlotChangedDelegate;

        // Serverside
        private TransferLogic transferLogic { get; set; }


        /// <summary>
        /// Sets the HUD element for interaction help, so that it can be updated when carrying interactable blocks.
        /// </summary>
        /// <param name="hudHelp"> The HUD element for interaction help. </param>
        public void SetHudHelp(Vintagestory.Client.NoObf.HudElementInteractionHelp hudHelp)
        {
            this.interactionLogic.HudHelp = hudHelp;
        }

        /// <summary>
        /// Initializes a new instance of the CarryHandler class with the specified CarrySystem.
        /// </summary>
        /// <param name="carrySystem"> The CarrySystem instance to be used by this handler. </param>
        /// <exception cref="ArgumentNullException"> Thrown if the provided CarrySystem instance is null. </exception>
        public CarryHandler(CarrySystem carrySystem)
        {
            if (carrySystem == null) throw new ArgumentNullException(nameof(carrySystem));
            this.carrySystem = carrySystem;

            this.BackSlotEnabled = this.carrySystem?.Config?.CarryOptions?.BackSlotEnabled ?? false;
        }

        /// <summary>
        /// Initializes the carry handler on the client side, setting up message handlers, hotkeys and event listeners.
        /// </summary>
        /// <param name="api"> The client API instance. </param>
        /// <exception cref="ArgumentNullException"> Thrown if the provided API instance is null. </exception>
        /// <exception cref="InvalidOperationException"> Thrown if the method is called on the server side or if Input is not initialized. </exception>
        public void InitClient(ICoreAPI api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));

            if (ClientApi == null)
            {
                throw new InvalidOperationException("CarryHandler.InitClient can only be initialized on the client side.");
            }

            var input = ClientApi.Input;
            if (input == null)
            {
                throw new InvalidOperationException("CarryHandler.InitClient requires Input to be initialized.");
            }

            this.interactionLogic = new InteractionLogic(ClientApi, this.carrySystem);

            RegisterCarryMessageTypes(this.carrySystem.ClientChannel)
                .SetMessageHandler<LockSlotsMessage>(OnLockSlotsMessage);

            input.RegisterHotKey(HotKeyCode.Pickup, GetLang("pickup-hotkey"), Default.PickupKeybind);
            input.RegisterHotKey(HotKeyCode.SwapBackModifier, GetLang("swap-back-hotkey"), Default.SwapBackModifierKeybind);

            input.InWorldAction += OnEntityAction;
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
        /// <exception cref="ArgumentNullException"> Thrown if the provided API instance is null. </exception>
        public void InitServer(ICoreServerAPI api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));

            var serverEvent = api.Event;

            this.transferLogic = new TransferLogic(api, this.carrySystem);

            RegisterCarryMessageTypes(this.carrySystem.ServerChannel)
                .SetMessageHandler<InteractMessage>(OnInteractMessage)
                .SetMessageHandler<PickUpMessage>(OnPickUpMessage)
                .SetMessageHandler<PlaceDownMessage>(OnPlaceDownMessage)
                .SetMessageHandler<SwapSlotsMessage>(OnSwapSlotsMessage)
                .SetMessageHandler<AttachMessage>(OnAttachMessage)
                .SetMessageHandler<DetachMessage>(OnDetachMessage)
                .SetMessageHandler<PutMessage>(OnPutMessage)
                .SetMessageHandler<TakeMessage>(OnTakeMessage)
                .SetMessageHandler<DismountMessage>(OnDismountMessage);

            serverEvent.OnEntitySpawn += OnServerEntitySpawn;
            serverEvent.PlayerNowPlaying += OnServerPlayerNowPlaying;

            this.serverBeforeActiveSlotChangedDelegate = (player, _) => OnBeforeActiveSlotChanged(player.Entity);
            serverEvent.BeforeActiveSlotChanged += this.serverBeforeActiveSlotChangedDelegate;

            if (this.IsCarryOnEnabled)
                TransferLogic.InitTransferBehaviors(api);
        }

        private static TChannel RegisterCarryMessageTypes<TChannel>(TChannel channel) where TChannel : class
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            var registerMessageTypeMethod = typeof(TChannel)
                .GetMethods()
                .FirstOrDefault(m => m.Name == nameof(IClientNetworkChannel.RegisterMessageType)
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 0);

            if (registerMessageTypeMethod == null)
            {
                throw new InvalidOperationException($"Could not find RegisterMessageType<T>() on channel type {typeof(TChannel).Name}.");
            }

            object current = channel;
            foreach (var messageType in CarryMessageTypes)
            {
                current = registerMessageTypeMethod.MakeGenericMethod(messageType).Invoke(current, null);
            }

            return (TChannel)current;
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
                    // TODO: add event hook here
                    block?.OnBlockInteractStart(world, player, blockSelection);
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
                FailCarryAction(player, message?.Position, "invalid-data", "pick-up-failed");
                return;
            }

            if (message.Slot != CarrySlot.Hands || !player.Entity.CanInteract(requireEmptyHanded: true))
            {
                FailCarryAction(player, message?.Position, "cannot-interact", "pick-up-failed");
                return;
            }
            
            string failureCode = FailureCode.Ignore;
            if (CarryManager.TryPickUp(
                player.Entity,
                message.Position,
                message.Slot,
                ref failureCode,
                checkIsCarryable: true,
                playSound: true))
            {
                return;
            }

            FailCarryAction(player, message.Position, failureCode, "pick-up-failed");
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
                FailCarryAction(player, message?.Position, "invalid-data", "place-down-failed");
                return;
            }

            if (!player.Entity.CanInteract(requireEmptyHanded: message.Slot != CarrySlot.Hands))
            {
                FailCarryAction(player, message?.Position, "cannot-interact", "place-down-failed");
                return;
            }

            string failureCode = FailureCode.Ignore;
            if (CarryManager.TryPlaceDownAt(
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

            FailCarryAction(player, message.Position, failureCode, "place-down-failed");
        }


        private void FailCarryAction(IServerPlayer player, BlockPos pos, string failureCode, string defaultCode)
        {
            // Invalidate the carry state for the player and block, so that the client can update and show the correct block and carry state after a failed pick-up or place-down attempt.
            if (pos != null) player.Entity.World.BlockAccessor.MarkBlockDirty(pos);
            CarryManager.TouchCarriedAttributes(player.Entity);
            player.Entity.WatchedAttributes.MarkPathDirty("stats/walkspeed");
            CarryManager.LockHotbarSlots(player);                

            if (!string.IsNullOrEmpty(failureCode) && failureCode != FailureCode.Ignore)
            {
                ServerApi.SendIngameError(player, failureCode, GetLang($"{defaultCode}-{failureCode}"));
            }
            else
            {
                ServerApi.SendIngameError(player, defaultCode, GetLang(defaultCode));
            }
        }

        /// <summary>
        /// Called when the player swaps slots.
        /// </summary>
        /// <param name="player"> The player performing the slot swap action. </param>
        /// <param name="message"> The message containing the slot swap details. </param>
        public void OnSwapSlotsMessage(IServerPlayer player, SwapSlotsMessage message)
        {
            if (!this.BackSlotEnabled) return;
            if ((message.First != message.Second)
                && (message.First == CarrySlot.Back || message.Second == CarrySlot.Back)
                && player.Entity.CanInteract(requireEmptyHanded: true))
            {

                if (player.Entity.SwapCarried(message.First, message.Second))
                {
                    Api.World.PlaySoundAt(new AssetLocation("sounds/player/throw"), player.Entity);
                    CarryManager.TouchCarriedAttributes(player.Entity);
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
            if (CarryManager.TryAttach(player, message.TargetEntityId, message.SlotIndex, ref failureCode))
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
            if (CarryManager.TryDetach(player, message.TargetEntityId, message.SlotIndex, ref failureCode))
            {
                return;
            }

            SendAttachDetachFailure(player, failureCode);

        }

        private void SendAttachDetachFailure(IServerPlayer player, string failureCode)
        {
            if (failureCode == null || failureCode == FailureCode.Ignore)
            {
                return;
            }

            ServerApi.SendIngameError(player, failureCode, GetLang(failureCode));
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

            if (!this.transferLogic.TryPutCarryable(player, message, out failureCode, out onScreenErrorMessage))
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

            if (!this.transferLogic.TryTakeCarryable(player, message, out string failureCode, out string onScreenErrorMessage))
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
            return (entity.GetCarried(CarrySlot.Hands) != null)
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
        /// TODO: Confirm this only triggers when the local player spawns.
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
                    if (this.interactionLogic.AllowSprintWhileCarrying) return;
                    isInteracting = false;
                    break;
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
            foreach (var carried in entity.GetCarried())
                carried.Set(entity, carried.Slot);
        }

        /// <summary>
        /// Handles the player now playing event on the server side, ensuring that any carried entities are properly initialized when a player starts playing.
        /// </summary>
        /// <param name="player"> The player who has started playing. </param>
        public void OnServerPlayerNowPlaying(IServerPlayer player)
        {
            foreach (var carried in player.Entity.GetCarried())
                carried.Set(player.Entity, carried.Slot);
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

                // Unsubscribe player ready and level finalize handlers
                try { api.Event.IsPlayerReady -= OnPlayerReady; } catch { }
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
