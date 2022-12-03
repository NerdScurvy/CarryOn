using CarryOn.Client;
using CarryOn.Common;
using CarryOn.Common.Network;
using CarryOn.Server;
using CarryOn.Utility;
using CarryOn;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

[assembly: ModInfo("Carry On",
    Description = "Adds the capability to carry various things",
    Website = "https://github.com/NerdScurvy/CarryCapacity",
    Authors = new[] { "copygirl", "NerdScurvy" })]
[assembly: ModDependency("game", "1.17.0")]

namespace CarryOn
{
    /// <summary> Main system for the "Carry On" mod, which allows certain
    ///           blocks such as chests to be picked up and carried around. </summary>
    public class CarrySystem : ModSystem
    {
        public static string ModId = "carryon";
        public static float PlaceSpeedDefault = 0.75f;
        public static float SwapSpeedDefault = 1.5f;
        public static float PickUpSpeedDefault = 0.8f;
        public override bool AllowRuntimeReload => true;

        // Client
        public ICoreClientAPI ClientAPI { get; private set; }
        public IClientNetworkChannel ClientChannel { get; private set; }
        public EntityCarryRenderer EntityCarryRenderer { get; private set; }
        public HudOverlayRenderer HudOverlayRenderer { get; private set; }

        // Server
        public ICoreServerAPI ServerAPI { get; private set; }
        public IServerNetworkChannel ServerChannel { get; private set; }
        public DeathHandler DeathHandler { get; private set; }
        public BackwardCompatHandler BackwardCompatHandler { get; private set; }

        // Common
        public CarryHandler CarryHandler { get; private set; }

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);

            ModConfig.ReadConfig(api);
            api.World.Logger.Event("started 'CarryOn' mod");
        }

        public override void Start(ICoreAPI api)
        {
            api.Register<BlockBehaviorCarryable>();

            CarryHandler = new CarryHandler(this);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientAPI = api;
            ClientChannel = api.Network.RegisterChannel(ModId)
                .RegisterMessageType<LockSlotsMessage>()
                .RegisterMessageType<PickUpMessage>()
                .RegisterMessageType<PlaceDownMessage>()
                .RegisterMessageType<SwapSlotsMessage>();

            EntityCarryRenderer = new EntityCarryRenderer(api);
            HudOverlayRenderer = new HudOverlayRenderer(api);

            CarryHandler.InitClient();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Register<EntityBehaviorDropCarriedOnDamage>();

            ServerAPI = api;
            ServerChannel = api.Network.RegisterChannel(ModId)
                .RegisterMessageType<LockSlotsMessage>()
                .RegisterMessageType<PickUpMessage>()
                .RegisterMessageType<PlaceDownMessage>()
                .RegisterMessageType<SwapSlotsMessage>();

            DeathHandler = new DeathHandler(api);
            BackwardCompatHandler = new BackwardCompatHandler(api);

            CarryHandler.InitServer();
        }
    }
}
