using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
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

        public EntityCarryRenderer(ICoreClientAPI api, ICarryManager carryManager, CarryOnConfig config)
        {
            ArgumentNullException.ThrowIfNull(api);

            this.carryManager = carryManager;
            this.config = config;
            this.api = api;

            var cache = new CarryRenderCache();
            var planBuilder = new CarryTransformPlanBuilder(api, carryManager, cache);
            var infoBuilder = new CarryRenderInfoBuilder(api);
            var labelRenderer = new CarriedLabelRenderer(api);

            cacheManager = new CarryRenderCacheManager(api, carryManager, config, planBuilder, infoBuilder, cache);
            dispatcher = new CarryRenderDispatcher(api, config, cacheManager, firstPersonRenderer, labelRenderer);

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
        }

        public void InvalidateRenderCaches() => cacheManager.InvalidateAll();

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
