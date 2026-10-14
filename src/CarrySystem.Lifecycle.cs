using System;
using CarryOn.Common.Network;
using CarryOn.Common.Services;
using CarryOn.Client.Logic;
using CarryOn.Client.Logic.Commands;
using CarryOn.Client.Logic.CarryRenderer;
using CarryOn.Client.Logic.TransformGroupResolvers;
using CarryOn.Client.Logic.TransformGroupResolvers.HiddenBackpack;
using CarryOn.Client.Logic.TransformTemplates;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Handlers;
using CarryOn.Client.Handlers.CarryHandlers;
using CarryOn.Client.Handlers.PackAdjustment;
using CarryOn.Common.Logic;
using CarryOn.Server.Behaviors;
using CarryOn.Server.Logic;
using CarryOn.Server.Logic.BehavioralConditions;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn
{
    public partial class CarrySystem
    {
        public override void StartClientSide(ICoreClientAPI api)
        {
            if (!RequireCarryManager(api)) return;
            var carryManager = CarryManager!;

            ClientChannel = api.Network.RegisterChannel(ModId);

            EntityBehaviorCarryable.Init(carryManager);
            EntityBehaviorAttachableCarryable.Init(carryManager);
            BlockBehaviorCarryable.Init(carryManager);
            BlockBehaviorGroundStorageBagHelp.Init(carryManager);
            CarryableInteractionHelpBuilder.Init(carryManager);
            InitCommon(api);

            HudOverlayRenderer = new HudOverlayRenderer(api);

            ClientModConfig = new ClientModConfig();
            ClientModConfig.Load(api);

            var cfg = ClientModConfig.Config;
            if (cfg == null)
            {
                api.Logger.Error("CarryOn: Client config failed to load, using defaults");
                cfg = new CarryOnClientConfig();
            }
            HudCarried = new HudCarried(api, carryManager, cfg);

            try
            {
                var iconTextureMode = ClientModConfig.Config?.IconTextureMode ?? default;
                if (ClientModConfig.Config == null)
                    api.Logger.Debug("CarryOn: ClientModConfig.Config is null, using default IconTextureMode");
                CarryLabelManager.IconTextureMode = iconTextureMode;
            }
            catch (Exception ex)
            {
                api.Logger.Warning("CarryOn: Failed to apply client config: " + ex.Message);
            }

            EntityCarryRenderer = new EntityCarryRenderer(api, carryManager, this, this.ClientModConfig!);

            CarryClientHandlers = new CarryClientHandlers(carryManager, this, () => this.ClientModConfig?.Config?.CarryOnEnabled ?? true);
            CarryClientHandlers.Init(api, this.ClientChannel!,
                () => { if (this.HudOverlayRenderer != null) this.HudOverlayRenderer.CircleVisible = false; },
                p => { if (this.HudOverlayRenderer != null) this.HudOverlayRenderer.CircleProgress = p; },
                () => this.HudCarried?.TriggerHandsHighlight(),
                () => this.HudCarried?.TriggerBackHighlight(),
                this.ClientModConfig!);
            ClientChannel.SetMessageHandler<ConfigSyncMessage>(OnClientConfigSync);
            ClientChannel.SetMessageHandler<CarryabilityStateMessage>(OnCarryabilityStateSync);
            HotKeyHandler!.InitClient(api, this.ClientChannel!, this.ClientModConfig!);

            carryManager.RegisterRootTransformGroupResolver(ModId, new GenericCodePathTransformGroupResolver());
            carryManager.RegisterAttachmentTransformGroupResolver(ModId, new DataAttributeTransformGroupResolver());
            carryManager.RegisterAttachmentTransformGroupResolver(ModId, new ContainerSlotTransformGroupResolverBase());
            carryManager.RegisterAttachmentTransformGroupResolver(ModId, new DisplayCaseTransformGroupResolver());
            carryManager.RegisterAttachmentTransformGroupResolver(ModId, new MoldRackTransformGroupResolver());
            var plantContainerResolver = new PlantContainerTransformGroupResolver();
            carryManager.RegisterRootTransformGroupResolver(ModId, plantContainerResolver);
            carryManager.RegisterAttachmentTransformGroupResolver(ModId, plantContainerResolver);
            carryManager.RegisterEntityTransformGroupResolver(ModId, new HiddenBackpackEntityResolver(api));

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
            if (!RequireCarryManager(api)) return;
            var carryManager = CarryManager!;

            EntityBehaviorDropCarriedOnDamage.Init(carryManager);
            EntityBehaviorCarryable.Init(carryManager);
            EntityBehaviorAttachableCarryable.Init(carryManager);
            BlockBehaviorCarryable.Init(carryManager);
            BlockBehaviorGroundStorageBagHelp.Init(carryManager);
            InitCommon(api);
            api.Register<EntityBehaviorDropCarriedOnDamage>();

            ServerChannel = api.Network.RegisterChannel(ModId);

            CarriedBlockEntityService = new CarriedBlockEntityService(api);

            DeathHandler = new DeathHandler(api, carryManager);
            CarryHandler!.InitServer(api, this.ServerChannel!);
            HotKeyHandler!.InitServer(api, this.ServerChannel!);

            if (CarryOnLib?.CarryManager is CarryManager libCarryManager)
            {
                libCarryManager.ConfigureServerChannel(this.ServerChannel!);
            }

            ConfigService.SetupFileWatcher(api);

            if (reconditionOnConfigChange)
            {
                // Mode B: push the current carryability snapshot to each joining player so
                // the client reconciles onto server reality (the engine's join payload is a
                // boot-era block/behavior transfer and stays stale after live reloads).
                onServerPlayerJoinSendCarryability = OnServerPlayerJoin;
                api.Event.PlayerJoin += onServerPlayerJoinSendCarryability;
            }

            var serverCommands = new ServerCommands(api, ConfigService);
            serverCommands.Register();
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            if (!RequireCarryManager(api)) return;

            if (api.Side == EnumAppSide.Server)
            {
                // Fresh world: drop any baseline left by a previous world in this
                // process (world reloads, test hosts) so the first Init below
                // re-captures from this world's freshly loaded behaviors.
                ConditioningBaseline.ResetBaseline();

                // Mode A holds no baseline: it runs conditioning exactly once, so there is
                // no second run to converge from. Seeded from the same latch the reload
                // gate reads, at the same boot site, so the two cannot disagree - which is
                // what makes it safe to ship both (pin W2).
                ConditioningBaseline.SetCaptureEnabled(reconditionOnConfigChange);

                var behavioralConditioning = new BehavioralConditioning();
                behavioralConditioning.Init(api, Config);

                // Baseline for Mode B's dirty gate and for the Mode A pending-restart
                // warning, plus - under Mode B only - the boot snapshot cache every
                // joining player receives until a reload refreshes it. The signature
                // seeds in both modes: the Mode A reload warning reads it to tell an
                // edit that applies on reload from one that waits for the next server
                // start, so under Mode A it is live state, not write-only. The snapshot
                // stays latch-gated because only Mode B broadcasts join snapshots.
                // Mode A runs conditioning exactly once per world and holds no
                // convergence baseline, because there is no second run to converge from.
                ConfigService.AppliedBehaviorSignature = Config.ComputeBehaviorSurfaceSignature();
                if (reconditionOnConfigChange)
                {
                    carryabilitySnapshot = BehavioralConditioning.BuildStateMessage(api);
                }

                carryableReportService = new CarryableReportService(api, this);
                carryableReportService.Generate();
            }
            else if (api is ICoreClientAPI capi)
            {
                if (Config?.CarryOptions?.PickupGroundStorageBagWhileCarrying != false)
                {
                    // Mirror the server-side block behavioral injection so the interaction-help
                    // HUD also sees the ground-storage bag help behavior on dedicated servers.
                    // Idempotent; harmless when the integrated server already injected it.
                    BlockBehaviorGroundStorageBagHelp.InjectInto(capi);
                }

                var templateManager = TransformTemplateManager.InitializeFromBlocks(capi);
                TransformTemplateManager = templateManager;
                templateManager.DiscoverItemPoseTemplates(capi);
                templateManager.RegisterRenderPlaceholderSettings(capi);
                EntityCarryRenderer!.TemplateManager = TransformTemplateManager;
                if (PackAdjustmentHandler != null)
                    PackAdjustmentHandler.TemplateManager = TransformTemplateManager;
            }
        }

        public override void Dispose()
        {
            UnwireConfigChangeHandlers();
            ConfigService.Dispose();

            if (ServerApi != null && onServerPlayerJoinSendCarryability != null)
            {
                ServerApi.Event.PlayerJoin -= onServerPlayerJoinSendCarryability;
                onServerPlayerJoinSendCarryability = null;
            }

            CarryPatcher.Remove();

            if (ClientApi != null)
            {
                EntityCarryRenderer?.Dispose();
                HudOverlayRenderer?.Dispose();
                HudCarried?.Dispose();
                CarryClientHandlers?.Dispose();
            }

            CarryHandler?.Dispose();
            DeathHandler?.Dispose();

            // Clear the manager we published to the shared API mod system after the consumers
            // are gone, so a later world in the same process cannot read a disposed manager.
            if (CarryOnLib != null)
            {
                CarryOnLib.CarryManager = null;
            }

            base.Dispose();
        }
    }
}
