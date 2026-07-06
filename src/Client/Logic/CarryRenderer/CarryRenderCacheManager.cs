using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class CarryRenderCacheManager
    {
        private readonly ICoreClientAPI api;
        private readonly ICarryManager carryManager;
        private CarryOnConfig config;
        private readonly CarryTransformPlanBuilder planBuilder;
        private readonly CarryRenderInfoBuilder infoBuilder;
        private readonly CarryRenderCache cache;

        public CarryRenderCacheManager(ICoreClientAPI api, ICarryManager carryManager, CarryOnConfig config, CarryTransformPlanBuilder planBuilder, CarryRenderInfoBuilder infoBuilder, CarryRenderCache cache)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.carryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.planBuilder = planBuilder ?? throw new ArgumentNullException(nameof(planBuilder));
            this.infoBuilder = infoBuilder ?? throw new ArgumentNullException(nameof(infoBuilder));
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public void UpdateConfig(CarryOnConfig newConfig)
        {
            this.config = newConfig;
        }

        private readonly Dictionary<(long EntityId, CarrySlot Slot), SignatureSidecarState> signatureSidecars = new();

        private sealed class SignatureSidecarState
        {
            public int LastSeenCarriedRevision { get; set; } = -1;
            public string? LastTransformsGroup { get; set; }
            public string? LastStackCode { get; set; }
            public ITreeAttribute? LastBlockEntityDataRef { get; set; }
            public CachedTransformPlan? Plan { get; set; }
            public string? RenderVariantSignature { get; set; }
        }

        private long signatureRecomputeCount;
        private long planRecomputeCount;
        private long variantRecomputeCount;
        private long signatureReuseCount;
        private long frameRenderInfoHitCount;
        private long persistentRenderInfoHitCount;
        private long renderInfoBuildCount;

        private long lastLoggedSignatureRecomputeCount;
        private long lastLoggedPlanRecomputeCount;
        private long lastLoggedVariantRecomputeCount;
        private long lastLoggedSignatureReuseCount;
        private long lastLoggedFrameRenderInfoHitCount;
        private long lastLoggedPersistentRenderInfoHitCount;
        private long lastLoggedRenderInfoBuildCount;
        private DateTime nextDebugCounterLogAtUtc = DateTime.MinValue;
        private static readonly TimeSpan DebugCounterLogInterval = TimeSpan.FromSeconds(5);
        private DateTime nextPruneAtUtc = DateTime.MinValue;
        private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(10);

        public CarriedRenderInfo[] GetRenderInfoCached(EntityAgent entity, CarriedBlock carried, string transformsGroup)
        {
            if (carried == null) return Array.Empty<CarriedRenderInfo>();
            var carriedBlock = carried;

            var slotStateKey = CarryRenderHelpers.BuildSlotStateKey(entity, carriedBlock.Slot);
            var sidecarKey = (entity.EntityId, carriedBlock.Slot);
            if (!signatureSidecars.TryGetValue(sidecarKey, out var sidecar))
            {
                sidecar = new SignatureSidecarState();
                signatureSidecars[sidecarKey] = sidecar;
            }

            var carriedRevision = carryManager?.GetCarriedRevision(entity) ?? 0;
            var stackCode = carriedBlock.ItemStack?.Collectible?.Code?.ToString() ?? "none";

            var signaturesDirty = sidecar.Plan == null
                || sidecar.LastSeenCarriedRevision != carriedRevision
                || !string.Equals(sidecar.LastTransformsGroup, transformsGroup, StringComparison.Ordinal)
                || !string.Equals(sidecar.LastStackCode, stackCode, StringComparison.Ordinal)
                || !ReferenceEquals(sidecar.LastBlockEntityDataRef, carriedBlock.BlockEntityData)
                || string.IsNullOrEmpty(sidecar.RenderVariantSignature);

            TreeAttribute? containerSlots = null;
            CachedTransformPlan? plan;
            string? renderVariantSignature;

            if (signaturesDirty)
            {
                signatureRecomputeCount++;
                planRecomputeCount++;
                plan = planBuilder.GetOrBuild(carried, transformsGroup);

                containerSlots = BlockUtils.GetContainerSlots(carriedBlock);
                variantRecomputeCount++;
                renderVariantSignature = CarryRenderHelpers.BuildRenderInfoVariantSignature(
                    carriedBlock,
                    containerSlots,
                    plan.EffectiveSettings,
                    api.World,
                    carriedBlock.GetCarryableBehavior()?.RootRenderVariant);

                sidecar.LastSeenCarriedRevision = carriedRevision;
                sidecar.LastTransformsGroup = transformsGroup;
                sidecar.LastStackCode = stackCode;
                sidecar.LastBlockEntityDataRef = carriedBlock.BlockEntityData;
                sidecar.Plan = plan;
                sidecar.RenderVariantSignature = renderVariantSignature;
            }
            else
            {
                signatureReuseCount++;
                plan = sidecar.Plan;
                renderVariantSignature = sidecar.RenderVariantSignature;
            }

            var frameKey = CarryRenderHelpers.BuildFrameCacheKey(entity, carriedBlock, plan?.Signature, renderVariantSignature);
            cache.InvalidateSlotState(slotStateKey, frameKey);

            if (cache.FrameRenderInfos.TryGetValue(frameKey, out var frameCached))
            {
                frameRenderInfoHitCount++;
                return CarryRenderHelpers.CloneCarriedRenderInfos(frameCached) ?? Array.Empty<CarriedRenderInfo>();
            }

            var now = DateTime.UtcNow;

            var renderInfoKey = string.Concat(plan?.Signature, "|ri|", renderVariantSignature);
            cache.SlotStates[slotStateKey] = new SlotCacheState
            {
                FrameKey = frameKey,
                PlanSignature = plan?.Signature,
                RenderInfoKey = renderInfoKey
            };

            if (cache.RenderInfos.TryGetValue(renderInfoKey, out var cachedRenderInfos))
            {
                if (now - cachedRenderInfos.CreatedAtUtc > CarryRenderCache.RenderInfoRebuildTtl)
                {
                    cache.RenderInfos.Remove(renderInfoKey);
                }
                else
                {
                    persistentRenderInfoHitCount++;
                    cachedRenderInfos.LastUsedAtUtc = now;
                    var clonedFromPersistent = CarryRenderHelpers.CloneCarriedRenderInfos(cachedRenderInfos.RenderInfos);
                    cache.FrameRenderInfos[frameKey] = CarryRenderHelpers.CloneCarriedRenderInfos(clonedFromPersistent);
                    return clonedFromPersistent;
                }
            }

            containerSlots ??= BlockUtils.GetContainerSlots(carriedBlock);
            renderInfoBuildCount++;
            var built = infoBuilder.BuildFromPlan(carriedBlock, plan, containerSlots);
            cache.RenderInfos[renderInfoKey] = new CachedRenderInfos
            {
                Signature = renderInfoKey,
                RenderInfos = built,
                LastUsedAtUtc = now,
                CreatedAtUtc = now
            };

            cache.FrameRenderInfos[frameKey] = built;
            return built;
        }

        public void InvalidateAll()
        {
            cache.InvalidateAll();
            signatureSidecars.Clear();
        }

        public void ClearFrameCache()
        {
            cache.ClearFrameCache();
        }

        public void PruneCaches()
        {
            var now = DateTime.UtcNow;
            if (now < nextPruneAtUtc) return;
            nextPruneAtUtc = now + PruneInterval;

            cache.PruneTransformPlans();
            cache.PruneRenderInfos();
        }

        public void CleanupStaleSidecars(HashSet<long> seenEntityIds)
        {
            foreach (var sidecarKey in signatureSidecars.Keys.Where(key => !seenEntityIds.Contains(key.EntityId)).ToList())
            {
                signatureSidecars.Remove(sidecarKey);
            }
        }

        public void TryLogCounters(ICoreClientAPI api)
        {
            var loggingEnabled = config?.DebuggingOptions?.LoggingEnabled ?? false;
            if (!loggingEnabled) return;

            var now = DateTime.UtcNow;
            if (now < nextDebugCounterLogAtUtc) return;

            var deltaRecomputed = signatureRecomputeCount - lastLoggedSignatureRecomputeCount;
            var deltaPlanRecomputed = planRecomputeCount - lastLoggedPlanRecomputeCount;
            var deltaVariantRecomputed = variantRecomputeCount - lastLoggedVariantRecomputeCount;
            var deltaReused = signatureReuseCount - lastLoggedSignatureReuseCount;
            var deltaFrameHits = frameRenderInfoHitCount - lastLoggedFrameRenderInfoHitCount;
            var deltaPersistentHits = persistentRenderInfoHitCount - lastLoggedPersistentRenderInfoHitCount;
            var deltaBuilds = renderInfoBuildCount - lastLoggedRenderInfoBuildCount;

            lastLoggedSignatureRecomputeCount = signatureRecomputeCount;
            lastLoggedPlanRecomputeCount = planRecomputeCount;
            lastLoggedVariantRecomputeCount = variantRecomputeCount;
            lastLoggedSignatureReuseCount = signatureReuseCount;
            lastLoggedFrameRenderInfoHitCount = frameRenderInfoHitCount;
            lastLoggedPersistentRenderInfoHitCount = persistentRenderInfoHitCount;
            lastLoggedRenderInfoBuildCount = renderInfoBuildCount;
            nextDebugCounterLogAtUtc = now + DebugCounterLogInterval;

            var totalDeltaRequests = deltaRecomputed + deltaReused;
            var reuseRate = totalDeltaRequests > 0
                ? (100.0 * deltaReused / totalDeltaRequests).ToString("F1")
                : "n/a";

            api.Logger.Debug(
                "[CarryOn] Renderer sidecar counters (last {0}s): recomputed={1}, planRecomputed={2}, variantRecomputed={3}, reused={4}, reuseRate={5}%, frameHits={6}, persistentHits={7}, builds={8}",
                (int)DebugCounterLogInterval.TotalSeconds,
                deltaRecomputed,
                deltaPlanRecomputed,
                deltaVariantRecomputed,
                deltaReused,
                reuseRate,
                deltaFrameHits,
                deltaPersistentHits,
                deltaBuilds);
        }
    }
}
