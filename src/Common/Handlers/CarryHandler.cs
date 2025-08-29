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
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Common.Logic;

namespace CarryOn.Common.Handlers
{
    /// <summary>
    ///   Takes care of core CarryCapacity handling, such as listening to input events,
    ///   picking up, placing and swapping blocks, as well as sending and handling messages.
    /// </summary>
    public class CarryHandler : IDisposable
    {


        private long gameTickListenerId;

        private readonly CarrySystem carrySystem;

        public bool IsCarryOnEnabled => this.carrySystem.CarryOnEnabled;

        public bool BackSlotEnabled { get; private set; }

        private ICarryManager carryManager;

        private ICoreAPI api;

        public ICoreAPI Api => this.api;
        public ICoreClientAPI ClientApi => this.api as ICoreClientAPI;
        public ICoreServerAPI ServerApi => this.api as ICoreServerAPI;
        public ICarryManager CarryManager => this.carryManager ??= this.carrySystem.CarryManager;

        // Clientside
        private InteractionProcessor interactProcessor { get; set; } 

        // Serverside
        private TransferProcessor transferProcessor { get; set; }

        public int MaxInteractionDistance { get; set; }

        public void SetHudHelp(Vintagestory.Client.NoObf.HudElementInteractionHelp hudHelp)
        {
            this.interactProcessor.HudHelp = hudHelp;
        }

        public CarryHandler(CarrySystem carrySystem)
        {
            if (carrySystem == null) throw new ArgumentNullException(nameof(carrySystem));
            this.carrySystem = carrySystem;

            BackSlotEnabled = this.carrySystem?.Config?.CarryOptions?.BackSlotEnabled ?? false;
        }

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

            this.interactProcessor = new InteractionProcessor(ClientApi, this.carrySystem);

            this.carrySystem.ClientChannel
                .RegisterMessageType<InteractMessage>()
                .RegisterMessageType<LockSlotsMessage>()
                .RegisterMessageType<PickUpMessage>()
                .RegisterMessageType<PlaceDownMessage>()
                .RegisterMessageType<SwapSlotsMessage>()
                .RegisterMessageType<AttachMessage>()
                .RegisterMessageType<DetachMessage>()
                .RegisterMessageType<PutMessage>()
                .RegisterMessageType<TakeMessage>()
                .RegisterMessageType<DismountMessage>()
                .SetMessageHandler<LockSlotsMessage>(OnLockSlotsMessage);

            input.RegisterHotKey(HotKeyCode.Pickup, GetLang("pickup-hotkey"), Default.PickupKeybind);
            input.RegisterHotKey(HotKeyCode.SwapBackModifier, GetLang("swap-back-hotkey"), Default.SwapBackModifierKeybind);

            input.InWorldAction += OnEntityAction;
            this.gameTickListenerId = ClientApi.Event.RegisterGameTickListener(OnGameTick, 0);

            ClientApi.Event.BeforeActiveSlotChanged +=
                (_) => OnBeforeActiveSlotChanged(ClientApi.World.Player.Entity);

            ClientApi.Event.PlayerEntitySpawn += OnPlayerEntitySpawn;

            ClientApi.Event.IsPlayerReady += OnPlayerReady;

        }

        public void InitServer(ICoreServerAPI api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));

            var serverEvent = api.Event;
            // TODO: Change this to a config value.
            MaxInteractionDistance = 6;

            this.transferProcessor = new TransferProcessor(api, this.carrySystem);

            this.carrySystem.ServerChannel
                .RegisterMessageType<InteractMessage>()
                .RegisterMessageType<LockSlotsMessage>()
                .RegisterMessageType<PickUpMessage>()
                .RegisterMessageType<PlaceDownMessage>()
                .RegisterMessageType<SwapSlotsMessage>()
                .RegisterMessageType<AttachMessage>()
                .RegisterMessageType<DetachMessage>()
                .RegisterMessageType<PutMessage>()
                .RegisterMessageType<TakeMessage>()
                .RegisterMessageType<DismountMessage>()
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

            serverEvent.BeforeActiveSlotChanged +=
                (player, _) => OnBeforeActiveSlotChanged(player.Entity);

            InitTransferBehaviors(api);
        }        

        /// <summary>
        /// Initializes the transfer behaviors for carryable blocks.
        /// </summary>
        /// <param name="api"></param>
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


        // ------------------------------
        //  Client side message handlers
        // ------------------------------

        /// <summary>
        /// Handles the locking and unlocking of hotbar slots on the client side.
        /// </summary>
        /// <param name="message"></param>
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
        /// <param name="player"></param>
        /// <param name="message"></param>
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
        /// <param name="player"></param>
        /// <param name="message"></param>
        public void OnPickUpMessage(IServerPlayer player, PickUpMessage message)
        {
            // FIXME: Do at least some validation of this data.

            var carried = player.Entity.GetCarried(message.Slot);
            if ((message.Slot == CarrySlot.Back) || (carried != null) ||
                !player.Entity.CanInteract(requireEmptyHanded: true))
            {
                InvalidCarry(player, message.Position);
            }

            var didPickUp = CarryManager.TryPickUp(player.Entity, message.Position, message.Slot, checkIsCarryable: true, playSound: true);
            if (!didPickUp)
            {
                InvalidCarry(player, message.Position);
            }

        }

        /// <summary>
        /// Called when PlaceDown message received.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        public void OnPlaceDownMessage(IServerPlayer player, PlaceDownMessage message)
        {
            // FIXME: Do at least some validation of this data.
            string failureCode = null;
            var carried = player.Entity.GetCarried(message.Slot);
            if ((message.Slot == CarrySlot.Back) || (carried == null) ||
                !player.Entity.CanInteract(requireEmptyHanded: message.Slot != CarrySlot.Hands) ||
                !CarryManager.TryPlaceDownAt(player, carried, message.Selection, out var placedAt, ref failureCode))
            {
                InvalidCarry(player, message.PlacedAt);

                if (failureCode != null && failureCode != FailureCode.Ignore)
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

        /// <summary>
        /// Called when the player swaps slots.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        public void OnSwapSlotsMessage(IServerPlayer player, SwapSlotsMessage message)
        {
            if (!BackSlotEnabled) return;

            if ((message.First != message.Second) && (message.First == CarrySlot.Back) ||
                player.Entity.CanInteract(requireEmptyHanded: true))
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

            if (!this.transferProcessor.TryPutCarryable(player, message, out failureCode, out onScreenErrorMessage))
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
        /// <param name="player"></param>
        /// <param name="message"></param>
        public void OnTakeMessage(IServerPlayer player, TakeMessage message)
        {

            if (!this.transferProcessor.TryTakeCarryable(player, message, out string failureCode, out string onScreenErrorMessage))
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


        // ------------------------------
        //  Both side event handlers
        // ------------------------------

        /// <summary>
        /// Called when the active hotbar slot is about to change.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
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
        /// <param name="handling"></param>
        /// <returns></returns>
        private bool OnPlayerReady(ref EnumHandling handling)
        {
            // Check if the player is ready and the carry system is enabled
            InitTransferBehaviors(Api);

            return true;
        }

        /// <summary>
        /// Called when a player entity spawns client side.
        /// Configures the entity's attributes and listeners.
        /// TODO: Confirm this only triggers when the local player spawns.
        /// </summary>
        /// <param name="byPlayer"></param>
        private void OnPlayerEntitySpawn(IClientPlayer byPlayer)
        {
            var entityCarriedListener = new TreeModifiedListener()
            {
                path = AttributeKey.Watched.EntityCarried,
                listener = this.interactProcessor.RefreshPlacedBlockInteractionHelp

            };

            byPlayer.Entity.WatchedAttributes.OnModified.Add(entityCarriedListener);
        }

        /// <summary>
        /// Called when an entity action is triggered.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="on"></param>
        /// <param name="handled"></param>
        public void OnEntityAction(EnumEntityAction action, bool on, ref EnumHandling handled)
        {

            if (!on && action == EnumEntityAction.InWorldRightMouseDown)
            {
                this.interactProcessor.CancelInteraction(resetTimeHeld: true);
                return;
            }

            // Only handle action if it's being activated rather than deactivated.
            if (!on || !IsCarryOnEnabled) return;

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
                    if (this.interactProcessor.AllowSprintWhileCarrying) return;
                    isInteracting = false;
                    break;
                default: return;
            }

            this.interactProcessor.TryInteraction(isInteracting, ref handled);

        }

        /// <summary>
        /// Client side game tick handler for player carry interactions
        /// </summary>
        /// <param name="deltaTime"></param>
        public void OnGameTick(float deltaTime)
        {
            if (!IsCarryOnEnabled) return;

            this.interactProcessor.TryContinueInteraction(deltaTime);

        }


        // ------------------------------
        //  Server side event handlers
        // ------------------------------


        /// <summary>
        /// Handles the spawning of a server entity.
        /// </summary>
        /// <param name="entity"></param>
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

        public void Dispose()
        {

            if (ClientApi != null)
            {
                
                ClientApi.Input.InWorldAction -= OnEntityAction;
                ClientApi.Event.UnregisterGameTickListener(this.gameTickListenerId);

                ClientApi.Event.BeforeActiveSlotChanged -=
                    (_) => OnBeforeActiveSlotChanged(ClientApi.World.Player.Entity);

                ClientApi.Event.PlayerEntitySpawn -= OnPlayerEntitySpawn;
            }

            if (ServerApi != null)
            {

                ServerApi.Event.OnEntitySpawn -= OnServerEntitySpawn;
                ServerApi.Event.PlayerNowPlaying -= OnServerPlayerNowPlaying;

                ServerApi.Event.BeforeActiveSlotChanged -=
                    (player, _) => OnBeforeActiveSlotChanged(player.Entity);
            }

        }
    }
}
