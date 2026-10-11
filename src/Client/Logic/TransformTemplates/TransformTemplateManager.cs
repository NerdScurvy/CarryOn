using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Models;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CarryOn.Client.Logic.TransformTemplates
{
    public sealed class TransformTemplateManager
    {
        private readonly ICoreClientAPI capi;
        private readonly TransformTemplateLoader loader;
        private readonly TransformTemplateParser parser;
        private readonly TransformTemplateResolver resolver;

        // templateCode -> (groupName -> groupDefinition)
        private Dictionary<string, Dictionary<string, TransformGroup?>> templatesByCode
            = new Dictionary<string, Dictionary<string, TransformGroup?>>();

        // block -> (groupName -> resolved TransformSettings[])
        private readonly Dictionary<Block, Dictionary<string, TransformSettings[]>> resolvedByBlock
            = new();

        // item code -> (groupName -> resolved TransformSettings[]). Item-only carries (e.g.
        // ground-storage bags held in hands) have a null Block and no BlockBehaviorCarryable,
        // so they cannot be keyed through resolvedByBlock; these settings are registered per
        // item code from the bag-item pose templates (see RegisterItemCodeTemplatePoses).
        private readonly Dictionary<string, Dictionary<string, TransformSettings[]>> resolvedByItemCode
            = new(StringComparer.OrdinalIgnoreCase);

        // Item-code templates using a trailing '*' wildcard, matched by code prefix.
        private readonly List<ItemCodeTemplateMatch> itemCodeWildcardTemplates = new();

        // item code -> pose group candidates (most-specific first). Only exact item code
        // registrations land here; wildcard registrations carry their candidates on
        // ItemCodeTemplateMatch.PoseGroupCandidates.
        private readonly Dictionary<string, string[]> itemPoseGroupCandidates
            = new(StringComparer.OrdinalIgnoreCase);

        // Runtime overrides written by the pack-adjustment tool for item carries (bags held in
        // hands). Keyed by exact item code and consulted before the asset-registered settings so
        // a tool-adjusted pose always wins over the item-code templates - including over a
        // wildcard match (e.g. an exact "game:quiver-north" wipes the "game:quiver*" entry).
        // Session-only, mirroring the block write-through in resolvedByBlock.
        private readonly Dictionary<string, Dictionary<string, TransformSettings[]>> itemPoseOverrides
            = new(StringComparer.OrdinalIgnoreCase);

        private sealed class ItemCodeTemplateMatch
        {
            public required string CodePrefix { get; init; }
            public required Dictionary<string, TransformSettings[]> Groups { get; init; }
            public string[]? PoseGroupCandidates { get; init; }
        }

        public TransformTemplateManager(ICoreClientAPI capi)
        {
            this.capi = capi;
            loader = new TransformTemplateLoader(capi);
            parser = new TransformTemplateParser(capi);
            resolver = new TransformTemplateResolver(capi);
        }

        public void LoadTemplates(IList<string> assetCodes)
        {
            this.templatesByCode = new Dictionary<string, Dictionary<string, TransformGroup?>>();

            if (assetCodes == null) return;

            foreach (var code in assetCodes)
            {
                if (string.IsNullOrWhiteSpace(code)) continue;

                if (!loader.TryLoadTemplateJson(code, out var assetLocation, out var templateJsonObj))
                {
                    continue;
                }

                if (templateJsonObj == null || assetLocation == null)
                {
                    continue;
                }

                if (!loader.ValidateTemplateCodeMatchesAssetName(templateJsonObj, assetLocation))
                {
                    continue;
                }

                if (!parser.TryParseTransformGroups(templateJsonObj, out var groups) || groups == null)
                {
                    capi.Logger.Warning($"CarryOn: Transform template asset is missing 'transformGroups' section in {assetLocation}");
                    continue;
                }

                this.templatesByCode[code.ToLowerInvariant()] = groups;
            }
        }

        public Dictionary<string, TransformSettings[]> ResolveAndFlattenTransformGroups(
            IList<string> templateCodes,
            Dictionary<string, TransformGroup?>? localTransformGroups = null)
        {
            return resolver.ResolveAndFlatten(this.templatesByCode, templateCodes, localTransformGroups);
        }

        public bool TryParseTransformGroups(JsonObject settingsJson, out Dictionary<string, TransformGroup?>? groups)
            => parser.TryParseTransformGroups(settingsJson, out groups);

        public bool TryGetSettings(Block block, string group, out TransformSettings[]? settings)
        {
            settings = null;
            if (block == null || !resolvedByBlock.TryGetValue(block, out var groups)) return false;
            return groups.TryGetValue(group, out settings);
        }

        /// <summary>
        /// Gets the resolved transform settings for a carried block or, when the carry is an
        /// item with a null <see cref="Block"/> (e.g. a bag held in hands), for the carried
        /// item's code. Returns false with null settings when neither source has the group.
        /// </summary>
        public bool TryGetSettings(CarriedBlock? carried, string group, out TransformSettings[]? settings)
        {
            settings = null;
            if (carried?.Block != null) return TryGetSettings(carried.Block, group, out settings);
            return TryGetItemSettings(carried?.ItemStack, group, out settings);
        }

        /// <summary>
        /// Gets the resolved transform settings registered for the given item code (bag-item
        /// pose templates). Supports a trailing-<c>*</c> wildcard registration matched by code
        /// prefix. Returns false with null settings when the code has no registration or group.
        /// </summary>
        public bool TryGetItemSettings(ItemStack? stack, string group, out TransformSettings[]? settings)
        {
            settings = null;
            var stackCode = stack?.Collectible?.Code?.ToString();
            if (stackCode == null) return false;

            // Pack-adjustment runtime overrides win over asset-registered item pose settings.
            if (itemPoseOverrides.TryGetValue(stackCode, out var overrideGroups)
                && overrideGroups.TryGetValue(group, out settings))
            {
                return true;
            }

            if (resolvedByItemCode.TryGetValue(stackCode, out var exactGroups))
            {
                return exactGroups.TryGetValue(group, out settings);
            }

            foreach (var wildcard in itemCodeWildcardTemplates)
            {
                if (stackCode.StartsWith(wildcard.CodePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return wildcard.Groups.TryGetValue(group, out settings);
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the ordered list of pose group names to try for an item carry (e.g. bag in
        /// hands), from the registered item pose templates. Most specific first; null when the
        /// item has no registration (caller falls back to the base group, e.g. "hands").
        /// </summary>
        public string[]? GetItemPoseGroupCandidates(ItemStack? stack)
        {
            if (stack?.Collectible?.Code == null) return null;

            var stackCode = stack.Collectible.Code.ToString();
            if (itemPoseGroupCandidates.TryGetValue(stackCode, out var exactCandidates))
                return exactCandidates is { Length: > 0 } ? exactCandidates : null;

            foreach (var wildcard in itemCodeWildcardTemplates)
            {
                if (wildcard.PoseGroupCandidates is { Length: > 0 }
                    && stackCode.StartsWith(wildcard.CodePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return wildcard.PoseGroupCandidates;
                }
            }

            return null;
        }

        public bool HasGroup(Block block, string group)
        {
            if (block == null || !resolvedByBlock.TryGetValue(block, out var groups)) return false;
            return groups.ContainsKey(group);
        }

        public bool HasGroup(CarriedBlock carried, string group)
        {
            if (carried?.Block != null) return HasGroup(carried.Block, group);
            return TryGetItemSettings(carried?.ItemStack, group, out _);
        }

        /// <summary>
        /// Gets the resolved transform settings for a block, or null when the block has none
        /// (not a carryable block, or never resolved).
        /// </summary>
        public IReadOnlyDictionary<string, TransformSettings[]>? GetResolvedGroups(Block block)
            => block != null && resolvedByBlock.TryGetValue(block, out var groups) ? groups : null;

        public void SetSettings(Block block, string group, TransformSettings[] settings)
        {
            ArgumentNullException.ThrowIfNull(block);
            if (!resolvedByBlock.TryGetValue(block, out var groups))
            {
                groups = new Dictionary<string, TransformSettings[]>(StringComparer.OrdinalIgnoreCase);
                resolvedByBlock[block] = groups;
            }
            groups[group] = settings;
        }

        /// <summary>
        /// Writes a runtime override for an item carry's pose group (e.g. a bag held in hands).
        /// Consulted before the asset-registered item pose settings so adjusted poses always
        /// win. Session-only, mirroring <see cref="SetSettings"/> for blocks.
        /// </summary>
        public void SetItemSettings(string itemCode, string group, TransformSettings[] settings)
        {
            if (string.IsNullOrEmpty(itemCode) || string.IsNullOrEmpty(group)) return;
            if (!itemPoseOverrides.TryGetValue(itemCode, out var groups))
            {
                groups = new Dictionary<string, TransformSettings[]>(StringComparer.OrdinalIgnoreCase);
                itemPoseOverrides[itemCode] = groups;
            }
            groups[group] = settings;
        }

        public bool TransformGroupExists(CarriedBlock carried, string group)
        {
            if (carried == null) return false;

            // Item carries (e.g. a bag in hands) have a null Block and the Default behavior
            // which has no slot storage; the item-code pose registry itself is the source of
            // truth for whether the group applies.
            if (carried.Block == null)
            {
                return HasItemGroup(carried.ItemStack, group);
            }

            var behavior = carried.GetCarryableBehavior();
            if (behavior == null || behavior.Slots[carried.Slot] == null) return false;
            return HasGroup(carried, group);
        }

        private bool HasItemGroup(ItemStack? stack, string group)
        {
            var stackCode = stack?.Collectible?.Code?.ToString();
            if (stackCode == null) return false;

            if (resolvedByItemCode.TryGetValue(stackCode, out var exactGroups))
                return exactGroups.ContainsKey(group);

            foreach (var wildcard in itemCodeWildcardTemplates)
            {
                if (stackCode.StartsWith(wildcard.CodePrefix, StringComparison.OrdinalIgnoreCase))
                    return wildcard.Groups.ContainsKey(group);
            }

            return false;
        }

        public string GetTransformGroupName(CarriedBlock carried, string baseGroup, bool checkExists = true)
        {
            var behavior = carried?.GetCarryableBehavior();
            var type = carried?.ItemStack?.Attributes?.GetString(CarryCodes.AttributeKeys.Type);

            if (!string.IsNullOrEmpty(type) && behavior?.TypeGroup != null && behavior.TypeGroup.Count > 0)
            {
                behavior.TypeGroup.TryGetValue(type, out var typeGroupName);
                if (typeGroupName != null)
                    typeGroupName = $"-{typeGroupName}";

                if (!checkExists || HasGroup(carried!, baseGroup + typeGroupName))
                    baseGroup += typeGroupName;
            }
            return baseGroup;
        }

        private void ResolveForBehavior(Block block, BlockBehaviorCarryable behavior)
        {
            Dictionary<string, TransformSettings[]> resolved;

            if (behavior.HasLocalTransformGroups && behavior.Properties != null)
            {
                if (TryParseTransformGroups(behavior.Properties, out var localTransformGroups) && localTransformGroups != null)
                    resolved = ResolveAndFlattenTransformGroups(behavior.TransformTemplates, localTransformGroups);
                else
                    resolved = ResolveAndFlattenTransformGroups(behavior.TransformTemplates);
            }
            else
            {
                resolved = ResolveAndFlattenTransformGroups(behavior.TransformTemplates);
            }

            resolvedByBlock[block] = resolved;
        }

        public static TransformTemplateManager InitializeFromBlocks(ICoreClientAPI api)
        {
            var carryableWithTemplates = api.World.Blocks
                .Where(b => b.GetBehavior<BlockBehaviorCarryable>()?.TransformTemplates != null)
                .ToList();

            var transformTemplateCodes = carryableWithTemplates
                .SelectMany(b => b.GetBehavior<BlockBehaviorCarryable>().TransformTemplates)
                .ToHashSet();

            var manager = new TransformTemplateManager(api);
            manager.LoadTemplates([.. transformTemplateCodes]);

            var carryablesToResolve = api.World.Blocks
                .Where(b => b.GetBehavior<BlockBehaviorCarryable>() != null &&
                            (b.GetBehavior<BlockBehaviorCarryable>().TransformTemplates != null || b.GetBehavior<BlockBehaviorCarryable>().HasLocalTransformGroups))
                .ToList();

            foreach (var block in carryablesToResolve)
            {
                manager.ResolveForBehavior(block, block.GetBehavior<BlockBehaviorCarryable>());
            }

            return manager;
        }

        /// <summary>
        /// Registers transform settings for the render placeholder block (<c>game:frame-empty</c>),
        /// cloned from a representative resolved carryable block. The placeholder carries no
        /// <see cref="BlockBehaviorCarryable"/> of its own, so without this it would never resolve
        /// transform settings and would silently render nothing. The clone is render-only; the
        /// placeholder is never placed or serialized.
        /// </summary>
        public void RegisterRenderPlaceholderSettings(ICoreClientAPI capi)
        {
            var placeholder = capi.World.GetBlock(new AssetLocation("game", "frame-empty"));
            if (placeholder == null) return;

            var reference = capi.World.Blocks.FirstOrDefault(b =>
                b.GetBehavior<BlockBehaviorCarryable>() is { } bh
                && (bh.TransformTemplates is { Length: > 0 } || bh.HasLocalTransformGroups));

            var resolved = reference != null
                ? GetResolvedGroups(reference)
                : null;
            if (resolved == null || resolved.Count == 0) return;

            TransformSettings[]? hands = resolved.TryGetValue("hands", out var h) ? h : null;
            if (hands != null)
            {
                SetSettings(placeholder, "hands", hands);
                SetSettings(placeholder, CarryCodes.DefaultTransformGroup, hands);
            }

            TransformSettings[]? back = resolved
                .OrderByDescending(kv => kv.Key.Equals("backpack-none", StringComparison.OrdinalIgnoreCase)
                    || kv.Key.StartsWith("backpack-none-", StringComparison.OrdinalIgnoreCase))
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(kv => kv.Key.StartsWith(CarryCodes.BackpackGroupPrefix, StringComparison.OrdinalIgnoreCase)
                    && kv.Value != null)
                .Value;
            if (back != null)
            {
                foreach (var group in CarryCodes.KnownBackpackTransformGroups)
                {
                    SetSettings(placeholder, group, back);
                }

                SetSettings(placeholder, CarryCodes.DefaultTransformGroup, back);
            }
            else if (hands != null)
            {
                foreach (var group in CarryCodes.KnownBackpackTransformGroups)
                {
                    SetSettings(placeholder, group, hands);
                }
            }
        }

        /// <summary>
        /// Registers transform settings for item-only carries (e.g. ground-storage bags held in
        /// hands) which have no <see cref="Block"/> and therefore no <see cref="BlockBehaviorCarryable"/>
        /// to carry the block-keyed <c>transformTemplates</c> on. Each template declares the item
        /// codes it applies to via <c>"matchesItemCodes"</c> (plain membership) and/or per-code pose
        /// groups via <c>"itemPoseGroups"</c> (item code → group name / ordered group candidates).
        /// A trailing <c>*</c> matches any item whose code starts with the prefix. The resolved
        /// <c>"transformGroups"</c> are keyed under those codes; when an item maps to a named pose
        /// group (e.g. <c>"hands-quiver"</c>), that group is tried first during rendering. Later
        /// registrations override earlier ones.
        /// </summary>
        public void RegisterItemCodeTemplatePoses(ICoreClientAPI capi, IList<string> templateCodes)
        {
            foreach (var code in templateCodes)
            {
                if (string.IsNullOrWhiteSpace(code)) continue;

                if (!loader.TryLoadTemplateJson(code, out var assetLocation, out var templateJsonObj)
                    || templateJsonObj == null
                    || assetLocation == null)
                {
                    continue;
                }

                if (!loader.ValidateTemplateCodeMatchesAssetName(templateJsonObj, assetLocation))
                {
                    continue;
                }

                RegisterItemPoseTemplateJson(capi, templateJsonObj, assetLocation.ToString());
            }
        }

        /// <summary>
        /// Discovers and registers item-pose transform templates from every loaded domain (own mod,
        /// vanilla game, and other mods). Any asset under <c>config/transformtemplates/</c> that
        /// declares <c>"matchesItemCodes"</c> and/or <c>"itemPoseGroups"</c> is treated as an
        /// item-pose template. Files on the same code/wildcard are applied in asset-path order so
        /// later files override earlier ones predictably. This lets other mods add bag-item poses
        /// with a plain JSON asset - no CarryOn code or patch required.
        /// </summary>
        public void DiscoverItemPoseTemplates(ICoreClientAPI capi)
        {
            const string prefix = "config/transformtemplates/";
            const string extension = ".json";

            var candidates = new List<(string path, IAsset asset)>();
            foreach (var kv in capi.Assets.AllAssets)
            {
                var location = kv.Key;
                if (location?.Path == null
                    || !location.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !location.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add((location.Path, kv.Value));
            }

            foreach (var (_, asset) in candidates.OrderBy(c => c.path, StringComparer.OrdinalIgnoreCase))
            {
                RegisterItemPoseTemplateFromAsset(capi, asset);
            }
        }

        private void RegisterItemPoseTemplateFromAsset(ICoreClientAPI capi, IAsset? asset)
        {
            if (asset == null) return;

            var jsonText = asset.ToText();
            if (string.IsNullOrWhiteSpace(jsonText)) return;

            JsonObject templateJsonObj;
            try
            {
                templateJsonObj = new JsonObject(JToken.Parse(jsonText));
            }
            catch (Exception)
            {
                capi.Logger.Warning($"CarryOn: Item-pose template asset '{asset.Location}' is not valid JSON and was skipped.");
                return;
            }

            RegisterItemPoseTemplateJson(capi, templateJsonObj, asset.Location?.ToString() ?? asset.Name ?? "?");
        }

        private void RegisterItemPoseTemplateJson(ICoreClientAPI capi, JsonObject templateJsonObj, string assetLabel)
        {
            var hasItemCodes = JsonHelper.TryGetStringArray(templateJsonObj, "matchesItemCodes", out var itemCodes);
            var hasPoseGroups = JsonHelper.TryGetObject(templateJsonObj, "itemPoseGroups", out var poseGroupsJObj);

            if (!hasItemCodes && !hasPoseGroups)
            {
                // Block-keyed transform template (registered via block behaviors); not an item pose.
                return;
            }

            if (!parser.TryParseTransformGroups(templateJsonObj, out var groups) || groups == null)
            {
                capi.Logger.Warning($"CarryOn: Item-pose template asset '{assetLabel}' is missing 'transformGroups' and was skipped.");
                return;
            }

            JsonHelper.TryGetString(templateJsonObj, "code", out var declaredCode);
            var templateCode = string.IsNullOrWhiteSpace(declaredCode)
                ? assetLabel
                : declaredCode;

            var templatesByCode = new Dictionary<string, Dictionary<string, TransformGroup?>>(
                StringComparer.OrdinalIgnoreCase)
            {
                [templateCode] = groups
            };
            var flattened = resolver.ResolveAndFlatten(templatesByCode, [templateCode], null);
            if (flattened.Count == 0) return;

            var poseGroups = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (poseGroupsJObj != null)
            {
                foreach (var prop in poseGroupsJObj.Properties())
                {
                    var candidates = new List<string>();
                    if (prop.Value?.Type == JTokenType.Array)
                    {
                        foreach (var token in (JArray)prop.Value)
                        {
                            if (token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(token.ToString()))
                                candidates.Add(token.ToString()!);
                        }
                    }
                    else if (prop.Value?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(prop.Value.ToString()))
                    {
                        candidates.Add(prop.Value.ToString()!);
                    }

                    if (candidates.Count > 0)
                        poseGroups[prop.Name.Trim()] = candidates.ToArray();
                }
            }

            // Registered codes = union of matchesItemCodes members and itemPoseGroups keys, so a
            // mod can add pose-only registrations (targeting a group defined elsewhere).
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (itemCodes != null)
            {
                foreach (var c in itemCodes)
                {
                    if (!string.IsNullOrWhiteSpace(c)) codes.Add(c.Trim());
                }
            }
            foreach (var key in poseGroups.Keys)
            {
                codes.Add(key);
            }

            foreach (var code in codes)
            {
                var normalized = code.ToLowerInvariant();
                var isWildcard = normalized.EndsWith("*", StringComparison.Ordinal);
                var prefix = isWildcard ? normalized[..^1] : null;
                if (isWildcard && prefix is { Length: 0 }) continue;

                poseGroups.TryGetValue(code, out var poseCandidates);

                if (poseCandidates is { Length: > 0 })
                {
                    var missing = poseCandidates.Where(g => !flattened.ContainsKey(g)).ToArray();
                    if (missing.Length > 0)
                    {
                        capi.Logger.Warning(
                            $"CarryOn: Item-pose template '{assetLabel}' maps item code '{code}' to pose group(s) " +
                            $"[{string.Join(", ", missing)}] that do not exist in its 'transformGroups'; " +
                            "the carry will fall back to the base group.");
                    }
                }

                if (isWildcard)
                {
                    var existingWildcard = itemCodeWildcardTemplates.FirstOrDefault(t =>
                        t.CodePrefix.Equals(prefix, StringComparison.OrdinalIgnoreCase));

                    if (existingWildcard != null)
                    {
                        itemCodeWildcardTemplates.Remove(existingWildcard);
                    }

                    itemCodeWildcardTemplates.Add(new ItemCodeTemplateMatch
                    {
                        CodePrefix = prefix!,
                        Groups = flattened,
                        PoseGroupCandidates = poseCandidates
                    });
                }
                else
                {
                    resolvedByItemCode[normalized] = flattened;
                    if (poseCandidates is { Length: > 0 })
                        itemPoseGroupCandidates[normalized] = poseCandidates;
                }
            }
        }
    }
}
