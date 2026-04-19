using System;
using System.Collections.Generic;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;

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
        public bool TryParseTransformGroups(JsonObject settingsJson, out Dictionary<string, TransformGroup> transformGroups)
        {
            transformGroups = null;

            if (settingsJson == null || !settingsJson.Exists) return false;
            if (!settingsJson.KeyExists("transformGroups")) return false;
            if (settingsJson["transformGroups"]?.Token is not JObject groupsJObj) return false;

            var parsed = new Dictionary<string, TransformGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in groupsJObj.Properties())
            {
                var groupName = prop.Name;

                if (prop.Value.Type == JTokenType.Array)
                {
                    var group = new TransformGroup { GroupName = groupName };

                    foreach (var token in (JArray)prop.Value)
                    {
                        var settings = ParseTransformGroupSettings(new JsonObject(token));
                        if (settings != null) group.AddBaseSettings(settings);
                    }

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

        public TransformGroup ParseTransformGroup(JsonObject groupJson, string groupName = null)
        {
            if (groupJson == null || !groupJson.Exists) return new TransformGroup { GroupName = groupName };

            var group = new TransformGroup { GroupName = groupName };

            if (JsonHelper.TryGetString(groupJson, "extends", out var extendsGroup))
                group.ExtendsGroup = extendsGroup;

            ReadSettingsArray(groupJson, "base", group.AddBaseSettings, groupName);
            ReadSettingsArray(groupJson, "overrides", group.AddOverrideSettings, groupName);
            ReadSettingsArray(groupJson, "appends", group.AddAppendSettings, groupName);

            return group;
        }

        /// <summary>
        /// Parses a single TransformGroupSettings object from the provided JSON. It reads various properties such as translation, 
        /// rotation, scale, asset references, and other settings to construct a TransformGroupSettings instance.
        /// </summary>
        /// <param name="settingsJson"> The JSON object containing the transform group settings. </param>
        /// <returns> A TransformGroupSettings instance populated with the parsed values, or null if the JSON is invalid. </returns>
        public TransformGroupSettings ParseTransformGroupSettings(JsonObject settingsJson)
        {
            if (settingsJson == null || !settingsJson.Exists) return null;

            var settings = new TransformGroupSettings();

            if (JsonHelper.TryGetString(settingsJson, "id", out var id)) settings.Id = id;

            if (JsonHelper.TryGetString(settingsJson, "item", out var item))
            {
                settings.AssetType = EnumAssetType.Item;
                settings.AssetName = item;
            }
            else if (JsonHelper.TryGetString(settingsJson, "block", out var block))
            {
                settings.AssetType = EnumAssetType.Block;
                settings.AssetName = block;
            }

            if (JsonHelper.TryGetVec3f(settingsJson, "translation", out var translation)) settings.SetTranslation(translation);
            if (JsonHelper.TryGetVec3f(settingsJson, "rotation", out var rotation)) settings.SetRotation(rotation);
            if (JsonHelper.TryGetVec3f(settingsJson, "origin", out var origin)) settings.SetOrigin(origin);
            if (JsonHelper.TryGetVec3f(settingsJson, "scale", out var scaleVec)) settings.SetScale(scaleVec);
            if (JsonHelper.TryGetFloat(settingsJson, "scale", out var scaleFloat)) settings.SetScale(scaleFloat, scaleFloat, scaleFloat);

            if (JsonHelper.TryGetFloat(settingsJson, "translationX", out var tx)) settings.TranslationX = tx;
            if (JsonHelper.TryGetFloat(settingsJson, "translationY", out var ty)) settings.TranslationY = ty;
            if (JsonHelper.TryGetFloat(settingsJson, "translationZ", out var tz)) settings.TranslationZ = tz;
            if (JsonHelper.TryGetFloat(settingsJson, "rotationX", out var rx)) settings.RotationX = rx;
            if (JsonHelper.TryGetFloat(settingsJson, "rotationY", out var ry)) settings.RotationY = ry;
            if (JsonHelper.TryGetFloat(settingsJson, "rotationZ", out var rz)) settings.RotationZ = rz;
            if (JsonHelper.TryGetFloat(settingsJson, "scaleX", out var sx)) settings.ScaleX = sx;
            if (JsonHelper.TryGetFloat(settingsJson, "scaleY", out var sy)) settings.ScaleY = sy;
            if (JsonHelper.TryGetFloat(settingsJson, "scaleZ", out var sz)) settings.ScaleZ = sz;
            if (JsonHelper.TryGetFloat(settingsJson, "originX", out var ox)) settings.OriginX = ox;
            if (JsonHelper.TryGetFloat(settingsJson, "originY", out var oy)) settings.OriginY = oy;
            if (JsonHelper.TryGetFloat(settingsJson, "originZ", out var oz)) settings.OriginZ = oz;

            if (JsonHelper.TryGetBool(settingsJson, "cullFaces", out var cullFaces)) settings.CullFaces = cullFaces;
            if (JsonHelper.TryGetFloat(settingsJson, "alphaTestOpaque", out var ato)) settings.AlphaTestOpaque = ato;
            if (JsonHelper.TryGetFloat(settingsJson, "alphaTestBlend", out var atb)) settings.AlphaTestBlend = atb;
            if (JsonHelper.TryGetBool(settingsJson, "normalShaded", out var normalShaded)) settings.NormalShaded = normalShaded;
            if (JsonHelper.TryGetString(settingsJson, "renderPass", out var renderPass)) settings.RenderPass = renderPass;
            if (JsonHelper.TryGetVec4f(settingsJson, "tintColor", out var tintColor)) settings.TintColor = tintColor;

            if (JsonHelper.TryGetString(settingsJson, "climateTintMap", out var climateTintMap)) settings.ClimateTintMap = climateTintMap;
            if (JsonHelper.TryGetString(settingsJson, "seasonalTintMap", out var seasonalTintMap)) settings.SeasonalTintMap = seasonalTintMap;
            if (JsonHelper.TryGetBool(settingsJson, "enabled", out var enabled)) settings.Enabled = enabled;

            return settings;
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
                 string groupName)
        {
            var token = parent[key]?.Token;

            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return;
            }

            if (token is not JArray arr)
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
