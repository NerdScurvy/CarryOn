using System;
using CarryOn.Common.Network;
using CarryOn.Common.Services;
using CarryOn.Client.Logic;
using CarryOn.Client.Logic.Commands;
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
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn
{
    public partial class CarrySystem
    {
        public override void StartClientSide(ICoreClientAPI api)
        {
            if (!RequireCarryManager(api)) return;

            ClientChannel = api.Network.RegisterChannel(ModId);

            EntityBehaviorAttachableCarryable.Init(CarryManager!);
            CarryableInteractionHelpBuilder.Init(CarryManager!);
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
            HudCarried = new HudCarried(api, CarryManager!, cfg);

            try
            {
                CarryLabelManager.IconTextureMode = ClientModConfig.Config?.IconTextureMode ?? default;
            }
            catch (Exception ex)
            {
                api.Logger.Warning("CarryOn: Failed to apply client config: " + ex.Message);
            }

            EntityCarryRenderer = new EntityCarryRenderer(api, this.CarryManager!, this.ClientModConfig!);

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
            if (!RequireCarryManager(api)) return;

            EntityBehaviorDropCarriedOnDamage.Init(CarryManager!, Config.DropCarriedOnDamage);
            InitCommon(api);
            api.Register<EntityBehaviorDropCarriedOnDamage>();

            ServerChannel = api.Network.RegisterChannel(ModId);

            CarriedBlockEntityService = new CarriedBlockEntityService(api);

            DeathHandler = new DeathHandler(api, CarryManager!);
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

                carryableReportService = new CarryableReportService(api, this);
                carryableReportService.Generate();
            }
            else if (api is ICoreClientAPI capi)
            {
                TransformTemplateManager = TransformTemplateManager.InitializeFromBlocks(capi);
            }
            base.AssetsFinalize(api);
        }

        public override void Dispose()
        {
            UnwireConfigChangeHandlers();
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
