using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Client.Models;
using Vintagestory.API.Client;

namespace CarryOn.Client.Logic.CarryRenderer
{
    public class EntityCarryRenderer : IRenderer
    {
        private ICarryManager carryManager { get; }
        private CarryOnConfig config { get; }
        private ICoreClientAPI api { get; }
        private long renderTick;
        private readonly CarryAnimationSync animationSync = new();
        private readonly CarryFirstPersonRenderer firstPersonRenderer = new();
        private readonly CarryRenderCacheManager cacheManager;
        private readonly CarryRenderDispatcher dispatcher;
        private readonly ClientModConfig clientModConfig;
        private readonly CarryRenderInfoBuilder infoBuilder;

        public EntityCarryRenderer(ICoreClientAPI api, ICarryManager carryManager, CarryOnConfig config, ClientModConfig clientModConfig)
        {
            ArgumentNullException.ThrowIfNull(api);

            this.carryManager = carryManager;
            this.config = config;
            this.api = api;
            this.clientModConfig = clientModConfig;

            bool renderAttached = this.clientModConfig.Config?.RenderAttachedBlocks ?? true;

            var cache = new CarryRenderCache();
            var planBuilder = new CarryTransformPlanBuilder(api, carryManager, cache);
            infoBuilder = new CarryRenderInfoBuilder(api, renderAttached);
            var labelRenderer = new CarriedLabelRenderer(api);

            cacheManager = new CarryRenderCacheManager(api, carryManager, config, planBuilder, infoBuilder, cache);
            dispatcher = new CarryRenderDispatcher(api, config, cacheManager, firstPersonRenderer, labelRenderer, renderAttached);

            this.api.Event.RegisterRenderer(this, EnumRenderStage.Opaque);
            this.api.Event.RegisterRenderer(this, EnumRenderStage.AfterOIT);
            this.api.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar);
            this.api.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear);
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

            var seenEntityIds = new HashSet<long>();

            foreach (var player in this.api.World.AllPlayers)
            {
                if (player.Entity == null) continue;
                seenEntityIds.Add(player.Entity.EntityId);

                var isShadowPass = stage == EnumRenderStage.ShadowFar || stage == EnumRenderStage.ShadowNear;
                var isLocalPlayer = player == this.api.World.Player;

                if (!isLocalPlayer
                    && (isShadowPass ? !player.Entity.IsShadowRendered : !player.Entity.IsRendered))
                    continue;

                animationSync.SyncCarryAnimations(player.Entity);
                dispatcher.RenderAllCarried(player.Entity, deltaTime, stage, isShadowPass, this.renderTick);
            }

            animationSync.CleanupStaleAnimations(seenEntityIds);
            cacheManager.CleanupStaleSidecars(seenEntityIds);

            if (stage == EnumRenderStage.Opaque)
                cacheManager.TryLogCounters(this.api);

            this.renderTick++;
        }
    }
}
