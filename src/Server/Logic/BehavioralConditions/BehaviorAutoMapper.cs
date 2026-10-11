using System;
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
            var allowedShapeOnlyMatches = new HashSet<string>(
                filters.AllowedShapeOnlyMatches ?? [], StringComparer.OrdinalIgnoreCase);

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

                    if (shapePath != null && allowedShapeOnlyMatches.Contains(shapePath))
                        TryAddMatchBehavior(matchBehaviors, shapeKey, carryableBlock, loggingEnabled, api);
                }
            }

            var autoMatchIgnoreMods = new HashSet<string>(
                filters.AutoMatchIgnoreMods ?? [], StringComparer.OrdinalIgnoreCase);

            foreach (var block in api.World.Blocks.Where(w => !w.IsCarryable() && !autoMatchIgnoreMods.Contains(w.Code?.Domain ?? "")))
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

                    // Factory-made instances: the marker is what the conditioning
                    // baseline's un-create sweep removes them by on the next run, so a
                    // config that stops producing them (AutoMapSimilar, the ignore
                    // list, the shape allowlist) converges to the fresh-boot surface
                    // instead of leaving the clones behind one-way.
                    var blockSide = new BlockBehaviorCarryable(block);
                    blockSide.CreatedByConditioning = true;
                    block.BlockBehaviors = block.BlockBehaviors.Append(blockSide).ToArray();
                    blockSide.Initialize(properties);

                    var collectibleSide = new BlockBehaviorCarryable(block);
                    collectibleSide.CreatedByConditioning = true;
                    block.CollectibleBehaviors = block.CollectibleBehaviors.Append(collectibleSide).ToArray();
                    collectibleSide.Initialize(properties);

                    ConditioningBaseline.NoteTouched(block.Code?.ToString());
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

            // Donors retained by behavior, not by EntityClass string alone: the twin has
            // to be initialized from the donor's properties (mirroring the carryable pass
            // at :93-109), so the first donor behavior per EntityClass is what this pass
            // keeps in scope.
            var matchBehaviors = new Dictionary<string, BlockBehaviorCarryableInteract>();
            foreach (var interactBlock in api.World.Blocks.Where(b => b.IsCarryableInteract()))
            {
                if (interactBlock.EntityClass == null || interactBlock.EntityClass == "Generic") continue;
                if (matchBehaviors.ContainsKey(interactBlock.EntityClass)) continue;

                var donor = interactBlock.GetBehavior<BlockBehaviorCarryableInteract>();
                if (donor == null) continue;

                matchBehaviors[interactBlock.EntityClass] = donor;
            }

            var autoMatchIgnoreModsInteract = new HashSet<string>(
                filters.AutoMatchIgnoreMods ?? [], StringComparer.OrdinalIgnoreCase);

            foreach (var block in api.World.Blocks.Where(w => !w.IsCarryableInteract()
                && w.EntityClass != null
                && matchBehaviors.ContainsKey(w.EntityClass)
                && !autoMatchIgnoreModsInteract.Contains(w.Code?.Domain ?? "")))
            {
                var donor = matchBehaviors[block.EntityClass!];
                var properties = donor.Properties;
                if (properties == null) continue;

                // Factory-made instances, same as the carryable pass: the marker lets
                // the next run's un-create sweep remove interact clones too, and the
                // donor's properties make the twin conditional (strippable when the
                // donor's enabledCondition is off) and its snapshot payload meaningful.
                var blockSide = new BlockBehaviorCarryableInteract(block);
                blockSide.CreatedByConditioning = true;
                block.BlockBehaviors = block.BlockBehaviors.Append(blockSide).ToArray();
                blockSide.Initialize(properties);

                var collectibleSide = new BlockBehaviorCarryableInteract(block);
                collectibleSide.CreatedByConditioning = true;
                block.CollectibleBehaviors = block.CollectibleBehaviors.Append(collectibleSide).ToArray();
                collectibleSide.Initialize(properties);

                ConditioningBaseline.NoteTouched(block.Code?.ToString());
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
