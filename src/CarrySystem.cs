
using System;
using CarryOn.API.Common;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Event;
using CarryOn.Client;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Handlers;
using CarryOn.Config;
using CarryOn.Server.Behaviors;
using CarryOn.Server.Logic;
using CarryOn.Utility;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static CarryOn.API.Common.Models.CarryCode;

[assembly: ModInfo("Carry On",
    modID: "carryon",
    Version = "2.0.0-pre.1",
    Description = "Adds the capability to carry various things",
    Website = "https://github.com/NerdScurvy/CarryOn",
    Authors = new[] { "copygirl", "NerdScurvy" })]
[assembly: ModDependency("game", "1.21.0")]
[assembly: ModDependency("carryonlib", "1.0.0-pre.1")]

namespace CarryOn
{
    /// <summary> Main system for the "Carry On" mod, which allows certain
    ///           blocks such as chests to be picked up and carried around. </summary>
    public class CarrySystem : ModSystem
    {
        // Whether CarryOn is enabled on the client side. This does not affect server-side behavior.
        public bool CarryOnEnabled { get; set; } = true;

        // Client
        public ICoreClientAPI ClientApi { get; private set; }
        public IClientNetworkChannel ClientChannel { get; private set; }
        public EntityCarryRenderer EntityCarryRenderer { get; private set; }
        public HudOverlayRenderer HudOverlayRenderer { get; private set; }

        // Server
        public ICoreServerAPI ServerApi { get; private set; }
        public IServerNetworkChannel ServerChannel { get; private set; }
        public DeathHandler DeathHandler { get; private set; }

        // Common
        public HotKeyHandler HotKeyHandler { get; private set; }

        public CarryHandler CarryHandler { get; private set; }

        public CarryEvents CarryEvents { get; private set; }

        public CarryOnLib.Core CarryOnLib { get; private set; }

        public ICarryManager CarryManager => CarryOnLib?.CarryManager;

        public CarryOnConfig Config { get; private set; }

        private Harmony harmony;

        public static string GetLang(string key) => Lang.Get(CarryOnCode(key)) ?? key;

        public override void StartPre(ICoreAPI api)
        {
            if (api.Side == EnumAppSide.Client)
            {
                ClientApi = api as ICoreClientAPI;
            }
            else
            {
                ServerApi = api as ICoreServerAPI;

                // Load the configuration into the world config
                var modConfig = new ModConfig();
                modConfig.Load(api);                
            }

            base.StartPre(api);

            // Extract the configuration from the world config
            Config = CarryOnConfig.FromTreeAttribute(api?.World?.Config?.GetTreeAttribute(ModId));

            if (!Config.DebuggingOptions.DisableHarmonyPatch)
            {
                try
                {
                    this.harmony = new Harmony(ModId);
                    this.harmony.PatchAll();
                    api.World.Logger.Notification("CarryOn: Harmony patches enabled.");
                }
                catch (Exception ex)
                {
                    api.World.Logger.Error($"CarryOn: Exception during Harmony patching: {ex}");
                }
            }
            else
            {
                api.World.Logger.Notification("CarryOn: Harmony patches are disabled by config.");
                // If runtime config changes are supported, call this.harmony.UnpatchAll("CarryOn") here
            }

            api.World.Logger.Event("started 'CarryOn' mod");
        }

        public override void Start(ICoreAPI api)
        {
            // Legacy support for EntityBoatCarryOn - pre.1
            api.RegisterEntity("EntityBoatCarryOn", typeof(EntityBoat));

            api.Register<BlockBehaviorCarryable>();
            api.Register<BlockBehaviorCarryableInteract>();
            api.Register<EntityBehaviorAttachableCarryable>();

            CarryHandler = new CarryHandler(this);
            CarryEvents = new CarryEvents();

            HotKeyHandler = new HotKeyHandler(this);

            CarryOnLib = api.ModLoader.GetModSystem<CarryOnLib.Core>();
            if (CarryOnLib != null)
            {
                CarryOnLib.CarryManager = new CarryManager(api, this);
            }
            else
            {
                api.World.Logger.Error("CarryOn: Failed to load CarryOnLib mod system");
            }
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api;
            ClientChannel = api.Network.RegisterChannel(ModId);

            EntityCarryRenderer = new EntityCarryRenderer(api);
            HudOverlayRenderer = new HudOverlayRenderer(api);

            CarryHandler.InitClient(api);
            CarryManager?.InitEvents(api);
            HotKeyHandler.InitClient(api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Register<EntityBehaviorDropCarriedOnDamage>();

            ServerApi = api;
            ServerChannel = api.Network.RegisterChannel(ModId);

            DeathHandler = new DeathHandler(api);
            CarryHandler.InitServer(api);
            CarryManager?.InitEvents(api);
            HotKeyHandler.InitServer(api);
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            if (api.Side == EnumAppSide.Server)
            {
                // Behavioral conditioning and reassignment
                var BehavioralConditioning = new BehavioralConditioning();
                BehavioralConditioning.Init(api, Config);
            }

            base.AssetsFinalize(api);
        }

        public override void Dispose()
        {
            if (this.harmony != null)
            {
                this.harmony.UnpatchAll(ModId);
                this.harmony = null;
            }

            if (ClientApi != null)
            {
                EntityCarryRenderer?.Dispose();
                HudOverlayRenderer?.Dispose();

                CarryHandler?.Dispose();
            }
            base.Dispose();
        }
    }
}
