using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Interfaces;
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
        private readonly IConfigProvider configProvider;
        private readonly CarryTransformPlanBuilder planBuilder;
        private readonly CarryRenderInfoBuilder infoBuilder;
        private readonly CarryRenderCache cache;

        public CarryRenderCacheManager(ICoreClientAPI api, ICarryManager carryManager, IConfigProvider configProvider, CarryTransformPlanBuilder planBuilder, CarryRenderInfoBuilder infoBuilder, CarryRenderCache cache)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.carryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
            this.configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            this.planBuilder = planBuilder ?? throw new ArgumentNullException(nameof(planBuilder));
            this.infoBuilder = infoBuilder ?? throw new ArgumentNullException(nameof(infoBuilder));
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public CarryOnConfig Config => configProvider.Config;

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

        private readonly DebugCounters debugCounters = new();
        private DateTime nextPruneAtUtc = DateTime.MinValue;
        private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(10);

        public CarriedRenderInfo[] GetRenderInfoCached(EntityAgent entity, CarriedBlock carried, string transformsGroup)
        {
            if (carried == null) return Array.Empty<CarriedRenderInfo>();

            var slotStateKey = CarryRenderHelpers.BuildSlotStateKey(entity, carried.Slot);
            var sidecarKey = (entity.EntityId, carried.Slot);
            if (!signatureSidecars.TryGetValue(sidecarKey, out var sidecar))
            {
                sidecar = new SignatureSidecarState();
                signatureSidecars[sidecarKey] = sidecar;
            }

            var carriedRevision = carryManager?.GetCarriedRevision(entity) ?? 0;
            var stackCode = carried.ItemStack?.Collectible?.Code?.ToString() ?? "none";

            var signaturesDirty = sidecar.Plan == null
                || sidecar.LastSeenCarriedRevision != carriedRevision
                || !string.Equals(sidecar.LastTransformsGroup, transformsGroup, StringComparison.Ordinal)
                || !string.Equals(sidecar.LastStackCode, stackCode, StringComparison.Ordinal)
                || !ReferenceEquals(sidecar.LastBlockEntityDataRef, carried.BlockEntityData)
                || string.IsNullOrEmpty(sidecar.RenderVariantSignature);

            TreeAttribute? containerSlots = null;
            CachedTransformPlan? plan;
            string? renderVariantSignature;

            if (signaturesDirty)
            {
                debugCounters.IncrementSignatureRecompute();
                debugCounters.IncrementPlanRecompute();
                plan = planBuilder.GetOrBuild(carried, transformsGroup);

                containerSlots = BlockUtils.GetContainerSlots(carried);
                debugCounters.IncrementVariantRecompute();
                renderVariantSignature = CarrySignatureBuilder.BuildRenderInfoVariantSignature(
                    carried,
                    containerSlots,
                    plan.EffectiveSettings,
                    api.World,
                    carried.GetCarryableBehavior()?.RootRenderVariant);

                sidecar.LastSeenCarriedRevision = carriedRevision;
                sidecar.LastTransformsGroup = transformsGroup;
                sidecar.LastStackCode = stackCode;
                sidecar.LastBlockEntityDataRef = carried.BlockEntityData;
                sidecar.Plan = plan;
                sidecar.RenderVariantSignature = renderVariantSignature;
            }
            else
            {
                debugCounters.IncrementSignatureReuse();
                plan = sidecar.Plan;
                renderVariantSignature = sidecar.RenderVariantSignature;
            }

            var frameKey = CarryRenderHelpers.BuildFrameCacheKey(entity, carried, plan?.Signature, renderVariantSignature);
            cache.InvalidateSlotState(slotStateKey, frameKey);

            if (cache.FrameRenderInfos.TryGetValue(frameKey, out var frameCached))
            {
                debugCounters.IncrementFrameRenderInfoHit();
                return CarryRenderHelpers.CloneCarriedRenderInfos(frameCached) ?? Array.Empty<CarriedRenderInfo>();
            }

            var now = DateTime.UtcNow;

            var renderInfoKey = string.Concat(plan?.Signature, "|ri|", renderVariantSignature);
            cache.SlotStates[slotStateKey] = new SlotCacheState
            {
                FrameKey = frameKey,
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
                    debugCounters.IncrementPersistentRenderInfoHit();
                    cachedRenderInfos.LastUsedAtUtc = now;
                    var clonedFromPersistent = CarryRenderHelpers.CloneCarriedRenderInfos(cachedRenderInfos.RenderInfos);
                    cache.FrameRenderInfos[frameKey] = clonedFromPersistent;
                    return clonedFromPersistent;
                }
            }

            containerSlots ??= BlockUtils.GetContainerSlots(carried);
            debugCounters.IncrementRenderInfoBuild();
            var built = infoBuilder.BuildFromPlan(carried, plan, containerSlots);
            cache.RenderInfos[renderInfoKey] = new CachedRenderInfos
            {
                Signature = renderInfoKey,
                RenderInfos = built,
                LastUsedAtUtc = now,
                CreatedAtUtc = now
            };

            var cloned = CarryRenderHelpers.CloneCarriedRenderInfos(built);
            cache.FrameRenderInfos[frameKey] = cloned;
            return cloned;
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
            debugCounters.LogIfDue(api, Config?.DebuggingOptions?.LoggingEnabled ?? false);
        }
    }
}
