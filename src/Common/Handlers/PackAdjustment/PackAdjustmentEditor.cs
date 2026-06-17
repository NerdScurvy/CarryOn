using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Behaviors;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Common.Handlers.PackAdjustment
{
    internal sealed class PackAdjustmentEditor(ICoreClientAPI api, ICarryManager? carryManager, PackAdjustmentHandler handler)
    {

        public TextCommandResult SetCurrentTransformId(string newId)
        {
            if (handler.CurrentTransformScope == PackAdjustmentHandler.TransformScope.Label)
                return TextCommandResult.Error("Cannot set id for labelTransform.");
            if (handler.TransformSettings == null || handler.TransformSettings.Length == 0)
                return TextCommandResult.Error("No transform selected.");
            if (handler.TransformsGroup == null)
                return TextCommandResult.Error("No transform group selected.");

            handler.TransformSettings[handler.TransformIndex] = handler.TransformSettings[handler.TransformIndex] with { Id = newId };

            var behavior = handler.GetCarryableBehavior(handler.CarrySlot);
            if (behavior != null)
            {
                behavior.ResolvedTransformGroups[handler.TransformsGroup] = handler.TransformSettings;
                handler.InvalidateCaches();
            }

            handler.ShowMessage($"Transform {handler.TransformIndex + 1} id set to '{newId}'");
            return TextCommandResult.Success();
        }

        public void AdjustAxis(char axis, int direction)
        {
            if (handler.CurrentTarget == PackAdjustmentHandler.AdjustmentTarget.FrontCarryAttachment)
            {
                AdjustAttachmentPoint(axis, direction);
                return;
            }

            var isLabelScope = handler.CurrentTransformScope == PackAdjustmentHandler.TransformScope.Label;
            var setting = handler.TransformSettings?[handler.TransformIndex];
            if (!isLabelScope && setting == null) return;

            var transform = isLabelScope ? handler.SelectedLabelTransform : setting!.Transform;
            if (transform == null)
            {
                if (isLabelScope) return;

                var currentBehavior = carryManager?.GetCarried(api?.World?.Player?.Entity!, handler.CarrySlot)?.GetCarryableBehavior();
                var defaultTransform = currentBehavior?.DefaultTransform ?? BlockBehaviorCarryable.DefaultBlockTransform;

                transform = defaultTransform.Clone();
                var ts = handler.TransformSettings;
                if (ts == null) return;
                ts[handler.TransformIndex] = ts[handler.TransformIndex] with
                {
                    Transform = transform
                };
            }

            float amount = CalculateAdjustmentAmount();
            float result = handler.CurrentMode switch
            {
                PackAdjustmentHandler.AdjustmentMode.Translation => AdjustTranslation(transform, axis, amount, direction),
                PackAdjustmentHandler.AdjustmentMode.Scale => AdjustScale(transform, axis, amount, direction),
                PackAdjustmentHandler.AdjustmentMode.Rotation => AdjustRotation(transform, axis, amount, direction),
                PackAdjustmentHandler.AdjustmentMode.Origin => AdjustOrigin(transform, axis, amount, direction),
                _ => 0f
            };

            if (isLabelScope)
            {
                handler.SelectedLabelTransform = transform;
            }

            var behavior = carryManager?.GetCarried(api?.World?.Player?.Entity!, handler.CarrySlot)?.GetCarryableBehavior();
            if (behavior != null)
            {
                if (isLabelScope)
                {
                    behavior.LabelRenderSettings ??= new LabelRenderSettings();
                    PackAdjustmentTransformResolver.SetLabelTransformAt(behavior.LabelRenderSettings, handler.SelectedLabelTransformIndex, handler.SelectedLabelTransform);
                }
                else
                {
                    behavior.ResolvedTransformGroups[handler.TransformsGroup!] = handler.TransformSettings!;
                }

                handler.InvalidateCaches();
            }
            handler.ShowMessage($"Adjust {axis} {result} ({handler.CurrentMode})");
        }

        public void AdjustTransformIndex(int direction)
        {
            if (handler.CurrentTarget == PackAdjustmentHandler.AdjustmentTarget.FrontCarryAttachment)
            {
                handler.ShowMessage("No transform index in FrontCarry mode");
                return;
            }

            if (handler.CurrentTransformScope == PackAdjustmentHandler.TransformScope.Label)
            {
                var behavior = carryManager?.GetCarried(api?.World?.Player?.Entity!, handler.CarrySlot)?.GetCarryableBehavior();
                var labelCount = PackAdjustmentTransformResolver.GetLabelTransformCount(behavior?.LabelRenderSettings);
                if (labelCount <= 0)
                {
                    handler.ShowMessage("No label transform available");
                    return;
                }

                handler.SetTransformIndex(Math.Clamp(handler.SelectedLabelTransformIndex + direction, 0, labelCount - 1));
                return;
            }

            if (handler.TransformSettings == null || handler.TransformSettings.Length == 0) return;
            handler.SetTransformIndex(Math.Clamp(handler.TransformIndex + direction, 0, handler.TransformSettings.Length - 1));
        }

        public void ToggleTransform()
        {
            if (handler.CurrentTransformScope == PackAdjustmentHandler.TransformScope.Label)
            {
                handler.ShowMessage("labelTransform has no enabled flag");
                return;
            }

            if (handler.TransformSettings != null && handler.TransformSettings.Length > 0)
            {
                handler.TransformSettings[handler.TransformIndex] = handler.TransformSettings[handler.TransformIndex] with
                {
                    Enabled = !(handler.TransformSettings[handler.TransformIndex].Enabled ?? true)
                };
                handler.InvalidateCaches();

                var enabledState = handler.TransformSettings[handler.TransformIndex].Enabled ?? true ? "enabled" : "disabled";
                handler.ShowMessage($"Transform {handler.TransformIndex + 1} {enabledState} | {handler.GetCurrentTransformInfo()}");
            }
        }

        public void AddTransform()
        {
            if (handler.CurrentTransformScope == PackAdjustmentHandler.TransformScope.Label)
            {
                handler.ShowMessage("Cannot add labelTransform entries");
                return;
            }

            var altDown = api.Input.KeyboardKeyState[(int)GlKeys.AltLeft];
            var shiftDown = api.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft];

            if (!altDown)
            {
                handler.ShowMessage("Hold ALT to add a new transform - Also hold SHIFT to duplicate selected transform");
                return;
            }

            if (handler.TransformSettings == null)
            {
                handler.TransformSettings = [];
            }

            TransformSettings transformSetting;
            if (shiftDown)
            {
                var currentSettings = handler.TransformSettings[handler.TransformIndex];
                transformSetting = currentSettings.DeepClone();
            }
            else
            {
                var transform = new ModelTransform()
                {
                    Translation = new Vec3f(0, 0, 0),
                    Rotation = new Vec3f(0, 0, 0),
                    ScaleXYZ = new Vec3f(1, 1, 1),
                    Origin = new Vec3f(0.5F, 0.5F, 0.5F)
                };

                transformSetting = new TransformSettings()
                {
                    Id = $"strap{handler.TransformSettings.Length + 1}",
                    AssetName = "carryon:strap",
                    AssetType = EnumAssetType.Item,
                    Transform = transform,
                    Enabled = true
                };
            }

            var temp = handler.TransformSettings;
            Array.Resize(ref temp, temp.Length + 1);
            temp[temp.Length - 1] = transformSetting;
            handler.TransformSettings = temp;

            var behavior = handler.GetCarryableBehavior(handler.CarrySlot)!;
            var carriedSlot = handler.GetCarriedSlotSettings(handler.CarrySlot);
            if (carriedSlot == null)
            {
                handler.ShowMessage("[PackAdjustmentHandler] AddTransform: carriedSlot is null, aborting.");
                return;
            }

            behavior.ResolvedTransformGroups[handler.TransformsGroup!] = handler.TransformSettings;
            handler.InvalidateCaches();
            handler.ShowMessage($"Transform added as index {handler.TransformSettings.Length}");
        }

        public void RemoveTransform()
        {
            if (handler.CurrentTransformScope == PackAdjustmentHandler.TransformScope.Label)
            {
                handler.ShowMessage("Cannot remove labelTransform");
                return;
            }

            var altDown = api.Input.KeyboardKeyState[(int)GlKeys.AltLeft];
            if (!altDown)
            {
                handler.ShowMessage("Hold ALT to remove a transform");
                return;
            }

            if (handler.TransformIndex == 0)
            {
                handler.ShowMessage("Cannot remove the first transform");
                return;
            }

            if (handler.TransformSettings != null && handler.TransformIndex > 0 && handler.TransformIndex < handler.TransformSettings.Length)
            {
                var newSettings = new TransformSettings[handler.TransformSettings.Length - 1];
                int newIndex = 0;
                for (int i = 0; i < handler.TransformSettings.Length; i++)
                {
                    if (i != handler.TransformIndex)
                    {
                        newSettings[newIndex++] = handler.TransformSettings[i];
                    }
                }
                handler.TransformSettings = newSettings;
            }

            if (handler.TransformSettings == null || handler.TransformSettings.Length == 0)
            {
                handler.ShowMessage("No transform settings available");
                return;
            }

            if (string.IsNullOrEmpty(handler.TransformsGroup))
            {
                handler.ShowMessage("No transform group selected");
                return;
            }

            handler.SetTransformIndex(handler.TransformIndex);

            var behavior = handler.GetCarryableBehavior(handler.CarrySlot);
            if (behavior == null)
            {
                handler.ShowMessage("No carryable behavior found");
                return;
            }

            behavior.ResolvedTransformGroups[handler.TransformsGroup] = handler.TransformSettings;
            handler.InvalidateCaches();
            handler.ShowMessage("Transform removed");
        }

        private void AdjustAttachmentPoint(char axis, int direction)
        {
            var attach = handler.GetFrontCarryAttachPoint();
            if (attach == null)
            {
                handler.ShowMessage("FrontCarry attachment point not found");
                return;
            }

            var shiftDown = api.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft];
            var ctrlDown = api.Input.KeyboardKeyState[(int)GlKeys.ControlLeft];

            if (handler.CurrentMode == PackAdjustmentHandler.AdjustmentMode.Rotation)
            {
                var amount = GetModifierAmount(shiftDown, ctrlDown, fine: 0.01, medium: 0.1, coarse: 1.0, veryCoarse: 10.0);
                double result = axis switch
                {
                    'X' => attach.RotationX = Math.Round(attach.RotationX + direction * amount, 2),
                    'Y' => attach.RotationY = Math.Round(attach.RotationY + direction * amount, 2),
                    'Z' => attach.RotationZ = Math.Round(attach.RotationZ + direction * amount, 2),
                    _ => 0
                };
                handler.ShowMessage($"FrontCarry AP {axis}: {result} ({handler.CurrentMode})");
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
                handler.ShowMessage($"FrontCarry AP {axis}: {result} ({handler.CurrentMode})");
            }
        }

        private float CalculateAdjustmentAmount()
        {
            var shiftDown = api.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft];
            var ctrlDown = api.Input.KeyboardKeyState[(int)GlKeys.ControlLeft];

            if (handler.CurrentMode == PackAdjustmentHandler.AdjustmentMode.Rotation)
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
            if (handler.ScaleAllAxesMode)
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
