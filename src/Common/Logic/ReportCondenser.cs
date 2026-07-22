using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Utility;

namespace CarryOn.Common.Logic
{
    internal static class ReportCondenser
    {
        private static readonly HashSet<string> CardinalDirections = new(StringComparer.OrdinalIgnoreCase)
            { "north", "east", "south", "west" };

        /// <summary>Condense cardinal-direction side variants (north/east/south/west) into a single canonical row.</summary>
        public static List<ReportRow> CondenseSides(List<ReportRow> rows)
        {
            var sideKeys = FindSideVariantKeys(rows);
            if (sideKeys.Count == 0) return rows;

            // Group by (condensationKey, CarryType, Slot), preserving first-seen order
            var groups = CollectionHelper.GroupByFirstIndex(rows,
                row => (BuildCondensationKey(row, sideKeys), row.CarryType, row.Slot));

            // Build result, condensing groups where all modifier values match
            var result = new List<ReportRow>();
            foreach (var group in groups.Values.OrderBy(g => g.FirstIndex))
            {
                if (group.Rows.Count <= 1 || !AllModifiersEqual(group.Rows))
                {
                    result.AddRange(group.Rows);
                    continue;
                }

                var condensed = group.Rows[0];
                condensed.BlockCode = BuildCanonicalBlockCode(condensed, sideKeys);
                result.Add(condensed);
            }

            return result;
        }

        private static HashSet<string> FindSideVariantKeys(List<ReportRow> rows)
        {
            var cardinalValuesByKey = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (row.Variants == null) continue;
                foreach (var kvp in row.Variants)
                {
                    if (!CardinalDirections.Contains(kvp.Value)) continue;
                    if (!cardinalValuesByKey.TryGetValue(kvp.Key, out var values))
                    {
                        values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        cardinalValuesByKey[kvp.Key] = values;
                    }
                    values.Add(kvp.Value);
                }
            }

            var sideKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in cardinalValuesByKey)
            {
                if (kvp.Value.Count >= 2)
                    sideKeys.Add(kvp.Key);
            }
            return sideKeys;
        }

        private static string BuildCondensationKey(ReportRow row, HashSet<string> sideKeys)
        {
            if (row.Variants == null || row.Variants.Count == 0 || sideKeys.Count == 0)
                return row.BlockCode;

            var colon = row.BlockCode.IndexOf(':');
            var domain = row.BlockCode[..(colon + 1)];
            var path = row.BlockCode[(colon + 1)..];
            var segments = path.Split('-');

            if (row.Variants.Count > segments.Length)
                return row.BlockCode;

            var variantSegments = segments[^row.Variants.Count..];
            var prefixSegments = segments[..^row.Variants.Count];

            var i = 0;
            var filtered = new List<string>(prefixSegments);
            foreach (var kvp in row.Variants)
            {
                if (!sideKeys.Contains(kvp.Key))
                    filtered.Add(variantSegments[i]);
                i++;
            }

            return domain + string.Join("-", filtered);
        }

        private static string BuildCanonicalBlockCode(ReportRow row, HashSet<string> sideKeys)
        {
            if (row.Variants == null || row.Variants.Count == 0 || sideKeys.Count == 0)
                return row.BlockCode;

            var colon = row.BlockCode.IndexOf(':');
            var domain = row.BlockCode[..(colon + 1)];
            var path = row.BlockCode[(colon + 1)..];
            var segments = path.Split('-');

            if (row.Variants.Count > segments.Length)
                return row.BlockCode;

            var variantSegments = segments[^row.Variants.Count..];
            var prefixSegments = segments[..^row.Variants.Count];

            var i = 0;
            var newVariantSegments = new string[variantSegments.Length];
            foreach (var kvp in row.Variants)
            {
                newVariantSegments[i] = sideKeys.Contains(kvp.Key) ? "east" : variantSegments[i];
                i++;
            }

            var allSegments = prefixSegments.Concat(newVariantSegments).ToArray();
            return domain + string.Join("-", allSegments);
        }

        /// <summary>Merge rows with the same BlockCode and Slot that share identical modifier signatures into a single row with comma-delimited types.</summary>
        public static List<ReportRow> CondenseTypes(List<ReportRow> rows)
        {
            var groups = CollectionHelper.GroupByFirstIndex(rows, (ReportRow row) => (row.BlockCode, row.Slot));

            var result = new List<ReportRow>();
            foreach (var group in groups.Values.OrderBy(g => g.FirstIndex))
            {
                result.AddRange(MergeTypesByModifierSignature(group.Rows));
            }

            return result;
        }

        private static List<ReportRow> MergeTypesByModifierSignature(List<ReportRow> group)
        {
            if (group.Count <= 1) return group;

            var signatureGroups = new List<(ReportRow Signature, List<ReportRow> Rows)>();
            foreach (var row in group)
            {
                var found = false;
                foreach (var sigGroup in signatureGroups)
                {
                    if (sigGroup.Signature.ModifierEquals(row))
                    {
                        sigGroup.Rows.Add(row);
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    signatureGroups.Add((row, new List<ReportRow> { row }));
                }
            }

            var result = new List<ReportRow>();
            foreach (var sigGroup in signatureGroups)
            {
                if (sigGroup.Rows.Count <= 1)
                {
                    result.AddRange(sigGroup.Rows);
                }
                else
                {
                    var merged = sigGroup.Rows[0];
                    merged.CarryType = string.Join(", ", sigGroup.Rows.Select(r => r.CarryType).Distinct());
                    result.Add(merged);
                }
            }

            return result;
        }

        private static bool AllModifiersEqual(List<ReportRow> group)
        {
            if (group.Count <= 1) return true;
            var first = group[0];
            for (int i = 1; i < group.Count; i++)
            {
                if (!first.ModifierEquals(group[i]))
                    return false;
            }
            return true;
        }

        /// <summary>Detect block code patterns that share a common prefix and identical modifiers, and condense them into a single wildcard row (e.g. flowerpot-*).</summary>
        /// <remarks>Groups by modifier signature + Slot + segment count + first segment (NOT by CarryType). All distinct CarryType values are aggregated. The wildcard always replaces a suffix with *, never the prefix.</remarks>
        public static List<ReportRow> CondenseWildcards(List<ReportRow> rows)
        {
            // Group by (Slot, modifier fields, segment count, first segment) - aggregates CarryType
            var rawGroups = new List<(ReportRow Key, List<ReportRow> Rows)>();
            foreach (var row in rows)
            {
                var found = false;
                var rowSegs = row.BlockCode.Split('-');
                foreach (var rg in rawGroups)
                {
                    if (rg.Key.ModifierEquals(row) && rg.Key.Slot == row.Slot)
                    {
                        var rgSegs = rg.Key.BlockCode.Split('-');
                        if (rowSegs.Length == rgSegs.Length && rowSegs[0] == rgSegs[0])
                        {
                            rg.Rows.Add(row);
                            found = true;
                            break;
                        }
                    }
                }
                if (!found)
                    rawGroups.Add((row, new List<ReportRow> { row }));
            }

            var result = new List<ReportRow>();
            foreach (var rawGroup in rawGroups)
            {
                if (rawGroup.Rows.Count <= 1)
                {
                    result.AddRange(rawGroup.Rows);
                    continue;
                }

                var pattern = FindWildcardPattern(rawGroup.Rows);
                if (pattern != null)
                {
                    var condensed = rawGroup.Rows[0];
                    condensed.BlockCode = pattern;
                    condensed.CarryType = string.Join(", ", rawGroup.Rows.Select(r => r.CarryType).Distinct().OrderBy(t => t));
                    result.Add(condensed);
                }
                else
                {
                    result.AddRange(rawGroup.Rows);
                }
            }

            return result;
        }

        /// <summary>Absorb non-wildcard rows whose BlockCode matches an existing wildcard pattern into that wildcard's Type list. Also merges duplicate wildcard rows with the same (BlockCode, Slot).</summary>
        /// <remarks>Phase 1 absorbs matching non-wildcard rows. Phase 2 merges duplicate wildcard rows and appends † when empty-Type variants are subsumed.</remarks>
        public static List<ReportRow> CondenseWildcardTypes(List<ReportRow> rows)
        {
            var wildcards = rows.Where(r => r.BlockCode.Contains('*')).ToList();
            if (wildcards.Count == 0) return rows;

            var result = new List<ReportRow>();

            // Phase 1: absorb non-wildcard rows into matching wildcards
            foreach (var row in rows)
            {
                if (row.BlockCode.Contains('*'))
                {
                    result.Add(row);
                    continue;
                }

                var prefix = wildcards
                    .Where(wc => wc.Slot == row.Slot && wc.ModifierEquals(row))
                    .Select(wc => wc.BlockCode[..^2])
                    .FirstOrDefault(p => row.BlockCode.StartsWith(p));

                if (prefix != null)
                {
                    var match = wildcards.First(wc =>
                        wc.Slot == row.Slot &&
                        wc.ModifierEquals(row) &&
                        wc.BlockCode == prefix + "-*");

                    var existing = match.CarryType
                        .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                        .ToHashSet();
                    if (!string.IsNullOrEmpty(row.CarryType) && existing.Add(row.CarryType))
                    {
                        var allTypes = existing.OrderBy(t => t).ToList();
                        match.CarryType = string.Join(", ", allTypes);
                    }
                }
                else
                {
                    result.Add(row);
                }
            }

            // Phase 2: merge wildcard rows with the same (BlockCode, Slot)
            var seen = new HashSet<(string BlockCode, CarrySlot Slot)>();
            var merged = new List<ReportRow>();
            foreach (var raw in result)
            {
                if (!raw.BlockCode.Contains('*'))
                {
                    merged.Add(raw);
                    continue;
                }

                var key = (raw.BlockCode, raw.Slot);
                if (!seen.Add(key))
                    continue;

                var same = result.Where(r => r.BlockCode.Contains('*') && r.BlockCode == key.BlockCode && r.Slot == key.Slot).ToList();
                if (same.Count <= 1)
                {
                    merged.Add(raw);
                    continue;
                }

                var keep = same[0];
                var allTypes = same
                    .SelectMany(r => r.CarryType.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                    .Distinct()
                    .OrderBy(t => t)
                    .ToList();
                if (same.Any(r => string.IsNullOrEmpty(r.CarryType)))
                    allTypes.Add("\u2020");
                keep.CarryType = string.Join(", ", allTypes);
                merged.Add(keep);
            }

            return merged;
        }

        private static string? FindWildcardPattern(List<ReportRow> group)
        {
            var allSegments = group.Select(r => r.BlockCode.Split('-')).ToArray();

            if (allSegments.Any(s => s.Length != allSegments[0].Length))
                return null;

            var segmentCount = allSegments[0].Length;

            // Find longest common prefix (segment by segment from the start)
            var commonPrefixLength = 0;
            while (commonPrefixLength < segmentCount)
            {
                var refSeg = allSegments[0][commonPrefixLength];
                if (allSegments.All(s => s[commonPrefixLength] == refSeg))
                    commonPrefixLength++;
                else
                    break;
            }

            // All segments identical - nothing to condense
            if (commonPrefixLength == segmentCount)
                return null;

            // Must have at least the domain segment in common
            if (commonPrefixLength < 1)
                return null;

            var prefix = string.Join("-", allSegments[0].Take(commonPrefixLength));
            var pattern = prefix + "-*";

            if (group.Any(r => r.BlockCode == pattern))
                return null;

            return pattern;
        }
    }
}
