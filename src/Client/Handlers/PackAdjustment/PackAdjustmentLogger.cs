using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using CarryOn.Common.Models;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Handlers.PackAdjustment
{
    internal sealed class PackAdjustmentLogger(ICoreClientAPI api, PackAdjustmentState state)
    {

        public void LogCurrentValues()
        {
            if (state.FrontCarryAttachmentMode)
            {
                LogAttachmentPointValues();
                return;
            }

            if (state.CurrentTransformScope == TransformScope.Label)
            {
                LogLabelTransform();
                return;
            }

            LogTransformSettings();
        }

        private void LogAttachmentPointValues()
        {
            var attach = state.GetFrontCarryAttachPoint();
            if (attach == null)
            {
                state.ShowMessage("FrontCarry attachment point not found");
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
            state.ShowMessage($"FrontCarry attachment point logged to {filename}");
        }

        private void LogLabelTransform()
        {
            state.TryGetCurrentCarryBehavior(out var behavior);
            var labelTransform = state.SelectedLabelTransform ?? behavior?.LabelRenderSettings?.Transform;
            if (labelTransform == null)
            {
                state.ShowMessage("No labelTransform to log.");
                return;
            }

            var entry = BuildTransformEntry(labelTransform);
            var labelOutput = new Dictionary<string, object> { [CarryCodes.LabelTransformGroup] = entry };
            string json = JsonConvert.SerializeObject(labelOutput, Formatting.Indented);
            json = CompactJsonArrays(json);

            var blockCode = CarryCodes.DisplayName(state.Block?.Code?.ToShortString());
            string filename = WriteToFile(json, CarryCodes.LabelTransformGroup, blockCode);
            state.ShowMessage($"Label transform logged to {filename}");
        }

        private void LogTransformSettings()
        {
            if (state.TransformSettings == null || state.TransformSettings.Length == 0)
            {
                state.ShowMessage("No transform settings to log.");
                return;
            }

            var list = new List<object>();
            foreach (var ts in state.TransformSettings)
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

            var transformKey = CarryCodes.DisplayName(state.TransformsGroup);
            var output = new Dictionary<string, object> { [transformKey] = list };
            string json = JsonConvert.SerializeObject(output, Formatting.Indented);
            json = CompactJsonArrays(json);

            var blockCode = CarryCodes.DisplayName(state.Block?.Code?.ToShortString() ?? state.ItemCode);
            string filename = WriteToFile(json, CarryCodes.DisplayName(state.TransformsGroup), blockCode);
            state.ShowMessage($"Transform settings logged to {filename} : {list.Count} entries");
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
            const float epsilon = 0.0001f;
            return Math.Abs(origin.X - 0.5f) < epsilon &&
                   Math.Abs(origin.Y - 0.5f) < epsilon &&
                   Math.Abs(origin.Z - 0.5f) < epsilon;
        }

        private static readonly string PackAdjustmentPath = Path.Combine("ModData", CarryCodes.ModId, "pack-adjustment");

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
