using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Client;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class EffectiveTransformSetting
    {
        public TransformSettings Setting { get; init; }
        public bool EnableVertexWarp { get; init; }
        public string SourceSlotKey { get; init; }
        public bool ApplyDisplaySlotYaw { get; init; }
        public bool ApplyDisplayCaseYawOffset { get; init; }
        public bool ApplyOnDisplayTransform { get; init; }
    }

    internal sealed class CarryTransformPlanBuilder
    {
        private readonly ICoreClientAPI api;
        private readonly CarrySystem carrySystem;
        private readonly CarryRenderCache cache;

        internal CarryTransformPlanBuilder(ICoreClientAPI api, CarrySystem carrySystem, CarryRenderCache cache)
        {
            this.api = api;
            this.carrySystem = carrySystem;
            this.cache = cache;
        }

        internal CachedTransformPlan GetOrBuild(CarriedBlock carried, string transformsGroupBase)
        {
            var now = DateTime.UtcNow;
            var carryBehavior = carried.GetCarryableBehavior();
            if (carryBehavior == null)
            {
                return new CachedTransformPlan
                {
                    Signature = "nocarry",
                    EffectiveSettings = Array.Empty<EffectiveTransformSetting>(),
                    ResolvedPrimaryGroup = transformsGroupBase,
                    RenderRootFirst = false,
                    LastUsedAtUtc = now
                };
            }

            var carriedSlot = carryBehavior.Slots[carried.Slot];
            var transformsGroup = transformsGroupBase;

            var primaryGroupCandidates = new List<string> { transformsGroup };
            string matchedResolverCode = null;
            string resolverCacheSignature = null;
            CarriedGroupResolution matchedResolution = null;

            var transformGroupResolvers = carrySystem?.CarryManager?.GetTransformGroupResolvers();
            if (transformGroupResolvers != null)
            {
                var requestedResolverCode = carryBehavior.RenderTransformResolver;
                foreach (var resolver in transformGroupResolvers)
                {
                    if (resolver == null) continue;

                    if (!string.IsNullOrEmpty(requestedResolverCode)
                        && !requestedResolverCode.Equals(resolver.ResolverCode, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!resolver.TryResolve(this.api, carried, transformsGroup, out var resolution) || resolution == null)
                    {
                        continue;
                    }

                    matchedResolverCode = resolver.ResolverCode;
                    resolverCacheSignature = resolver.GetCacheSignature(this.api, carried, transformsGroup, resolution);
                    matchedResolution = resolution;

                    if (resolution.PrimaryGroupCandidates != null && resolution.PrimaryGroupCandidates.Count > 0)
                    {
                        primaryGroupCandidates = new List<string>(resolution.PrimaryGroupCandidates);
                    }
                    else if (!string.IsNullOrEmpty(resolution.PrimaryGroup))
                    {
                        primaryGroupCandidates = new List<string> { resolution.PrimaryGroup };
                    }

                    break;
                }
            }

            var signature = CarryRenderHelpers.BuildTransformPlanSignature(
                carried,
                transformsGroupBase,
                matchedResolverCode,
                resolverCacheSignature);

            if (cache.TransformPlans.TryGetValue(signature, out var existing))
            {
                existing.LastUsedAtUtc = now;
                return existing;
            }

            // Cache miss: do expensive materialization only now
            var additionalSettingsList = new List<EffectiveTransformSetting>();
            if (matchedResolution?.AdditionalGroupCandidates != null && matchedResolution.AdditionalGroupCandidates.Count > 0)
            {
                var resolvedAdditional = ResolveAdditionalSettings(
                    carried,
                    carryBehavior,
                    carriedSlot,
                    matchedResolution.AdditionalGroupCandidates,
                    matchedResolution.EnableVertexWarpForAdditionalTransforms);

                additionalSettingsList.AddRange(resolvedAdditional);
            }

            var resolvedPrimaryGroup = ResolvePrimaryGroupFromCandidates(
                carryBehavior,
                carried,
                primaryGroupCandidates,
                transformsGroupBase);

            var primarySettingsList = new List<EffectiveTransformSetting>();
            if (!string.IsNullOrEmpty(resolvedPrimaryGroup)
                && carryBehavior.ResolvedTransformGroups.TryGetValue(resolvedPrimaryGroup, out var primarySettings)
                && primarySettings != null)
            {
                foreach (var setting in primarySettings)
                {
                    primarySettingsList.Add(new EffectiveTransformSetting
                    {
                        Setting = setting,
                        EnableVertexWarp = false,
                        SourceSlotKey = null
                    });
                }
            }

            var allSettings = new List<EffectiveTransformSetting>();
            if (primarySettingsList.Count > 0) allSettings.AddRange(primarySettingsList);
            if (additionalSettingsList.Count > 0) allSettings.AddRange(additionalSettingsList);

            if (allSettings.Count == 0
                && carryBehavior.ResolvedTransformGroups.TryGetValue("default", out var defaultSettings)
                && defaultSettings != null)
            {
                foreach (var setting in defaultSettings)
                {
                    allSettings.Add(new EffectiveTransformSetting
                    {
                        Setting = setting,
                        EnableVertexWarp = false
                    });
                }
            }

            api.Logger.Debug(
                "[CarryOn] Transform plan cache miss: block={0}, slot={1}, resolver={2}, sig={3}",
                carried?.Block?.Code,
                carried?.Slot,
                matchedResolverCode ?? "none",
                signature);

            var plan = new CachedTransformPlan
            {
                Signature = signature,
                EffectiveSettings = allSettings.ToArray(),
                ResolvedPrimaryGroup = resolvedPrimaryGroup,
                RenderRootFirst = carryBehavior.RenderRootFirst,
                LastUsedAtUtc = now
            };

            cache.TransformPlans[signature] = plan;
            return plan;
        }

        private static string ResolvePrimaryGroupFromCandidates(
            BlockBehaviorCarryable carryBehavior,
            CarriedBlock carried,
            IList<string> primaryGroupCandidates,
            string fallbackGroup)
        {
            if (carryBehavior == null)
            {
                return fallbackGroup;
            }

            if (primaryGroupCandidates != null)
            {
                foreach (var candidate in primaryGroupCandidates)
                {
                    if (string.IsNullOrEmpty(candidate)) continue;

                    var resolvedCandidate = carryBehavior.GetTransformGroupName(carried, candidate) ?? candidate;
                    if (carryBehavior.TransformGroupExists(carried, resolvedCandidate))
                    {
                        return resolvedCandidate;
                    }
                }
            }

            var resolvedFallback = carryBehavior.GetTransformGroupName(carried, fallbackGroup) ?? fallbackGroup;
            if (carryBehavior.TransformGroupExists(carried, resolvedFallback))
            {
                return resolvedFallback;
            }

            return fallbackGroup;
        }

        private static IList<EffectiveTransformSetting> ResolveAdditionalSettings(
            CarriedBlock carried,
            BlockBehaviorCarryable carryBehavior,
            BlockBehaviorCarryable.SlotSettings carriedSlot,
            IList<CarriedGroupCandidateSet> candidateSets,
            bool enableVertexWarp)
        {
            var list = new List<EffectiveTransformSetting>();
            if (carried == null || carryBehavior == null || carriedSlot == null || candidateSets == null) return list;

            foreach (var candidateSet in candidateSets)
            {
                if (candidateSet?.Groups == null || candidateSet.Groups.Count == 0) continue;

                var groupsToApply = new List<string>();
                foreach (var group in candidateSet.Groups)
                {
                    if (string.IsNullOrEmpty(group)) continue;
                    if (!carryBehavior.TransformGroupExists(carried, group)) continue;

                    groupsToApply.Add(group);
                    if (!candidateSet.AddAllMatches) break;
                }

                foreach (var group in groupsToApply)
                {
                    if (!carryBehavior.ResolvedTransformGroups.TryGetValue(group, out var settings) || settings == null) continue;

                    foreach (var setting in settings)
                    {
                        var settingClone = setting.Clone();
                        if (settingClone.AssetType == EnumAssetType.None
                            && !string.IsNullOrEmpty(candidateSet.AssetNameIfUnset)
                            && candidateSet.AssetTypeIfUnset != CarriedGroupAssetType.None)
                        {
                            settingClone.AssetName = candidateSet.AssetNameIfUnset;
                            settingClone.AssetType = candidateSet.AssetTypeIfUnset == CarriedGroupAssetType.Item
                                ? EnumAssetType.Item
                                : EnumAssetType.Block;
                        }

                        list.Add(new EffectiveTransformSetting
                        {
                            Setting = settingClone,
                            EnableVertexWarp = enableVertexWarp,
                            SourceSlotKey = candidateSet.SourceSlotKey,
                            ApplyDisplaySlotYaw = candidateSet.ApplyDisplaySlotYaw,
                            ApplyDisplayCaseYawOffset = candidateSet.ApplyDisplayCaseYawOffset,
                            ApplyOnDisplayTransform = candidateSet.ApplyOnDisplayTransform
                        });
                    }
                }
            }

            return list;
        }
    }
}
