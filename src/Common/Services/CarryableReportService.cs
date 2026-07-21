using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Logic;
using CarryOn.Common.Models;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Services
{
    internal sealed class CarryableReportService
    {
        private readonly ICoreAPI api;
        private readonly IConfigProvider configProvider;
        private readonly WalkSpeedModifierResolver walkSpeedResolver = new();
        private readonly HungerRateModifierResolver hungerRateResolver = new();

        public CarryableReportService(ICoreAPI api, IConfigProvider configProvider)
        {
            this.api = api;
            this.configProvider = configProvider;
        }

        public void Generate()
        {
            var config = configProvider.Config;
            var reportConfig = config.DebuggingOptions?.CarryableReport;
            if (reportConfig == null || !reportConfig.Enabled) return;

            var rows = CollectRows();

            if (reportConfig.ReportMode >= ReportMode.CondensedSide)
                rows = ReportCondenser.CondenseSides(rows);

            if (reportConfig.ReportMode >= ReportMode.CondensedType)
                rows = ReportCondenser.CondenseTypes(rows);

            if (reportConfig.ReportMode >= ReportMode.CondensedAll)
            {
                rows = ReportCondenser.CondenseWildcards(rows);
                rows = ReportCondenser.CondenseWildcardTypes(rows);
            }

            if (rows.Count == 0)
            {
                api.World.Logger.Notification("CarryOn: No carryable blocks found for report");
                return;
            }

            if (reportConfig.OutputToLog)
                WriteToLog(rows, config);

            if (reportConfig.FileFormat == ReportFileFormat.Markdown)
                WriteMarkdownFile(rows, config);
            else if (reportConfig.FileFormat == ReportFileFormat.Html)
                WriteHtmlFile(rows, config);
        }

        private List<ReportRow> CollectRows()
        {
            var rows = new List<ReportRow>();
            var walkConfig = configProvider.Config.CarryWalkSpeed ?? new CarryWalkSpeedConfig();
            var hungerConfig = configProvider.Config.CarryHungerRate ?? new CarryHungerRateConfig();
            var filters = configProvider.Config.DebuggingOptions?.CarryableReport?.BlockFilters;

            foreach (var block in api.World.Blocks)
            {
                if (block?.Code == null) continue;
                if (!block.HasBehavior<BlockBehaviorCarryable>()) continue;

                var blockCode = block.Code.ToString();
                if (filters != null && filters.Length > 0 && !MatchesAnyFilter(blockCode, filters))
                    continue;

                var behavior = block.GetBehavior<BlockBehaviorCarryable>();
                if (behavior == null) continue;

                var stack = new ItemStack(block);
                var types = GetCarryTypesForReport(stack, behavior);

                foreach (var (type, typeStack) in types)
                {
                    var group = ResolveGroup(behavior, type);

                    foreach (var slot in new[] { CarrySlot.Hands, CarrySlot.Back })
                    {
                        var slotSettings = behavior.Slots?[slot];
                        if (slotSettings == null) continue;

                        var baseWalk = ResolveWalkBase(typeStack, behavior, slotSettings, slot, walkConfig);
                        var finalWalk = walkSpeedResolver.Resolve(typeStack, behavior, slotSettings, slot, walkConfig);

                        var baseHunger = ResolveHungerBase(typeStack, behavior, slotSettings, slot, hungerConfig);
                        var finalHunger = hungerRateResolver.Resolve(typeStack, behavior, slotSettings, slot, hungerConfig);

                        var walkMultiplier = ModifierMultiplierResolver.ResolveMultiplier(block, walkConfig.Multipliers, slot);
                        var hungerMultiplier = ModifierMultiplierResolver.ResolveMultiplier(block, hungerConfig.Multipliers, slot);

                        rows.Add(new ReportRow
                        {
                            BlockCode = block.Code.ToString(),
                            Slot = slot,
                            CarryType = type ?? "",
                            Group = group ?? "",
                            WalkMultiplier = walkMultiplier,
                            HungerMultiplier = hungerMultiplier,
                            BaseWalkSpeed = baseWalk,
                            FinalWalkSpeed = finalWalk,
                            BaseHungerRate = baseHunger,
                            FinalHungerRate = finalHunger,
                            Variants = block.Variant?.Count > 0 ? new Dictionary<string, string>(block.Variant) : null
                        });
                    }
                }
            }

            return rows;
        }

        private List<(string?, ItemStack)> GetCarryTypesForReport(ItemStack stack, BlockBehaviorCarryable behavior)
        {
            var resolved = CarryTypeResolver.ResolveCarryType(stack);
            if (!string.IsNullOrWhiteSpace(resolved))
                return [(resolved, stack)];

            // Fallback 1: types from behavior TypeGroup (populated from patch's "groups" property)
            if (behavior.TypeGroup?.Count > 0)
            {
                var results = new List<(string?, ItemStack)>();
                foreach (var type in behavior.TypeGroup.Keys)
                {
                    if (string.IsNullOrWhiteSpace(type)) continue;
                    var typedStack = stack.Clone();
                    typedStack.Attributes?.SetString("type", type);
                    results.Add((type, typedStack));
                }
                return results;
            }

            // Fallback 2: types from block's JSON attributes (e.g. "types" array on chests).
            // May be an array of strings or an array of objects with a "code" property.
            var typesArray = stack.Block?.Attributes?["types"]?.AsArray();
            if (typesArray?.Length > 0)
            {
                var results = new List<(string?, ItemStack)>();
                foreach (var entry in typesArray)
                {
                    var tokenType = entry.Token?.Type;
                    string? type = tokenType switch
                    {
                        JTokenType.String => entry.AsString(),
                        JTokenType.Object => entry["code"]?.AsString(),
                        _ => null
                    };
                    if (string.IsNullOrWhiteSpace(type)) continue;
                    var typedStack = stack.Clone();
                    typedStack.Attributes?.SetString("type", type);
                    results.Add((type, typedStack));
                }
                return results;
            }

            return [(null, stack)];
        }

        private static string? ResolveGroup(BlockBehaviorCarryable behavior, string? carryType)
        {
            if (string.IsNullOrWhiteSpace(carryType)) return null;
            if (behavior.TypeGroup == null || behavior.TypeGroup.Count == 0) return null;
            return behavior.TypeGroup.TryGetValue(carryType!, out var group) ? group : null;
        }

        private static float ResolveWalkBase(ItemStack stack, BlockBehaviorCarryable behavior, SlotSettings slotSettings, CarrySlot slot, CarryWalkSpeedConfig config)
        {
            var baseConfig = new CarryWalkSpeedConfig
            {
                HandsEnabled = config.HandsEnabled,
                BackEnabled = config.BackEnabled,
                HandsAllowSprint = config.HandsAllowSprint,
                BackAllowSprint = config.BackAllowSprint,
                ModifierOverrides = config.ModifierOverrides,
                Multipliers = null
            };
            return new WalkSpeedModifierResolver().Resolve(stack, behavior, slotSettings, slot, baseConfig);
        }

        private static float ResolveHungerBase(ItemStack stack, BlockBehaviorCarryable behavior, SlotSettings slotSettings, CarrySlot slot, CarryHungerRateConfig config)
        {
            var baseConfig = new CarryHungerRateConfig
            {
                HandsEnabled = config.HandsEnabled,
                BackEnabled = config.BackEnabled,
                MinSaturationThreshold = config.MinSaturationThreshold,
                ModifierOverrides = config.ModifierOverrides,
                Multipliers = null
            };
            return new HungerRateModifierResolver().Resolve(stack, behavior, slotSettings, slot, baseConfig);
        }

        private void WriteToLog(List<ReportRow> rows, CarryOnConfig config)
        {
            var walkEnabled = config.CarryWalkSpeed?.HandsEnabled == true || config.CarryWalkSpeed?.BackEnabled == true;
            var hungerEnabled = config.CarryHungerRate?.HandsEnabled == true || config.CarryHungerRate?.BackEnabled == true;
            if (!walkEnabled && !hungerEnabled) return;

            api.World.Logger.Notification("=== Carryable Block Report ===");

            // Group rows by (BlockCode, CarryType): one line per type per block with Hands and Back side by side
            var grouped = new Dictionary<(string BlockCode, string CarryType), (string Group, ReportRow? Hands, ReportRow? Back)>();
            foreach (var row in rows)
            {
                var key = (row.BlockCode, row.CarryType);
                if (!grouped.ContainsKey(key))
                    grouped[key] = (row.Group, null, null);
                var entry = grouped[key];
                if (row.Slot == CarrySlot.Hands)
                    entry.Hands = row;
                else
                    entry.Back = row;
                grouped[key] = entry;
            }

            if (walkEnabled)
            {
                api.World.Logger.Notification("  WalkSpeed Modifiers:");
                api.World.Logger.Notification("  {0,-45} {1,-12} {2,-8} {3,-10} {4,-10} {5,-10} {6,-10}",
                    "BlockCode", "Type", "Group", "WMult", "Hands", "BMult", "Back");
                api.World.Logger.Notification("  " + new string('-', 110));
                foreach (var kv in grouped)
                {
                    var ((blockCode, carryType), (group, hands, back)) = kv;
                    api.World.Logger.Notification("  {0,-45} {1,-12} {2,-8} {3,-10} {4,-10} {5,-10} {6,-10}",
                        blockCode,
                        carryType,
                        group,
                        hands != null ? FormatMultiplier(hands.WalkMultiplier) : "",
                        hands != null ? FormatModifierPair(hands.BaseWalkSpeed, hands.FinalWalkSpeed) : "",
                        back != null ? FormatMultiplier(back.WalkMultiplier) : "",
                        back != null ? FormatModifierPair(back.BaseWalkSpeed, back.FinalWalkSpeed) : "");
                }
            }

            if (hungerEnabled)
            {
                api.World.Logger.Notification("  HungerRate Modifiers:");
                api.World.Logger.Notification("  {0,-45} {1,-12} {2,-8} {3,-10} {4,-10} {5,-10} {6,-10}",
                    "BlockCode", "Type", "Group", "HMult", "Hands", "HMult", "Back");
                api.World.Logger.Notification("  " + new string('-', 110));
                foreach (var kv in grouped)
                {
                    var ((blockCode, carryType), (group, hands, back)) = kv;
                    api.World.Logger.Notification("  {0,-45} {1,-12} {2,-8} {3,-10} {4,-10} {5,-10} {6,-10}",
                        blockCode,
                        carryType,
                        group,
                        hands != null ? FormatMultiplier(hands.HungerMultiplier) : "",
                        hands != null ? FormatModifierPair(hands.BaseHungerRate, hands.FinalHungerRate) : "",
                        back != null ? FormatMultiplier(back.HungerMultiplier) : "",
                        back != null ? FormatModifierPair(back.BaseHungerRate, back.FinalHungerRate) : "");
                }
            }

            api.World.Logger.Notification("=== End Carryable Block Report ===");
        }

        private void WriteMarkdownFile(List<ReportRow> rows, CarryOnConfig config)
        {
            WriteReportFile(rows, config, "md");
        }

        private void WriteHtmlFile(List<ReportRow> rows, CarryOnConfig config)
        {
            WriteReportFile(rows, config, "html");
        }

        private static Dictionary<(string BlockCode, string CarryType), (string Group, ReportRow? Hands, ReportRow? Back)> GroupRowsForReport(List<ReportRow> rows)
        {
            var grouped = new Dictionary<(string BlockCode, string CarryType), (string Group, ReportRow? Hands, ReportRow? Back)>();
            foreach (var row in rows)
            {
                var key = (row.BlockCode, row.CarryType);
                if (!grouped.ContainsKey(key))
                    grouped[key] = (row.Group, null, null);
                var entry = grouped[key];
                if (row.Slot == CarrySlot.Hands)
                    entry.Hands = row;
                else
                    entry.Back = row;
                grouped[key] = entry;
            }
            return grouped;
        }

        private static StringBuilder BuildMarkdownSection(
            string title,
            string description,
            Dictionary<(string BlockCode, string CarryType), (string Group, ReportRow? Hands, ReportRow? Back)> grouped,
            bool walkFieldSelector)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {title}");
            sb.AppendLine();
            sb.AppendLine($"Generated at world load. Shows base (pre-multiplier) and final (post-multiplier) {description} values.");
            sb.AppendLine();
            sb.AppendLine("| BlockCode | Type | Group | Hands Mult | Hands (base\u2192final) | Back Mult | Back (base\u2192final) |");
            sb.AppendLine("|---|---|---|---|---|---|---|");

            var hasFootnote = false;
            foreach (var kv in grouped.OrderBy(g => g.Key.BlockCode))
            {
                var ((blockCode, carryType), (group, hands, back)) = kv;
                if (carryType.Contains("\u2020")) hasFootnote = true;
                sb.AppendLine($"| {blockCode} | {carryType} | {group} | " +
                    $"{(hands != null ? FormatMultiplier(walkFieldSelector ? hands.WalkMultiplier : hands.HungerMultiplier) : "")} | " +
                    $"{(hands != null ? FormatModifierPair(walkFieldSelector ? hands.BaseWalkSpeed : hands.BaseHungerRate, walkFieldSelector ? hands.FinalWalkSpeed : hands.FinalHungerRate) : "")} | " +
                    $"{(back != null ? FormatMultiplier(walkFieldSelector ? back.WalkMultiplier : back.HungerMultiplier) : "")} | " +
                    $"{(back != null ? FormatModifierPair(walkFieldSelector ? back.BaseWalkSpeed : back.BaseHungerRate, walkFieldSelector ? back.FinalWalkSpeed : back.FinalHungerRate) : "")} |");
            }

            if (hasFootnote)
            {
                sb.AppendLine();
                sb.AppendLine("\u2020 This wildcard block code also matches variants without a listed type.");
            }

            return sb;
        }

        private void WriteReportFile(List<ReportRow> rows, CarryOnConfig config, string extension)
        {
            try
            {
                var reportDir = api.GetOrCreateDataPath(Path.Combine("ModData", ModId, "reports"));
                var isHtml = extension == "html";

                var walkEnabled = config.CarryWalkSpeed?.HandsEnabled == true || config.CarryWalkSpeed?.BackEnabled == true;
                var hungerEnabled = config.CarryHungerRate?.HandsEnabled == true || config.CarryHungerRate?.BackEnabled == true;
                if (!walkEnabled && !hungerEnabled) return;

                if (walkEnabled)
                {
                    var walkRows = rows.Where(r =>
                        (r.Slot == CarrySlot.Hands && config.CarryWalkSpeed?.HandsEnabled == true) ||
                        (r.Slot == CarrySlot.Back && config.CarryWalkSpeed?.BackEnabled == true)).ToList();
                    var walkGrouped = GroupRowsForReport(walkRows);
                    var walkPath = Path.Combine(reportDir, $"CarryableReport-WalkSpeed.{extension}");
                    var sb = isHtml
                        ? BuildHtmlSection("Carryable Block Report \u2014 WalkSpeed Modifiers", "walk speed modifier", walkGrouped, walkFieldSelector: true)
                        : BuildMarkdownSection("Carryable Block Report \u2014 WalkSpeed Modifiers", "walk speed modifier", walkGrouped, walkFieldSelector: true);
                    File.WriteAllText(walkPath, sb.ToString());
                    api.World.Logger.Notification($"CarryOn: Walk speed report written to {walkPath}");
                }

                if (hungerEnabled)
                {
                    var hungerRows = rows.Where(r =>
                        (r.Slot == CarrySlot.Hands && config.CarryHungerRate?.HandsEnabled == true) ||
                        (r.Slot == CarrySlot.Back && config.CarryHungerRate?.BackEnabled == true)).ToList();
                    var hungerGrouped = GroupRowsForReport(hungerRows);
                    var hungerPath = Path.Combine(reportDir, $"CarryableReport-HungerRate.{extension}");
                    var sb = isHtml
                        ? BuildHtmlSection("Carryable Block Report \u2014 HungerRate Modifiers", "hunger rate modifier", hungerGrouped, walkFieldSelector: false)
                        : BuildMarkdownSection("Carryable Block Report \u2014 HungerRate Modifiers", "hunger rate modifier", hungerGrouped, walkFieldSelector: false);
                    File.WriteAllText(hungerPath, sb.ToString());
                    api.World.Logger.Notification($"CarryOn: Hunger rate report written to {hungerPath}");
                }
            }
            catch (Exception ex)
            {
                api.World.Logger.Error($"CarryOn: Failed to write report file: {ex.Message}");
            }
        }

        private static StringBuilder BuildHtmlSection(
            string title,
            string description,
            Dictionary<(string BlockCode, string CarryType), (string Group, ReportRow? Hands, ReportRow? Back)> grouped,
            bool walkFieldSelector)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"color-scheme\" content=\"light dark\">");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 2em; background: #1e1e1e; color: #e0e0e0; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; }");
            sb.AppendLine("th, td { border: 1px solid #444; padding: 0.4em 0.6em; text-align: left; }");
            sb.AppendLine("th { background: #2d2d2d; font-weight: 600; white-space: nowrap; }");
            sb.AppendLine("td { font-family: 'Consolas', 'Monaco', monospace; font-size: 0.9em; }");
            sb.AppendLine("tr:nth-child(even) td { background: #252525; }");
            sb.AppendLine(".footnote { margin-top: 1em; font-size: 0.85em; color: #999; }");
            sb.AppendLine("@media (prefers-color-scheme: light) {");
            sb.AppendLine("body { background: #fff; color: #000; }");
            sb.AppendLine("th, td { border-color: #ccc; }");
            sb.AppendLine("th { background: #f5f5f5; }");
            sb.AppendLine("tr:nth-child(even) td { background: #fafafa; }");
            sb.AppendLine(".footnote { color: #666; }");
            sb.AppendLine("}");
            sb.AppendLine("</style>");
            sb.AppendLine($"<title>{title}</title></head><body>");
            sb.AppendLine($"<h1>{title}</h1>");
            sb.AppendLine($"<p>Generated at world load. Shows base (pre-multiplier) and final (post-multiplier) {description} values.</p>");
            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th>BlockCode</th><th>Type</th><th>Group</th><th>Hands Mult</th><th>Hands (base→final)</th><th>Back Mult</th><th>Back (base→final)</th>");
            sb.AppendLine("</tr></thead><tbody>");

            var hasFootnote = false;
            foreach (var kv in grouped.OrderBy(g => g.Key.BlockCode))
            {
                var ((blockCode, carryType), (group, hands, back)) = kv;
                if (carryType.Contains("\u2020")) hasFootnote = true;
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{HtmlEncode(blockCode)}</td><td>{HtmlEncode(carryType)}</td><td>{HtmlEncode(group)}</td>");
                sb.AppendLine($"<td>{(hands != null ? FormatMultiplier(walkFieldSelector ? hands.WalkMultiplier : hands.HungerMultiplier) : "")}</td>");
                sb.AppendLine($"<td>{(hands != null ? FormatModifierPair(walkFieldSelector ? hands.BaseWalkSpeed : hands.BaseHungerRate, walkFieldSelector ? hands.FinalWalkSpeed : hands.FinalHungerRate) : "")}</td>");
                sb.AppendLine($"<td>{(back != null ? FormatMultiplier(walkFieldSelector ? back.WalkMultiplier : back.HungerMultiplier) : "")}</td>");
                sb.AppendLine($"<td>{(back != null ? FormatModifierPair(walkFieldSelector ? back.BaseWalkSpeed : back.BaseHungerRate, walkFieldSelector ? back.FinalWalkSpeed : back.FinalHungerRate) : "")}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");

            if (hasFootnote)
                sb.AppendLine("<p class=\"footnote\">\u2020 This wildcard block code also matches variants without a listed type.</p>");

            sb.AppendLine("</body></html>");
            return sb;
        }

        private static string HtmlEncode(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static bool MatchesAnyFilter(string blockCode, string[] filters)
        {
            foreach (var filter in filters)
            {
                if (string.IsNullOrWhiteSpace(filter)) continue;
                var pattern = WildcardToRegex(filter.Trim());
                if (Regex.IsMatch(blockCode, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
            return false;
        }

        private static string WildcardToRegex(string pattern)
        {
            return "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        }

        private static string FormatModifierPair(float baseVal, float finalVal)
        {
            return $"{baseVal:F3} → {finalVal:F3}";
        }

        private static string FormatMultiplier(float mult)
        {
            return $"{mult:F3}×";
        }

    }
}
