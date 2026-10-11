using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Utility;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Client.Logic.TransformTemplates;
using Vintagestory.API.Client;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class EffectiveTransformSetting
    {
        public TransformSettings Setting { get; init; } = null!;
        public bool EnableVertexWarp { get; init; }
        public string? SourceSlotKey { get; init; }
        public bool ApplyDisplaySlotYaw { get; init; }
        public bool ApplyDisplayCaseYawOffset { get; init; }
        public bool ApplyOnDisplayTransform { get; init; }
    }

    internal sealed class CarryTransformPlanBuilder(ICoreClientAPI api, ICarryManager carryManager, CarryRenderCache cache)
    {

        internal TransformTemplateManager? TemplateManager { get; set; }

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

            var transformsGroup = transformsGroupBase;

            var primaryGroupCandidates = new List<string> { transformsGroup };
            string? matchedResolverCode = null;
            var rootCacheSig = new System.Text.StringBuilder(128);
            var attachmentCandidates = new List<CarriedGroupCandidateSet>();
            bool enableVertexWarp = false;

            // Item carries (null Block) can declare per-item pose groups (e.g. "hands-quiver")
            // via the bag-item pose templates. Seed them as the most-specific primary candidates.
            if (carried.Block == null
                && TemplateManager?.GetItemPoseGroupCandidates(carried.ItemStack) is { Length: > 0 } itemPoseGroups)
            {
                var candidates = new List<string>(itemPoseGroups);
                if (!candidates.Any(g => g.Equals(transformsGroup, StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(transformsGroup);
                primaryGroupCandidates = candidates;
            }

            // Determine resolver codes
            var rootGroupResolverCode = carryBehavior.RootGroupResolver ?? carryBehavior.TransformGroupResolver;
            var attachmentResolverCode = carryBehavior.AttachmentGroupResolver ?? carryBehavior.TransformGroupResolver;

            // Run primary resolver
            if (!string.IsNullOrEmpty(rootGroupResolverCode)
                && carryManager?.TryGetRootTransformGroupResolver(rootGroupResolverCode, out var primaryResolver) == true
                && primaryResolver != null)
            {
                if (primaryResolver.TryResolve(api, carried, transformsGroup, out var candidates)
                    && candidates != null && candidates.Count > 0)
                {
                    matchedResolverCode = primaryResolver.ResolverCode;
                    primaryGroupCandidates = [.. candidates];
                }

                var sig = primaryResolver.GetCacheSignature(api, carried, transformsGroup);
                if (!string.IsNullOrEmpty(sig))
                    rootCacheSig.Append("root=").Append(sig);
            }

            // Run attachment resolver
            if (!string.IsNullOrEmpty(attachmentResolverCode)
                && carryManager?.TryGetAttachmentTransformGroupResolver(attachmentResolverCode, out var attachmentResolver) == true
                && attachmentResolver != null)
            {
                if (attachmentResolver.TryResolve(api, carried, transformsGroup, out var result)
                    && result != null && result.Candidates.Count > 0)
                {
                    matchedResolverCode = matchedResolverCode != null
                        ? matchedResolverCode + "+" + attachmentResolver.ResolverCode
                        : attachmentResolver.ResolverCode;
                    attachmentCandidates = [.. result.Candidates];
                    enableVertexWarp = result.EnableVertexWarp;
                }

                var sig = attachmentResolver.GetCacheSignature(api, carried, transformsGroup);
                if (!string.IsNullOrEmpty(sig))
                    rootCacheSig.Append("|attachment=").Append(sig);
            }

            var combinedCacheSig = rootCacheSig.Length > 0 ? rootCacheSig.ToString() : null;

            var signature = CarrySignatureBuilder.BuildTransformPlanSignature(
                carried,
                transformsGroupBase,
                matchedResolverCode,
                combinedCacheSig);

            if (cache.TransformPlans.TryGetValue(signature, out var existing))
            {
                existing.LastUsedAtUtc = now;
                return existing;
            }

            // Cache miss: do expensive materialization only now
            var additionalSettingsList = new List<EffectiveTransformSetting>();
            if (attachmentCandidates.Count > 0)
            {
                var resolvedAdditional = ResolveAdditionalSettings(
                    carried,
                    carryBehavior,
                    attachmentCandidates,
                    enableVertexWarp);

                additionalSettingsList.AddRange(resolvedAdditional);
            }

            var resolvedPrimaryGroup = ResolvePrimaryGroupFromCandidates(
                carryBehavior,
                carried,
                primaryGroupCandidates,
                transformsGroupBase);

            var primarySettingsList = new List<EffectiveTransformSetting>();
            if (!string.IsNullOrEmpty(resolvedPrimaryGroup)
                && TemplateManager?.TryGetSettings(carried, resolvedPrimaryGroup, out var primarySettings) == true
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
                && TemplateManager?.TryGetSettings(carried, CarryCodes.DefaultTransformGroup, out var defaultSettings) == true
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

        private string ResolvePrimaryGroupFromCandidates(
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

                    var resolvedCandidate = TemplateManager?.GetTransformGroupName(carried, candidate) ?? candidate;
                    if (TemplateManager?.TransformGroupExists(carried, resolvedCandidate) == true)
                    {
                        return resolvedCandidate;
                    }
                }
            }

            var resolvedFallback = TemplateManager?.GetTransformGroupName(carried, fallbackGroup) ?? fallbackGroup;
            if (TemplateManager?.TransformGroupExists(carried, resolvedFallback) == true)
            {
                return resolvedFallback;
            }

            return fallbackGroup;
        }

        private IList<EffectiveTransformSetting> ResolveAdditionalSettings(
            CarriedBlock carried,
            BlockBehaviorCarryable carryBehavior,
            IList<CarriedGroupCandidateSet> candidateSets,
            bool enableVertexWarp)
        {
            var list = new List<EffectiveTransformSetting>();
            if (carried == null || carryBehavior == null || candidateSets == null) return list;

            foreach (var candidateSet in candidateSets)
            {
                if (candidateSet?.Groups == null || candidateSet.Groups.Count == 0) continue;

                var groupsToApply = new List<string>();
                foreach (var group in candidateSet.Groups)
                {
                    if (string.IsNullOrEmpty(group)) continue;
                    if (TemplateManager?.TransformGroupExists(carried, group) != true) continue;

                    groupsToApply.Add(group);
                    if (!candidateSet.AddAllMatches) break;
                }

                foreach (var group in groupsToApply)
                {
                    if (TemplateManager?.TryGetSettings(carried, group, out var settings) != true || settings == null) continue;

                    foreach (var setting in settings)
                    {
                        var settingClone = setting.DeepCloneWithDefaults(
                            defaultAssetName: candidateSet.AssetNameIfUnset,
                            defaultAssetType: candidateSet.AssetTypeIfUnset
                        );

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
