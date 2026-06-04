using System;
using CarryOn.API.Common;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.API.Event;
using CarryOn.Client.Logic;
using CarryOn.Client.Logic.CarryRenderer;
using CarryOn.Client.Logic.TransformGroupResolvers;
using CarryOn.Client.Logic.TransformTemplates;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Handlers;
using CarryOn.Server.Behaviors;
using CarryOn.Server.Logic;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using static CarryOn.API.Common.Models.CarryCode;

[assembly: ModInfo("Carry On",
    modID: "carryon",
    Version = "2.0.0-pre.3",
    Description = "Adds the capability to carry various things",
    Website = "https://github.com/NerdScurvy/CarryOn",
    Authors = new[] { "copygirl", "NerdScurvy" })]
[assembly: ModDependency("game", "1.22.0")]
[assembly: ModDependency("carryonlib", "1.0.0-pre.3")]

namespace CarryOn
{
    /// <summary> Main system for the "Carry On" mod, which allows certain
    ///           blocks such as chests to be picked up and carried around. </summary>
    public class CarrySystem : ModSystem
    {
        // Whether CarryOn is enabled on the client side. This does not affect server-side behavior.
        public bool CarryOnEnabled { get; set; } = true;

        // Client
        public ICoreClientAPI? ClientApi { get; private set; }
        public IClientNetworkChannel? ClientChannel { get; private set; }
        public EntityCarryRenderer? EntityCarryRenderer { get; private set; }
        public HudOverlayRenderer? HudOverlayRenderer { get; private set; }

        public void SetOverlayProgress(float progress)
        {
            if (HudOverlayRenderer != null)
                HudOverlayRenderer.CircleProgress = progress;
        }

        public void HideOverlay()
        {
            if (HudOverlayRenderer != null)
                HudOverlayRenderer.CircleVisible = false;
        }
        public HudCarried? HudCarried { get; private set; }
        public ClientModConfig? ClientConfig { get; private set; }

        public TransformTemplateManager? TransformTemplateManager { get; private set; }

        public PackAdjustmentHandler? PackAdjustmentHandler { get; private set; }

        // Server
        public ICoreServerAPI? ServerApi { get; private set; }
        public IServerNetworkChannel? ServerChannel { get; private set; }
        public DeathHandler? DeathHandler { get; private set; }

        // Common
        public HotKeyHandler HotKeyHandler { get; private set; } = null!;

        public CarryHandler CarryHandler { get; private set; } = null!;

        public CarryEvents CarryEvents { get; private set; } = null!;

        public CarryOnLib.Core? CarryOnLib { get; private set; }

        public ICarryManager? CarryManager => CarryOnLib?.CarryManager;

        public CarryOnConfig Config { get; private set; } = null!;

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

            var carryOnWorldConfig = api.World.Config?.GetTreeAttribute(ModId);

            if (carryOnWorldConfig == null)
            {
                api.World.Logger.Warning("CarryOn: No world config found for CarryOn; using defaults");
                Config = new CarryOnConfig();
            }
            else
            {
                Config = CarryOnConfig.FromTreeAttribute(carryOnWorldConfig);
            }  

            if (!Config.DebuggingOptions.DisableHarmonyPatch)
            {
                CarryPatcher.Apply(api);
            }
            else
            {
                api.World.Logger.Notification("CarryOn: Harmony patches are disabled by config.");
            }

            api.World.Logger.Event("started 'CarryOn' mod");
        }

        public override void Start(ICoreAPI api)
        {
            api.Register<BlockBehaviorCarryable>();
            api.Register<BlockBehaviorCarryableInteract>();
            api.Register<EntityBehaviorAttachableCarryable>();

            CarryHandler = new CarryHandler(this);
            CarryEvents = new CarryEvents
            {
                OnEventHandlerError = ex => api.World.Logger.Error(ex.ToString())
            };

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

            EntityCarryRenderer = new EntityCarryRenderer(api, this);
            HudOverlayRenderer = new HudOverlayRenderer(api);
            HudCarried = new HudCarried(api);

try
            {
                ClientConfig = new ClientModConfig();
                ClientConfig.Load(api);

                ClientConfig.Config?.ApplyTo();
            }
catch (Exception ex)
            {
                api.Logger.Warning("CarryOn: Failed to apply client config: " + ex.Message);
            }

            HudCarried.UpdateParsedColors();

            CarryHandler.InitClient(api);
            CarryManager?.InitEvents(api);
            HotKeyHandler.InitClient(api);

            CarryManager?.RegisterTransformGroupResolver(ModId, new PlantContainerTransformGroupResolver());
            CarryManager?.RegisterTransformGroupResolver(ModId, new DisplayCaseTransformGroupResolver());
            CarryManager?.RegisterTransformGroupResolver(ModId, new MoldRackTransformGroupResolver());
            CarryManager?.RegisterTransformGroupResolver(ModId, new GenericCodePathTransformGroupResolver());

            if (Config.DebuggingOptions.EnablePackAdjustmentTool)
            {
                PackAdjustmentHandler = new PackAdjustmentHandler(api, this);
                PackAdjustmentHandler.InitClient();
            }
            // Register client chat commands through Commands helper
            var commands = new ClientCommands(this);
            commands.Register();
        }

        // ...existing code...

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
                var behavioralConditioning = new BehavioralConditioning();
                behavioralConditioning.Init(api, Config);
            }
            else
            {
                TransformTemplateManager = TransformTemplateManager.InitializeFromBlocks((ICoreClientAPI)api);
            }
            base.AssetsFinalize(api);
        }

        public override void Dispose()
        {
            CarryPatcher.Remove();

            if (ClientApi != null)
            {
                EntityCarryRenderer?.Dispose();
                HudOverlayRenderer?.Dispose();
                HudCarried?.Dispose();

                CarryHandler?.Dispose();
            }
            base.Dispose();
        }
    }
}
