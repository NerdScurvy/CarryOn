using System.Collections.Generic;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Logic
{
    public static class LabelRenderSettingsParser
    {
        public static LabelRenderSettings? Parse(JsonObject json, bool transformInChildObject = true)
        {
            if (json == null || !json.Exists)
                return null;

            var settings = new LabelRenderSettings();
            var hasAnyValue = false;

            var transformJson = transformInChildObject ? json["transform"] : json;
            if (JsonHelper.HasAnyTransformValue(transformJson))
            {
                settings.Transform = JsonHelper.GetTransform(transformJson, null);
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetInt(json, "maxWidth", out var maxWidth))
            {
                settings.MaxWidth = maxWidth;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetInt(json, "maxHeight", out var maxHeight))
            {
                settings.MaxHeight = maxHeight;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetInt(json, "iconPixelSize", out var iconPixelSize))
            {
                settings.IconPixelSize = iconPixelSize;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetFloat(json, "iconScale", out var iconScale))
            {
                settings.IconScale = iconScale;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetBool(json, "iconFromInventory", out var iconFromInventory))
            {
                settings.IconFromInventory = iconFromInventory;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetString(json, "fontName", out var fontName))
            {
                settings.FontName = fontName ?? string.Empty;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetString(json, "verticalAlign", out var verticalAlign))
            {
                settings.VerticalAlign = verticalAlign ?? string.Empty;
                hasAnyValue = true;
            }

            if (JsonHelper.TryGetBool(json, "boldFont", out var boldFont))
            {
                settings.BoldFont = boldFont;
                hasAnyValue = true;
            }

            if (json.KeyExists("additionalTransforms"))
            {
                var arr = json["additionalTransforms"]?.AsArray();
                if (arr != null && arr.Length > 0)
                {
                    var additional = new List<ModelTransform?>(arr.Length);
                    foreach (var entry in arr)
                    {
                        if (JsonHelper.HasAnyTransformValue(entry))
                            additional.Add(JsonHelper.GetTransform(entry, null));
                    }
                    if (additional.Count > 0)
                    {
                        settings.AdditionalTransforms = additional;
                        hasAnyValue = true;
                    }
                }
            }

            return hasAnyValue ? settings : null;
        }
    }
}
