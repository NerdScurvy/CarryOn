using System;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Common;

namespace CarryOn.Server.Logic.BehavioralConditions
{
    internal class BlockFilter
    {
        private readonly CarryOnConfig config;

        internal BlockFilter(CarryOnConfig config)
        {
            this.config = config;
        }

        /// <summary>
        /// Removes all conditional behaviors from blocks that are not enabled by the EnabledCondition.
        /// </summary>
        internal void RemoveDisabledConditionalBehaviors(ICoreAPI api)
        {
            if (api.World == null || api.World.Config == null)
            {
                api.Logger.Error("CarryOn: World or World.Config is null in RemoveDisabledConditionalBehaviors");
                return;
            }

            var worldConfig = api.World.Config;

            foreach (var block in api.World.Blocks.Where(b => b.BlockBehaviors.Any(beh => beh is IConditionalBlockBehavior) == true))
            {
                var conditionalBehaviors = block.BlockBehaviors.OfType<IConditionalBlockBehavior>().ToList();

                foreach (var behavior in conditionalBehaviors)
                {
                    if (behavior.EnabledCondition != null && !worldConfig.EvaluateDotNotationLogic(api, behavior.EnabledCondition))
                    {
                        block.BlockBehaviors = RemoveBehaviorsOfType(block.BlockBehaviors, behavior.GetType());
                        block.CollectibleBehaviors = RemoveBehaviorsOfType(block.CollectibleBehaviors, behavior.GetType());
                        continue;
                    }
                    behavior.ProcessConditions(api, block);
                }
            }
        }

        /// <summary>
        /// Removes carryable behaviors from blocks that are excluded in the config.
        /// </summary>
        internal void RemoveExcludedCarryableBehaviors(ICoreAPI api)
        {
            var loggingEnabled = config.DebuggingOptions?.LoggingEnabled ?? false;
            var filters = config.CarryablesFilters;
            if (filters == null) return;

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
                            api.Logger.Debug($"CarryOn Removed Carryable Behavior: {block.Code}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Removes all behaviors of a given type from an array of behaviors.
        /// </summary>
        internal static T[] RemoveBehaviorsOfType<T>(T[] behaviors, Type typeToRemove)
        {
            var behaviorList = behaviors.ToList();
            behaviorList.RemoveAll(r => typeToRemove.IsInstanceOfType(r));
            return behaviorList.ToArray();
        }
    }
}
