using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using static CarryOn.Common.Behaviors.BlockBehaviorCarryable;
using System.Text.RegularExpressions;
using Vintagestory.API.MathTools;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;

namespace CarryOn.Common.Handlers
{
    // This is a Client side handler class for adjusting the placement of straps and other items on the player's back
    // Hotkeys: 
    //      V will be to select Translation mode
    //      B will be to select Rotation mode
    //      N will be to select Scale mode
    //      , will be to select Origin mode
    //      PageUp/PageDown will adjust the Y axis
    //      Left/Right cursor keys will adjust the X axis
    //      Up/Down cursor keys will adjust the Z axis
    //      ;/' keys will adjust the amount of change applied (this will be different depending on the selected mode)
    //      [] keys will adjust the strap index
    //      \ will write the current values to a log file
    //      Insert will add a new strap
    //      Delete will remove the selected strap
    //      End will toggle the enabled state of the selected transform
    //      Home will toggle between backpack and hands
    /// <summary>
    /// This class is responsible for adjusting the placement of straps and other items on the player's back.
    /// The class will check the backpackType the player is carrying and select the group of transforms on the carried block behavior for that backpack type.
    /// Any change to values, mode or strap index will be confirmed on screen using the on screen error trigger
    /// </summary>

    public class PackAdjustmentHandler(ICoreClientAPI api, CarrySystem carrySystem)
    {

        private enum AdjustmentMode { Translation, Rotation, Scale, Origin }
        private enum AdjustmentTarget { Back, Hands, FrontCarryAttachment }
        private enum TransformScope { Parent, Child, Label }

        private readonly ICoreClientAPI api = api;
        private readonly CarrySystem carrySystem = carrySystem;


        private AdjustmentMode currentMode = AdjustmentMode.Translation;
        private AdjustmentTarget currentTarget = AdjustmentTarget.Back;
        private TransformScope currentTransformScope = TransformScope.Parent;
        private bool scaleAllAxesMode = false;
        private int transformIndex = 0;

        private Block block = null;

        private string transformsGroup = null;
        private CarrySlot carrySlot = CarrySlot.Back;

        private TransformSettings[] transformSettings = null;
        private ModelTransform selectedLabelTransform = null;
        private readonly List<string> availableChildGroups = new();
        private int childGroupIndex = -1;
        private string selectedChildGroup = null;

        public void InitClient()
        {
            // Initialize client-side specific functionality here
            RegisterKeybinds();
        }

        private void RegisterKeybinds()
        {
            var input = api.Input;
            input.RegisterHotKey("carryon-packadj-translation", "Pack Adjust: Translation Mode", GlKeys.V, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-rotation", "Pack Adjust: Rotation Mode", GlKeys.B, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-scale", "Pack Adjust: Scale Mode", GlKeys.N, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-origin", "Pack Adjust: Origin Mode", GlKeys.Comma, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-y-plus", "Pack Adjust: Y Axis Plus", GlKeys.PageUp, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-y-minus", "Pack Adjust: Y Axis Minus", GlKeys.PageDown, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-x-plus", "Pack Adjust: X Axis Plus", GlKeys.Left, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-x-minus", "Pack Adjust: X Axis Minus", GlKeys.Right, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-z-plus", "Pack Adjust: Z Axis Plus", GlKeys.Up, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-z-minus", "Pack Adjust: Z Axis Minus", GlKeys.Down, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-amount-plus", "Pack Adjust: Increase Amount", GlKeys.Quote, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-amount-minus", "Pack Adjust: Decrease Amount", GlKeys.Semicolon, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-transform-next", "Pack Adjust: Next Transform", GlKeys.BracketRight, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-transform-prev", "Pack Adjust: Previous Transform", GlKeys.BracketLeft, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-log", "Pack Adjust: Log Values", GlKeys.BackSlash, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-insert", "Pack Adjust: Add Transform", GlKeys.Insert, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-delete", "Pack Adjust: Remove Transform", GlKeys.Delete, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-toggle", "Pack Adjust: Toggle Transform", GlKeys.End, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-togglebackpack", "Pack Adjust: Toggle Backpack/Hands", GlKeys.Home, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey("carryon-packadj-togglescope", "Pack Adjust: Toggle Transform Scope", GlKeys.P, HotkeyType.GUIOrOtherControls);

            input.SetHotKeyHandler("carryon-packadj-translation", (kc) => { SetMode(AdjustmentMode.Translation); return true; });
            input.SetHotKeyHandler("carryon-packadj-rotation", (kc) => { SetMode(AdjustmentMode.Rotation); return true; });
            input.SetHotKeyHandler("carryon-packadj-scale", (kc) => { SetMode(AdjustmentMode.Scale); return true; });
            input.SetHotKeyHandler("carryon-packadj-origin", (kc) => { SetMode(AdjustmentMode.Origin); return true; });
            input.SetHotKeyHandler("carryon-packadj-y-plus", (kc) => { AdjustAxis('Y', +1); return true; });
            input.SetHotKeyHandler("carryon-packadj-y-minus", (kc) => { AdjustAxis('Y', -1); return true; });
            input.SetHotKeyHandler("carryon-packadj-x-plus", (kc) => { AdjustAxis('X', +1); return true; });
            input.SetHotKeyHandler("carryon-packadj-x-minus", (kc) => { AdjustAxis('X', -1); return true; });
            input.SetHotKeyHandler("carryon-packadj-z-plus", (kc) => { AdjustAxis('Z', +1); return true; });
            input.SetHotKeyHandler("carryon-packadj-z-minus", (kc) => { AdjustAxis('Z', -1); return true; });
            input.SetHotKeyHandler("carryon-packadj-amount-plus", (kc) => { AdjustAmount(+1); return true; });
            input.SetHotKeyHandler("carryon-packadj-amount-minus", (kc) => { AdjustAmount(-1); return true; });
            input.SetHotKeyHandler("carryon-packadj-transform-next", (kc) => { AdjustTransformIndex(+1); return true; });
            input.SetHotKeyHandler("carryon-packadj-transform-prev", (kc) => { AdjustTransformIndex(-1); return true; });
            input.SetHotKeyHandler("carryon-packadj-log", (kc) => { LogCurrentValues(); return true; });
            input.SetHotKeyHandler("carryon-packadj-insert", (kc) => { AddTransform(); return true; });
            input.SetHotKeyHandler("carryon-packadj-delete", (kc) => { RemoveTransform(); return true; });
            input.SetHotKeyHandler("carryon-packadj-toggle", (kc) => { ToggleTransform(); return true; });
            input.SetHotKeyHandler("carryon-packadj-togglebackpack", (kc) => { ToggleCarrySlot(); return true; });
            input.SetHotKeyHandler("carryon-packadj-togglescope", (kc) => { ToggleTransformScope(); return true; });

            api.Event.RegisterGameTickListener(OnGameTick, 100);
        }

        private void ToggleCarrySlot()
        {
            switch (currentTarget)
            {
                case AdjustmentTarget.Back:
                    currentTarget = AdjustmentTarget.Hands;
                    this.carrySlot = CarrySlot.Hands;
                    ShowOnScreen("Carry Slot: Hands");
                    break;
                case AdjustmentTarget.Hands:
                    currentTarget = AdjustmentTarget.FrontCarryAttachment;
                    ShowOnScreen("Mode: FrontCarry Attachment Point");
                    break;
                default:
                    currentTarget = AdjustmentTarget.Back;
                    this.carrySlot = CarrySlot.Back;
                    ShowOnScreen("Carry Slot: Back");
                    break;
            }

            availableChildGroups.Clear();
            childGroupIndex = -1;
            selectedChildGroup = null;
        }

        private void OnGameTick(float obj)
        {
            UpdateOnChange();
        }

        private void ToggleTransformScope()
        {
            var includeLabelScope = false;
            CarriedBlock carried = null;
            BlockBehaviorCarryable carryBehavior = null;
            var entityPlayer = api?.World?.Player?.Entity;

            if (TryGetCurrentCarried(out carried, out carryBehavior))
            {
                includeLabelScope = HasCarriedLabel(carried, carryBehavior);
            }

            var baseTransformsGroup = entityPlayer?.ResolveCarryTransformGroupBase(carrySystem, carrySlot);

            if (currentTransformScope == TransformScope.Parent)
            {
                currentTransformScope = TransformScope.Child;
                if (carried != null && carryBehavior != null && !string.IsNullOrEmpty(baseTransformsGroup))
                {
                    RefreshAvailableChildGroups(carried, carryBehavior, baseTransformsGroup);
                }
            }
            else if (currentTransformScope == TransformScope.Child)
            {
                if (!AdvanceChildGroup())
                {
                    currentTransformScope = includeLabelScope ? TransformScope.Label : TransformScope.Parent;
                    if (currentTransformScope != TransformScope.Child)
                    {
                        availableChildGroups.Clear();
                        selectedChildGroup = null;
                        childGroupIndex = -1;
                    }
                }
            }
            else
            {
                currentTransformScope = TransformScope.Parent;
                availableChildGroups.Clear();
                selectedChildGroup = null;
                childGroupIndex = -1;
            }

            transformsGroup = null;
            if (currentTransformScope != TransformScope.Label)
            {
                selectedLabelTransform = null;
            }

            ShowOnScreen($"Transform Scope: {currentTransformScope} | {GetCurrentTransformInfo()}");
        }

        private bool TryGetCurrentCarried(out CarriedBlock carried, out BlockBehaviorCarryable carryBehavior)
        {
            carried = api?.World?.Player?.Entity?.GetCarried(this.carrySlot);
            carryBehavior = carried?.GetCarryableBehavior();
            return carried != null && carryBehavior != null;
        }

        private bool HasCarriedLabel(CarriedBlock carried, BlockBehaviorCarryable carryBehavior)
        {
            if (carried == null || carryBehavior?.LabelRenderSettings?.Transform == null)
            {
                return false;
            }

            var beData = carried.BlockEntityData;
            var text = beData?.GetString("text", null);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var labelStack = beData?.GetItemstack("labelStack", null);
            labelStack?.ResolveBlockOrItem(api.World);
            return labelStack?.Collectible != null;
        }

        private void RefreshAvailableChildGroups(CarriedBlock carried, BlockBehaviorCarryable carryBehavior, string baseTransformsGroup)
        {
            availableChildGroups.Clear();
            selectedChildGroup = null;
            childGroupIndex = -1;

            var resolvedBaseGroup = baseTransformsGroup;
            CarriedGroupResolution resolverResolution = null;

            var transformGroupResolvers = carrySystem?.CarryManager?.GetTransformGroupResolvers();
            if (transformGroupResolvers != null)
            {
                var requestedResolverCode = carryBehavior?.RenderTransformResolver;
                foreach (var resolver in transformGroupResolvers)
                {
                    if (resolver == null) continue;

                    if (!string.IsNullOrEmpty(requestedResolverCode)
                        && !requestedResolverCode.Equals(resolver.ResolverCode, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!resolver.TryResolve(this.api, carried, resolvedBaseGroup, out var resolution) || resolution == null)
                    {
                        continue;
                    }

                    resolverResolution = resolution;
                    if (resolution.PrimaryGroupCandidates != null && resolution.PrimaryGroupCandidates.Count > 0)
                    {
                        resolvedBaseGroup = resolution.PrimaryGroupCandidates[0];
                    }
                    else if (!string.IsNullOrEmpty(resolution.PrimaryGroup))
                    {
                        resolvedBaseGroup = resolution.PrimaryGroup;
                    }

                    break;
                }
            }

            if (resolverResolution?.AdditionalGroupCandidates == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidateSet in resolverResolution.AdditionalGroupCandidates)
            {
                if (candidateSet?.Groups == null) continue;

                foreach (var group in candidateSet.Groups)
                {
                    if (string.IsNullOrEmpty(group)) continue;
                    if (!carryBehavior.TransformGroupExists(carried, group)) continue;
                    if (!seen.Add(group)) continue;

                    availableChildGroups.Add(group);
                }
            }

            if (availableChildGroups.Count > 0)
            {
                childGroupIndex = 0;
                selectedChildGroup = availableChildGroups[0];
            }
        }

        private bool AdvanceChildGroup()
        {
            if (availableChildGroups.Count == 0)
            {
                return false;
            }

            var nextIndex = childGroupIndex + 1;
            if (nextIndex >= availableChildGroups.Count)
            {
                return false;
            }

            childGroupIndex = nextIndex;
            selectedChildGroup = availableChildGroups[childGroupIndex];
            return true;
        }

        private string GetCurrentTransformInfo()
        {
            if (currentTransformScope == TransformScope.Label)
            {
                var labelItem = "unavailable";
                var carried = api?.World?.Player?.Entity?.GetCarried(this.carrySlot);
                var behavior = carried?.GetCarryableBehavior();
                if (HasCarriedLabel(carried, behavior))
                {
                    var text = carried?.BlockEntityData?.GetString("text", null);
                    labelItem = !string.IsNullOrWhiteSpace(text) ? "label-text" : "label-icon";
                }

                return selectedLabelTransform == null
                    ? "id: labelTransform, item: unavailable"
                    : $"id: labelTransform, item: {labelItem}";
            }

            if (currentTransformScope == TransformScope.Child && !string.IsNullOrEmpty(selectedChildGroup))
            {
                return $"group: {selectedChildGroup}";
            }

            if (transformSettings == null || transformSettings.Length == 0 || transformIndex < 0 || transformIndex >= transformSettings.Length)
                return "No transform";
            var ts = transformSettings[transformIndex];
            string id = ts.Id != null ? $"id: {ts.Id}" : "id: (none)";
            string asset = !string.IsNullOrEmpty(ts.AssetName) ? $"item: {ts.AssetName}" : "item: (none)";
            return $"{id}, {asset}";
        }

        private AttachmentPoint GetFrontCarryAttachPoint()
        {
            var apPose = api?.World?.Player?.Entity?.AnimManager?.Animator?.GetAttachmentPointPose("carryon:FrontCarry");
            return apPose?.AttachPoint;
        }

        private void UpdateOnChange()
        {
            if (currentTarget == AdjustmentTarget.FrontCarryAttachment) return;

            bool hasChanged = false;
            var entityPlayer = api?.World?.Player?.Entity;
            var carried = entityPlayer?.GetCarried(this.carrySlot);
            if (carried == null)
            {
                // No item carried in the current slot
                return;
            }
            if (carried?.Block != this.block)
            {
                ShowOnScreen("Carried Block changed");
                this.block = carried.Block;

                hasChanged = true;
                availableChildGroups.Clear();
                childGroupIndex = -1;
                selectedChildGroup = null;
            }

            var baseTransformsGroup = entityPlayer?.ResolveCarryTransformGroupBase(carrySystem, carrySlot);
            var carryBehavior = carried.GetCarryableBehavior();
            if (carryBehavior == null)
            {
                ShowOnScreen("No carryable behavior found");
                return;
            }

            if (currentTransformScope == TransformScope.Label)
            {
                if (!HasCarriedLabel(carried, carryBehavior))
                {
                    currentTransformScope = TransformScope.Parent;
                    selectedLabelTransform = null;
                    this.transformsGroup = null;
                    ShowOnScreen("Label transform unavailable, switched to Parent scope");
                }
                else
                {
                    selectedLabelTransform = carryBehavior.LabelRenderSettings?.Transform;
                    transformSettings = null;

                    if (this.transformsGroup != "labelTransform")
                    {
                        this.transformsGroup = "labelTransform";
                        hasChanged = true;
                    }

                    if (hasChanged)
                    {
                        SetTransformIndex(0);
                    }

                    return;
                }
            }

            string transformsGroup = ResolveTransformsGroup(carried, carryBehavior, baseTransformsGroup);

            if (!carryBehavior.TransformGroupExists(carried, transformsGroup))
            {
                transformsGroup = baseTransformsGroup;
            }

            if (!carryBehavior.TransformGroupExists(carried, transformsGroup))
            {
                transformsGroup = "default";
            }

            if (hasChanged || transformsGroup != this.transformsGroup)
            {
                this.transformsGroup = transformsGroup;
                hasChanged = true;
                ShowOnScreen("Transform Group: " + transformsGroup);
            }

            if (hasChanged)
            {

                var carriedSlot = carryBehavior.Slots[carried.Slot];
                if (carriedSlot == null)
                {
                    ShowOnScreen("Invalid carry slot");
                    return;
                }

                carryBehavior.ResolvedTransformGroups.TryGetValue(transformsGroup, out this.transformSettings);
                if (this.transformSettings == null || this.transformSettings.Length == 0)
                {
                    this.transformSettings = [new TransformSettings() { Transform = carryBehavior.DefaultTransform.Clone() }];
                }

                SetTransformIndex(0);
            }
        }

        private string ResolveTransformsGroup(CarriedBlock carried, BlockBehaviorCarryable carryBehavior, string baseTransformsGroup)
        {
            var resolvedBaseGroup = baseTransformsGroup;
            CarriedGroupResolution resolverResolution = null;
            var primaryGroupCandidates = new List<string> { baseTransformsGroup };

            var transformGroupResolvers = carrySystem?.CarryManager?.GetTransformGroupResolvers();
            if (transformGroupResolvers != null)
            {
                var requestedResolverCode = carryBehavior?.RenderTransformResolver;
                foreach (var resolver in transformGroupResolvers)
                {
                    if (resolver == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(requestedResolverCode)
                        && !requestedResolverCode.Equals(resolver.ResolverCode, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!resolver.TryResolve(this.api, carried, resolvedBaseGroup, out var resolution) || resolution == null)
                    {
                        continue;
                    }

                    resolverResolution = resolution;
                    if (resolution.PrimaryGroupCandidates != null && resolution.PrimaryGroupCandidates.Count > 0)
                    {
                        primaryGroupCandidates = new List<string>(resolution.PrimaryGroupCandidates);
                        resolvedBaseGroup = resolution.PrimaryGroupCandidates[0];
                    }
                    else if (!string.IsNullOrEmpty(resolution.PrimaryGroup))
                    {
                        primaryGroupCandidates = new List<string> { resolution.PrimaryGroup };
                        resolvedBaseGroup = resolution.PrimaryGroup;
                    }

                    break;
                }
            }

            var resolvedPrimaryGroup = ResolvePrimaryGroupFromCandidates(carried, carryBehavior, primaryGroupCandidates, resolvedBaseGroup);

            if (currentTransformScope == TransformScope.Parent)
            {
                return resolvedPrimaryGroup;
            }

            if (currentTransformScope == TransformScope.Child)
            {
                if (availableChildGroups.Count == 0 || string.IsNullOrEmpty(selectedChildGroup))
                {
                    RefreshAvailableChildGroups(carried, carryBehavior, baseTransformsGroup);
                }

                if (!string.IsNullOrEmpty(selectedChildGroup) && carryBehavior.TransformGroupExists(carried, selectedChildGroup))
                {
                    return selectedChildGroup;
                }

                if (availableChildGroups.Count > 0)
                {
                    childGroupIndex = Math.Clamp(childGroupIndex, 0, availableChildGroups.Count - 1);
                    selectedChildGroup = availableChildGroups[childGroupIndex];
                    return selectedChildGroup;
                }
            }

            if (resolverResolution?.AdditionalGroupCandidates != null)
            {
                foreach (var candidateSet in resolverResolution.AdditionalGroupCandidates)
                {
                    if (candidateSet?.Groups == null) continue;

                    foreach (var group in candidateSet.Groups)
                    {
                        if (string.IsNullOrEmpty(group)) continue;
                        if (carryBehavior.TransformGroupExists(carried, group))
                        {
                            return group;
                        }
                    }
                }
            }

            return resolvedPrimaryGroup;
        }

        private static string ResolvePrimaryGroupFromCandidates(
            CarriedBlock carried,
            BlockBehaviorCarryable carryBehavior,
            IList<string> primaryGroupCandidates,
            string fallbackGroup)
        {
            if (carryBehavior == null)
            {
                return fallbackGroup;
            }

            if (primaryGroupCandidates != null)
            {
                foreach (var candidate in primaryGroupCandidates)
                {
                    if (string.IsNullOrEmpty(candidate)) continue;

                    var resolvedCandidate = carryBehavior.GetTransformGroupName(carried, candidate, checkExists: false) ?? candidate;
                    if (carryBehavior.TransformGroupExists(carried, resolvedCandidate))
                    {
                        return resolvedCandidate;
                    }
                }
            }

            var resolvedFallback = carryBehavior.GetTransformGroupName(carried, fallbackGroup, checkExists: false) ?? fallbackGroup;
            if (carryBehavior.TransformGroupExists(carried, resolvedFallback))
            {
                return resolvedFallback;
            }

            return fallbackGroup;
        }

        private void ToggleTransform()
        {
            if (currentTransformScope == TransformScope.Label)
            {
                ShowOnScreen("labelTransform has no enabled flag");
                return;
            }

            if (transformSettings != null && transformSettings.Length > 0)
            {
                transformSettings[transformIndex].Enabled = !transformSettings[transformIndex].Enabled;
                InvalidateCarryRendererCaches();

                var enabledState = transformSettings[transformIndex].Enabled ?? true ? "enabled" : "disabled";
                ShowOnScreen($"Transform {transformIndex + 1} {enabledState} | {GetCurrentTransformInfo()}");
            }
        }

        private void SetMode(AdjustmentMode mode)
        {
            if (mode == AdjustmentMode.Scale && currentMode == AdjustmentMode.Scale)
            {
                scaleAllAxesMode = !scaleAllAxesMode;
                ShowOnScreen("Mode: Scale (" + (scaleAllAxesMode ? "All Axes" : "Single Axis") + ")");
                return;
            }

            currentMode = mode;
            if (currentMode != AdjustmentMode.Scale)
            {
                ShowOnScreen("Mode: " + mode);
                return;
            }

            ShowOnScreen("Mode: Scale (" + (scaleAllAxesMode ? "All Axes" : "Single Axis") + ")");
        }

        private void AdjustAttachmentPoint(char axis, int direction)
        {
            var attach = GetFrontCarryAttachPoint();
            if (attach == null) { ShowOnScreen("FrontCarry attachment point not found"); return; }

            var shiftDown = api.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft];
            var ctrlDown = api.Input.KeyboardKeyState[(int)GlKeys.ControlLeft];

            double result;
            if (currentMode == AdjustmentMode.Rotation)
            {
                double amount;
                if (shiftDown && ctrlDown) amount = 0.01;
                else if (shiftDown) amount = 0.1;
                else if (ctrlDown) amount = 1.0;
                else amount = 10.0;

                result = axis switch
                {
                    'X' => attach.RotationX = Math.Round(attach.RotationX + direction * amount, 2),
                    'Y' => attach.RotationY = Math.Round(attach.RotationY + direction * amount, 2),
                    'Z' => attach.RotationZ = Math.Round(attach.RotationZ + direction * amount, 2),
                    _ => 0
                };
            }
            else
            {
                double amount;
                if (shiftDown && ctrlDown) amount = 0.001;
                else if (shiftDown) amount = 0.01;
                else if (ctrlDown) amount = 0.1;
                else amount = 1.0;

                result = axis switch
                {
                    'X' => attach.PosX = Math.Round(attach.PosX + direction * amount, 4),
                    'Y' => attach.PosY = Math.Round(attach.PosY + direction * amount, 4),
                    'Z' => attach.PosZ = Math.Round(attach.PosZ + direction * amount, 4),
                    _ => 0
                };
            }

            ShowOnScreen($"FrontCarry AP {axis}: {result} ({currentMode})");
        }

        private void AdjustAxis(char axis, int direction)
        {
            if (currentTarget == AdjustmentTarget.FrontCarryAttachment)
            {
                AdjustAttachmentPoint(axis, direction);
                return;
            }

            var isLabelScope = currentTransformScope == TransformScope.Label;
            var setting = transformSettings?[transformIndex];
            if (!isLabelScope && setting == null) return;

            var transform = isLabelScope ? selectedLabelTransform : setting.Transform;
            if (transform == null)
            {
                // Label scope has no fallback source; keep current behavior.
                if (isLabelScope) return;

                // If a transform entry omitted translation/rotation/scale/origin in JSON,
                // it can flatten to null. Seed from defaults so adjustment can edit it.
                var currentBehavior = api?.World?.Player?.Entity?.GetCarried(this.carrySlot)?.GetCarryableBehavior();
                var defaultTransform = currentBehavior?.DefaultTransform ?? BlockBehaviorCarryable.DefaultBlockTransform;
                transform = defaultTransform.Clone();
                setting.Transform = transform;
            }

            float amount;
            var result = 0f;

            var shiftDown = api.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft];
            var ctrlDown = api.Input.KeyboardKeyState[(int)GlKeys.ControlLeft];

            if (currentMode == AdjustmentMode.Rotation)
            {
                if (shiftDown && ctrlDown) amount = 0.01F;
                else if (shiftDown) amount = 0.1F;
                else if (ctrlDown) amount = 1F;
                else amount = 10F;
            }
            else
            {
                if (shiftDown && ctrlDown) amount = 0.0001F;
                else if (shiftDown) amount = 0.001F;
                else if (ctrlDown) amount = 0.01F;
                else amount = 0.1F;
            }

            switch (currentMode)
            {
                case AdjustmentMode.Translation:
                    switch (axis)
                    {
                        case 'X':
                            result = float.Round(transform.Translation.X + direction * amount, 4);
                            transform.Translation.X = result;
                            break;
                        case 'Y':
                            result = float.Round(transform.Translation.Y + direction * amount, 4);
                            transform.Translation.Y = result;
                            break;
                        case 'Z':
                            result = float.Round(transform.Translation.Z + direction * amount, 4);
                            transform.Translation.Z = result;
                            break;
                    }
                    break;

                case AdjustmentMode.Scale:
                    if (scaleAllAxesMode)
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
                            // Degenerate ratio case: fallback to equal additive scaling
                            scaleX = float.Round(scaleX + direction * amount, 4);
                            scaleY = float.Round(scaleY + direction * amount, 4);
                            scaleZ = float.Round(scaleZ + direction * amount, 4);

                            transform.ScaleXYZ = new Vec3f(scaleX, scaleY, scaleZ);

                            result = axis switch
                            {
                                'X' => scaleX,
                                'Y' => scaleY,
                                'Z' => scaleZ,
                                _ => scaleX
                            };
                            break;
                        }

                        var newReferenceScale = float.Round(referenceScale + direction * amount, 4);
                        var scaleFactor = newReferenceScale / referenceScale;

                        scaleX = float.Round(scaleX * scaleFactor, 4);
                        scaleY = float.Round(scaleY * scaleFactor, 4);
                        scaleZ = float.Round(scaleZ * scaleFactor, 4);

                        transform.ScaleXYZ = new Vec3f(scaleX, scaleY, scaleZ);

                        result = axis switch
                        {
                            'X' => scaleX,
                            'Y' => scaleY,
                            'Z' => scaleZ,
                            _ => scaleX
                        };
                        break;
                    }

                    switch (axis)
                    {
                        case 'X':
                            result = float.Round(transform.ScaleXYZ.X + direction * amount, 4);
                            transform.ScaleXYZ.X = result;
                            break;
                        case 'Y':
                            result = float.Round(transform.ScaleXYZ.Y + direction * amount, 4);
                            transform.ScaleXYZ.Y = result;
                            break;
                        case 'Z':
                            result = float.Round(transform.ScaleXYZ.Z + direction * amount, 4);
                            transform.ScaleXYZ.Z = result;
                            break;
                    }
                    break;

                case AdjustmentMode.Rotation:
                    switch (axis)
                    {
                        case 'X':
                            result = float.Round(transform.Rotation.X + direction * amount, 1);
                            transform.Rotation.X = result;
                            break;
                        case 'Y':
                            result = float.Round(transform.Rotation.Y + direction * amount, 1);
                            transform.Rotation.Y = result;
                            break;
                        case 'Z':
                            result = float.Round(transform.Rotation.Z + direction * amount, 1);
                            transform.Rotation.Z = result;
                            break;
                    }
                    break;

                case AdjustmentMode.Origin:
                    switch (axis)
                    {
                        case 'X':
                            result = float.Round(transform.Origin.X + direction * amount, 4);
                            transform.Origin.X = result;
                            break;
                        case 'Y':
                            result = float.Round(transform.Origin.Y + direction * amount, 4);
                            transform.Origin.Y = result;
                            break;
                        case 'Z':
                            result = float.Round(transform.Origin.Z + direction * amount, 4);
                            transform.Origin.Z = result;
                            break;
                    }
                    break;
            }

            if (isLabelScope)
            {
                selectedLabelTransform = transform;
            }

            var behavior = api?.World?.Player?.Entity?.GetCarried(this.carrySlot)?.GetCarryableBehavior();
            if (behavior != null)
            {
                if (isLabelScope)
                {
                    behavior.LabelRenderSettings ??= new LabelRenderSettings();
                    behavior.LabelRenderSettings.Transform = selectedLabelTransform;
                }
                else
                {
                    behavior.ResolvedTransformGroups[this.transformsGroup] = transformSettings;
                }

                InvalidateCarryRendererCaches();
            }
            ShowOnScreen($"Adjust {axis} {result} ({currentMode})");
        }

        private void AdjustAmount(int direction)
        {
            // TODO: Implement amount adjustment logic
            ShowOnScreen($"Amount {(direction > 0 ? "+" : "-")}");
        }

        private void AdjustTransformIndex(int direction)
        {
            if (currentTarget == AdjustmentTarget.FrontCarryAttachment) { ShowOnScreen("No transform index in FrontCarry mode"); return; }
            if (currentTransformScope == TransformScope.Label) { ShowOnScreen("Label scope has a single transform"); return; }
            if (this.transformSettings == null || this.transformSettings.Length == 0) return;
            SetTransformIndex(Math.Clamp(this.transformIndex + direction, 0, this.transformSettings.Length - 1));
        }

        private void SetTransformIndex(int index)
        {
            if (currentTransformScope == TransformScope.Label)
            {
                this.transformIndex = 0;
                ShowOnScreen($"Transform: 1/1 | {GetCurrentTransformInfo()}");
                return;
            }

            this.transformIndex = Math.Clamp(index, 0, this.transformSettings.Length - 1);
            ShowOnScreen($"Transform: {this.transformIndex + 1}/{this.transformSettings.Length} | {GetCurrentTransformInfo()}");
        }


        private void LogAttachmentPointValues()
        {
            var attach = GetFrontCarryAttachPoint();
            if (attach == null) { ShowOnScreen("FrontCarry attachment point not found"); return; }

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
            var localPath = Path.Combine("ModData", CarryCode.ModId, "pack-adjustment");
            var modDataDir = api.GetOrCreateDataPath(localPath);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string filename = $"{timestamp}-frontcarry-attachpoint.json";
            File.WriteAllText(Path.Combine(modDataDir, filename), json);
            ShowOnScreen($"FrontCarry attachment point logged to {filename}");
        }

        private void LogCurrentValues()
        {
            if (currentTarget == AdjustmentTarget.FrontCarryAttachment)
            {
                LogAttachmentPointValues();
                return;
            }

            if (currentTransformScope == TransformScope.Label)
            {
                var behavior = api?.World?.Player?.Entity?.GetCarried(this.carrySlot)?.GetCarryableBehavior();
                var labelTransform = selectedLabelTransform ?? behavior?.LabelRenderSettings?.Transform;
                if (labelTransform == null)
                {
                    ShowOnScreen("No labelTransform to log.");
                    return;
                }

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
                {
                    entry["scale"] = sx;
                }
                else
                {
                    entry["scale"] = new float[] { sx, sy, sz };
                }

                const float epsilon = 0.0001F;
                bool isOriginDefault =
                    Math.Abs(labelTransform.Origin.X - 0.5F) < epsilon &&
                    Math.Abs(labelTransform.Origin.Y - 0.5F) < epsilon &&
                    Math.Abs(labelTransform.Origin.Z - 0.5F) < epsilon;
                if (!isOriginDefault)
                {
                    entry["origin"] = new float[] {
                        labelTransform.Origin.X,
                        labelTransform.Origin.Y,
                        labelTransform.Origin.Z
                    };
                }

                var labelOutput = new Dictionary<string, object>
                {
                    ["labelTransform"] = entry
                };

                string jsonLabel = JsonConvert.SerializeObject(labelOutput, Formatting.Indented);
                jsonLabel = Regex.Replace(jsonLabel, @"\[\s*([\d\.,\s\-eE]+)\s*\]", m =>
                {
                    string content = Regex.Replace(m.Groups[1].Value, @"\s+", " ");
                    return "[" + content.Trim() + "]";
                });

                var localPathLabel = Path.Combine("ModData", CarryCode.ModId, "pack-adjustment");
                var modDataDirLabel = api.GetOrCreateDataPath(localPathLabel);
                string timestampLabel = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var blockCodeLabel = this.block?.Code?.ToShortString() ?? "unknown";
                string filenameLabel = $"{timestampLabel}-{blockCodeLabel}-labelTransform.json".Replace(':', '+');
                string filePathLabel = Path.Combine(modDataDirLabel, filenameLabel);
                File.WriteAllText(filePathLabel, jsonLabel);
                ShowOnScreen($"Label transform logged to {filenameLabel}");
                return;
            }

            // Traverse transformSettings and write the current values as a json file in the ModData directory
            if (transformSettings == null || transformSettings.Length == 0)
            {
                ShowOnScreen("No transform settings to log.");
                return;
            }

            var list = new List<object>();
            foreach (var ts in transformSettings)
            {
                var entry = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(ts.Id))
                {
                    entry["id"] = ts.Id;    
                }

                if (!string.IsNullOrEmpty(ts.AssetName))
                {
                    entry["item"] = ts.AssetName;
                }

                // Only write enabled if it is false (default is true)
                if (!(ts.Enabled ?? true))
                {
                    entry["enabled"] = ts.Enabled;
                }

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
                // If scale is uniform, write as float, else as array
                var sx = ts.Transform.ScaleXYZ.X;
                var sy = ts.Transform.ScaleXYZ.Y;
                var sz = ts.Transform.ScaleXYZ.Z;
                if (Math.Abs(sx - sy) < 0.0001f && Math.Abs(sx - sz) < 0.0001f)
                {
                    entry["scale"] = sx;
                }
                else
                {
                    entry["scale"] = new float[] { sx, sy, sz };
                }

                // Only write origin if it is not the default (0.5, 0.5, 0.5)
                const float epsilon = 0.0001F;
                bool isOriginDefault =
                    Math.Abs(ts.Transform.Origin.X - 0.5F) < epsilon &&
                    Math.Abs(ts.Transform.Origin.Y - 0.5F) < epsilon &&
                    Math.Abs(ts.Transform.Origin.Z - 0.5F) < epsilon;
                if (!isOriginDefault)
                {
                    entry["origin"] = new float[] {
                        ts.Transform.Origin.X,
                        ts.Transform.Origin.Y,
                        ts.Transform.Origin.Z
                    };
                }                
                list.Add(entry);
            }

            var output = new Dictionary<string, object>
            {
                [this.transformsGroup] = list
            };

            string json = JsonConvert.SerializeObject(output, Formatting.Indented);

            // Use regex to reformat arrays to single line
            json = Regex.Replace(json, @"\[\s*([\d\.,\s\-eE]+)\s*\]", m =>
            {
                // Remove newlines and extra spaces inside the array
                string content = Regex.Replace(m.Groups[1].Value, @"\s+", " ");
                return "[" + content.Trim() + "]";
            });

            // Get ModData directory (client-side)
            var localPath = Path.Combine("ModData", CarryCode.ModId, "pack-adjustment");
            var modDataDir = api.GetOrCreateDataPath(localPath);

            // Timestamp string
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

            string filename = $"{timestamp}-{this.block.Code.ToShortString()}-{this.transformsGroup}.json".Replace(':', '+');
            string filePath = Path.Combine(modDataDir, filename);
            File.WriteAllText(filePath, json);
            ShowOnScreen($"Transform settings logged to {filename} : {list.Count} entries");

        }

        private BlockBehaviorCarryable GetCarryableBehavior(CarrySlot carrySlot)
        {
            var carried = api?.World?.Player?.Entity?.GetCarried(carrySlot);
            if (carried == null)
            {
                ShowOnScreen($"[PackAdjustmentHandler] GetCarryableBehavior: No item carried in {carrySlot.ToString()} slot.");
                return null;
            }

            var carryBehavior = carried.GetCarryableBehavior();
            if (carryBehavior == null)
            {
                ShowOnScreen($"[PackAdjustmentHandler] GetCarryableBehavior: Carried item does not have a carryable behavior.");
                return null;
            }

            return carryBehavior;
        }

        private SlotSettings GetCarriedSlotSettings(CarrySlot carrySlot)
        {
            var carried = api?.World?.Player?.Entity?.GetCarried(carrySlot);
            if (carried == null)
            {
                ShowOnScreen($"[PackAdjustmentHandler] GetCarriedSlot: No item carried in {carrySlot.ToString()} slot.");
                return null;
            }

            var carryBehavior = carried.GetCarryableBehavior();

            var slotSettings = carryBehavior.Slots[carried.Slot];
            return slotSettings;
        }

        private void InvalidateCarryRendererCaches()
        {
            carrySystem?.EntityCarryRenderer?.InvalidateRenderCaches();
        }


        private void AddTransform()
        {
            if (currentTransformScope == TransformScope.Label)
            {
                ShowOnScreen("Cannot add labelTransform entries");
                return;
            }

            var altDown = api.Input.KeyboardKeyState[(int)GlKeys.AltLeft];
            var shiftDown = api.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft];

            if (!altDown)
            {
                ShowOnScreen("Hold ALT to add a new transform - Also hold SHIFT to duplicate selected transform");
                return;
            }

            // Append a new strap to the transformSettings
            if (this.transformSettings == null)
            {
                this.transformSettings = [];
            }

            TransformSettings transformSetting;

            if (shiftDown)
            {
                // Duplicate the current transform
                var currentSettings = this.transformSettings[this.transformIndex];

                transformSetting = currentSettings.Clone();
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

                transformSetting = new TransformSettings() { AssetName = "carryon:strap", Transform = transform, Enabled = true };
            }

            Array.Resize(ref this.transformSettings, this.transformSettings.Length + 1);
            this.transformSettings[this.transformSettings.Length - 1] = transformSetting;

            var behavior = GetCarryableBehavior(this.carrySlot);

            var carriedSlot = GetCarriedSlotSettings(this.carrySlot);
            if (carriedSlot == null)
            {
                ShowOnScreen("[PackAdjustmentHandler] AddTransform: carriedSlot is null, aborting.");
                return;
            }

            behavior.ResolvedTransformGroups[this.transformsGroup] = transformSettings;
            InvalidateCarryRendererCaches();
            ShowOnScreen($"Transform added as index {this.transformSettings.Length - 1}");
        }

        private void RemoveTransform()
        {
            if (currentTransformScope == TransformScope.Label)
            {
                ShowOnScreen("Cannot remove labelTransform");
                return;
            }

            var altDown = api.Input.KeyboardKeyState[(int)GlKeys.AltLeft];
            if (!altDown)
            {
                ShowOnScreen("Hold ALT to remove a transform");
                return;
            }

            if (transformIndex == 0)
            {
                ShowOnScreen("Cannot remove the first transform");
                return;
            }
            // Remove current transform index from transformSettings (except for the first one)
            if (transformSettings != null && transformIndex > 0 && transformIndex < transformSettings.Length)
            {
                var newSettings = new TransformSettings[transformSettings.Length - 1];
                int newIndex = 0;
                for (int i = 0; i < transformSettings.Length; i++)
                {
                    if (i != transformIndex)
                    {
                        newSettings[newIndex++] = transformSettings[i];
                    }
                }
                transformSettings = newSettings;
            }            // Set the value to ensure it is clamped to the array size
            SetTransformIndex(this.transformIndex);

            var behavior = GetCarryableBehavior(this.carrySlot);
            behavior.ResolvedTransformGroups[this.transformsGroup] = transformSettings;
            InvalidateCarryRendererCaches();

            ShowOnScreen("Transform removed");
        }

        private void ShowOnScreen(string message)
        {
            api?.World?.Player?.ShowChatNotification(message);
        }
    }
}