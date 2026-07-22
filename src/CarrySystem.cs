using CarryOn.Common.Services;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Models;
using CarryOn.API.Event;
using CarryOn.Client.Logic;
using CarryOn.Client.Logic.CarryRenderer;
using CarryOn.Client.Logic.Events;
using CarryOn.Client.Logic.TransformTemplates;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Entities;
using CarryOn.Common.Handlers;
using CarryOn.Common.Handlers.CarryHandlers;
using CarryOn.Common.Handlers.PackAdjustment;
using CarryOn.Common.Logic;
using CarryOn.Events;
using CarryOn.Server.Logic;
using CarryOn.Utility;
using CarryOn.CarryOnLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using static CarryOn.Common.Models.CarryCodes;

[assembly: ModInfo("Carry On",
    modID: "carryon",
    Version = "2.0.0-pre.8",
    Description = "Adds the capability to carry various things",
    Website = "https://github.com/NerdScurvy/CarryOn",
    Authors = new[] { "copygirl", "NerdScurvy" })]
[assembly: ModDependency("game", "1.22.0")]
[assembly: ModDependency("carryonlib", "1.0.0-pre.8")]

namespace CarryOn
{
    /// <summary> Main system for the "Carry On" mod, which allows certain
    ///           blocks such as chests to be picked up and carried around. </summary>
    public partial class CarrySystem : ModSystem, IConfigProvider
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

        private CarryableReportService? carryableReportService;

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
            }

            base.StartPre(api);

            CarryOnConfig config;

            if (api is ICoreServerAPI sapi)
            {
                // Load from disk and sync to world config for client access
                config = new ModConfig().Load(sapi) ?? new CarryOnConfig();
            }
            else
            {
                var carryOnWorldConfig = api.World.Config?.GetTreeAttribute(ModId);
                if (carryOnWorldConfig == null)
                {
                    api.World.Logger.Warning("CarryOn: No world config found for CarryOn; using defaults");
                    config = new CarryOnConfig();
                }
                else
                {
                    config = CarryOnConfig.FromTreeAttribute(carryOnWorldConfig);
                }
            }

            ConfigService = new CarryOnConfigService(config);
            // Note: EntityCarriedBlock.Config is a static field that persists across mod system instances.
            // This is intentional for a singleton mod system. Updated both here and in config change handlers.
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
                var carryManager = new CarryManager(api, this, CarryEvents);
                CarryOnLib.CarryManager = carryManager;
                CarryHandler = new CarryHandler(carryManager, () => this.ClientModConfig?.Config?.CarryOnEnabled ?? true);
                HotKeyHandler = new HotKeyHandler(carryManager);

                // Register event handlers explicitly (no assembly scanning)
                carryManager.RegisterEventHandler<TooHotToPickup>();
                carryManager.RegisterEventHandler<MessageOnBlockDropped>();
                carryManager.RegisterEventHandler<MeshAngleFix>();
                carryManager.RegisterEventHandler<DroppedBlockTracker>();
                carryManager.RegisterEventHandler<CloseBlockEntityDialog>();
            }
            else
            {
                api.World.Logger.Error("CarryOn: Failed to load CarryOnLib mod system");
            }

            WireConfigChangeHandlers();

        }

        private void InitCommon(ICoreAPI api)
        {
            BlockBehaviorCarryableInteract.Init(CarryManager!);
            CarryManager!.InitEvents(api);
        }

        private bool RequireCarryManager(ICoreAPI api)
        {
            if (CarryManager != null) return true;

            var side = api.Side == EnumAppSide.Client ? "Client" : "Server";
            api.Logger.Error($"CarryOn: CarryManager not available - CarryOnLib failed to load. {side}-side features disabled.");
            return false;
        }
    }
}
