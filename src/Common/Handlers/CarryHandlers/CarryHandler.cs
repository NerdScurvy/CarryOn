using System;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Logic;
using CarryOn.Common.Network;
using CarryOn.API.Common.Interfaces;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CarryOn.Common.Handlers.CarryHandlers
{
    public class CarryHandler : IDisposable
    {
        private readonly ICarryManager carryManager;
        private readonly IConfigProvider configProvider;

        private ICoreAPI? api;
        private TransferLogic? transferLogic;

        private CarryServerHandler? serverHandler;

        private Vintagestory.API.Common.Func<IServerPlayer, ActiveSlotChangeEventArgs, EnumHandling>? serverBeforeActiveSlotChangedDelegate;

        public ICoreAPI Api => api ?? throw new InvalidOperationException("CarryHandler not initialized");
        public ICoreServerAPI? ServerApi => api as ICoreServerAPI;

        public bool BackSlotEnabled => configProvider.Config.CarryOptions?.BackSlotEnabled ?? false;

        public CarryHandler(ICarryManager carryManager, IConfigProvider configProvider)
        {
            ArgumentNullException.ThrowIfNull(carryManager);
            ArgumentNullException.ThrowIfNull(configProvider);

            this.carryManager = carryManager;
            this.configProvider = configProvider;
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
                .SetMessageHandler<PickUpGroundStorageBagMessage>(serverHandler.OnPickUpGroundStorageBagMessage)
                .SetMessageHandler<BagToBackpackMessage>(serverHandler.OnBagToBackpackMessage)
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
        }

        public void Dispose()
        {
            if (ServerApi == null) return;

            if (serverHandler != null)
            {
                ServerApi.Event.OnEntitySpawn -= serverHandler.OnServerEntitySpawn;
                ServerApi.Event.PlayerNowPlaying -= serverHandler.OnServerPlayerNowPlaying;
            }

            if (this.serverBeforeActiveSlotChangedDelegate != null)
            {
                ServerApi.Event.BeforeActiveSlotChanged -= this.serverBeforeActiveSlotChangedDelegate;
                this.serverBeforeActiveSlotChangedDelegate = null;
            }
        }
    }
}
