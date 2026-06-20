using System;
using System.IO;
using System.Reflection;
using CarryOn.Common.Network;
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
using CarryOn.Common.Logic;
using CarryOn.Server.Behaviors;
using CarryOn.Server.Logic;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using static CarryOn.API.Common.Models.CarryCode;
using Vintagestory.API.Config;

[assembly: ModInfo("Carry On",
    modID: "carryon",
    Version = "2.0.0-pre.5",
    Description = "Adds the capability to carry various things",
    Website = "https://github.com/NerdScurvy/CarryOn",
    Authors = new[] { "copygirl", "NerdScurvy" })]
[assembly: ModDependency("game", "1.22.0")]
[assembly: ModDependency("carryonlib", "1.0.0-pre.5")]

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

        // Config file watcher (server-side, detects AutoConfigLib saves and manual edits to reload config without restart)
        private FileSystemWatcher? configWatcher;
        private DateTime lastConfigFileChange = DateTime.MinValue;
        private readonly object configWatcherLock = new();

        // Common
        public HotKeyHandler HotKeyHandler { get; private set; } = null!;

        public CarryHandler CarryHandler { get; private set; } = null!;

        public CarryEvents CarryEvents { get; private set; } = null!;

        public CarryOnLib.Core? CarryOnLib { get; private set; }

        public ICarryManager? CarryManager => CarryOnLib?.CarryManager;

        public CarryOnConfig Config { get; private set; } = null!;

        /// <summary>
        /// Fired when config file changes
        /// Subscribe to invalidate cached config values.
        /// </summary>
        public event Action<CarryOnConfig>? OnConfigChanged;

        internal void NotifyConfigChanged()
        {
            OnConfigChanged?.Invoke(this.Config);
        }

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

  
            CarryEvents = new CarryEvents
            {
                OnEventHandlerError = ex => api.World.Logger.Error(ex.ToString())
            };

            CarryOnLib = api.ModLoader.GetModSystem<CarryOnLib.Core>();
            if (CarryOnLib != null)
            {
                CarryOnLib.CarryManager = new CarryManager(api, this);
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
            OnConfigChanged += _ => this.Config.InvalidateBackpackCache();
            OnConfigChanged += _ => this.CarryHandler?.InvalidateConfigCache();
            OnConfigChanged += _ =>
            {
                if (ServerApi == null) return;
                var tree = ServerApi.World.Config?.GetOrAddTreeAttribute(ModId);
                tree?.MergeTree(this.Config.ToTreeAttribute());
                ServerApi.StoreModConfig(this.Config, CarryCode.ConfigFile);
            };
        }

        private void TrySetupConfigWatcher(ICoreServerAPI api)
        {
            try
            {
                var configPath = Path.Combine(GamePaths.DataPath, "ModConfig", CarryCode.ConfigFile);
                if (!File.Exists(configPath))
                {
                    api.Logger.Warning("CarryOn: Config file not found at " + configPath);
                    return;
                }

                var configDir = Path.GetDirectoryName(configPath);
                if (configDir == null) return;

                configWatcher = new FileSystemWatcher(configDir, CarryCode.ConfigFile)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                configWatcher.Changed += OnConfigFileChanged;
                configWatcher.Created += OnConfigFileChanged;

                api.Logger.Notification("CarryOn: Watching " + configPath + " for config changes");
            }
            catch (Exception ex)
            {
                api.Logger?.Warning("CarryOn: Failed to set up config file watcher: " + ex.Message);
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (configWatcherLock)
            {
                var now = DateTime.UtcNow;
                if ((now - lastConfigFileChange).TotalMilliseconds < 500) return;
                lastConfigFileChange = now;
            }

            // Small delay to ensure the file write is complete
            System.Threading.Thread.Sleep(100);

            // FileSystemWatcher fires on a thread-pool thread; all game API calls
            // (LoadModConfig, BroadcastPacket, Logger, etc.) must run on the main thread.
            ServerApi?.Event.EnqueueMainThreadTask(ReloadAndBroadcastConfig, "carryon-config-reload");
        }

        private void ReloadAndBroadcastConfig()
        {
            if (ServerApi == null) return;

            try
            {
                var reloaded = ServerApi.LoadModConfig<CarryOnConfig>(CarryCode.ConfigFile);
                if (reloaded == null) return;

                reloaded.UpgradeVersion();
                ApplyConfigInPlace(reloaded);
                NotifyConfigChanged();
                ServerApi.Logger.Notification("CarryOn: Config reloaded and applied");

                // Broadcast updated config to all connected clients
                if (ServerChannel != null)
                {
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(reloaded);
                    ServerChannel.BroadcastPacket(new ConfigSyncMessage(json));
                    ServerApi.Logger.Notification("CarryOn: Config broadcast to all connected clients");
                }
            }
            catch (Exception ex)
            {
                ServerApi.Logger?.Warning("CarryOn: Failed to reload config from file: " + ex.Message);
            }
        }

        private void OnClientConfigSync(ConfigSyncMessage message)
        {
            if (message.ConfigJson == null || ClientApi == null) return;

            try
            {
                var reloaded = Newtonsoft.Json.JsonConvert.DeserializeObject<CarryOnConfig>(message.ConfigJson);
                if (reloaded != null)
                {
                    reloaded.UpgradeVersion();
                    ApplyConfigInPlace(reloaded);
                    NotifyConfigChanged();
                    ClientApi.Logger?.Notification("CarryOn: Config synced from server and applied");
                }
            }
            catch (Exception ex)
            {
                ClientApi.Logger?.Warning("CarryOn: Failed to apply synced config: " + ex.Message);
            }
        }

        private void ApplyConfigInPlace(CarryOnConfig source)
        {
            foreach (var prop in typeof(CarryOnConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.CanRead && prop.CanWrite)
                {
                    prop.SetValue(this.Config, prop.GetValue(source));
                }
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

            BlockBehaviorCarryableInteract.Init(CarryManager);
            EntityBehaviorAttachableCarryable.Init(CarryManager);
            CarryableInteractionHelpBuilder.Init(CarryManager);

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

            CarryHandler.InitClient(api, this.ClientChannel!,
                () => { if (this.HudOverlayRenderer != null) this.HudOverlayRenderer.CircleVisible = false; },
                p => { if (this.HudOverlayRenderer != null) this.HudOverlayRenderer.CircleProgress = p; },
                this.ClientModConfig!);
            ClientChannel.SetMessageHandler<ConfigSyncMessage>(OnClientConfigSync);
            CarryManager?.InitEvents(api);
            HotKeyHandler.InitClient(api, this.ClientChannel!, this.ClientModConfig!);

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
                api.Logger.Error("CarryOn: CarryManager not available — CarryOnLib failed to load. Server-side features disabled.");
                return;
            }

            EntityBehaviorDropCarriedOnDamage.Init(CarryManager, Config.DropCarriedOnDamage);
            BlockBehaviorCarryableInteract.Init(CarryManager);
            api.Register<EntityBehaviorDropCarriedOnDamage>();

            ServerApi = api;
            ServerChannel = api.Network.RegisterChannel(ModId);

            DeathHandler = new DeathHandler(api, CarryManager);
            CarryHandler.InitServer(api, this.ServerChannel!);
            CarryManager.InitEvents(api);
            HotKeyHandler.InitServer(api, this.ServerChannel!);

            TrySetupConfigWatcher(api);
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
            if (configWatcher != null)
            {
                configWatcher.EnableRaisingEvents = false;
                configWatcher.Dispose();
                configWatcher = null;
            }

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
