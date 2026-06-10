using System;
using System.Collections.Generic;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Logic.TransformTemplates
{
    /// <summary>
    /// Handles parsing of transform template JSON content into structured TransformGroup and TransformGroupSettings objects. 
    /// It reads the JSON definitions of transform groups, including their base settings, overrides, and appends, and constructs the 
    /// corresponding C# objects that can be used for resolving and applying transforms to carryable blocks. 
    /// This class is responsible for interpreting the JSON structure of transform templates and converting it into a format that can 
    /// be easily utilized by the TransformTemplateManager and other parts of the CarryOn mod's client-side logic.
    /// </summary>
    public sealed class TransformTemplateParser
    {
        private readonly ICoreClientAPI capi;

        public TransformTemplateParser(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        /// <summary>
        /// Parses a single TransformGroupSettings object from the provided JSON. It reads various properties such as translation, 
        /// rotation, scale, asset references, and other settings to construct a TransformGroupSettings instance.
        /// </summary>
        /// <param name="settingsJson"> The JSON object containing the transform group settings. </param>
        /// <param name="transformGroups"> The dictionary to populate with parsed transform groups. </param>
        /// <returns> True if parsing was successful, false otherwise. </returns>
        public bool TryParseTransformGroups(JsonObject settingsJson, out Dictionary<string, TransformGroup?>? transformGroups)
        {
            transformGroups = null;

            if (settingsJson == null || !settingsJson.Exists) return false;
            if (!JsonHelper.TryGetObject(settingsJson, "transformGroups", out var groupsJObj) || groupsJObj == null) return false;

            var parsed = new Dictionary<string, TransformGroup?>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in groupsJObj.Properties())
            {
                var groupName = prop.Name;

                if (prop.Value.Type == JTokenType.Array)
                {
                    var baseList = new List<TransformGroupSettings>();
                    foreach (var token in (JArray)prop.Value)
                    {
                        var settings = ParseTransformGroupSettings(new JsonObject(token));
                        if (settings != null) baseList.Add(settings);
                    }

                    var group = new TransformGroup(
                        GroupName: groupName,
                        ExtendsGroup: null,
                        Base: baseList,
                        Overrides: Array.Empty<TransformGroupSettings>(),
                        Appends: Array.Empty<TransformGroupSettings>()
                    );

                    parsed[groupName] = group;
                    continue;
                }

                if (groupName.StartsWith("@", StringComparison.OrdinalIgnoreCase) ||
                    groupName.StartsWith("~", StringComparison.OrdinalIgnoreCase) ||
                    groupName.StartsWith("^", StringComparison.OrdinalIgnoreCase))
                {
                    capi.Logger.Warning($"CarryOn: transform group adjustment '{groupName}' must use array syntax.");
                    continue;
                }

                var parsedGroup = ParseTransformGroup(new JsonObject(prop.Value), groupName);
                parsed[groupName] = parsedGroup;
            }

            transformGroups = parsed;
            return true;
        }

        public TransformGroup ParseTransformGroup(JsonObject groupJson, string? groupName = null)
        {
            if (groupJson == null || !groupJson.Exists)
            {
                return new TransformGroup(
                    GroupName: groupName,
                    ExtendsGroup: null,
                    Base: Array.Empty<TransformGroupSettings>(),
                    Overrides: Array.Empty<TransformGroupSettings>(),
                    Appends: Array.Empty<TransformGroupSettings>()
                );
            }

            string? extendsGroup = null;
            if (JsonHelper.TryGetString(groupJson, "extends", out var ext)) extendsGroup = ext;

            var baseList = new List<TransformGroupSettings>();
            var overridesList = new List<TransformGroupSettings>();
            var appendsList = new List<TransformGroupSettings>();

            ReadSettingsArray(groupJson, "base", s => baseList.Add(s), groupName);
            ReadSettingsArray(groupJson, "overrides", s => overridesList.Add(s), groupName);
            ReadSettingsArray(groupJson, "appends", s => appendsList.Add(s), groupName);

            return new TransformGroup(
                GroupName: groupName,
                ExtendsGroup: extendsGroup,
                Base: baseList,
                Overrides: overridesList,
                Appends: appendsList
            );
        }

        /// <summary>
        /// Parses a single TransformGroupSettings object from the provided JSON. It reads various properties such as translation, 
        /// rotation, scale, asset references, and other settings to construct a TransformGroupSettings instance.
        /// </summary>
        /// <param name="settingsJson"> The JSON object containing the transform group settings. </param>
        /// <returns> A TransformGroupSettings instance populated with the parsed values, or null if the JSON is invalid. </returns>
        public TransformGroupSettings? ParseTransformGroupSettings(JsonObject settingsJson)
        {
            if (settingsJson == null || !settingsJson.Exists) return null;

            string? id = null;
            EnumAssetType assetType = EnumAssetType.None;
            string? assetName = null;
            string? disableIfItemStackPath = null;
            string? beDataItemStackPath = null;
            float? translationX = null, translationY = null, translationZ = null;
            float? rotationX = null, rotationY = null, rotationZ = null;
            float? scaleX = null, scaleY = null, scaleZ = null;
            float? originX = null, originY = null, originZ = null;
            bool? cullFaces = null;
            float? alphaTestOpaque = null, alphaTestBlend = null;
            bool? normalShaded = null;
            string? renderPass = null;
            Vec4f? tintColor = null;
            string? climateTintMap = null, seasonalTintMap = null;
            float? glowIntensity = null;
            bool? attachedRoot = null;
            bool enabled = true;

            if (JsonHelper.TryGetString(settingsJson, "id", out var idVal)) id = idVal;

            if (JsonHelper.TryGetString(settingsJson, "item", out var item))
            {
                assetType = EnumAssetType.Item;
                assetName = item;
            }
            else if (JsonHelper.TryGetString(settingsJson, "block", out var block))
            {
                assetType = EnumAssetType.Block;
                assetName = block;
            }

            if (JsonHelper.TryGetString(settingsJson, "disableIfItemStackPath", out var disableIfPath))
            {
                disableIfItemStackPath = disableIfPath;
            }

            if (JsonHelper.TryGetString(settingsJson, "blockEntityDataItemStackPath", out var bePath))
            {
                beDataItemStackPath = bePath;
            }

            if (JsonHelper.TryGetVec3f(settingsJson, "translation", out var translation) && translation != null)
            {
                translationX = translation.X;
                translationY = translation.Y;
                translationZ = translation.Z;
            }
            if (JsonHelper.TryGetVec3f(settingsJson, "rotation", out var rotation) && rotation != null)
            {
                rotationX = rotation.X;
                rotationY = rotation.Y;
                rotationZ = rotation.Z;
            }
            if (JsonHelper.TryGetVec3f(settingsJson, "origin", out var origin) && origin != null)
            {
                originX = origin.X;
                originY = origin.Y;
                originZ = origin.Z;
            }
            if (JsonHelper.TryGetVec3f(settingsJson, "scale", out var scaleVec) && scaleVec != null)
            {
                scaleX = scaleVec.X;
                scaleY = scaleVec.Y;
                scaleZ = scaleVec.Z;
            }
            if (JsonHelper.TryGetFloat(settingsJson, "scale", out var scaleFloat))
            {
                scaleX = scaleY = scaleZ = scaleFloat;
            }

            if (JsonHelper.TryGetFloat(settingsJson, "translationX", out var tx)) translationX = tx;
            if (JsonHelper.TryGetFloat(settingsJson, "translationY", out var ty)) translationY = ty;
            if (JsonHelper.TryGetFloat(settingsJson, "translationZ", out var tz)) translationZ = tz;
            if (JsonHelper.TryGetFloat(settingsJson, "rotationX", out var rx)) rotationX = rx;
            if (JsonHelper.TryGetFloat(settingsJson, "rotationY", out var ry)) rotationY = ry;
            if (JsonHelper.TryGetFloat(settingsJson, "rotationZ", out var rz)) rotationZ = rz;
            if (JsonHelper.TryGetFloat(settingsJson, "scaleX", out var sx)) scaleX = sx;
            if (JsonHelper.TryGetFloat(settingsJson, "scaleY", out var sy)) scaleY = sy;
            if (JsonHelper.TryGetFloat(settingsJson, "scaleZ", out var sz)) scaleZ = sz;
            if (JsonHelper.TryGetFloat(settingsJson, "originX", out var ox)) originX = ox;
            if (JsonHelper.TryGetFloat(settingsJson, "originY", out var oy)) originY = oy;
            if (JsonHelper.TryGetFloat(settingsJson, "originZ", out var oz)) originZ = oz;

            if (JsonHelper.TryGetBool(settingsJson, "cullFaces", out var cull)) cullFaces = cull;
            if (JsonHelper.TryGetFloat(settingsJson, "alphaTestOpaque", out var ato)) alphaTestOpaque = ato;
            if (JsonHelper.TryGetFloat(settingsJson, "alphaTestBlend", out var atb)) alphaTestBlend = atb;
            if (JsonHelper.TryGetBool(settingsJson, "normalShaded", out var normal)) normalShaded = normal;
            if (JsonHelper.TryGetString(settingsJson, "renderPass", out var rp)) renderPass = rp;
            if (JsonHelper.TryGetVec4f(settingsJson, "tintColor", out var tint)) tintColor = tint;

            if (JsonHelper.TryGetString(settingsJson, "climateTintMap", out var climate)) climateTintMap = climate;
            if (JsonHelper.TryGetString(settingsJson, "seasonalTintMap", out var seasonal)) seasonalTintMap = seasonal;
            if (JsonHelper.TryGetFloat(settingsJson, "glowIntensity", out var glow)) glowIntensity = glow;
            if (JsonHelper.TryGetBool(settingsJson, "enabled", out var en)) enabled = en;
            if (JsonHelper.TryGetBool(settingsJson, "attachedRoot", out var ar)) attachedRoot = ar;

            return new TransformGroupSettings(
                Id: id,
                AssetType: assetType,
                AssetName: assetName,
                DisableIfItemStackPath: disableIfItemStackPath,
                BlockEntityDataItemStackPath: beDataItemStackPath,
                TranslationX: translationX,
                TranslationY: translationY,
                TranslationZ: translationZ,
                RotationX: rotationX,
                RotationY: rotationY,
                RotationZ: rotationZ,
                ScaleX: scaleX,
                ScaleY: scaleY,
                ScaleZ: scaleZ,
                OriginX: originX,
                OriginY: originY,
                OriginZ: originZ,
                CullFaces: cullFaces,
                AlphaTestOpaque: alphaTestOpaque,
                AlphaTestBlend: alphaTestBlend,
                NormalShaded: normalShaded,
                RenderPass: renderPass,
                TintColor: tintColor,
                ClimateTintMap: climateTintMap,
                SeasonalTintMap: seasonalTintMap,
                GlowIntensity: glowIntensity,
                AttachedRoot: attachedRoot,
                Enabled: enabled
            );
        }

        /// <summary>
        /// Reads an array of TransformGroupSettings from the specified JSON property and adds them to the provided collection using the specified add action.
        /// It validates the JSON structure and logs errors if the expected format is not met.
        /// </summary>
        /// <param name="parent"> The parent JSON object containing the array. </param>
        /// <param name="key"> The key of the JSON property containing the array. </param>
        /// <param name="add"> The action to add each parsed TransformGroupSettings to the collection. </param>
        /// <param name="groupName"> The name of the transform group for logging purposes. </param>

        private void ReadSettingsArray(
                 JsonObject parent,
                 string key,
                 Action<TransformGroupSettings> add,
                 string? groupName)
        {
            var token = parent[key]?.Token;

            if (token == null || JsonHelper.IsNullOrUndefined(token))
            {
                return;
            }

            if (!JsonHelper.TryGetArray(parent, key, out var arr) || arr == null)
            {
                capi.Logger.Error(
                    $"CarryOn: transform group '{groupName ?? "<unknown>"}' has invalid '{key}'. " +
                    $"Expected array of objects, got {token.Type}. Use \"{key}\": [ <object> ].");
                return;
            }

            for (int i = 0; i < arr.Count; i++)
            {
                var entry = arr[i];

                if (entry is not JObject)
                {
                    capi.Logger.Error(
                    $"CarryOn: transform group '{groupName ?? "<unknown>"}' has invalid '{key}[{i}]'. " +
                    $"Expected object, got {entry?.Type.ToString() ?? "null"}.");
                    continue;
                }

                var settings = ParseTransformGroupSettings(new JsonObject(entry));
                if (settings != null)
                {
                    add(settings);
                }
            }
        }
    }
}
