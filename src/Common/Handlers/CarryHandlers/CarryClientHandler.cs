using System;
using System.Linq;
using CarryOn.Client.Logic;
using CarryOn.Client.Logic.Interaction;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Logic;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using CarryOn.Utility;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Handlers.CarryHandlers
{
    internal class CarryClientHandler
    {
        private readonly ICarryManager carryManager;
        private readonly IConfigProvider configProvider;
        private readonly Func<bool> isCarryOnEnabled;

        private ICoreClientAPI? api;
        internal CarryInteractionController InteractionLogic { get; private set; } = null!;

        private long gameTickListenerId;
        private bool lastCanInteractState = true;
        private TreeModifiedListener? entityCarriedListener;
        private Entity? watchedClientPlayerEntity;
        private Vintagestory.API.Common.Func<ActiveSlotChangeEventArgs, EnumHandling>? beforeActiveSlotChangedDelegate;

        internal CarryClientHandler(ICarryManager carryManager, IConfigProvider configProvider, Func<bool> isCarryOnEnabled)
        {
            this.carryManager = carryManager;
            this.configProvider = configProvider;
            this.isCarryOnEnabled = isCarryOnEnabled;
        }

        internal void Init(
            ICoreClientAPI api,
            CarryInteractionController interactionLogic,
            Action hideOverlay,
            Action<float> setOverlayProgress,
            ClientModConfig? clientModConfig)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.InteractionLogic = interactionLogic ?? throw new ArgumentNullException(nameof(interactionLogic));

            api.Input.InWorldAction += OnEntityAction;
            this.gameTickListenerId = api.Event.RegisterGameTickListener(OnGameTick, 0);

            this.beforeActiveSlotChangedDelegate = (_entity) => OnBeforeActiveSlotChanged(api.World.Player.Entity);
            api.Event.BeforeActiveSlotChanged += this.beforeActiveSlotChangedDelegate;

            api.Event.PlayerEntitySpawn += OnPlayerEntitySpawn;
            api.Event.IsPlayerReady += OnPlayerReady;
        }

        internal void SetHudHelp(Vintagestory.Client.NoObf.HudElementInteractionHelp? hudHelp)
        {
            if (InteractionLogic == null)
            {
                throw new InvalidOperationException("SetHudHelp can only be called after Init.");
            }

            InteractionLogic.HudHelp = hudHelp;
        }

        internal void RefreshConfigCache()
        {
            InteractionLogic?.RefreshConfigCache();
        }

        // ------------------------------
        //  Client side message handlers
        // ------------------------------

        internal void OnLockSlotsMessage(LockSlotsMessage message)
        {
            if (api == null)
            {
                throw new InvalidOperationException("OnLockSlotsMessage can only be handled on the client side.");
            }

            var player = api.World.Player;
            var hotbar = player.InventoryManager.GetHotbarInventory();
            for (var i = 0; i < hotbar.Count; i++)
            {
                var slot = hotbar[i];
                if (slot == null) continue;
                if (message.HotbarSlots?.Contains(i) == true)
                    LockedItemSlot.Lock(slot);
                else LockedItemSlot.Restore(slot);
            }
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
        //  Client side event handlers
        // ------------------------------

        private bool OnPlayerReady(ref EnumHandling handling)
        {
            if (!isCarryOnEnabled()) return true;
            TransferLogic.InitTransferBehaviors(api!, carryManager);
            return true;
        }

        private void OnPlayerEntitySpawn(IClientPlayer byPlayer)
        {
            if (watchedClientPlayerEntity == byPlayer.Entity && entityCarriedListener != null)
            {
                return;
            }

            if (watchedClientPlayerEntity?.WatchedAttributes?.OnModified != null && entityCarriedListener != null)
            {
                watchedClientPlayerEntity.WatchedAttributes.OnModified.Remove(entityCarriedListener);
            }

            entityCarriedListener = new TreeModifiedListener()
            {
                path = AttributeKeys.Watched.EntityCarried,
                listener = InteractionLogic.RefreshPlacedBlockInteractionHelp
            };

            watchedClientPlayerEntity = byPlayer.Entity;
            byPlayer.Entity.WatchedAttributes.OnModified.Add(entityCarriedListener);
        }

        internal void OnEntityAction(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            if (!on && action == EnumEntityAction.InWorldRightMouseDown)
            {
                InteractionLogic.CancelInteraction(resetTimeHeld: true);
                return;
            }

            if (!on || !isCarryOnEnabled()) return;

            bool isInteracting;
            switch (action)
            {
                case EnumEntityAction.InWorldRightMouseDown:
                    isInteracting = true; break;
                case EnumEntityAction.InWorldLeftMouseDown:
                    isInteracting = false;
                    break;
                case EnumEntityAction.Sprint:
                {
                    var player = api?.World.Player?.Entity;
                    if (player != null && CanSprintWhileCarrying(player)) return;
                    handled = EnumHandling.PreventDefault;
                    return;
                }
                default: return;
            }

            InteractionLogic.TryBeginInteraction(isInteracting, ref handled);
        }

        internal void OnGameTick(float deltaTime)
        {
            if (!isCarryOnEnabled()) return;

            var entity = api?.World?.Player?.Entity;

            if (entity != null)
            {
                bool canInteractNow = entity.CanDoCarryAction(requireEmptyHanded: true);
                if (canInteractNow != lastCanInteractState)
                {
                    lastCanInteractState = canInteractNow;
                    InteractionLogic.RefreshPlacedBlockInteractionHelp();
                }
            }

            InteractionLogic.TryContinueInteraction(deltaTime);
            InteractionLogic.FlushPlacedBlockInteractionHelpRefresh();
        }

        // ------------------------------
        //  Helpers
        // ------------------------------

        private bool CanSprintWhileCarrying(EntityPlayer player)
        {
            var cfg = configProvider.Config.CarryWalkSpeed;
            if (cfg == null) return true;

            var handsCarried = carryManager.GetCarried(player, CarrySlot.Hands);
            var backCarried = carryManager.GetCarried(player, CarrySlot.Back);

            if (handsCarried != null && !cfg.HandsAllowSprint) return false;
            if (backCarried != null && !cfg.BackAllowSprint) return false;

            return true;
        }

        // ------------------------------
        //  Dispose
        // ------------------------------

        internal void Dispose()
        {
            if (api == null) return;

            api.Input.InWorldAction -= OnEntityAction;
            api.Event.UnregisterGameTickListener(this.gameTickListenerId);

            if (watchedClientPlayerEntity?.WatchedAttributes?.OnModified != null && entityCarriedListener != null)
            {
                watchedClientPlayerEntity.WatchedAttributes.OnModified.Remove(entityCarriedListener);
                entityCarriedListener = null;
                watchedClientPlayerEntity = null;
            }

            if (beforeActiveSlotChangedDelegate != null)
            {
                api.Event.BeforeActiveSlotChanged -= this.beforeActiveSlotChangedDelegate;
                beforeActiveSlotChangedDelegate = null;
            }

            api.Event.PlayerEntitySpawn -= OnPlayerEntitySpawn;

            try { api.Event.IsPlayerReady -= OnPlayerReady; }
            catch (Exception ex) { api.Logger.Debug($"CarryOn: Could not unsubscribe IsPlayerReady during dispose: {ex.Message}"); }
        }
    }
}
