using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Interfaces;
using CarryOn.Client.Logic.TransformTemplates;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.CarryRenderer
{
    public class EntityCarryRenderer : IRenderer
    {
        private readonly ICarryManager carryManager;
        private readonly ICoreClientAPI api;
        private readonly IConfigProvider configProvider;
        private long renderTick;
        private readonly CarryAnimationSync animationSync;
        private readonly CarryFirstPersonTransform firstPersonRenderer = new();
        private readonly CarryRenderCacheManager cacheManager;
        private readonly CarryRenderDispatcher dispatcher;
        private readonly ClientModConfig clientModConfig;
        private readonly CarryRenderInfoBuilder infoBuilder;
        private readonly CarryTransformPlanBuilder planBuilder;
        private readonly HashSet<long> seenEntityIds = new();

        public EntityCarryRenderer(ICoreClientAPI api, ICarryManager carryManager, IConfigProvider configProvider, ClientModConfig clientModConfig)
        {
            ArgumentNullException.ThrowIfNull(api);

            this.carryManager = carryManager;
            this.api = api;
            this.configProvider = configProvider;
            this.clientModConfig = clientModConfig;

            animationSync = new CarryAnimationSync(carryManager);

            bool renderAttached = this.clientModConfig.Config?.RenderAttachedBlocks ?? true;

            var cache = new CarryRenderCache();
            planBuilder = new CarryTransformPlanBuilder(api, carryManager, cache);
            infoBuilder = new CarryRenderInfoBuilder(api, renderAttached);
            var labelRenderer = new CarryLabelRenderer(api);

            cacheManager = new CarryRenderCacheManager(api, carryManager, configProvider, planBuilder, infoBuilder, cache);
            dispatcher = new CarryRenderDispatcher(api, carryManager, configProvider, cacheManager, firstPersonRenderer, labelRenderer, renderAttached);

            this.api.Event.RegisterRenderer(this, EnumRenderStage.Opaque);
            this.api.Event.RegisterRenderer(this, EnumRenderStage.AfterOIT);
            this.api.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar);
            this.api.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear);
        }

        internal TransformTemplateManager? TemplateManager
        {
            set => planBuilder.TemplateManager = value;
        }

        public void Dispose()
        {
            this.api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            this.api.Event.UnregisterRenderer(this, EnumRenderStage.AfterOIT);
            this.api.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
            this.api.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
            cacheManager.InvalidateAll();
            dispatcher.ClearMatrixPool();
            infoBuilder.Dispose();
        }

        public void InvalidateRenderCaches() => cacheManager.InvalidateAll();

        public void SetRenderAttachedBlocks(bool enabled)
        {
            infoBuilder.RenderAttachedBlocks = enabled;
            dispatcher.RenderAttachedBlocks = enabled;
        }

        public double RenderOrder => 1.0;
        public int RenderRange => 99;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            dispatcher.ClearMatrixPool();

            if (stage != EnumRenderStage.Opaque
                && stage != EnumRenderStage.AfterOIT
                && stage != EnumRenderStage.ShadowFar
                && stage != EnumRenderStage.ShadowNear)
                return;

            if (stage == EnumRenderStage.Opaque)
            {
                cacheManager.ClearFrameCache();
                cacheManager.PruneCaches();
            }

            this.seenEntityIds.Clear();

            foreach (var player in this.api.World.AllPlayers)
            {
                if (player.Entity == null) continue;

                // Spectators are invisible observers - don't render their carried blocks
                if (player.WorldData?.CurrentGameMode == EnumGameMode.Spectator) continue;

                this.seenEntityIds.Add(player.Entity.EntityId);

                var isShadowPass = stage == EnumRenderStage.ShadowFar || stage == EnumRenderStage.ShadowNear;
                var isLocalPlayer = player == this.api.World.Player;

                if (!isLocalPlayer
                    && (isShadowPass ? !player.Entity.IsShadowRendered : !player.Entity.IsRendered))
                    continue;

                animationSync.SyncCarryAnimations(player.Entity);
                dispatcher.RenderAllCarried(player.Entity, deltaTime, stage, isShadowPass, this.renderTick);
            }

            animationSync.CleanupStaleAnimations(this.seenEntityIds);
            cacheManager.CleanupStaleSidecars(this.seenEntityIds);

            if (stage == EnumRenderStage.Opaque)
                cacheManager.TryLogCounters(this.api);

            this.renderTick++;
        }
    }
}
