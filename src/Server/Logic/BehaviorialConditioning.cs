using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Common.Behaviors;
using CarryOn.Config;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using static CarryOn.Utility.Extensions;

namespace CarryOn.Server.Logic
{
    public class BehaviorialConditioning
    {

        public void Init(ICoreAPI api)
        {
            RemoveDisabledCarryableBehaviors(api);
            ResolveMultipleCarryableBehaviors(api);
            AutoMapSimilarCarryables(api);
            AutoMapSimilarCarryableInteract(api);
            RemoveExcludedCarryableBehaviors(api);
        }

        /// <summary>
        /// Removes all carryable behaviors from blocks that are not enabled by the EnabledCondition.
        /// </summary>
        private void RemoveDisabledCarryableBehaviors(ICoreAPI api)
        {
            // Find all blocks with disabled carryable behaviors
            var blocksWithEnabledKey = api.World.Blocks.Where(b => b.HasBehavior<BlockBehaviorCarryable>());

            var config = api.World.Config;

            foreach (var block in blocksWithEnabledKey)
            {
                var behavior = block.GetBehavior<BlockBehaviorCarryable>();

                bool isEnabled = config.EvaluateDotNotationLogic(api, behavior.EnabledCondition);

                if (isEnabled) continue;

                block.BlockBehaviors = RemoveCarryableBehaviors(block.BlockBehaviors);
                block.CollectibleBehaviors = RemoveCarryableBehaviors(block.CollectibleBehaviors);
            }
        }

        // Helper to create, initialize, and append BlockBehaviorCarryable to a collection
        private void AddCarryableBehavior(Block block, ref BlockBehavior[] blockBehaviors, ref CollectibleBehavior[] collectibleBehaviors, JsonObject properties)
        {
            var blockBehavior = new BlockBehaviorCarryable(block);
            blockBehaviors = blockBehaviors.Append(blockBehavior);
            blockBehavior.Initialize(properties);

            collectibleBehaviors = collectibleBehaviors.Append(blockBehavior);
        }

        /// <summary>
        /// Removes carryable behaviors from blocks that are excluded in the config.
        /// </summary>
        /// <param name="api"></param>
        private void RemoveExcludedCarryableBehaviors(ICoreAPI api)
        {
            var loggingEnabled = ModConfig.ServerConfig.DebuggingOptions.LoggingEnabled;
            var filters = ModConfig.ServerConfig.CarryablesFilters;

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
                        block.BlockBehaviors = RemoveCarryableBehaviors(block.BlockBehaviors);
                        block.CollectibleBehaviors = RemoveCarryableBehaviors(block.CollectibleBehaviors);

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
            var filters = ModConfig.ServerConfig.CarryablesFilters;

            foreach (var block in api.World.Blocks)
            {
                bool removeBaseBehavior = false;
                if (block.Code == null || block.Id == 0) continue;
                foreach (var match in filters.RemoveBaseCarryableBehaviour)
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
        /// <param name="api"></param>
        private T[] RemoveOverriddenCarryableBehaviors<T>(T[] behaviours, bool removeBaseBehavior = false)
        {
            if (behaviours == null || behaviours.Length == 0) return behaviours;
            var behaviourList = behaviours.ToList();
            // Only consider BlockBehaviorCarryable instances for removal
            var carryableList = behaviourList.OfType<BlockBehaviorCarryable>().ToList();
            if (carryableList.Count > 1)
            {
                var maxPriority = carryableList.Max(m => m.PatchPriority);
                var priorityCarryable = carryableList.FirstOrDefault(p => p.PatchPriority == maxPriority);
                if (priorityCarryable != null)
                {
                    if (!(removeBaseBehavior && priorityCarryable.PatchPriority == 0))
                    {
                        carryableList.Remove(priorityCarryable);
                    }
                    behaviourList.RemoveAll(r => carryableList.Any(c => ReferenceEquals(r, c)));
                }
            }
            else if (removeBaseBehavior && carryableList.Count == 1 && carryableList[0].PatchPriority == 0)
            {
                // Remove base behavior
                behaviourList.RemoveAll(r => carryableList.Any(c => ReferenceEquals(r, c)));
            }
            return behaviourList.ToArray();
        }

        /// <summary>
        /// Removes all BlockBehaviorCarryable from an array of behaviors (BlockBehavior[] or CollectibleBehavior[]).
        /// </summary>
        public static T[] RemoveCarryableBehaviors<T>(T[] behaviours)
        {
            if (behaviours == null) return null;
            var behaviourList = behaviours.ToList();
            behaviourList.RemoveAll(r => r is BlockBehaviorCarryable);
            return behaviourList.ToArray();
        }        

        /// <summary>
        /// Automatically maps carryable interact behaviors to similar blocks.
        /// </summary>
        /// <param name="api"></param>
        private void AutoMapSimilarCarryableInteract(ICoreAPI api)
        {
            var loggingEnabled = ModConfig.ServerConfig.DebuggingOptions.LoggingEnabled;
            var filters = ModConfig.ServerConfig.CarryablesFilters;

            if (!filters.AutoMapSimilar) return;

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
            var loggingEnabled = ModConfig.ServerConfig.DebuggingOptions.LoggingEnabled;

            var filters = ModConfig.ServerConfig.CarryablesFilters;

            if (!filters.AutoMapSimilar) return;

            var matchBehaviors = new Dictionary<string, BlockBehaviorCarryable>();
            foreach (var carryableBlock in api.World.Blocks.Where(b => b.IsCarryable() && b.Code.Domain == "game"))
            {
                var shapePath = carryableBlock?.ShapeInventory?.Base?.Path ?? carryableBlock?.Shape?.Base?.Path;
                var shapeKey = shapePath != null && shapePath != "block/basic/cube" ? $"Shape:{shapePath}" : null;

                string entityClassKey = null;

                if (carryableBlock.EntityClass != null && carryableBlock.EntityClass != "Generic" && carryableBlock.EntityClass != "Transient")
                {
                    entityClassKey = $"EntityClass:{carryableBlock.EntityClass}";
                    if (!matchBehaviors.ContainsKey(entityClassKey))
                    {
                        matchBehaviors[entityClassKey] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
                        if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {entityClassKey} carryableBlock: {carryableBlock.Code}");
                    }
                }

                string classKey = null;
                if (carryableBlock.Class != "Block")
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
                string key = null;

                var classKey = $"Class:{block.Class}";
                var entityClassKey = $"EntityClass:{block.EntityClass}";
                var shapePath = block?.ShapeInventory?.Base?.Path ?? block?.Shape?.Base?.Path;
                var shapeKey = shapePath != null ? $"Shape:{shapePath}" : null;

                var matchKeys = new List<string>
                {
                    $"{classKey}|{shapeKey}",
                    $"{entityClassKey}|{shapeKey}",
                    shapeKey,
                    classKey
                };

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
                    newBehavior.Initialize(behavior.Properties);

                    newBehavior = new BlockBehaviorCarryable(block);
                    block.CollectibleBehaviors = block.CollectibleBehaviors.Append(newBehavior);
                    newBehavior.Initialize(behavior.Properties);
                }
            }
        }
    }
}