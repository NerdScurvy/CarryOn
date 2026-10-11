using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Client.Logic.TransformTemplates;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Client.Handlers.PackAdjustment
{
    internal sealed class PackAdjustmentTransformResolver(ICoreClientAPI api, ICarryManager? carryManager)
    {
        private readonly List<string> availableChildGroups = new();
        private int childGroupIndex = -1;
        internal string? SelectedChildGroup;

        internal TransformTemplateManager? TemplateManager { get; set; }

        internal void ClearChildGroups()
        {
            availableChildGroups.Clear();
            childGroupIndex = -1;
            SelectedChildGroup = null;
        }

        internal string ResolveTransformsGroup(
            CarriedBlock carried, BlockBehaviorCarryable carryBehavior,
            string baseTransformsGroup, TransformScope scope)
        {
            var resolvedPrimaryBaseGroup = baseTransformsGroup;
            var primaryGroupCandidates = new List<string> { baseTransformsGroup };

            // Item carries (null Block) can declare per-item pose groups (e.g. "hands-quiver") via
            // the bag-item pose templates. Seed them as the most-specific primary candidates so the
            // tool edits the group the renderer actually uses (mirrors CarryTransformPlanBuilder).
            if (carried.Block == null
                && TemplateManager?.GetItemPoseGroupCandidates(carried.ItemStack) is { Length: > 0 } itemPoseGroups)
            {
                var candidates = new List<string>(itemPoseGroups.Length + 1);
                candidates.AddRange(itemPoseGroups);
                var hasBase = false;
                foreach (var candidate in itemPoseGroups)
                {
                    if (candidate.Equals(baseTransformsGroup, StringComparison.OrdinalIgnoreCase))
                    {
                        hasBase = true;
                        break;
                    }
                }
                if (!hasBase) candidates.Add(baseTransformsGroup);
                primaryGroupCandidates = candidates;
            }

            var rootGroupResolverCode = carryBehavior.RootGroupResolver ?? carryBehavior.TransformGroupResolver;
            var attachmentResolverCode = carryBehavior.AttachmentGroupResolver ?? carryBehavior.TransformGroupResolver;

            var attachmentCandidates = new List<CarriedGroupCandidateSet>();

            // Run primary resolver
            if (!string.IsNullOrEmpty(rootGroupResolverCode)
                && carryManager?.TryGetRootTransformGroupResolver(rootGroupResolverCode, out var primaryResolver) == true
                && primaryResolver != null)
            {
                if (primaryResolver.TryResolve(api, carried, resolvedPrimaryBaseGroup, out var candidates)
                    && candidates != null && candidates.Count > 0)
                {
                    primaryGroupCandidates = new List<string>(candidates);
                    resolvedPrimaryBaseGroup = candidates[0];
                }
            }

            // Run attachment resolver
            if (!string.IsNullOrEmpty(attachmentResolverCode)
                && carryManager?.TryGetAttachmentTransformGroupResolver(attachmentResolverCode, out var attachmentResolver) == true
                && attachmentResolver != null)
            {
                if (attachmentResolver.TryResolve(api, carried, baseTransformsGroup, out var result)
                    && result != null && result.Candidates.Count > 0)
                {
                    attachmentCandidates = new List<CarriedGroupCandidateSet>(result.Candidates);
                }
            }

            var resolvedPrimaryGroup = ResolvePrimaryGroupFromCandidates(carried, carryBehavior, primaryGroupCandidates, resolvedPrimaryBaseGroup);

            if (scope == TransformScope.Parent)
                return resolvedPrimaryGroup;

            if (scope == TransformScope.Child)
            {
                if (availableChildGroups.Count == 0 || string.IsNullOrEmpty(SelectedChildGroup))
                    RefreshAvailableChildGroups(carried, carryBehavior, baseTransformsGroup);

                if (!string.IsNullOrEmpty(SelectedChildGroup) && TemplateManager?.TransformGroupExists(carried, SelectedChildGroup) == true)
                    return SelectedChildGroup;

                if (availableChildGroups.Count > 0)
                {
                    childGroupIndex = Math.Clamp(childGroupIndex, 0, availableChildGroups.Count - 1);
                    SelectedChildGroup = availableChildGroups[childGroupIndex];
                    return SelectedChildGroup;
                }
            }

            foreach (var candidateSet in attachmentCandidates)
            {
                if (candidateSet?.Groups == null) continue;
                foreach (var group in candidateSet.Groups)
                {
                    if (string.IsNullOrEmpty(group)) continue;
                    if (TemplateManager?.TransformGroupExists(carried, group) == true)
                        return group;
                }
            }

            return resolvedPrimaryGroup;
        }

        internal void RefreshAvailableChildGroups(
            CarriedBlock carried, BlockBehaviorCarryable carryBehavior, string baseTransformsGroup)
        {
            availableChildGroups.Clear();
            SelectedChildGroup = null;
            childGroupIndex = -1;

            var attachmentResolverCode = carryBehavior.AttachmentGroupResolver ?? carryBehavior.TransformGroupResolver;

            if (!string.IsNullOrEmpty(attachmentResolverCode)
                && carryManager?.TryGetAttachmentTransformGroupResolver(attachmentResolverCode, out var attachmentResolver) == true
                && attachmentResolver != null)
            {
                if (attachmentResolver.TryResolve(api, carried, baseTransformsGroup, out var result)
                    && result != null && result.Candidates.Count > 0)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var candidateSet in result.Candidates)
                    {
                        if (candidateSet?.Groups == null) continue;
                        foreach (var group in candidateSet.Groups)
                        {
                            if (string.IsNullOrEmpty(group)) continue;
                            if (TemplateManager?.TransformGroupExists(carried, group) != true) continue;
                            if (!seen.Add(group)) continue;
                            availableChildGroups.Add(group);
                        }
                    }
                }
            }

            if (availableChildGroups.Count > 0)
            {
                childGroupIndex = 0;
                SelectedChildGroup = availableChildGroups[0];
            }
        }

        internal bool AdvanceChildGroup()
        {
            if (availableChildGroups.Count == 0) return false;
            var nextIndex = childGroupIndex + 1;
            if (nextIndex >= availableChildGroups.Count) return false;
            childGroupIndex = nextIndex;
            SelectedChildGroup = availableChildGroups[childGroupIndex];
            return true;
        }

        internal string ResolvePrimaryGroupFromCandidates(
            CarriedBlock carried, BlockBehaviorCarryable carryBehavior,
            IList<string> primaryGroupCandidates, string fallbackGroup)
        {
            if (primaryGroupCandidates != null)
            {
                foreach (var candidate in primaryGroupCandidates)
                {
                    if (string.IsNullOrEmpty(candidate)) continue;
                    var resolvedCandidate = TemplateManager?.GetTransformGroupName(carried, candidate, checkExists: false) ?? candidate;
                    if (TemplateManager?.TransformGroupExists(carried, resolvedCandidate) == true)
                        return resolvedCandidate;
                }
            }

            var resolvedFallback = TemplateManager?.GetTransformGroupName(carried, fallbackGroup, checkExists: false) ?? fallbackGroup;
            if (TemplateManager?.TransformGroupExists(carried, resolvedFallback) == true)
                return resolvedFallback;

            return fallbackGroup;
        }

        internal static int GetLabelTransformCount(LabelRenderSettings? settings)
        {
            if (settings?.Transform == null) return 0;
            return 1 + (settings.AdditionalTransforms?.Count ?? 0);
        }

        internal static ModelTransform? GetLabelTransformAt(LabelRenderSettings? settings, int index)
        {
            if (settings == null || index < 0) return null;
            if (index == 0) return settings.Transform;

            var additional = settings.AdditionalTransforms;
            var additionalIndex = index - 1;
            if (additional == null || additionalIndex < 0 || additionalIndex >= additional.Count)
                return null;

            return additional[additionalIndex];
        }

        internal static void SetLabelTransformAt(LabelRenderSettings? settings, int index, ModelTransform? transform)
        {
            if (settings == null || index < 0) return;
            if (index == 0)
            {
                settings.Transform = transform;
                return;
            }

            settings.AdditionalTransforms ??= new List<ModelTransform?>();
            var additionalIndex = index - 1;
            while (settings.AdditionalTransforms.Count <= additionalIndex)
                settings.AdditionalTransforms.Add(null);

            settings.AdditionalTransforms[additionalIndex] = transform;
        }

        internal static bool HasCarriedLabel(ICoreClientAPI api, CarriedBlock? carried, BlockBehaviorCarryable? carryBehavior)
        {
            if (carried == null || carryBehavior?.LabelRenderSettings?.Transform == null)
                return false;

            var beData = carried.BlockEntityData;
            var text = beData?.GetString("text", null);
            if (!string.IsNullOrWhiteSpace(text))
                return true;

            var labelSettings = carryBehavior.LabelRenderSettings;
            if (labelSettings?.IconFromInventory == true && beData?["inventory"] is TreeAttribute inventory && inventory["slots"] is TreeAttribute slots)
            {
                foreach (var slotValue in slots.Values)
                {
                    if (slotValue is ItemstackAttribute itemAttr)
                    {
                        var stack = itemAttr.GetValue() as ItemStack;
                        if (stack?.Collectible != null)
                            return true;
                    }
                }
            }

            var labelStack = beData?.GetItemstack("labelStack", null);
            labelStack?.ResolveBlockOrItem(api.World);
            return labelStack?.Collectible != null;
        }
    }
}
