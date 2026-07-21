using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Server.Logic.BehavioralConditions
{
    internal class MultipleBehaviorResolver
    {
        private readonly CarryOnConfig config;

        internal MultipleBehaviorResolver(CarryOnConfig config)
        {
            this.config = config;
        }

        /// <summary>
        /// Resolves multiple carryable behaviors for blocks.
        /// </summary>
        internal void Resolve(ICoreAPI api)
        {
            var filters = config.CarryablesFilters;
            if (filters == null)
            {
                api.Logger.Error("CarryOn: CarryablesFilters is null in ResolveMultipleCarryableBehaviors");
                return;
            }

            var removeBaseFilters = filters.RemoveBaseCarryableBehaviour;
            if (removeBaseFilters == null)
            {
                api.Logger.Error("CarryOn: RemoveBaseCarryableBehaviour is null in ResolveMultipleCarryableBehaviors");
                return;
            }
            
            foreach (var block in api.World.Blocks)
            {
                bool removeBaseBehavior = false;
                if (block.Code == null || block.Id == 0) continue;
                foreach (var match in removeBaseFilters)
                {
                    if (block.Code.ToString().StartsWith(match))
                    {
                        removeBaseBehavior = true;
                        break;
                    }
                }
                block.BlockBehaviors = RemoveOverriddenCarryableBehaviors(block.BlockBehaviors);
                block.CollectibleBehaviors = RemoveOverriddenCarryableBehaviors(block.CollectibleBehaviors, removeBaseBehavior);
            }
        }

        /// <summary>
        /// Removes overridden carryable behaviors from blocks based on patch priority.
        /// </summary>
        private T[] RemoveOverriddenCarryableBehaviors<T>(T[] behaviors, bool removeBaseBehavior = false)
        {
            if (behaviors.Length == 0) return behaviors;

            var behaviorList = behaviors.ToList();
            var carryableList = behaviorList.OfType<BlockBehaviorCarryable>().ToList();

            if (carryableList.Count > 1)
            {
                var hasOverrides = carryableList.Any(c => c.OverrideExistingProperties);

                BlockBehaviorCarryable? keepBehavior = null;

                if (!hasOverrides)
                {
                    var maxPriority = carryableList.Max(m => m.PatchPriority);
                    keepBehavior = carryableList.FirstOrDefault(p => p.PatchPriority == maxPriority);
                }
                else
                {
                    var ordered = carryableList.OrderBy(c => c.PatchPriority).ToList();

                    var primaryBehavior = ordered
                        .Where(c => !c.OverrideExistingProperties)
                        .OrderByDescending(c => c.PatchPriority)
                        .FirstOrDefault()
                        ?? ordered.Last();

                    keepBehavior = primaryBehavior;

                    var overlays = ordered
                        .Where(c => c.OverrideExistingProperties && c.PatchPriority >= primaryBehavior.PatchPriority)
                        .ToList();

                    if (overlays.Count > 0)
                    {
                        var merged = MergeCarryableProperties(primaryBehavior, overlays);
                        primaryBehavior.Initialize(merged);
                    }
                }

                if (keepBehavior != null && ShouldKeepBehavior(keepBehavior, keepBehavior.PatchPriority, removeBaseBehavior))
                {
                    carryableList.Remove(keepBehavior);
                }

                behaviorList.RemoveAll(r => carryableList.Any(c => ReferenceEquals(r, c)));
            }
            else if (removeBaseBehavior && carryableList.Count == 1 && carryableList[0].PatchPriority == 0)
            {
                behaviorList.RemoveAll(r => carryableList.Any(c => ReferenceEquals(r, c)));
            }

            return behaviorList.ToArray();
        }

        private static bool ShouldKeepBehavior(BlockBehaviorCarryable behavior, int maxPriority, bool removeBaseBehavior)
        {
            return behavior.PatchPriority == maxPriority &&
                !(removeBaseBehavior && behavior.PatchPriority == 0);
        }

        private static JsonObject MergeCarryableProperties(
            BlockBehaviorCarryable baseBehavior,
            List<BlockBehaviorCarryable> overlays)
        {
            var merged = baseBehavior?.Properties?.Token as JObject;
            merged = merged != null ? (JObject)merged.DeepClone() : new JObject();

            foreach (var overlay in overlays)
            {
                if (overlay?.Properties?.Token is JObject overlayObj)
                {
                    merged.Merge(overlayObj, new JsonMergeSettings
                    {
                        MergeArrayHandling = MergeArrayHandling.Replace,
                        MergeNullValueHandling = MergeNullValueHandling.Merge
                    });
                }
            }

            return new JsonObject(merged);
        }
    }
}
