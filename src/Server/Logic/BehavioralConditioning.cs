using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using static CarryOn.Utility.Extensions;

namespace CarryOn.Server.Logic
{
    public class BehavioralConditioning
    {

        public CarryOnConfig Config { get; private set; } = null!;

        public void Init(ICoreAPI api, CarryOnConfig config)
        {
            if(config == null)
            {
                api.Logger.Error("CarryOn: Config is null in BehavioralConditioning.Init");
                return;
            }
            this.Config = config;

            RemoveDisabledConditionalBehaviors(api);
            ResolveMultipleCarryableBehaviors(api);
            AutoMapSimilarCarryables(api);
            AutoMapSimilarCarryableInteract(api);
            RemoveExcludedCarryableBehaviors(api);
        }


        /// <summary>
        /// Removes all conditional behaviors from blocks that are not enabled by the EnabledCondition.
        /// </summary>
        private void RemoveDisabledConditionalBehaviors(ICoreAPI api)
        {
            if(api.World == null || api.World.Config == null)
            {
                api.Logger.Error("CarryOn: World or World.Config is null in RemoveDisabledConditionalBehaviors");
                return;
            }

            var worldConfig = api.World.Config;

            foreach (var block in api.World.Blocks.Where(b => b.BlockBehaviors.Any(beh => beh is IConditionalBlockBehavior) == true))
            {
                // Get all conditional behaviors
                var conditionalBehaviors = block.BlockBehaviors.OfType<IConditionalBlockBehavior>().ToList();

                foreach (var behavior in conditionalBehaviors)
                {
                    if (behavior.EnabledCondition != null && !worldConfig.EvaluateDotNotationLogic(api, behavior.EnabledCondition))
                    {
                        // Remove all behaviors of this type if disabled
                        block.BlockBehaviors = RemoveBehaviorsOfType(block.BlockBehaviors, behavior.GetType());
                        block.CollectibleBehaviors = RemoveBehaviorsOfType(block.CollectibleBehaviors, behavior.GetType());
                        continue;
                    }
                    // If we reach here, the behavior is enabled
                    behavior.ProcessConditions(api, block);

                }
            }
        }

        /// <summary>
        /// Removes carryable behaviors from blocks that are excluded in the config.
        /// </summary>
        /// <param name="api"></param>
        private void RemoveExcludedCarryableBehaviors(ICoreAPI api)
        {

            if (Config == null)
            {
                api.Logger.Error("CarryOn: Config is null in RemoveExcludedCarryableBehaviours");
                return;
            }

            if (Config.DebuggingOptions == null)
            {
                api.Logger.Error("CarryOn: DebuggingOptions is null in RemoveExcludedCarryableBehaviours");
                return;
            }
            
            if (Config.CarryablesFilters == null)
            {
                api.Logger.Error("CarryOn: CarryablesFilters is null in RemoveExcludedCarryableBehaviours");
                return;
            }

            var loggingEnabled = Config.DebuggingOptions.LoggingEnabled;
            var filters = Config.CarryablesFilters;


            var removeArray = filters.RemoveCarryableBehaviour;
            if (removeArray == null || removeArray.Length == 0)
            {
                return;
            }

            foreach (var block in api.World.Blocks.Where(b => b.Code != null))
            {
                foreach (var remove in removeArray)
                {
                    if (block.Code.ToString().StartsWith(remove))
                    {
                        var count = block.BlockBehaviors.Length;
                        block.BlockBehaviors = RemoveBehaviorsOfType(block.BlockBehaviors, typeof(BlockBehaviorCarryable));
                        block.CollectibleBehaviors = RemoveBehaviorsOfType(block.CollectibleBehaviors, typeof(BlockBehaviorCarryable));

                        if (count != block.BlockBehaviors.Length && loggingEnabled)
                        {
                            api.Logger.Debug($"CarryOn Removed Carryable Behaviour: {block.Code}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Resolves multiple carryable behaviors for blocks.
        /// </summary>
        /// <param name="api"></param>
        private void ResolveMultipleCarryableBehaviors(ICoreAPI api)
        {
            if (Config == null)
            {
                api.Logger.Error("CarryOn: Config is null in ResolveMultipleCarryableBehaviors");
                return;
            }

            var filters = Config.CarryablesFilters;
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
                if (removeBaseFilters != null)
                {
                    foreach (var match in removeBaseFilters)
                    {
                        if (block.Code.ToString().StartsWith(match))
                        {
                            removeBaseBehavior = true;
                            break;
                        }
                    }
                }
                block.BlockBehaviors = RemoveOverriddenCarryableBehaviors(block.BlockBehaviors);
                block.CollectibleBehaviors = RemoveOverriddenCarryableBehaviors(block.CollectibleBehaviors, removeBaseBehavior);
            }
        }

        /// <summary>
        /// Removes overridden carryable behaviors from blocks based on patch priority.
        /// </summary>
        /// <param name="api"></param>
        private T[] RemoveOverriddenCarryableBehaviors<T>(T[] behaviours, bool removeBaseBehavior = false)
        {
            if (behaviours.Length == 0) return behaviours;

            var behaviourList = behaviours.ToList();
            var carryableList = behaviourList.OfType<BlockBehaviorCarryable>().ToList();

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

                    keepBehavior = ordered
                        .Where(c => !c.OverrideExistingProperties)
                        .OrderByDescending(c => c.PatchPriority)
                        .FirstOrDefault()
                        ?? ordered.Last();

                    var overlays = ordered
                        .Where(c => c.OverrideExistingProperties && c.PatchPriority >= keepBehavior!.PatchPriority)
                        .ToList();

                    if (overlays.Count > 0)
                    {
                        var merged = MergeCarryableProperties(keepBehavior!, overlays);
                        keepBehavior!.Initialize(merged);
                    }
                }

                if (keepBehavior != null && ShouldKeepBehavior(keepBehavior, keepBehavior.PatchPriority, removeBaseBehavior))
                {
                    carryableList.Remove(keepBehavior);
                }

                behaviourList.RemoveAll(r => carryableList.Any(c => ReferenceEquals(r, c)));
            }
            else if (removeBaseBehavior && carryableList.Count == 1 && carryableList[0].PatchPriority == 0)
            {
                behaviourList.RemoveAll(r => carryableList.Any(c => ReferenceEquals(r, c)));
            }

            return behaviourList.ToArray();
        }

        /// <summary>
        /// Removes all BlockBehaviorCarryable from an array of behaviors (BlockBehavior[] or CollectibleBehavior[]).
        /// </summary>
        public static T[] RemoveBehaviorsOfType<T>(T[] behaviours, Type typeToRemove)
        {
            var behaviourList = behaviours.ToList();
            behaviourList.RemoveAll(r => typeToRemove.IsInstanceOfType(r));
            return behaviourList.ToArray();
        }

        /// <summary>
        /// Automatically maps carryable interact behaviors to similar blocks.
        /// </summary>
        /// <param name="api"></param>
        private void AutoMapSimilarCarryableInteract(ICoreAPI api)
        {
            if (Config == null)
            {
                api.Logger.Error("CarryOn: Config is null in AutoMapSimilarCarryableInteract");
                return;
            }

            if (Config.DebuggingOptions == null)
            {
                api.Logger.Error("CarryOn: DebuggingOptions is null in AutoMapSimilarCarryableInteract");
                return;
            }

            var loggingEnabled = Config.DebuggingOptions.LoggingEnabled;
            var filters = Config.CarryablesFilters;

            if (filters == null)
            {
                api.Logger.Error("CarryOn: CarryablesFilters is null in AutoMapSimilarCarryableInteract");
                return;
            }

            if (filters?.AutoMapSimilar != true) return;

            var matchKeys = new List<string>();
            foreach (var interactBlock in api.World.Blocks.Where(b => b.IsCarryableInteract()))
            {
                if (interactBlock.EntityClass == null || interactBlock.EntityClass == "Generic") continue;

                if (!matchKeys.Contains(interactBlock.EntityClass))
                {
                    matchKeys.Add(interactBlock.EntityClass);
                }
            }

            foreach (var block in api.World.Blocks.Where(w => !w.IsCarryableInteract()
                && matchKeys.Contains(w.EntityClass)
                && !filters.AutoMatchIgnoreMods.Contains(w?.Code?.Domain)))
            {
                block.BlockBehaviors = block.BlockBehaviors.Append(new BlockBehaviorCarryableInteract(block));
                block.CollectibleBehaviors = block.CollectibleBehaviors.Append(new BlockBehaviorCarryableInteract(block));
                if (loggingEnabled) api.Logger.Debug($"CarryOn AutoMatch Interact: {block.Code} key: {block.EntityClass}");
            }
        }

        /// <summary>
        /// Automatically maps carryable behaviors to similar blocks.
        /// </summary>
        /// <param name="api"></param>
        private void AutoMapSimilarCarryables(ICoreAPI api)
        {
            if (Config == null)
            {
                api.Logger.Error("CarryOn: Config is null in AutoMapSimilarCarryables");
                return;
            }
            if (Config.DebuggingOptions == null)
            {
                api.Logger.Error("CarryOn: DebuggingOptions is null in AutoMapSimilarCarryables");
                return;
            }
            var loggingEnabled = Config.DebuggingOptions.LoggingEnabled;
            var filters = Config.CarryablesFilters;
            if (filters == null)
            {
                api.Logger.Error("CarryOn: CarryablesFilters is null in AutoMapSimilarCarryables");
                return;
            }

            if (filters?.AutoMapSimilar != true) return;

            var matchBehaviors = new Dictionary<string, BlockBehaviorCarryable>();
            foreach (var carryableBlock in api.World.Blocks.Where(b => b.IsCarryable() && b.Code.Domain == "game"))
            {
                var shapePath = carryableBlock.ShapeInventory?.Base?.Path ?? carryableBlock.Shape?.Base?.Path;
                var shapeKey = shapePath != null && shapePath != "block/basic/cube" ? $"Shape:{shapePath}" : null;

                string? entityClassKey = null;

                if (carryableBlock.EntityClass != null && carryableBlock.EntityClass != "Generic" && carryableBlock.EntityClass != "Transient")
                {
                    entityClassKey = $"EntityClass:{carryableBlock.EntityClass}";
                    if (!matchBehaviors.ContainsKey(entityClassKey))
                    {
                        matchBehaviors[entityClassKey] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
                        if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {entityClassKey} carryableBlock: {carryableBlock.Code}");
                    }
                }

                string? classKey = null;
                if (carryableBlock.Class is not "Block" and not "BlockGeneric")
                {
                    classKey = $"Class:{carryableBlock.Class}";
                    if (!matchBehaviors.ContainsKey(classKey))
                    {
                        matchBehaviors[classKey] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
                        if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {classKey} carryableBlock: {carryableBlock.Code}");
                    }
                }

                if (shapeKey != null)
                {
                    if (entityClassKey != null)
                    {
                        var key = $"{entityClassKey}|{shapeKey}";
                        if (!matchBehaviors.ContainsKey(key))
                        {
                            matchBehaviors[key] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
                            if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {key} carryableBlock: {carryableBlock.Code}");
                        }
                    }

                    if (classKey != null)
                    {
                        var key = $"{classKey}|{shapeKey}";
                        if (!matchBehaviors.ContainsKey(key))
                        {
                            matchBehaviors[key] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
                            if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {key} carryableBlock: {carryableBlock.Code}");
                        }
                    }

                    if (filters.AllowedShapeOnlyMatches.Contains(shapePath) && !matchBehaviors.ContainsKey(shapeKey))
                    {
                        matchBehaviors[shapeKey] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();

                        if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {shapeKey} carryableBlock: {carryableBlock.Code}");
                    }
                }
            }

            foreach (var block in api.World.Blocks.Where(w => !w.IsCarryable() && !filters.AutoMatchIgnoreMods.Contains(w?.Code?.Domain)))
            {
                if (block.EntityClass == null) continue;
                var matchKeys = GetPotentialMatchKeys(block);
                string? key = null;

                foreach (var matchKey in matchKeys)
                {
                    if (matchBehaviors.ContainsKey(matchKey))
                    {
                        key = matchKey;
                        if (loggingEnabled) api.Logger.Debug($"CarryOn AutoMatch: {block.Code} key: {key}");
                        break;
                    }
                }

                if (key != null)
                {
                    var behavior = matchBehaviors[key];

                    var newBehavior = new BlockBehaviorCarryable(block);
                    block.BlockBehaviors = block.BlockBehaviors.Append(newBehavior);
                    newBehavior.Initialize(behavior.Properties!);

                    newBehavior = new BlockBehaviorCarryable(block);
                    block.CollectibleBehaviors = block.CollectibleBehaviors.Append(newBehavior);
                    newBehavior.Initialize(behavior.Properties!);
                }
            }
        }

        // Move outside the loop if the keys don't change per block
        private string[] GetPotentialMatchKeys(Block block)
        {
            var classKey = $"Class:{block.Class}";
            var entityClassKey = $"EntityClass:{block.EntityClass}";
            var shapePath = block?.ShapeInventory?.Base?.Path ?? block?.Shape?.Base?.Path;
            var shapeKey = shapePath != null ? $"Shape:{shapePath}" : null;

            if (shapeKey == null)
                return [classKey];

            return
            [
                $"{classKey}|{shapeKey}",
                $"{entityClassKey}|{shapeKey}",
                shapeKey,
                classKey
            ];
        }

        private bool ShouldKeepBehavior(BlockBehaviorCarryable behavior, int maxPriority, bool removeBaseBehavior)
        {
            return behavior.PatchPriority == maxPriority &&
                !(removeBaseBehavior && behavior.PatchPriority == 0);
        }

        private JsonObject MergeCarryableProperties(
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