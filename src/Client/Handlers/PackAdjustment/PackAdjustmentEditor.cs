using System;
using CarryOn.Common.Behaviors;
using CarryOn.Client.Models;
using CarryOn.Common.Models;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Handlers.PackAdjustment
{
    internal sealed class PackAdjustmentEditor(ICoreClientAPI api, PackAdjustmentState state)
    {

        public TextCommandResult SetCurrentTransformId(string newId)
        {
            if (state.CurrentTransformScope == TransformScope.Label)
                return TextCommandResult.Error("Cannot set id for labelTransform.");
            if (state.TransformSettings == null || state.TransformSettings.Length == 0)
                return TextCommandResult.Error("No transform selected.");
            if (state.TransformsGroup == null)
                return TextCommandResult.Error("No transform group selected.");

            state.TransformSettings[state.TransformIndex] = state.TransformSettings[state.TransformIndex] with { Id = newId };

            var behavior = state.GetCarryableBehavior(state.CarrySlot);
            if (behavior != null)
                state.SaveTransformSettings(behavior);

            state.ShowMessage($"Transform {state.TransformIndex + 1} id set to '{newId}'");
            return TextCommandResult.Success();
        }

        public void AdjustAxis(char axis, int direction)
        {
            if (state.FrontCarryAttachmentMode)
            {
                AdjustAttachmentPoint(axis, direction);
                return;
            }

            var isLabelScope = state.CurrentTransformScope == TransformScope.Label;
            var setting = state.TransformSettings?[state.TransformIndex];
            if (!isLabelScope && setting == null) return;

            var transform = isLabelScope ? state.SelectedLabelTransform : setting!.Transform;
            if (transform == null)
            {
                if (isLabelScope) return;

                state.TryGetCurrentCarryBehavior(out var currentBehavior);
                var defaultTransform = currentBehavior?.DefaultTransform ?? BlockBehaviorCarryable.DefaultBlockTransform;
                transform = defaultTransform.Clone();
                var ts = state.TransformSettings;
                if (ts == null) return;
                ts[state.TransformIndex] = ts[state.TransformIndex] with
                {
                    Transform = transform
                };
            }

            float amount = CalculateAdjustmentAmount();
            float result = state.CurrentMode switch
            {
                AdjustmentMode.Translation => AdjustTranslation(transform, axis, amount, direction),
                AdjustmentMode.Scale => AdjustScale(transform, axis, amount, direction),
                AdjustmentMode.Rotation => AdjustRotation(transform, axis, amount, direction),
                AdjustmentMode.Origin => AdjustOrigin(transform, axis, amount, direction),
                _ => 0f
            };

            if (isLabelScope)
            {
                state.SelectedLabelTransform = transform;
            }

            if (isLabelScope)
            {
                if (state.TryGetCurrentCarryBehavior(out var behavior))
                {
                    behavior!.LabelRenderSettings ??= new LabelRenderSettings();
                    PackAdjustmentTransformResolver.SetLabelTransformAt(behavior.LabelRenderSettings, state.SelectedLabelTransformIndex, state.SelectedLabelTransform);
                    state.InvalidateCaches();
                }
            }
            else
            {
                if (state.TransformsGroup == null || state.TransformSettings == null) return;
                if (state.TryGetCurrentCarryBehavior(out var behavior))
                    state.SaveTransformSettings(behavior!);
            }
            state.ShowMessage($"Adjust {axis} {result} ({state.CurrentMode})");
        }

        public void AdjustTransformIndex(int direction)
        {
            if (state.FrontCarryAttachmentMode)
            {
                state.ShowMessage("No transform index in FrontCarry mode");
                return;
            }

            if (state.CurrentTransformScope == TransformScope.Label)
            {
                state.TryGetCurrentCarryBehavior(out var behavior);
                var labelCount = PackAdjustmentTransformResolver.GetLabelTransformCount(behavior?.LabelRenderSettings);
                if (labelCount <= 0)
                {
                    state.ShowMessage("No label transform available");
                    return;
                }

                state.SetTransformIndex(Math.Clamp(state.SelectedLabelTransformIndex + direction, 0, labelCount - 1));
                return;
            }

            if (state.TransformSettings == null || state.TransformSettings.Length == 0) return;
            state.SetTransformIndex(Math.Clamp(state.TransformIndex + direction, 0, state.TransformSettings.Length - 1));
        }

        public void ToggleTransform()
        {
            if (state.CurrentTransformScope == TransformScope.Label)
            {
                state.ShowMessage("labelTransform has no enabled flag");
                return;
            }

            if (state.TransformSettings != null && state.TransformSettings.Length > 0)
            {
                state.TransformSettings[state.TransformIndex] = state.TransformSettings[state.TransformIndex] with
                {
                    Enabled = !(state.TransformSettings[state.TransformIndex].Enabled ?? true)
                };
                state.InvalidateCaches();

                var enabledState = state.TransformSettings[state.TransformIndex].Enabled ?? true ? "enabled" : "disabled";
                state.ShowMessage($"Transform {state.TransformIndex + 1} {enabledState} | {state.GetCurrentTransformInfo()}");
            }
        }

        public void AddTransform()
        {
            if (state.CurrentTransformScope == TransformScope.Label)
            {
                state.ShowMessage("Cannot add labelTransform entries");
                return;
            }

            var altDown = api.Input.KeyboardKeyState[(int)GlKeys.AltLeft];
            var shiftDown = api.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft];

            if (!altDown)
            {
                state.ShowMessage("Hold ALT to add a new transform - Also hold SHIFT to duplicate selected transform");
                return;
            }

            if (state.TransformSettings == null)
            {
                state.TransformSettings = [];
            }

            TransformSettings transformSetting;
            if (shiftDown)
            {
                var currentSettings = state.TransformSettings[state.TransformIndex];
                transformSetting = currentSettings.DeepClone();
            }
            else
            {
                var transform = new ModelTransform()
                {
                    Translation = new Vec3f(0, 0, 0),
                    Rotation = new Vec3f(0, 0, 0),
                    ScaleXYZ = new Vec3f(1, 1, 1),
                    Origin = new Vec3f(0.5f, 0.5f, 0.5f)
                };

                transformSetting = new TransformSettings()
                {
                    Id = $"strap{state.TransformSettings.Length + 1}",
                    AssetName = "carryon:strap",
                    AssetType = EnumAssetType.Item,
                    Transform = transform,
                    Enabled = true
                };
            }

            var temp = state.TransformSettings;
            Array.Resize(ref temp, temp.Length + 1);
            temp[temp.Length - 1] = transformSetting;
            state.TransformSettings = temp;

            var behavior = state.GetCarryableBehavior(state.CarrySlot);
            if (behavior == null)
            {
                state.ShowMessage("[PackAdjustmentEditor] AddTransform: no carryable behavior found, aborting.");
                return;
            }
            if (state.Block != null && behavior.Slots[state.CarrySlot] == null)
            {
                state.ShowMessage("[PackAdjustmentHandler] AddTransform: carriedSlot is null, aborting.");
                return;
            }

            if (state.TransformsGroup == null || state.TransformSettings == null) return;
            if (!state.SaveTransformSettings(behavior))
                return;
            state.ShowMessage($"Transform added as index {state.TransformSettings.Length}");
        }

        public void RemoveTransform()
        {
            if (state.CurrentTransformScope == TransformScope.Label)
            {
                state.ShowMessage("Cannot remove labelTransform");
                return;
            }

            var altDown = api.Input.KeyboardKeyState[(int)GlKeys.AltLeft];
            if (!altDown)
            {
                state.ShowMessage("Hold ALT to remove a transform");
                return;
            }

            if (state.TransformIndex == 0)
            {
                state.ShowMessage("Cannot remove the first transform");
                return;
            }

            if (state.TransformSettings != null && state.TransformIndex > 0 && state.TransformIndex < state.TransformSettings.Length)
            {
                var newSettings = new TransformSettings[state.TransformSettings.Length - 1];
                int newIndex = 0;
                for (int i = 0; i < state.TransformSettings.Length; i++)
                {
                    if (i != state.TransformIndex)
                    {
                        newSettings[newIndex++] = state.TransformSettings[i];
                    }
                }
                state.TransformSettings = newSettings;
            }

            if (state.TransformSettings == null || state.TransformSettings.Length == 0)
            {
                state.ShowMessage("No transform settings available");
                return;
            }

            if (string.IsNullOrEmpty(state.TransformsGroup))
            {
                state.ShowMessage("No transform group selected");
                return;
            }

            state.SetTransformIndex(state.TransformIndex);

            var behavior = state.GetCarryableBehavior(state.CarrySlot);
            if (behavior == null)
            {
                state.ShowMessage("No carryable behavior found");
                return;
            }

            if (!state.SaveTransformSettings(behavior))
                return;
            state.ShowMessage("Transform removed");
        }

        private void AdjustAttachmentPoint(char axis, int direction)
        {
            var attach = state.GetFrontCarryAttachPoint();
            if (attach == null)
            {
                state.ShowMessage("FrontCarry attachment point not found");
                return;
            }

            var shiftDown = api.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft];
            var ctrlDown = api.Input.KeyboardKeyState[(int)GlKeys.ControlLeft];

            if (state.CurrentMode == AdjustmentMode.Rotation)
            {
                var amount = GetModifierAmount(shiftDown, ctrlDown, fine: 0.01, medium: 0.1, coarse: 1.0, veryCoarse: 10.0);
                double result = axis switch
                {
                    'X' => attach.RotationX = Math.Round(attach.RotationX + direction * amount, 2),
                    'Y' => attach.RotationY = Math.Round(attach.RotationY + direction * amount, 2),
                    'Z' => attach.RotationZ = Math.Round(attach.RotationZ + direction * amount, 2),
                    _ => 0
                };
                state.ShowMessage($"FrontCarry AP {axis}: {result} ({state.CurrentMode})");
            }
            else
            {
                var amount = GetModifierAmount(shiftDown, ctrlDown, fine: 0.001, medium: 0.01, coarse: 0.1, veryCoarse: 1.0);
                double result = axis switch
                {
                    'X' => attach.PosX = Math.Round(attach.PosX + direction * amount, 4),
                    'Y' => attach.PosY = Math.Round(attach.PosY + direction * amount, 4),
                    'Z' => attach.PosZ = Math.Round(attach.PosZ + direction * amount, 4),
                    _ => 0
                };
                state.ShowMessage($"FrontCarry AP {axis}: {result} ({state.CurrentMode})");
            }
        }

        private float CalculateAdjustmentAmount()
        {
            var shiftDown = api.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft];
            var ctrlDown = api.Input.KeyboardKeyState[(int)GlKeys.ControlLeft];

            if (state.CurrentMode == AdjustmentMode.Rotation)
                return (float)GetModifierAmount(shiftDown, ctrlDown, fine: 0.01, medium: 0.1, coarse: 1.0, veryCoarse: 10.0);

            return (float)GetModifierAmount(shiftDown, ctrlDown, fine: 0.0001, medium: 0.001, coarse: 0.01, veryCoarse: 0.1);
        }

        private static double GetModifierAmount(bool shiftDown, bool ctrlDown, double fine, double medium, double coarse, double veryCoarse)
        {
            if (shiftDown && ctrlDown) return fine;
            if (shiftDown) return medium;
            if (ctrlDown) return coarse;
            return veryCoarse;
        }

        private static float AdjustTranslation(ModelTransform transform, char axis, float amount, int direction)
        {
            return axis switch
            {
                'X' => transform.Translation.X = float.Round(transform.Translation.X + direction * amount, 4),
                'Y' => transform.Translation.Y = float.Round(transform.Translation.Y + direction * amount, 4),
                'Z' => transform.Translation.Z = float.Round(transform.Translation.Z + direction * amount, 4),
                _ => 0f
            };
        }

        private float AdjustScale(ModelTransform transform, char axis, float amount, int direction)
        {
            if (state.ScaleAllAxesMode)
            {
                var scaleX = transform.ScaleXYZ.X;
                var scaleY = transform.ScaleXYZ.Y;
                var scaleZ = transform.ScaleXYZ.Z;

                var referenceScale = axis switch
                {
                    'X' => scaleX,
                    'Y' => scaleY,
                    'Z' => scaleZ,
                    _ => scaleX
                };

                if (Math.Abs(referenceScale) < 0.0001f)
                {
                    scaleX = float.Round(scaleX + direction * amount, 4);
                    scaleY = float.Round(scaleY + direction * amount, 4);
                    scaleZ = float.Round(scaleZ + direction * amount, 4);
                    transform.ScaleXYZ = new Vec3f(scaleX, scaleY, scaleZ);
                    return axis switch { 'X' => scaleX, 'Y' => scaleY, 'Z' => scaleZ, _ => scaleX };
                }

                var newReferenceScale = float.Round(referenceScale + direction * amount, 4);
                var scaleFactor = newReferenceScale / referenceScale;
                scaleX = float.Round(scaleX * scaleFactor, 4);
                scaleY = float.Round(scaleY * scaleFactor, 4);
                scaleZ = float.Round(scaleZ * scaleFactor, 4);
                transform.ScaleXYZ = new Vec3f(scaleX, scaleY, scaleZ);
                return axis switch { 'X' => scaleX, 'Y' => scaleY, 'Z' => scaleZ, _ => scaleX };
            }

            return axis switch
            {
                'X' => transform.ScaleXYZ.X = float.Round(transform.ScaleXYZ.X + direction * amount, 4),
                'Y' => transform.ScaleXYZ.Y = float.Round(transform.ScaleXYZ.Y + direction * amount, 4),
                'Z' => transform.ScaleXYZ.Z = float.Round(transform.ScaleXYZ.Z + direction * amount, 4),
                _ => 0f
            };
        }

        private static float AdjustRotation(ModelTransform transform, char axis, float amount, int direction)
        {
            return axis switch
            {
                'X' => transform.Rotation.X = float.Round(transform.Rotation.X + direction * amount, 1),
                'Y' => transform.Rotation.Y = float.Round(transform.Rotation.Y + direction * amount, 1),
                'Z' => transform.Rotation.Z = float.Round(transform.Rotation.Z + direction * amount, 1),
                _ => 0f
            };
        }

        private static float AdjustOrigin(ModelTransform transform, char axis, float amount, int direction)
        {
            return axis switch
            {
                'X' => transform.Origin.X = float.Round(transform.Origin.X + direction * amount, 4),
                'Y' => transform.Origin.Y = float.Round(transform.Origin.Y + direction * amount, 4),
                'Z' => transform.Origin.Z = float.Round(transform.Origin.Z + direction * amount, 4),
                _ => 0f
            };
        }
    }
}
