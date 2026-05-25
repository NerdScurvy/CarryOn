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

        private void PruneCache<TKey, TValue>(
            Dictionary<TKey, TValue> dict, int maxEntries, TimeSpan ttl, Func<TValue, DateTime> lastUsedSelector)
        {
            if (dict.Count <= maxEntries) return;
            var cutoff = DateTime.UtcNow - ttl;
            var staleKeys = dict.Where(kv => lastUsedSelector(kv.Value) < cutoff).Select(kv => kv.Key).ToList();
            foreach (var key in staleKeys) dict.Remove(key);
            if (dict.Count <= maxEntries) return;
            foreach (var key in dict.OrderBy(kv => lastUsedSelector(kv.Value))
                                    .Take(dict.Count - maxEntries)
                                    .Select(kv => kv.Key).ToList())
            {
                dict.Remove(key);
            }
        }
        // Usage:
        internal void PruneTransformPlans() =>
            PruneCache(TransformPlans, TransformPlanCacheMaxEntries, TransformPlanCacheTtl, v => v.LastUsedAtUtc);
        internal void PruneRenderInfos() =>
            PruneCache(RenderInfos, RenderInfoCacheMaxEntries, RenderInfoCacheTtl, v => v.LastUsedAtUtc);

    }
}
