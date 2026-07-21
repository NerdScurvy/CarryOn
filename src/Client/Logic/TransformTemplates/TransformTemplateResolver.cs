using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Client;

namespace CarryOn.Client.Logic.TransformTemplates
{
    /// <summary>
    /// Resolves and flattens transform group definitions by processing inheritance chains,
    /// applying pre-resolve adjustments, overrides, and relative adjustments to produce
    /// a final dictionary of transform group names to resolved TransformSettings arrays.
    /// </summary>
    public sealed class TransformTemplateResolver
    {
        private readonly ICoreClientAPI capi;

        public TransformTemplateResolver(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        /// <summary>
        /// Resolves and flattens transform groups based on the provided template definitions and inheritance. 
        /// It processes the inheritance chain defined by the "extends" property, applies overrides from groups with "@" prefix, applies 
        /// relative adjustments from groups with "~" prefix, and produces a final flattened dictionary of transform group names to arrays 
        /// of TransformSettings that can be used for rendering or other logic.
        /// </summary>
        /// <param name="templatesByCode"> A dictionary mapping template codes to their corresponding transform group definitions. </param>
        /// <param name="templateCodes"> A list of template codes to be processed. </param>
        /// <param name="localTransformGroups"> Optional local transform groups that can override or extend the templates. </param>
        /// <returns> A dictionary mapping transform group names to arrays of resolved TransformSettings. </returns>
        public Dictionary<string, TransformSettings[]> ResolveAndFlatten(
            IDictionary<string, Dictionary<string, TransformGroup?>> templatesByCode,
            IList<string> templateCodes,
            Dictionary<string, TransformGroup?>? localTransformGroups = null)
        {
            var mergedDefinitions = MergeDefinitions(templatesByCode, templateCodes, localTransformGroups);

            ApplyCaretPrefixedPreResolveAdjustments(mergedDefinitions);

            var resolved = new Dictionary<string, List<TransformGroupSettings>>(StringComparer.OrdinalIgnoreCase);
            var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var groupName in mergedDefinitions.Keys.ToList())
            {
                ResolveGroupRecursive(groupName, mergedDefinitions, resolved, resolving);
            }

            ApplyAtPrefixedOverrides(resolved);
            ApplyTildePrefixedAdjustments(resolved);

            return FlattenResolved(resolved);
        }

        /// <summary>
        /// Merges transform group definitions from the specified template codes and local definitions. 
        /// It combines the transform groups from the templates in the order they are specified, allowing later templates to override earlier
        /// ones, and then applies local definitions on top of the merged template definitions.
        /// </summary>
        /// <param name="templatesByCode"> A dictionary mapping template codes to their corresponding transform group definitions. </param>
        /// <param name="templateCodes"> A list of template codes to be processed. </param>
        /// <param name="localTransformGroups"> Optional local transform groups that can override or extend the templates. </param>
        /// <returns> A dictionary mapping transform group names to their merged TransformGroup definitions. </returns>
        public Dictionary<string, TransformGroup?> MergeDefinitions(
            IDictionary<string, Dictionary<string, TransformGroup?>> templatesByCode,
            IList<string> templateCodes,
            Dictionary<string, TransformGroup?>? localTransformGroups)
        {
            var merged = new Dictionary<string, TransformGroup?>(StringComparer.OrdinalIgnoreCase);

            if (templateCodes != null)
            {
                foreach (var code in templateCodes)
                {
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    var key = code.ToLowerInvariant();

                    if (!templatesByCode.TryGetValue(key, out var groups) || groups == null) continue;

                    foreach (var kv in groups)
                    {
                        if (kv.Value != null) merged[kv.Key] = kv.Value.DeepClone();
                    }
                }
            }

            if (localTransformGroups != null)
            {
                foreach (var kv in localTransformGroups)
                {
                    if (kv.Value != null) merged[kv.Key] = kv.Value.DeepClone();
                }
            }

            return merged;
        }

        /// <summary>
        /// Recursively resolves a transform group by processing its inheritance chain defined by the "extends" property.
        /// </summary>
        /// <param name="groupName"> The name of the transform group to resolve. </param>
        /// <param name="definitions"> A dictionary of all available transform group definitions. </param>
        /// <param name="resolved"> A dictionary to store resolved transform groups. </param>
        /// <param name="resolving"> A set to track currently resolving groups to detect cycles. </param>
        /// <returns> A list of resolved TransformGroupSettings for the specified group. </returns>
        private List<TransformGroupSettings>? ResolveGroupRecursive(
            string groupName,
            IDictionary<string, TransformGroup?> definitions,
            IDictionary<string, List<TransformGroupSettings>> resolved,
            ISet<string> resolving)
        {
            if (resolved.TryGetValue(groupName, out var existing))
            {
                return existing;
            }

            if (!definitions.TryGetValue(groupName, out var group) || group == null)
            {
                return null;
            }

            if (!resolving.Add(groupName))
            {
                capi.Logger.Warning($"CarryOn: transformGroups inheritance cycle detected at '{groupName}'.");
                return null;
            }

            try
            {
                var merged = new List<TransformGroupSettings>();

                // Inherit parent first
                if (!string.IsNullOrWhiteSpace(group.ExtendsGroup))
                {
                    var parent = ResolveGroupRecursive(group.ExtendsGroup, definitions, resolved, resolving);
                    if (parent == null)
                    {
                        capi.Logger.Warning($"CarryOn: transform group '{groupName}' extends missing/invalid group '{group.ExtendsGroup}'.");
                    }
                    else
                    {
                        merged.AddRange(parent.Select(s => s.DeepClone()));
                    }
                }

                // Then apply base, then overrides, then append
                ApplyUpsertById(merged, group.Base);
                ApplyUpsertById(merged, group.Overrides);

                if (group.Appends != null)
                {
                    foreach (var s in group.Appends)
                    {
                        if (s == null) continue;
                        merged.Add(s.DeepClone());
                    }
                }

                resolved[groupName] = merged;
                return merged;
            }
            finally
            {
                resolving.Remove(groupName);
            }
        }

        /// <summary>
        /// Applies overrides from groups with "@" prefix to the groups they target (e.g. "@groupA" overrides apply to "groupA"). The "@" groups themselves are not included in the final resolved output.
        /// </summary>
        /// <param name="target"> The list of target TransformGroupSettings to apply overrides to. </param>
        /// <param name="incoming"> The list of incoming TransformGroupSettings containing overrides. </param>
        private static void ApplyUpsertById(List<TransformGroupSettings> target, IReadOnlyList<TransformGroupSettings>? incoming)
        {
            if (incoming == null) return;

            foreach (var s in incoming)
            {
                if (s == null) continue;

                if (!string.IsNullOrWhiteSpace(s.Id))
                {
                    var idx = target.FindIndex(x =>
                        !string.IsNullOrWhiteSpace(x?.Id) &&
                        x.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase));

                    if (idx >= 0)
                    {
                        target[idx] = target[idx]?.MergeOverlay(s) ?? s?.DeepClone()!;
                        continue;
                    }
                }

                target.Add(s.DeepClone());
            }
        }

        /// <summary>
        /// Processes prefixed transform groups (with "@" or "~" prefix) against the resolved dictionary.
        /// Handles the common pattern of: find prefixed keys, validate source, strip prefix, validate target, apply operation, remove prefix entry.
        /// </summary>
        private void ProcessPrefixedGroups(
            IDictionary<string, List<TransformGroupSettings>> groups,
            char prefix,
            string logLabel,
            Action<string, List<TransformGroupSettings>, List<TransformGroupSettings>, string> apply)
        {
            if (groups == null || groups.Count == 0) return;

            var prefixedNames = groups.Keys
                .Where(name => !string.IsNullOrWhiteSpace(name) && name.StartsWith(prefix.ToString(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var prefixedName in prefixedNames)
            {
                if (!groups.TryGetValue(prefixedName, out var source) || source == null)
                {
                    groups.Remove(prefixedName);
                    continue;
                }

                var targetName = prefixedName[1..];
                if (string.IsNullOrWhiteSpace(targetName))
                {
                    capi.Logger.Warning($"CarryOn: transform group {logLabel} '{prefixedName}' has no target group name.");
                    groups.Remove(prefixedName);
                    continue;
                }

                if (!groups.TryGetValue(targetName, out var target) || target == null)
                {
                    capi.Logger.Warning($"CarryOn: transform group {logLabel} '{prefixedName}' has no matching target group '{targetName}'.");
                    groups.Remove(prefixedName);
                    continue;
                }

                apply(prefixedName, source, target, targetName);
                groups.Remove(prefixedName);
            }
        }

        /// <summary>
        /// Applies overrides from groups with "@" prefix to the groups they target (e.g. "@groupA" overrides apply to "groupA"). 
        /// </summary>
        private void ApplyAtPrefixedOverrides(IDictionary<string, List<TransformGroupSettings>> resolved)
        {
            ProcessPrefixedGroups(resolved, '@', "override",
                (_, source, target, _) => ApplyUpsertById(target, source));
        }

        /// <summary> 
        /// Applies relative adjustments from groups with "~" prefix to the groups they target (e.g. "~groupA" adjustments apply to "groupA"). 
        /// </summary>
        private void ApplyTildePrefixedAdjustments(IDictionary<string, List<TransformGroupSettings>> resolved)
        {
            ProcessPrefixedGroups(resolved, '~', "relative adjustment",
                (prefixedName, source, target, targetGroupName) =>
                    ApplyRelativeAdjustments(target, source, prefixedName, targetGroupName));
        }

        /// <summary>
        /// Enumerates all TransformGroupSettings entries from the base, overrides, and appends of a TransformGroup in that order.
        /// </summary>
        /// <param name="group"> The TransformGroup to enumerate entries from. </param>
        /// <returns> An enumerable of TransformGroupSettings entries. </returns>
        private static IEnumerable<TransformGroupSettings> EnumeratePatchEntries(TransformGroup group)
        {
            if (group?.Base != null) foreach (var s in group.Base) if (s != null) yield return s;
            if (group?.Overrides != null) foreach (var s in group.Overrides) if (s != null) yield return s;
            if (group?.Appends != null) foreach (var s in group.Appends) if (s != null) yield return s;
        }
 
        /// <summary>
        /// Applies relative adjustments from groups with "^" prefix to the groups they target (e.g. "^groupA" adjustments apply to "groupA"). 
        /// The "^" groups themselves are not included in the final resolved output.
        /// </summary>
        /// <param name="definitions"> A dictionary mapping transform group names to their TransformGroup definitions. </param>
        private void ApplyCaretPrefixedPreResolveAdjustments(IDictionary<string, TransformGroup?> definitions)
        {
            if (definitions == null || definitions.Count == 0) return;

            var patchGroupNames = definitions.Keys
                .Where(name => !string.IsNullOrWhiteSpace(name) && name.StartsWith("^", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var patchGroupName in patchGroupNames)
            {
                if (!definitions.TryGetValue(patchGroupName, out var patchGroup) || patchGroup == null)
                {
                    definitions.Remove(patchGroupName);
                    continue;
                }

                var targetGroupName = patchGroupName[1..];
                if (string.IsNullOrWhiteSpace(targetGroupName))
                {
                    capi.Logger.Warning($"CarryOn: transform group pre-adjustment '{patchGroupName}' has no target group name.");
                    definitions.Remove(patchGroupName);
                    continue;
                }

                if (!definitions.TryGetValue(targetGroupName, out var targetGroup) || targetGroup == null)
                {
                    capi.Logger.Warning($"CarryOn: transform group pre-adjustment '{patchGroupName}' has no matching target group '{targetGroupName}'.");
                    definitions.Remove(patchGroupName);
                    continue;
                }

                // Incoming entries come from base/overrides/appends of the ^ group, in that order.
                var incoming = EnumeratePatchEntries(patchGroup);
                definitions[targetGroupName] = ApplyRelativeAdjustmentsToDefinition(targetGroup, incoming, patchGroupName, targetGroupName);

                // ^ groups are control groups only; do not resolve/flatten them.
                definitions.Remove(patchGroupName);
            }
        }

        /// <summary>
        /// Applies relative adjustments from groups with "^" prefix to the groups they target (e.g. "^groupA" adjustments apply to "groupA"). 
        /// The "^" groups themselves are not included in the final resolved output.
        /// </summary>
        /// <param name="targetGroup"> The target TransformGroup to apply the relative adjustments to. </param>
        /// <param name="incoming"> The incoming TransformGroupSettings containing the relative adjustments. </param>
        /// <param name="patchGroupName"> The name of the patch group (with "^" prefix). </param>
        /// <param name="targetGroupName"> The name of the target group. </param>
        private TransformGroup? ApplyRelativeAdjustmentsToDefinition(
            TransformGroup targetGroup,
            IEnumerable<TransformGroupSettings> incoming,
            string patchGroupName,
            string targetGroupName)
        {
            if (targetGroup == null || incoming == null) return targetGroup;

            // Work on local copies of the lists
            List<TransformGroupSettings> baseList = targetGroup.Base?.ToList() ?? new List<TransformGroupSettings>();
            List<TransformGroupSettings> overridesList = targetGroup.Overrides?.ToList() ?? new List<TransformGroupSettings>();
            List<TransformGroupSettings> appendsList = targetGroup.Appends?.ToList() ?? new List<TransformGroupSettings>();

            foreach (var patch in incoming)
            {
                if (patch == null) continue;

                if (!string.IsNullOrWhiteSpace(patch.Id))
                {
                    if (TryMergeRelativeById(baseList, patch)) continue;
                    if (TryMergeRelativeById(overridesList, patch)) continue;
                    if (TryMergeRelativeById(appendsList, patch)) continue;

                    // If id not present in target group definition, append to overrides.
                    overridesList.Add(patch.DeepClone());
                    continue;
                }

                // No id: allow only if target definition has exactly one entry total.
                var total = baseList.Count + overridesList.Count + appendsList.Count;
                if (total == 1)
                {
                    if (TryMergeRelativeFirst(baseList, patch)) continue;
                    if (TryMergeRelativeFirst(overridesList, patch)) continue;
                    if (TryMergeRelativeFirst(appendsList, patch)) continue;
                }

                capi.Logger.Warning($"CarryOn: transform group pre-adjustment '{patchGroupName}' has entry without id, but target group '{targetGroupName}' cannot be uniquely matched.");
            }

            // Return a new record with updated lists
            return targetGroup with
            {
                Base = baseList,
                Overrides = overridesList,
                Appends = appendsList
            };
        }
 


        /// <summary>
        /// Applies relative adjustments from groups with "~" prefix to the groups they target (e.g. "~groupA" adjustments apply to "groupA"). 
        /// The "~" groups themselves are not included in the final resolved output.
        /// </summary>
        /// <param name="target"> The target list of TransformGroupSettings to which the relative adjustments will be applied. </param>
        /// <param name="incoming"> The list of incoming TransformGroupSettings containing the relative adjustments. </param>
        /// <param name="relativeGroupName"> The name of the relative group (prefixed with "~"). </param>
        /// <param name="targetGroupName"> The name of the target group to which the adjustments will be applied. </param>
        private void ApplyRelativeAdjustments(
            List<TransformGroupSettings> target,
            IList<TransformGroupSettings> incoming,
            string relativeGroupName,
            string targetGroupName)
        {
            if (incoming == null) return;

            foreach (var s in incoming)
            {
                if (s == null) continue;

                if (!string.IsNullOrWhiteSpace(s.Id))
                {
                    var idx = target.FindIndex(x =>
                        !string.IsNullOrWhiteSpace(x?.Id) &&
                        x.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase));

                    if (idx < 0)
                    {
                        capi.Logger.Warning($"CarryOn: transform group relative adjustment '{relativeGroupName}' could not find id '{s.Id}' in target group '{targetGroupName}'.");
                        continue;
                    }

                    target[idx] = target[idx]?.MergeRelative(s) ?? s.DeepClone();

                    continue;
                }

                if (target.Count == 1)
                {
                    target[0] = target[0]?.MergeRelative(s) ?? s.DeepClone();
                    continue;
                }

                capi.Logger.Warning($"CarryOn: transform group relative adjustment '{relativeGroupName}' contains an entry without id, but target group '{targetGroupName}' has {target.Count} entries.");
            }
        }

        /// <summary>
        /// Flattens the resolved transform groups into a dictionary mapping group names to arrays of TransformSettings. 
        /// This is the final output format that can be used for rendering or other logic that requires the resolved transform groups.
        /// </summary>
        /// <param name="resolved"> The dictionary of resolved transform groups. </param>
        /// <returns> A dictionary mapping group names to arrays of TransformSettings. </returns>
        private static Dictionary<string, TransformSettings[]> FlattenResolved(
            IDictionary<string, List<TransformGroupSettings>> resolved)
        {
            var flattened = new Dictionary<string, TransformSettings[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in resolved)
            {
                var list = new List<TransformSettings>();
                foreach (var s in kv.Value)
                {
                    if (s != null) list.Add(s.ToTransformSettings(BlockBehaviorCarryable.DefaultBlockTransform));
                }
                flattened[kv.Key] = list.ToArray();
            }

            return flattened;
        }

        /// <summary>
        /// Attempts to merge a relative adjustment TransformGroupSettings into a list of TransformGroupSettings based on matching "id" properties.
        /// If an entry in the list has an "id" that matches the "id" of the patch, it will be merged as a relative adjustment. 
        /// The method returns true if a merge was performed, or false if no matching id was found.
        /// </summary>
        /// <param name="list"> The list of TransformGroupSettings to merge into. </param>
        /// <param name="patch"> The TransformGroupSettings containing the relative adjustments. </param>
        /// <returns> True if a merge was performed, false otherwise. </returns>
        private static bool TryMergeRelativeById(IList<TransformGroupSettings>? list, TransformGroupSettings patch)
        {
            if (list == null || patch == null || string.IsNullOrWhiteSpace(patch.Id))
            {
                return false;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var current = list[i];
                if (string.IsNullOrWhiteSpace(current?.Id))
                {
                    continue;
                }

                if (!current.Id.Equals(patch.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                list[i] = current.MergeRelative(patch);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to merge a relative adjustment TransformGroupSettings into the first entry of a list of TransformGroupSettings if the patch does not have an "id" and the list has exactly one entry.
        /// </summary>
        /// <param name="list"> The list of TransformGroupSettings to merge into. </param>
        /// <param name="patch"> The TransformGroupSettings containing the relative adjustments. </param>
        /// <returns> True if a merge was performed, false otherwise. </returns>
        private static bool TryMergeRelativeFirst(IList<TransformGroupSettings>? list, TransformGroupSettings patch)
        {
            if (list == null || patch == null || list.Count == 0)
            {
                return false;
            }

            list[0] = (list[0]?.MergeRelative(patch)) ?? patch.DeepClone();
            return true;
        }      
    }
}