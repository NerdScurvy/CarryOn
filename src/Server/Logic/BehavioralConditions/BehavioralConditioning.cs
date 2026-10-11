using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using CarryOn.Server.Logic.BehavioralConditions;
using Vintagestory.API.Common;

namespace CarryOn.Server.Logic
{
    public class BehavioralConditioning
    {
        private BlockFilter? blockFilter;
        private BehaviorAutoMapper? autoMapper;
        private MultipleBehaviorResolver? behaviorResolver;

        public void Init(ICoreAPI api, CarryOnConfig config)
        {
            if (config == null)
            {
                api.Logger.Error("CarryOn: Config is null in BehavioralConditioning.Init");
                return;
            }

            blockFilter = new BlockFilter(config);
            autoMapper = new BehaviorAutoMapper(config);
            behaviorResolver = new MultipleBehaviorResolver();

            // Pass 0 is the convergence pass (ConditioningBaseline). The first run of a
            // world captures the baseline - every CarryOn-type behavior the world booted
            // with, pristine - and every later run (1) un-creates the instances CarryOn's
            // factory constructed (automap clones, earlier remounts), (2) re-mounts every
            // dirty baseline entry from its pristine JSON into the views it booted in, and
            // (3) records any unmarked behavior a later registration appended - all before
            // any pass below can mutate a block. The passes below then re-derive the
            // configured surface from boot state, so every run converges to what a fresh
            // boot with the same config would produce instead of compounding mutations.
            // ReconditionFullRestore (DebuggingOptions, default false) treats every entry
            // as dirty - the full-restore fallback for live debugging.
            //
            // Conditionally-disabled removal stays deferred to the final strip (after
            // resolution and automapping) for the same two reasons as before:
            //  (1) a disabled block is never a map target (IsCarryable() is behavior
            //      presence), so it cannot be re-enabled with a carryable cloned from an
            //      enabled "similar" source - which would also arrive with the source
            //      block's transforms/straps, wrong for the target; and
            //  (2) twins cloned from a disabled source inherit the disabled condition and
            //      are stripped by the final pass.
            // Every strip marks the entries it removes or condition-processes dirty, so
            // the next run's pass 0 remounts a stripped entry before the automapper can
            // mistake the behaviorless block for a map target - the shield the
            // removed-base restore used to mount by hand is subsumed by convergence: a
            // matched base is remounted the same way, and the base strip re-empties it
            // in the same run.
            ConditioningBaseline.RestoreBaseline(api, config.DebuggingOptions?.ReconditionFullRestore ?? false);

            behaviorResolver.Resolve(api);
            autoMapper.AutoMapSimilarCarryables(api);
            autoMapper.AutoMapSimilarCarryableInteract(api);
            // Both late filters run after the automapper so they see every view a carryable can
            // live in, and after the disabled strip so a condition-disabled survivor cannot keep
            // a matched block alive.
            blockFilter.RemoveDisabledConditionalBehaviors(api);
            blockFilter.RemoveMatchedBaseCarryableBehaviors(api);
            blockFilter.RemoveExcludedCarryableBehaviors(api);

            if (config.CarryOptions?.PickupGroundStorageBagWhileCarrying != false)
            {
                BlockBehaviorGroundStorageBagHelp.InjectInto(api);
            }
        }

        /// <summary>
        /// Whether the behavior is a conditional carryable / carryable-interact instance -
        /// a non-whitespace block-level condition, or for carryables any slot-level
        /// condition. Shared with the client carryability sync, which must remove
        /// exactly these engine-loaded instances - never their unconditional siblings -
        /// when reconciling a block onto the server's snapshot.
        /// </summary>
        internal static bool IsConditionalBehavior(BlockBehavior behavior)
            => behavior switch
            {
                BlockBehaviorCarryable carryable when !string.IsNullOrWhiteSpace(carryable.EnabledCondition)
                    || carryable.Slots.SlotSettingsDict.Values.Any(settings => !string.IsNullOrWhiteSpace(settings.EnabledCondition)) => true,
                BlockBehaviorCarryableInteract interact when !string.IsNullOrWhiteSpace(interact.EnabledCondition) => true,
                _ => false
            };

        /// <summary>
        /// Builds the full carryability snapshot of the managed-set for a
        /// <see cref="CarryabilityStateMessage"/>. Managed codes = every code the
        /// conditioning baseline holds - the per-world capture of every CarryOn-type
        /// behavior, whether currently stripped or restored - ∪ codes currently
        /// carrying a conditional behavior (covers automapped twins on the first boot,
        /// which appear after the capture pass). Each managed code always emits both a
        /// carryable and a carryable-interact entry - with post-condition properties JSON
        /// when the behavior is present, null when stripped - so the client reconciles
        /// presence/absence for both kinds from a single snapshot.
        /// </summary>
        internal static CarryabilityStateMessage BuildStateMessage(ICoreAPI api)
        {
            var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            managed.UnionWith(ConditioningBaseline.ManagedCodes);

            foreach (var block in api.World.Blocks)
            {
                if (block?.Code == null) continue;

                if (block.GetBehavior<BlockBehaviorCarryable>() is { } carryable
                    && !string.IsNullOrWhiteSpace(carryable.EnabledCondition))
                {
                    managed.Add(block.Code.ToString());
                }

                if (block.GetBehavior<BlockBehaviorCarryableInteract>() is { } interact
                    && !string.IsNullOrWhiteSpace(interact.EnabledCondition))
                {
                    managed.Add(block.Code.ToString());
                }
            }

            var entries = new List<CarryabilityStateMessage.CarryabilityEntry>(managed.Count * 2);
            foreach (var code in managed)
            {
                if (string.IsNullOrEmpty(code)) continue;

                var block = api.World.GetBlock(new AssetLocation(code));
                if (block == null || block.Id == 0) continue;

                var carryable = block.GetBehavior<BlockBehaviorCarryable>();
                var interact = block.GetBehavior<BlockBehaviorCarryableInteract>();

                entries.Add(new CarryabilityStateMessage.CarryabilityEntry(
                    code, BlockBehaviorCarryable.Name, Normalize(carryable?.CurrentPropertiesJson)));
                entries.Add(new CarryabilityStateMessage.CarryabilityEntry(
                    code, BlockBehaviorCarryableInteract.Name, Normalize(interact?.CurrentPropertiesJson)));
            }

            return new CarryabilityStateMessage(entries);
        }

        private static string? Normalize(string? json)
            => string.IsNullOrWhiteSpace(json) ? null : json;
    }
}
