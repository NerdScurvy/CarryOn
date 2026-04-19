using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Client.Models;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class CachedTransformPlan
    {
        public string Signature { get; init; }
        public EffectiveTransformSetting[] EffectiveSettings { get; init; }
        public string ResolvedPrimaryGroup { get; init; }
        public bool RenderRootFirst { get; init; }
        public DateTime LastUsedAtUtc { get; set; }
    }

    internal sealed class CachedRenderInfos
    {
        public string Signature { get; init; }
        public CarriedRenderInfo[] RenderInfos { get; init; }
        public DateTime LastUsedAtUtc { get; set; }
    }

    internal sealed class SlotCacheState
    {
        public string FrameKey { get; set; }
        public string PlanSignature { get; set; }
        public string RenderInfoKey { get; set; }
    }

    internal sealed class CarryRenderCache
    {
        private const int TransformPlanCacheMaxEntries = 512;
        private static readonly TimeSpan TransformPlanCacheTtl = TimeSpan.FromMinutes(5);
        private const int RenderInfoCacheMaxEntries = 512;
        private static readonly TimeSpan RenderInfoCacheTtl = TimeSpan.FromMinutes(3);

        internal readonly Dictionary<string, CachedTransformPlan> TransformPlans = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, CachedRenderInfos> RenderInfos = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, CarriedRenderInfo[]> FrameRenderInfos = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, SlotCacheState> SlotStates = new(StringComparer.Ordinal);

        internal void InvalidateAll()
        {
            TransformPlans.Clear();
            RenderInfos.Clear();
            FrameRenderInfos.Clear();
            SlotStates.Clear();
        }

        internal void ClearFrameCache()
        {
            FrameRenderInfos.Clear();
        }

        internal void InvalidateSlotState(string slotStateKey, string currentFrameKey)
        {
            if (string.IsNullOrEmpty(slotStateKey) || string.IsNullOrEmpty(currentFrameKey))
            {
                return;
            }

            if (!SlotStates.TryGetValue(slotStateKey, out var previousState) || previousState == null)
            {
                return;
            }

            if (string.Equals(previousState.FrameKey, currentFrameKey, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(previousState.FrameKey))
            {
                FrameRenderInfos.Remove(previousState.FrameKey);
            }

            if (!string.IsNullOrEmpty(previousState.RenderInfoKey))
            {
                RenderInfos.Remove(previousState.RenderInfoKey);
            }

            if (!string.IsNullOrEmpty(previousState.PlanSignature))
            {
                TransformPlans.Remove(previousState.PlanSignature);
            }
        }

        internal void PruneTransformPlans()
        {
            if (TransformPlans.Count <= TransformPlanCacheMaxEntries)
            {
                return;
            }

            var cutoff = DateTime.UtcNow - TransformPlanCacheTtl;
            var staleKeys = TransformPlans
                .Where(kv => kv.Value.LastUsedAtUtc < cutoff)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                TransformPlans.Remove(key);
            }

            if (TransformPlans.Count <= TransformPlanCacheMaxEntries)
            {
                return;
            }

            foreach (var key in TransformPlans
                .OrderBy(kv => kv.Value.LastUsedAtUtc)
                .Take(TransformPlans.Count - TransformPlanCacheMaxEntries)
                .Select(kv => kv.Key)
                .ToList())
            {
                TransformPlans.Remove(key);
            }
        }

        internal void PruneRenderInfos()
        {
            if (RenderInfos.Count <= RenderInfoCacheMaxEntries)
            {
                return;
            }

            var cutoff = DateTime.UtcNow - RenderInfoCacheTtl;
            var staleKeys = RenderInfos
                .Where(kv => kv.Value.LastUsedAtUtc < cutoff)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                RenderInfos.Remove(key);
            }

            if (RenderInfos.Count <= RenderInfoCacheMaxEntries)
            {
                return;
            }

            foreach (var key in RenderInfos
                .OrderBy(kv => kv.Value.LastUsedAtUtc)
                .Take(RenderInfos.Count - RenderInfoCacheMaxEntries)
                .Select(kv => kv.Key)
                .ToList())
            {
                RenderInfos.Remove(key);
            }
        }
    }
}
