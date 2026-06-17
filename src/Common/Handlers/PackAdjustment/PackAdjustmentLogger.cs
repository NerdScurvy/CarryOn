using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Handlers.PackAdjustment
{
    internal sealed class PackAdjustmentLogger(ICoreClientAPI api, ICarryManager? carryManager, PackAdjustmentHandler handler)
    {

        public void LogCurrentValues()
        {
            if (handler.CurrentTarget == PackAdjustmentHandler.AdjustmentTarget.FrontCarryAttachment)
            {
                LogAttachmentPointValues();
                return;
            }

            if (handler.CurrentTransformScope == PackAdjustmentHandler.TransformScope.Label)
            {
                LogLabelTransform();
                return;
            }

            LogTransformSettings();
        }

        private void LogAttachmentPointValues()
        {
            var attach = handler.GetFrontCarryAttachPoint();
            if (attach == null)
            {
                handler.ShowMessage("FrontCarry attachment point not found");
                return;
            }

            var output = new Dictionary<string, object>
            {
                ["code"] = attach.Code,
                ["posX"] = attach.PosX,
                ["posY"] = attach.PosY,
                ["posZ"] = attach.PosZ,
                ["rotationX"] = attach.RotationX,
                ["rotationY"] = attach.RotationY,
                ["rotationZ"] = attach.RotationZ
            };

            string json = JsonConvert.SerializeObject(output, Formatting.Indented);
            string filename = WriteToFile(json, "frontcarry-attachpoint", attach.Code);
            handler.ShowMessage($"FrontCarry attachment point logged to {filename}");
        }

        private void LogLabelTransform()
        {
            var behavior = carryManager?.GetCarried(api?.World?.Player?.Entity!, handler.CarrySlot)?.GetCarryableBehavior();
            var labelTransform = handler.SelectedLabelTransform ?? behavior?.LabelRenderSettings?.Transform;
            if (labelTransform == null)
            {
                handler.ShowMessage("No labelTransform to log.");
                return;
            }

            var entry = BuildTransformEntry(labelTransform);
            var labelOutput = new Dictionary<string, object> { ["labelTransform"] = entry };
            string json = JsonConvert.SerializeObject(labelOutput, Formatting.Indented);
            json = CompactJsonArrays(json);

            var blockCode = handler.Block?.Code?.ToShortString() ?? "unknown";
            string filename = WriteToFile(json, "labelTransform", blockCode);
            handler.ShowMessage($"Label transform logged to {filename}");
        }

        private void LogTransformSettings()
        {
            if (handler.TransformSettings == null || handler.TransformSettings.Length == 0)
            {
                handler.ShowMessage("No transform settings to log.");
                return;
            }

            var list = new List<object>();
            foreach (var ts in handler.TransformSettings)
            {
                if (ts.Transform == null) continue;

                var entry = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(ts.Id))
                    entry["id"] = ts.Id;

                if (!string.IsNullOrEmpty(ts.AssetName))
                    entry["item"] = ts.AssetName;

                if (!(ts.Enabled ?? true))
                    entry["enabled"] = ts.Enabled;

                entry["translation"] = new float[] {
                    ts.Transform.Translation.X,
                    ts.Transform.Translation.Y,
                    ts.Transform.Translation.Z
                };
                entry["rotation"] = new float[] {
                    ts.Transform.Rotation.X,
                    ts.Transform.Rotation.Y,
                    ts.Transform.Rotation.Z
                };

                var sx = ts.Transform.ScaleXYZ.X;
                var sy = ts.Transform.ScaleXYZ.Y;
                var sz = ts.Transform.ScaleXYZ.Z;
                if (Math.Abs(sx - sy) < 0.0001f && Math.Abs(sx - sz) < 0.0001f)
                    entry["scale"] = sx;
                else
                    entry["scale"] = new float[] { sx, sy, sz };

                if (!IsOriginDefault(ts.Transform.Origin))
                    entry["origin"] = new float[] {
                        ts.Transform.Origin.X,
                        ts.Transform.Origin.Y,
                        ts.Transform.Origin.Z
                    };

                list.Add(entry);
            }

            var output = new Dictionary<string, object> { [handler.TransformsGroup!] = list };
            string json = JsonConvert.SerializeObject(output, Formatting.Indented);
            json = CompactJsonArrays(json);

            var blockCode = handler.Block?.Code?.ToShortString() ?? "unknown";
            string filename = WriteToFile(json, handler.TransformsGroup ?? "unknown", blockCode);
            handler.ShowMessage($"Transform settings logged to {filename} : {list.Count} entries");
        }

        private static Dictionary<string, object> BuildTransformEntry(ModelTransform labelTransform)
        {
            var entry = new Dictionary<string, object>
            {
                ["translation"] = new float[] {
                    labelTransform.Translation.X,
                    labelTransform.Translation.Y,
                    labelTransform.Translation.Z
                },
                ["rotation"] = new float[] {
                    labelTransform.Rotation.X,
                    labelTransform.Rotation.Y,
                    labelTransform.Rotation.Z
                }
            };

            var sx = labelTransform.ScaleXYZ.X;
            var sy = labelTransform.ScaleXYZ.Y;
            var sz = labelTransform.ScaleXYZ.Z;
            if (Math.Abs(sx - sy) < 0.0001f && Math.Abs(sx - sz) < 0.0001f)
                entry["scale"] = sx;
            else
                entry["scale"] = new float[] { sx, sy, sz };

            if (!IsOriginDefault(labelTransform.Origin))
                entry["origin"] = new float[] {
                    labelTransform.Origin.X,
                    labelTransform.Origin.Y,
                    labelTransform.Origin.Z
                };

            return entry;
        }

        private static bool IsOriginDefault(FastVec3f origin)
        {
            const float epsilon = 0.0001F;
            return Math.Abs(origin.X - 0.5F) < epsilon &&
                   Math.Abs(origin.Y - 0.5F) < epsilon &&
                   Math.Abs(origin.Z - 0.5F) < epsilon;
        }

        private static readonly string PackAdjustmentPath = Path.Combine("ModData", CarryCode.ModId, "pack-adjustment");

        private static string CompactJsonArrayContent(string content)
        {
            return Regex.Replace(content, @"\s+", " ").Trim();
        }

        private static string CompactJsonArrays(string json)
        {
            return Regex.Replace(json, @"\[\s*([\d\.,\s\-eE]+)\s*\]", m =>
            {
                return "[" + CompactJsonArrayContent(m.Groups[1].Value) + "]";
            });
        }

        private string WriteToFile(string json, string group, string blockCode)
        {
            var modDataDir = api.GetOrCreateDataPath(PackAdjustmentPath);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string sanitizedBlock = blockCode.Replace(':', '+');
            string filename = $"{timestamp}-{sanitizedBlock}-{group}.json";
            File.WriteAllText(Path.Combine(modDataDir, filename), json);
            return filename;
        }
    }
}
