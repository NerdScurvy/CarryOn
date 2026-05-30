using System;
using System.Linq;
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
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using static CarryOn.API.Common.Models.CarryCode;

[assembly: ModInfo("Carry On",
    modID: "carryon",
    Version = "2.0.0-pre.2",
    Description = "Adds the capability to carry various things",
    Website = "https://github.com/NerdScurvy/CarryOn",
    Authors = new[] { "copygirl", "NerdScurvy" })]
[assembly: ModDependency("game", "1.22.0")]
[assembly: ModDependency("carryonlib", "1.0.0-pre.2")]

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
        public HudCarried HudCarried { get; private set; }
        public ClientModConfig ClientConfig { get; private set; }

        public TransformTemplateManager TransformTemplateManager { get; private set; }

        public PackAdjustmentHandler PackAdjustmentHandler { get; private set; }

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

            EntityCarryRenderer = new EntityCarryRenderer(api, this);
            HudOverlayRenderer = new HudOverlayRenderer(api);
            HudCarried = new HudCarried(api);

            // Load client-side configuration (HUD anchor placements etc.) and apply anchors
            try
            {
                ClientConfig = new ClientModConfig();
                ClientConfig.Load(api);

                var cfg = ClientConfig.Config;
                if (cfg != null)
                {
                    // Parse and apply HandsAnchor
                    if (!string.IsNullOrEmpty(cfg.HandsAnchor) && Enum.TryParse<HudCarried.Anchor>(cfg.HandsAnchor, true, out var handsAnchor))
                    {
                        HudCarried.HandsAnchor = handsAnchor;
                    }

                    // Parse and apply BackAnchor
                    if (!string.IsNullOrEmpty(cfg.BackAnchor) && Enum.TryParse<HudCarried.Anchor>(cfg.BackAnchor, true, out var backAnchor))
                    {
                        HudCarried.BackAnchor = backAnchor;
                    }


                    // Apply client anchor background preferences (persisted client-side)
                    try
                    {
                        HudCarried.AnchorBackgroundEnabled = cfg.AnchorBackgroundEnabled;
                        if (!string.IsNullOrEmpty(cfg.AnchorBackgroundColor))
                        {
                            HudCarried.AnchorBackgroundColor = cfg.AnchorBackgroundColor;
                        }
                        HudCarried.AnchorBackgroundAlpha = cfg.AnchorBackgroundAlpha;

                        HudCarried.AnchorBorderEnabled = cfg.AnchorBorderEnabled;
                        if (!string.IsNullOrEmpty(cfg.AnchorBorderColor))
                        {
                            HudCarried.AnchorBorderColor = cfg.AnchorBorderColor;
                        }
                        HudCarried.AnchorBorderAlpha = cfg.AnchorBorderAlpha;

                        HudCarried.IconHighlightEnabled = cfg.IconHighlightEnabled;
                        if (!string.IsNullOrEmpty(cfg.IconHighlightColor))
                        {
                            HudCarried.IconHighlightColor = cfg.IconHighlightColor;
                        }
                        HudCarried.IconHighlightAlpha = cfg.IconHighlightAlpha;
                    }
                    catch (Exception ex)
                    {
                        api.Logger.Warning("CarryOn: Failed to apply anchor background settings: " + ex.Message);
                    }                    
                }
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
                // Behavioral conditioning and reassignment
                var BehavioralConditioning = new BehavioralConditioning();
                BehavioralConditioning.Init(api, Config);
            } else{
                // Process transform templates for carryable blocks on the client side
                var capi = api as ICoreClientAPI;

                // Find all carryableBehaviors with transformTemplates defined 
                var carryableWithTemplates = api.World.Blocks
                    .Where(b => b.GetBehavior<BlockBehaviorCarryable>()?.TransformTemplates != null)
                    .ToList();

                // Build unique set of all template codes used by carryable blocks
                var transformTemplateCodes = carryableWithTemplates
                    .SelectMany(b => b.GetBehavior<BlockBehaviorCarryable>().TransformTemplates)
                    .ToHashSet();

                TransformTemplateManager = new TransformTemplateManager(capi);
                TransformTemplateManager.LoadTemplates([.. transformTemplateCodes]);

                // Get list of carryable blocks that have transform templates and or local transform groups, and resolve their transform groups
                var carryablesToResolve = api.World.Blocks
                    .Where(b => b.GetBehavior<BlockBehaviorCarryable>() != null && 
                                (b.GetBehavior<BlockBehaviorCarryable>().TransformTemplates != null || b.GetBehavior<BlockBehaviorCarryable>().HasLocalTransformGroups))
                    .ToList();

                // Resolve transform groups for carryable blocks
                foreach (var block in carryablesToResolve)
                {
                    block.GetBehavior<BlockBehaviorCarryable>().ResolveTransformGroups(TransformTemplateManager);
                }
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
                HudCarried?.Dispose();

                CarryHandler?.Dispose();
            }
            base.Dispose();
        }
    }
}
