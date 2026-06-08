using System;
using CarryOn.Common.Services;
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
using CarryOn.Common.Handlers.PackAdjustment;
using CarryOn.Server.Behaviors;
using CarryOn.Server.Logic;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
        // Client
        public ICoreClientAPI? ClientApi { get; private set; }
        public IClientNetworkChannel? ClientChannel { get; private set; }
        public EntityCarryRenderer? EntityCarryRenderer { get; private set; }
        public HudOverlayRenderer? HudOverlayRenderer { get; private set; }

        public HudCarried? HudCarried { get; private set; }
        public ClientModConfig? ClientModConfig { get; private set; }

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

            CarryHandler = new CarryHandler(this.Config, () => this.ClientModConfig?.Config?.CarryOnEnabled ?? true);
            CarryEvents = new CarryEvents
            {
                OnEventHandlerError = ex => api.World.Logger.Error(ex.ToString())
            };

            CarryOnLib = api.ModLoader.GetModSystem<CarryOnLib.Core>();
            if (CarryOnLib != null)
            {
                CarryOnLib.CarryManager = new CarryManager(api, this);
                CarryHandler.CarryManager = CarryOnLib.CarryManager;

                HotKeyHandler = new HotKeyHandler(CarryOnLib.CarryManager);
            }
            else
            {
                api.World.Logger.Error("CarryOn: Failed to load CarryOnLib mod system");
            }
           
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api;

            if (CarryManager == null)
            {
                api.Logger.Error("CarryOn: CarryManager not available — CarryOnLib failed to load. Client-side features disabled.");
                return;
            }

            ClientChannel = api.Network.RegisterChannel(ModId);

           
            HudOverlayRenderer = new HudOverlayRenderer(api);
            HudCarried = new HudCarried(api);

            try
            {
                ClientModConfig = new ClientModConfig();
                ClientModConfig.Load(api);

                ClientModConfig.Config?.ApplyTo();
            }
            catch (Exception ex)
            {
                api.Logger.Warning("CarryOn: Failed to apply client config: " + ex.Message);
            }

            HudCarried.UpdateParsedColors();

            EntityCarryRenderer = new EntityCarryRenderer(api, this.CarryManager, this.Config, this.ClientModConfig!);

            CarryHandler.InitClient(api, this.ClientChannel!,
                () => { if (this.HudOverlayRenderer != null) this.HudOverlayRenderer.CircleVisible = false; },
                p => { if (this.HudOverlayRenderer != null) this.HudOverlayRenderer.CircleProgress = p; });
            CarryManager?.InitEvents(api);
            HotKeyHandler.InitClient(api, this.ClientChannel!, this.ClientModConfig!);

            CarryManager?.RegisterTransformGroupResolver(ModId, new PlantContainerTransformGroupResolver());
            CarryManager?.RegisterTransformGroupResolver(ModId, new DisplayCaseTransformGroupResolver());
            CarryManager?.RegisterTransformGroupResolver(ModId, new MoldRackTransformGroupResolver());
            CarryManager?.RegisterTransformGroupResolver(ModId, new GenericCodePathTransformGroupResolver());

            if (Config.DebuggingOptions.EnablePackAdjustmentTool)
            {
                PackAdjustmentHandler = new PackAdjustmentHandler(api, this.CarryManager, this.Config, this.EntityCarryRenderer);
                PackAdjustmentHandler.InitClient();
            }
            // Register client chat commands through Commands helper
            var commands = new ClientCommands(api, this.ClientModConfig!);
            commands.Register();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            if (CarryManager == null)
            {
                api.Logger.Error("CarryOn: CarryManager not available — CarryOnLib failed to load. Server-side features disabled.");
                return;
            }

            api.Register<EntityBehaviorDropCarriedOnDamage>();

            ServerApi = api;
            ServerChannel = api.Network.RegisterChannel(ModId);

            DeathHandler = new DeathHandler(api);
            CarryHandler.InitServer(api, this.ServerChannel!);
            CarryManager.InitEvents(api);
            HotKeyHandler.InitServer(api, this.ServerChannel!);
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

            if (ServerApi != null)
            {
                CarryHandler?.Dispose();
            }
            base.Dispose();
        }
    }
}
