using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Common.Behaviors;
using CarryOn.Config;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace CarryOn.Server.Logic
{
    public class BehaviorialConditioning
    {

        public void Init(ICoreAPI api)
        {
            RemoveDisabledCarryableBehaviours(api);
            ManuallyAddCarryableBehaviors(api);
            ResolveMultipleCarryableBehaviors(api);
            AutoMapSimilarCarryables(api);
            AutoMapSimilarCarryableInteract(api);
            RemoveExcludedCarryableBehaviours(api);
        }

        /// <summary>
        /// Removes all carryable behaviors from blocks that are not enabled by the EnabledCondition.
        /// </summary>
        private void RemoveDisabledCarryableBehaviours(ICoreAPI api)
        {
            // Find all blocks with disabled carryable behaviors
            var blocksWithEnabledKey = api.World.Blocks.Where(b => b.HasBehavior<BlockBehaviorCarryable>());

            IAttribute carryOnConfig, carryables;

            // TODO: Allow checking in different world config locations
            // Current implementation is only looking at carryon.Carryables

            if (!api.World.Config.TryGetAttribute("carryon", out carryOnConfig)) return;

            carryables = carryOnConfig.TryGet("Carryables");
            if (carryables == null)
            {
                api.Logger.Warning("CarryOn: Cannot find carryon.Carryables in world config");
                return;
            }

            if (carryables is not TreeAttribute carryablesTree)
            {
                api.Logger.Warning("CarryOn: carryon.Carryables is not a TreeAttribute");
                return;
            }

            foreach (var block in blocksWithEnabledKey)
            {
                var behavior = block.GetBehavior<BlockBehaviorCarryable>();

                if (string.IsNullOrWhiteSpace(behavior.EnabledCondition)) continue;

                // Support dot notation for enabledCondition
                var keys = behavior.EnabledCondition.Split('.');
                IAttribute current = api.World.Config;
                bool? isEnabled = null;
                foreach (var key in keys)
                {
                    if (current is TreeAttribute tree)
                    {
                        if (tree.HasAttribute(key))
                        {
                            current = tree[key];
                        }
                        else
                        {
                            current = null;
                            break;
                        }
                    }
                    else
                    {
                        current = null;
                        break;
                    }
                }
                if (current is BoolAttribute boolAttr)
                {
                    isEnabled = boolAttr.value;
                }
                else if (current is TreeAttribute treeAttr && treeAttr is not null)
                {
                    // If the final attribute is a TreeAttribute, treat as enabled
                    isEnabled = true;
                }
                else
                {
                    isEnabled = null;
                }

                if (!isEnabled.HasValue)
                {
                    api.Logger.Warning($"CarryOn: {behavior.EnabledCondition} is not a boolean or not found");
                }

                if (isEnabled.HasValue && isEnabled.Value) continue;

                block.BlockBehaviors = RemoveCarryableBehaviours(block.BlockBehaviors.OfType<CollectibleBehavior>().ToArray()).OfType<BlockBehavior>().ToArray();
                block.CollectibleBehaviors = RemoveCarryableBehaviours(block.CollectibleBehaviors);
            }
        }

        /// <summary>
        /// Manually adds carryable behaviors to specific blocks.
        /// </summary>
        /// <param name="api"></param>
        private void ManuallyAddCarryableBehaviors(ICoreAPI api)
        {
            try
            {
                if (ModConfig.HenboxEnabled)
                {
                    var block = api.World.BlockAccessor.GetBlock("henbox");
                    if (block != null)
                    {
                        // Only allow default hand slot 
                        var properties = JsonObject.FromJson("{slots:{Hands:{}}}");
                        AddCarryableBehavior(block, ref block.BlockBehaviors, ref block.CollectibleBehaviors, properties);
                    }
                }
            }
            catch (Exception e)
            {
                api.Logger.Error($"Error in ManuallyAddCarryableBehaviors: {e.Message}");
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
        private void RemoveExcludedCarryableBehaviours(ICoreAPI api)
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
                        block.BlockBehaviors = RemoveCarryableBehaviours(block.BlockBehaviors.OfType<CollectibleBehavior>().ToArray()).OfType<BlockBehavior>().ToArray();
                        block.CollectibleBehaviors = RemoveCarryableBehaviours(block.CollectibleBehaviors);

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
                block.BlockBehaviors = RemoveOverriddenCarryableBehaviours(block.BlockBehaviors.OfType<CollectibleBehavior>().ToArray(), removeBaseBehavior).OfType<BlockBehavior>().ToArray();
                block.CollectibleBehaviors = RemoveOverriddenCarryableBehaviours(block.CollectibleBehaviors, removeBaseBehavior);
            }
        }

        /// <summary>
        /// Removes overridden carryable behaviors from blocks based on patch priority.
        /// </summary>
        /// <param name="api"></param>
        private CollectibleBehavior[] RemoveOverriddenCarryableBehaviours(CollectibleBehavior[] behaviours, bool removeBaseBehavior = false)
        {
            var behaviourList = behaviours.ToList();
            var carryableList = FindCarryables(behaviourList);
            if (carryableList.Count > 1)
            {
                var priorityCarryable = carryableList.First(p => p.PatchPriority == carryableList.Max(m => m.PatchPriority));
                if (priorityCarryable != null)
                {
                    if (!(removeBaseBehavior && priorityCarryable.PatchPriority == 0))
                    {
                        carryableList.Remove(priorityCarryable);
                    }
                    behaviourList.RemoveAll(r => carryableList.Contains(r));
                }
            }
            else if (removeBaseBehavior && carryableList.Count == 1 && carryableList[0].PatchPriority == 0)
            {
                // Remove base behavior
                behaviourList.RemoveAll(r => carryableList.Contains(r));
            }
            return behaviourList.ToArray();
        }

        /// <summary>
        /// Removes all carryable behaviors from a collection of behaviors.
        /// </summary>
        /// <param name="api"></param>
        private CollectibleBehavior[] RemoveCarryableBehaviours(CollectibleBehavior[] behaviours)
        {
            var behaviourList = behaviours.ToList();
            var carryableList = FindCarryables(behaviourList);

            if (carryableList.Count == 0) return behaviours;

            behaviourList.RemoveAll(r => carryableList.Contains(r));

            return behaviourList.ToArray();
        }

        // Finds all carryable behaviors in a list of behaviors.
        private List<BlockBehaviorCarryable> FindCarryables<T>(List<T> behaviors)
        {
            var carryables = new List<BlockBehaviorCarryable>();
            foreach (var behavior in behaviors)
            {
                if (behavior is BlockBehaviorCarryable carryable)
                {
                    carryables.Add(carryable);
                }
            }
            return carryables;
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