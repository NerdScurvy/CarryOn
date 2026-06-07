using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Common.Handlers.PackAdjustment
{
    internal sealed class PackAdjustmentTransformResolver
    {
        private readonly ICoreClientAPI api;
        private readonly ICarryManager? carryManager;

        private readonly List<string> availableChildGroups = new();
        private int childGroupIndex = -1;
        internal string? SelectedChildGroup;

        internal IReadOnlyList<string> AvailableChildGroups => availableChildGroups;
        internal int ChildGroupIndex => childGroupIndex;

        internal PackAdjustmentTransformResolver(ICoreClientAPI api, ICarryManager? carryManager)
        {
            this.api = api;
            this.carryManager = carryManager;
        }

        internal void ClearChildGroups()
        {
            availableChildGroups.Clear();
            childGroupIndex = -1;
            SelectedChildGroup = null;
        }

        internal string ResolveTransformsGroup(
            CarriedBlock carried, BlockBehaviorCarryable carryBehavior,
            string baseTransformsGroup, PackAdjustmentHandler.TransformScope scope)
        {
            var resolvedBaseGroup = baseTransformsGroup;
            CarriedGroupResolution? resolverResolution = null;
            var primaryGroupCandidates = new List<string> { baseTransformsGroup };
            var requestedResolverCode = carryBehavior.TransformGroupResolver;

            if (!string.IsNullOrEmpty(requestedResolverCode)
                && carryManager?.TryGetTransformGroupResolver(requestedResolverCode, out var resolver) == true && resolver != null)
            {
                if (resolver.TryResolve(api, carried, resolvedBaseGroup, out var resolution) && resolution != null)
                {
                    resolverResolution = resolution;
                    if (resolution.PrimaryGroupCandidates != null && resolution.PrimaryGroupCandidates.Count > 0)
                    {
                        primaryGroupCandidates = new List<string>(resolution.PrimaryGroupCandidates);
                        resolvedBaseGroup = resolution.PrimaryGroupCandidates[0];
                    }
                    else if (!string.IsNullOrEmpty(resolution.PrimaryGroup))
                    {
                        primaryGroupCandidates = new List<string> { resolution.PrimaryGroup };
                        resolvedBaseGroup = resolution.PrimaryGroup;
                    }
                }
            }

            var resolvedPrimaryGroup = ResolvePrimaryGroupFromCandidates(carried, carryBehavior, primaryGroupCandidates, resolvedBaseGroup);

            if (scope == PackAdjustmentHandler.TransformScope.Parent)
                return resolvedPrimaryGroup;

            if (scope == PackAdjustmentHandler.TransformScope.Child)
            {
                if (availableChildGroups.Count == 0 || string.IsNullOrEmpty(SelectedChildGroup))
                    RefreshAvailableChildGroups(carried, carryBehavior, baseTransformsGroup);

                if (!string.IsNullOrEmpty(SelectedChildGroup) && carryBehavior.TransformGroupExists(carried, SelectedChildGroup))
                    return SelectedChildGroup;

                if (availableChildGroups.Count > 0)
                {
                    childGroupIndex = Math.Clamp(childGroupIndex, 0, availableChildGroups.Count - 1);
                    SelectedChildGroup = availableChildGroups[childGroupIndex];
                    return SelectedChildGroup;
                }
            }

            if (resolverResolution?.AdditionalGroupCandidates != null)
            {
                foreach (var candidateSet in resolverResolution.AdditionalGroupCandidates)
                {
                    if (candidateSet?.Groups == null) continue;
                    foreach (var group in candidateSet.Groups)
                    {
                        if (string.IsNullOrEmpty(group)) continue;
                        if (carryBehavior.TransformGroupExists(carried, group))
                            return group;
                    }
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

            CarriedGroupResolution? resolverResolution = null;
            var requestedResolverCode = carryBehavior.TransformGroupResolver;

            if (!string.IsNullOrEmpty(requestedResolverCode)
                && carryManager?.TryGetTransformGroupResolver(requestedResolverCode, out var resolver) == true && resolver != null)
            {
                if (resolver.TryResolve(api, carried, baseTransformsGroup, out var resolution) && resolution != null)
                    resolverResolution = resolution;
            }

            if (resolverResolution?.AdditionalGroupCandidates == null)
                return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidateSet in resolverResolution.AdditionalGroupCandidates)
            {
                if (candidateSet?.Groups == null) continue;
                foreach (var group in candidateSet.Groups)
                {
                    if (string.IsNullOrEmpty(group)) continue;
                    if (!carryBehavior.TransformGroupExists(carried, group)) continue;
                    if (!seen.Add(group)) continue;
                    availableChildGroups.Add(group);
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

        internal static string ResolvePrimaryGroupFromCandidates(
            CarriedBlock carried, BlockBehaviorCarryable carryBehavior,
            IList<string> primaryGroupCandidates, string fallbackGroup)
        {
            if (primaryGroupCandidates != null)
            {
                foreach (var candidate in primaryGroupCandidates)
                {
                    if (string.IsNullOrEmpty(candidate)) continue;
                    var resolvedCandidate = carryBehavior.GetTransformGroupName(carried, candidate, checkExists: false) ?? candidate;
                    if (carryBehavior.TransformGroupExists(carried, resolvedCandidate))
                        return resolvedCandidate;
                }
            }

            var resolvedFallback = carryBehavior.GetTransformGroupName(carried, fallbackGroup, checkExists: false) ?? fallbackGroup;
            if (carryBehavior.TransformGroupExists(carried, resolvedFallback))
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