using System;
using CarryOn.Client.Logic;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Logic;
using CarryOn.Common.Network;
using CarryOn.API.Common.Interfaces;
using CarryOn.Client.Logic.Interaction;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Handlers.CarryHandlers
{
    public class CarryHandler : IDisposable
    {
        private readonly Func<bool> getIsCarryOnEnabled;
        private readonly ICarryManager carryManager;
        private readonly IConfigProvider configProvider;

        private ICoreAPI? api;
        private TransferLogic? transferLogic;

        private CarryServerHandler? serverHandler;
        private CarryClientHandler? clientHandler;

        private Vintagestory.API.Common.Func<IServerPlayer, ActiveSlotChangeEventArgs, EnumHandling>? serverBeforeActiveSlotChangedDelegate;

        public bool IsCarryOnEnabled => getIsCarryOnEnabled();
        public bool BackSlotEnabled => configProvider.Config.CarryOptions?.BackSlotEnabled ?? false;

        public ICoreAPI Api => api ?? throw new InvalidOperationException("CarryHandler not initialized");
        public ICoreClientAPI? ClientApi => api as ICoreClientAPI;
        public ICoreServerAPI? ServerApi => api as ICoreServerAPI;

        public CarryHandler(ICarryManager carryManager, Func<bool> isCarryOnEnabled)
        {
            ArgumentNullException.ThrowIfNull(carryManager);
            ArgumentNullException.ThrowIfNull(isCarryOnEnabled);

            this.carryManager = carryManager;
            this.configProvider = carryManager as IConfigProvider
                ?? throw new ArgumentException("carryManager must implement IConfigProvider", nameof(carryManager));
            this.getIsCarryOnEnabled = isCarryOnEnabled;
        }

        public void InitClient(
            ICoreAPI api,
            IClientNetworkChannel clientChannel,
            Action hideOverlay,
            Action<float> setOverlayProgress,
            ClientModConfig? clientModConfig = null)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));

            if (ClientApi == null)
                throw new InvalidOperationException("CarryHandler.InitClient can only be initialized on the client side.");
            if (ClientApi.Input == null)
                throw new InvalidOperationException("CarryHandler.InitClient requires Input to be initialized.");

            ArgumentNullException.ThrowIfNull(clientChannel);
            ArgumentNullException.ThrowIfNull(hideOverlay);
            ArgumentNullException.ThrowIfNull(setOverlayProgress);

            var interactionLogic = new CarryInteractionController(
                ClientApi, carryManager, clientChannel, hideOverlay, setOverlayProgress, clientModConfig);

            clientHandler = new CarryClientHandler(carryManager, configProvider, getIsCarryOnEnabled);
            clientHandler.Init(ClientApi, interactionLogic, hideOverlay, setOverlayProgress, clientModConfig);

            CarryNetworkSetup.RegisterClient(clientChannel)
                .SetMessageHandler<LockSlotsMessage>(clientHandler.OnLockSlotsMessage);

            ClientApi.Input.RegisterHotKey(HotKeyCodes.Pickup, LocalizationHelper.GetLang("pickup-hotkey"), Defaults.PickupKeybind);
            ClientApi.Input.RegisterHotKey(HotKeyCodes.SwapBackModifier, LocalizationHelper.GetLang("swap-back-hotkey"), Defaults.SwapBackModifierKeybind);
        }

        public void InitServer(ICoreServerAPI api, IServerNetworkChannel serverChannel)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            ArgumentNullException.ThrowIfNull(serverChannel);

            this.transferLogic = new TransferLogic(api, carryManager);

            serverHandler = new CarryServerHandler(carryManager, configProvider);
            serverHandler.Init(api, transferLogic);

            CarryNetworkSetup.RegisterServer(serverChannel)
                .SetMessageHandler<InteractMessage>(serverHandler.OnInteractMessage)
                .SetMessageHandler<PickUpMessage>(serverHandler.OnPickUpMessage)
                .SetMessageHandler<PlaceDownMessage>(serverHandler.OnPlaceDownMessage)
                .SetMessageHandler<SwapSlotsMessage>(serverHandler.OnSwapSlotsMessage)
                .SetMessageHandler<AttachMessage>(serverHandler.OnAttachMessage)
                .SetMessageHandler<DetachMessage>(serverHandler.OnDetachMessage)
                .SetMessageHandler<PutMessage>(serverHandler.OnPutMessage)
                .SetMessageHandler<TakeMessage>(serverHandler.OnTakeMessage)
                .SetMessageHandler<DismountMessage>(serverHandler.OnDismountMessage)
                .SetMessageHandler<PickupEntityMessage>(serverHandler.OnPickupEntityMessage);

            var serverEvent = api.Event;
            serverEvent.OnEntitySpawn += serverHandler.OnServerEntitySpawn;
            serverEvent.PlayerNowPlaying += serverHandler.OnServerPlayerNowPlaying;

            this.serverBeforeActiveSlotChangedDelegate = (player, _) => serverHandler.OnBeforeActiveSlotChanged(player.Entity);
            serverEvent.BeforeActiveSlotChanged += this.serverBeforeActiveSlotChangedDelegate;

            if (IsCarryOnEnabled)
                TransferLogic.InitTransferBehaviors(api, carryManager);
        }

        public void SetHudHelp(Vintagestory.Client.NoObf.HudElementInteractionHelp? hudHelp)
        {
            clientHandler?.SetHudHelp(hudHelp);
        }

        public void RefreshConfigCache()
        {
            clientHandler?.RefreshConfigCache();
        }

        public void Dispose()
        {
            clientHandler?.Dispose();

            if (ServerApi != null && this.serverBeforeActiveSlotChangedDelegate != null)
            {
                ServerApi.Event.BeforeActiveSlotChanged -= this.serverBeforeActiveSlotChangedDelegate;
                this.serverBeforeActiveSlotChangedDelegate = null;
            }
        }
    }
}
