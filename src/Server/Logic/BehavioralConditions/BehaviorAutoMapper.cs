using System.Collections.Generic;
using System.Linq;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Common;

namespace CarryOn.Server.Logic.BehavioralConditions
{
    internal class BehaviorAutoMapper
    {
        private readonly CarryOnConfig config;

        private const string ClassKeyPrefix = "Class:";
        private const string EntityClassKeyPrefix = "EntityClass:";
        private const string ShapeKeyPrefix = "Shape:";
        private const string DefaultShapePath = "block/basic/cube";

        internal BehaviorAutoMapper(CarryOnConfig config)
        {
            this.config = config;
        }

        /// <summary>
        /// Automatically maps carryable behaviors to similar blocks.
        /// </summary>
        internal void AutoMapSimilarCarryables(ICoreAPI api)
        {
            if (config.CarryablesFilters?.AutoMapSimilar != true) return;
            var loggingEnabled = config.DebuggingOptions?.LoggingEnabled ?? false;
            var filters = config.CarryablesFilters;
            if (filters == null) return;

            var matchBehaviors = new Dictionary<string, BlockBehaviorCarryable>();
            foreach (var carryableBlock in api.World.Blocks.Where(b => b.IsCarryable() && b.Code.Domain == "game"))
            {
                var shapePath = carryableBlock.ShapeInventory?.Base?.Path ?? carryableBlock.Shape?.Base?.Path;
                var shapeKey = shapePath != null && shapePath != DefaultShapePath ? $"{ShapeKeyPrefix}{shapePath}" : null;

                string? entityClassKey = null;

                if (carryableBlock.EntityClass != null && carryableBlock.EntityClass != "Generic" && carryableBlock.EntityClass != "Transient")
                {
                    entityClassKey = $"{EntityClassKeyPrefix}{carryableBlock.EntityClass}";
                    TryAddMatchBehavior(matchBehaviors, entityClassKey, carryableBlock, loggingEnabled, api);
                }

                string? classKey = null;
                if (carryableBlock.Class is not "Block" and not "BlockGeneric")
                {
                    classKey = $"{ClassKeyPrefix}{carryableBlock.Class}";
                    TryAddMatchBehavior(matchBehaviors, classKey, carryableBlock, loggingEnabled, api);
                }

                if (shapeKey != null)
                {
                    if (entityClassKey != null)
                        TryAddMatchBehavior(matchBehaviors, $"{entityClassKey}|{shapeKey}", carryableBlock, loggingEnabled, api);

                    if (classKey != null)
                        TryAddMatchBehavior(matchBehaviors, $"{classKey}|{shapeKey}", carryableBlock, loggingEnabled, api);

                    if (filters.AllowedShapeOnlyMatches.Contains(shapePath))
                        TryAddMatchBehavior(matchBehaviors, shapeKey, carryableBlock, loggingEnabled, api);
                }
            }

            foreach (var block in api.World.Blocks.Where(w => !w.IsCarryable() && !filters.AutoMatchIgnoreMods.Contains(w.Code?.Domain)))
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

                if (key != null && matchBehaviors.TryGetValue(key, out var behavior))
                {
                    var properties = behavior.Properties;
                    if (properties == null) continue;

                    var newBehavior = new BlockBehaviorCarryable(block);
                    block.BlockBehaviors = block.BlockBehaviors.Append(newBehavior).ToArray();
                    newBehavior.Initialize(properties);

                    newBehavior = new BlockBehaviorCarryable(block);
                    block.CollectibleBehaviors = block.CollectibleBehaviors.Append(newBehavior).ToArray();
                    newBehavior.Initialize(properties);
                }
            }
        }

        /// <summary>
        /// Automatically maps carryable interact behaviors to similar blocks.
        /// </summary>
        internal void AutoMapSimilarCarryableInteract(ICoreAPI api)
        {
            if (config.CarryablesFilters?.AutoMapSimilar != true) return;
            var loggingEnabled = config.DebuggingOptions?.LoggingEnabled ?? false;
            var filters = config.CarryablesFilters;
            if (filters == null) return;

            var matchKeys = new HashSet<string>();
            foreach (var interactBlock in api.World.Blocks.Where(b => b.IsCarryableInteract()))
            {
                if (interactBlock.EntityClass == null || interactBlock.EntityClass == "Generic") continue;

                matchKeys.Add(interactBlock.EntityClass);
            }

            foreach (var block in api.World.Blocks.Where(w => !w.IsCarryableInteract()
                && matchKeys.Contains(w.EntityClass)
                && !filters.AutoMatchIgnoreMods.Contains(w.Code?.Domain)))
            {
                block.BlockBehaviors = block.BlockBehaviors.Append(new BlockBehaviorCarryableInteract(block)).ToArray();
                block.CollectibleBehaviors = block.CollectibleBehaviors.Append(new BlockBehaviorCarryableInteract(block)).ToArray();
                if (loggingEnabled) api.Logger.Debug($"CarryOn AutoMatch Interact: {block.Code} key: {block.EntityClass}");
            }
        }

        private static void TryAddMatchBehavior(Dictionary<string, BlockBehaviorCarryable> matchBehaviors, string key, Block carryableBlock, bool loggingEnabled, ICoreAPI api)
        {
            if (matchBehaviors.ContainsKey(key)) return;
            matchBehaviors[key] = carryableBlock.GetBehavior<BlockBehaviorCarryable>();
            if (loggingEnabled) api.Logger.Debug($"CarryOn matchBehavior: {key} carryableBlock: {carryableBlock.Code}");
        }

        private string[] GetPotentialMatchKeys(Block block)
        {
            var classKey = $"{ClassKeyPrefix}{block.Class}";
            var entityClassKey = $"{EntityClassKeyPrefix}{block.EntityClass}";
            var shapePath = block.ShapeInventory?.Base?.Path ?? block.Shape?.Base?.Path;
            var shapeKey = shapePath != null ? $"{ShapeKeyPrefix}{shapePath}" : null;

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
    }
}
