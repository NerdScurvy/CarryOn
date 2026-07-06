using System;
using CarryOn.Common.Network;
using CarryOn.Common.Services;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Models;
using CarryOn.API.Event;
using CarryOn.Client.Logic;
using CarryOn.Client.Logic.CarryRenderer;
using CarryOn.Client.Logic.TransformGroupResolvers;
using CarryOn.Client.Logic.TransformTemplates;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Handlers;
using CarryOn.Common.Handlers.PackAdjustment;
using CarryOn.Common.Logic;
using CarryOn.Server.Behaviors;
using CarryOn.Server.Logic;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using CarryOn.Common.Entities;
using Newtonsoft.Json;
using CarryOn.CarryOnLib;
using static CarryOn.Common.Models.CarryCode;

[assembly: ModInfo("Carry On",
    modID: "carryon",
    Version = "2.0.0-pre.7",
    Description = "Adds the capability to carry various things",
    Website = "https://github.com/NerdScurvy/CarryOn",
    Authors = new[] { "copygirl", "NerdScurvy" })]
[assembly: ModDependency("game", "1.22.0")]
[assembly: ModDependency("carryonlib", "1.0.0-pre.7")]

namespace CarryOn
{
    /// <summary> Main system for the "Carry On" mod, which allows certain
    ///           blocks such as chests to be picked up and carried around. </summary>
    public class CarrySystem : ModSystem, IConfigProvider
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
        public CarriedBlockEntityService? CarriedBlockEntityService { get; private set; }

        // Common
        public HotKeyHandler? HotKeyHandler { get; private set; }

        public CarryHandler? CarryHandler { get; private set; }

        public CarryEvents CarryEvents { get; private set; } = null!;

        public CarryOnLibSystem? CarryOnLib { get; private set; }

        public ICarryManager? CarryManager => CarryOnLib?.CarryManager;

        public CarryOnConfigService ConfigService { get; private set; } = null!;

        public CarryOnConfig Config => ConfigService.Config;

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

            CarryOnConfig config;
            if (carryOnWorldConfig == null)
            {
                api.World.Logger.Warning("CarryOn: No world config found for CarryOn; using defaults");
                config = new CarryOnConfig();
            }
            else
            {
                config = CarryOnConfig.FromTreeAttribute(carryOnWorldConfig);
            }

            ConfigService = new CarryOnConfigService(config);
            EntityCarriedBlock.Config = config.CarriedBlockEntity;

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

            api.RegisterEntity("EntityCarriedBlock", typeof(EntityCarriedBlock));

            CarryEvents = new CarryEvents
            {
                OnEventHandlerError = ex => api.World.Logger.Error(ex.ToString())
            };

            CarryOnLib = api.ModLoader.GetModSystem<CarryOnLibSystem>();
            if (CarryOnLib != null)
            {
                CarryOnLib.CarryManager = new CarryManager(api, this, CarryEvents);
                CarryHandler = new CarryHandler(CarryOnLib.CarryManager, this.Config, () => this.ClientModConfig?.Config?.CarryOnEnabled ?? true);
                HotKeyHandler = new HotKeyHandler(CarryOnLib.CarryManager);
            }
            else
            {
                api.World.Logger.Error("CarryOn: Failed to load CarryOnLib mod system");
            }

            WireConfigChangeHandlers();

        }

        private void WireConfigChangeHandlers()
        {
            ConfigService.OnConfigChanged += _ => Config.InvalidateBackpackCache();
            ConfigService.OnConfigChanged += cfg => this.CarryHandler?.UpdateConfig(cfg);
            ConfigService.OnConfigChanged += cfg => this.EntityCarryRenderer?.UpdateConfig(cfg);
            ConfigService.OnConfigChanged += _ => EntityCarriedBlock.Config = Config.CarriedBlockEntity;
            ConfigService.OnConfigChanged += _ =>
            {
                if (ServerApi == null) return;
                if (ServerApi.World.Config is ITreeAttribute worldTree)
                    worldTree[ModId] = Config.ToTreeAttribute();
                ServerApi.StoreModConfig(Config, ConfigFile);
            };
            ConfigService.OnConfigChanged += _ =>
            {
                if (ServerChannel == null) return;
                var json = JsonConvert.SerializeObject(Config);
                ServerChannel.BroadcastPacket(new ConfigSyncMessage(json));
            };
        }

        private void InitCommon(ICoreAPI api)
        {
            BlockBehaviorCarryableInteract.Init(CarryManager!);
            CarryManager!.InitEvents(api);
        }

        private void OnClientConfigSync(ConfigSyncMessage message)
        {
            if (message.ConfigJson == null || ClientApi == null) return;

            try
            {
                var reloaded = JsonConvert.DeserializeObject<CarryOnConfig>(message.ConfigJson);
                if (reloaded != null)
                {
                    reloaded.UpgradeVersion();
                    ConfigService.Replace(reloaded);
                    ClientApi.Logger?.Notification("CarryOn: Config synced from server and applied");
                }
            }
            catch (Exception ex)
            {
                ClientApi.Logger?.Warning("CarryOn: Failed to apply synced config: " + ex.Message);
            }
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            if (CarryManager == null)
            {
                api.Logger.Error("CarryOn: CarryManager not available - CarryOnLib failed to load. Client-side features disabled.");
                return;
            }

            ClientChannel = api.Network.RegisterChannel(ModId);

            EntityBehaviorAttachableCarryable.Init(CarryManager);
            CarryableInteractionHelpBuilder.Init(CarryManager);
            InitCommon(api);

            HudOverlayRenderer = new HudOverlayRenderer(api);
            HudCarried = new HudCarried(api, CarryManager);

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

            CarryHandler!.InitClient(api, this.ClientChannel!,
                () => { if (this.HudOverlayRenderer != null) this.HudOverlayRenderer.CircleVisible = false; },
                p => { if (this.HudOverlayRenderer != null) this.HudOverlayRenderer.CircleProgress = p; },
                this.ClientModConfig!);
            ClientChannel.SetMessageHandler<ConfigSyncMessage>(OnClientConfigSync);
            HotKeyHandler!.InitClient(api, this.ClientChannel!, this.ClientModConfig!);

            CarryManager?.RegisterRootTransformGroupResolver(ModId, new GenericCodePathTransformGroupResolver());
            CarryManager?.RegisterAttachmentTransformGroupResolver(ModId, new DataAttributeTransformGroupResolver());
            CarryManager?.RegisterAttachmentTransformGroupResolver(ModId, new ContainerSlotTransformGroupResolverBase());
            CarryManager?.RegisterAttachmentTransformGroupResolver(ModId, new DisplayCaseTransformGroupResolver());
            CarryManager?.RegisterAttachmentTransformGroupResolver(ModId, new MoldRackTransformGroupResolver());
            var plantContainerResolver = new PlantContainerTransformGroupResolver();
            CarryManager?.RegisterRootTransformGroupResolver(ModId, plantContainerResolver);
            CarryManager?.RegisterAttachmentTransformGroupResolver(ModId, plantContainerResolver);

            if (Config.DebuggingOptions.EnablePackAdjustmentTool)
            {
                PackAdjustmentHandler = new PackAdjustmentHandler(api, this.CarryManager, this.Config, this.EntityCarryRenderer);
                PackAdjustmentHandler.InitClient();
            }
            // Register client chat commands through Commands helper
            var commands = new ClientCommands(api, this.ClientModConfig!, this.EntityCarryRenderer);
            commands.Register();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            if (CarryManager == null)
            {
                api.Logger.Error("CarryOn: CarryManager not available - CarryOnLib failed to load. Server-side features disabled.");
                return;
            }

            EntityBehaviorDropCarriedOnDamage.Init(CarryManager, Config.DropCarriedOnDamage);
            InitCommon(api);
            api.Register<EntityBehaviorDropCarriedOnDamage>();
            api.RegisterEntity("EntityCarriedBlock", typeof(EntityCarriedBlock));

            ServerApi = api;
            ServerChannel = api.Network.RegisterChannel(ModId);

            CarriedBlockEntityService = new CarriedBlockEntityService(api);

            DeathHandler = new DeathHandler(api, CarryManager);
            CarryHandler!.InitServer(api, this.ServerChannel!);
            HotKeyHandler!.InitServer(api, this.ServerChannel!);

            ConfigService.SetupFileWatcher(api);

            var serverCommands = new ServerCommands(api, ConfigService);
            serverCommands.Register();
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
            ConfigService.Dispose();

            CarryPatcher.Remove();

            if (ClientApi != null)
            {
                EntityCarryRenderer?.Dispose();
                HudOverlayRenderer?.Dispose();
                HudCarried?.Dispose();
            }

            CarryHandler?.Dispose();
            base.Dispose();
        }
    }
}
