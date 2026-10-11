using System;
using Vintagestory.API.Client;

namespace CarryOn.Client.Logic.CarryRenderer
{
    /// <summary>
    /// Tracks renderer cache counters and logs their deltas on a fixed interval when
    /// debugging is enabled. Extracted from <see cref="CarryRenderCacheManager"/> so the
    /// cache manager stays focused on cache bookkeeping.
    /// </summary>
    internal sealed class DebugCounters
    {
        private static readonly TimeSpan LogInterval = TimeSpan.FromSeconds(5);

        private const int SignatureRecompute = 0;
        private const int PlanRecompute = 1;
        private const int VariantRecompute = 2;
        private const int SignatureReuse = 3;
        private const int FrameRenderInfoHit = 4;
        private const int PersistentRenderInfoHit = 5;
        private const int RenderInfoBuild = 6;

        private readonly long[] current = new long[7];
        private readonly long[] lastLogged = new long[7];
        private DateTime nextLogAtUtc = DateTime.MinValue;

        public void IncrementSignatureRecompute() => current[SignatureRecompute]++;
        public void IncrementPlanRecompute() => current[PlanRecompute]++;
        public void IncrementVariantRecompute() => current[VariantRecompute]++;
        public void IncrementSignatureReuse() => current[SignatureReuse]++;
        public void IncrementFrameRenderInfoHit() => current[FrameRenderInfoHit]++;
        public void IncrementPersistentRenderInfoHit() => current[PersistentRenderInfoHit]++;
        public void IncrementRenderInfoBuild() => current[RenderInfoBuild]++;

        /// <summary>
        /// Logs the delta since the previous call on the fixed interval, when
        /// <paramref name="loggingEnabled"/> is true.
        /// </summary>
        public void LogIfDue(ICoreClientAPI api, bool loggingEnabled)
        {
            if (!loggingEnabled) return;

            var now = DateTime.UtcNow;
            if (now < nextLogAtUtc) return;

            var deltas = new long[current.Length];
            for (int i = 0; i < current.Length; i++)
            {
                deltas[i] = current[i] - lastLogged[i];
                lastLogged[i] = current[i];
            }
            nextLogAtUtc = now + LogInterval;

            var totalRequests = deltas[SignatureRecompute] + deltas[SignatureReuse];
            var reuseRate = totalRequests > 0
                ? (100.0 * deltas[SignatureReuse] / totalRequests).ToString("F1")
                : "n/a";

            api.Logger.Debug(
                "[CarryOn] Renderer sidecar counters (last {0}s): recomputed={1}, planRecomputed={2}, variantRecomputed={3}, reused={4}, reuseRate={5}%, frameHits={6}, persistentHits={7}, builds={8}",
                (int)LogInterval.TotalSeconds,
                deltas[SignatureRecompute],
                deltas[PlanRecompute],
                deltas[VariantRecompute],
                deltas[SignatureReuse],
                reuseRate,
                deltas[FrameRenderInfoHit],
                deltas[PersistentRenderInfoHit],
                deltas[RenderInfoBuild]);
        }
    }
}
