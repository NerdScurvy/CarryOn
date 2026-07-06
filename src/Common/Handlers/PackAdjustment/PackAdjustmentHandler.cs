using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Client.Logic.CarryRenderer;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CarryOn.Common.Handlers.PackAdjustment
{
    public class PackAdjustmentHandler(ICoreClientAPI api, ICarryManager? carryManager, CarryOnConfig? config, EntityCarryRenderer? entityCarryRenderer)
    {
        internal enum AdjustmentMode { Translation, Rotation, Scale, Origin }
        internal enum AdjustmentTarget { Back, Hands, FrontCarryAttachment }
        internal enum TransformScope { Parent, Child, Label }

        private readonly ICoreClientAPI api = api;
        private readonly ICarryManager? carryManager = carryManager;
        private readonly CarryOnConfig? config = config;
        private readonly EntityCarryRenderer? entityCarryRenderer = entityCarryRenderer;

        private AdjustmentMode currentMode = AdjustmentMode.Translation;
        private AdjustmentTarget currentTarget = AdjustmentTarget.Back;
        private TransformScope currentTransformScope = TransformScope.Parent;
        private bool scaleAllAxesMode;
        private int transformIndex;

        private Block? block;
        private string? transformsGroup;
        private CarrySlot carrySlot = CarrySlot.Back;
        private TransformSettings[]? transformSettings;
        private ModelTransform? selectedLabelTransform;
        private int selectedLabelTransformIndex;

        internal PackAdjustmentTransformResolver resolver = null!;

        // Exposed state for sub-services
        internal AdjustmentMode CurrentMode => currentMode;
        internal AdjustmentTarget CurrentTarget => currentTarget;
        internal TransformScope CurrentTransformScope => currentTransformScope;
        internal bool ScaleAllAxesMode => scaleAllAxesMode;
        internal int TransformIndex => transformIndex;
        internal Block? Block => block;
        internal string? TransformsGroup => transformsGroup;
        internal CarrySlot CarrySlot => carrySlot;
        internal TransformSettings[]? TransformSettings { get => transformSettings; set => transformSettings = value; }
        internal ModelTransform? SelectedLabelTransform { get => selectedLabelTransform; set => selectedLabelTransform = value; }
        internal int SelectedLabelTransformIndex { get => selectedLabelTransformIndex; set => selectedLabelTransformIndex = value; }

        private PackAdjustmentEditor editor = null!;
        private PackAdjustmentLogger logger = null!;

        public void InitClient()
        {
            resolver = new PackAdjustmentTransformResolver(api, carryManager);
            editor = new PackAdjustmentEditor(api, carryManager, this);
            logger = new PackAdjustmentLogger(api, carryManager, this);
            RegisterKeybinds();
            RegisterChatCommands();
        }

        public void RegisterChatCommands()
        {
            api.ChatCommands.Create("packadj")
                .BeginSubCommand("setid")
                    .WithDescription("Set the id of the currently selected transform")
                    .WithArgs(api.ChatCommands.Parsers.Word("id"))
                    .HandleWith(args => editor.SetCurrentTransformId((string)args[0]!))
                .EndSubCommand();
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
            input.SetHotKeyHandler("carryon-packadj-y-plus", (kc) => { editor.AdjustAxis('Y', +1); return true; });
            input.SetHotKeyHandler("carryon-packadj-y-minus", (kc) => { editor.AdjustAxis('Y', -1); return true; });
            input.SetHotKeyHandler("carryon-packadj-x-plus", (kc) => { editor.AdjustAxis('X', +1); return true; });
            input.SetHotKeyHandler("carryon-packadj-x-minus", (kc) => { editor.AdjustAxis('X', -1); return true; });
            input.SetHotKeyHandler("carryon-packadj-z-plus", (kc) => { editor.AdjustAxis('Z', +1); return true; });
            input.SetHotKeyHandler("carryon-packadj-z-minus", (kc) => { editor.AdjustAxis('Z', -1); return true; });
            input.SetHotKeyHandler("carryon-packadj-transform-next", (kc) => { editor.AdjustTransformIndex(+1); return true; });
            input.SetHotKeyHandler("carryon-packadj-transform-prev", (kc) => { editor.AdjustTransformIndex(-1); return true; });
            input.SetHotKeyHandler("carryon-packadj-log", (kc) => { logger.LogCurrentValues(); return true; });
            input.SetHotKeyHandler("carryon-packadj-insert", (kc) => { editor.AddTransform(); return true; });
            input.SetHotKeyHandler("carryon-packadj-delete", (kc) => { editor.RemoveTransform(); return true; });
            input.SetHotKeyHandler("carryon-packadj-toggle", (kc) => { editor.ToggleTransform(); return true; });
            input.SetHotKeyHandler("carryon-packadj-togglebackpack", (kc) => { ToggleCarrySlot(); return true; });
            input.SetHotKeyHandler("carryon-packadj-togglescope", (kc) => { ToggleTransformScope(); return true; });

            api.Event.RegisterGameTickListener(OnGameTick, 100);
        }

        private void OnGameTick(float _) => UpdateOnChange();

        private void ToggleCarrySlot()
        {
            switch (currentTarget)
            {
                case AdjustmentTarget.Back:
                    currentTarget = AdjustmentTarget.Hands;
                    carrySlot = CarrySlot.Hands;
                    ShowMessage("Carry Slot: Hands");
                    break;
                case AdjustmentTarget.Hands:
                    currentTarget = AdjustmentTarget.FrontCarryAttachment;
                    ShowMessage("Mode: FrontCarry Attachment Point");
                    break;
                default:
                    currentTarget = AdjustmentTarget.Back;
                    carrySlot = CarrySlot.Back;
                    ShowMessage("Carry Slot: Back");
                    break;
            }

            resolver.ClearChildGroups();
        }

        private void ToggleTransformScope()
        {
            var includeLabelScope = false;
            if (TryGetCurrentCarried(out var carried, out var carryBehavior))
                includeLabelScope = PackAdjustmentTransformResolver.HasCarriedLabel(api, carried, carryBehavior);

            var entityPlayer = api.World.Player.Entity;
            var baseTransformsGroup = entityPlayer?.ResolveCarryTransformGroupBase(config!, carrySlot);

            switch (currentTransformScope)
            {
                case TransformScope.Parent:
                    currentTransformScope = TransformScope.Child;
                    if (carried != null && carryBehavior != null && !string.IsNullOrEmpty(baseTransformsGroup))
                        resolver.RefreshAvailableChildGroups(carried, carryBehavior, baseTransformsGroup);
                    break;
                case TransformScope.Child:
                    if (!resolver.AdvanceChildGroup())
                    {
                        currentTransformScope = includeLabelScope ? TransformScope.Label : TransformScope.Parent;
                        if (currentTransformScope != TransformScope.Child)
                        {
                            resolver.ClearChildGroups();
                        }
                    }
                    break;
                default:
                    currentTransformScope = TransformScope.Parent;
                    resolver.ClearChildGroups();
                    break;
            }

            transformsGroup = null;
            if (currentTransformScope != TransformScope.Label)
            {
                selectedLabelTransform = null;
                selectedLabelTransformIndex = 0;
            }

            ShowMessage($"Transform Scope: {currentTransformScope} | {GetCurrentTransformInfo()}");
        }

        private void SetMode(AdjustmentMode mode)
        {
            if (mode == AdjustmentMode.Scale && currentMode == AdjustmentMode.Scale)
            {
                scaleAllAxesMode = !scaleAllAxesMode;
                ShowMessage("Mode: Scale (" + (scaleAllAxesMode ? "All Axes" : "Single Axis") + ")");
                return;
            }

            currentMode = mode;
            ShowMessage("Mode: " + (mode == AdjustmentMode.Scale
                ? "Scale (" + (scaleAllAxesMode ? "All Axes" : "Single Axis") + ")"
                : mode.ToString()));
        }

        private void UpdateOnChange()
        {
            if (currentTarget == AdjustmentTarget.FrontCarryAttachment) return;

            bool hasChanged = false;
            var entityPlayer = api.World.Player.Entity;
            var carried =             carryManager?.GetCarried(entityPlayer!, carrySlot);
            if (carried == null) return;

            if (carried.Block != block)
            {
                ShowMessage("Carried Block changed");
                block = carried.Block;
                hasChanged = true;
                resolver.ClearChildGroups();
            }

            var baseTransformsGroup = entityPlayer?.ResolveCarryTransformGroupBase(config!, carrySlot);
            var carryBehavior = carried.GetCarryableBehavior();
            if (carryBehavior == null)
            {
                ShowMessage("No carryable behavior found");
                return;
            }

            if (currentTransformScope == TransformScope.Label)
            {
                UpdateLabelScope(carried, carryBehavior, ref hasChanged);
                return;
            }

            string resolvedGroup = resolver.ResolveTransformsGroup(carried, carryBehavior, baseTransformsGroup!, currentTransformScope);

            if (!carryBehavior.TransformGroupExists(carried, resolvedGroup))
                resolvedGroup = baseTransformsGroup!;
            if (!carryBehavior.TransformGroupExists(carried, resolvedGroup))
                resolvedGroup = CarryCode.DefaultTransformGroup;

            if (hasChanged || resolvedGroup != transformsGroup)
            {
                transformsGroup = resolvedGroup;
                hasChanged = true;
                ShowMessage("Transform Group: " + transformsGroup);
            }

            if (hasChanged)
            {
                var carriedSlot = carryBehavior.Slots[carried.Slot];
                if (carriedSlot == null)
                {
                    ShowMessage("Invalid carry slot");
                    return;
                }

                carryBehavior.ResolvedTransformGroups.TryGetValue(transformsGroup, out transformSettings);
                if (transformSettings == null || transformSettings.Length == 0)
                    transformSettings = [new TransformSettings() { Transform = carryBehavior.DefaultTransform.Clone() }];

                SetTransformIndex(0);
            }
        }

        private void UpdateLabelScope(CarriedBlock carried, BlockBehaviorCarryable carryBehavior, ref bool hasChanged)
        {
            if (!PackAdjustmentTransformResolver.HasCarriedLabel(api, carried, carryBehavior))
            {
                currentTransformScope = TransformScope.Parent;
                selectedLabelTransform = null;
                selectedLabelTransformIndex = 0;
                transformsGroup = null;
                ShowMessage("Label transform unavailable, switched to Parent scope");
                return;
            }

            var labelSettings = carryBehavior.LabelRenderSettings;
            var labelCount = PackAdjustmentTransformResolver.GetLabelTransformCount(labelSettings);
            if (labelCount <= 0)
            {
                currentTransformScope = TransformScope.Parent;
                selectedLabelTransform = null;
                selectedLabelTransformIndex = 0;
                transformsGroup = null;
                ShowMessage("Label transform unavailable, switched to Parent scope");
                return;
            }

            selectedLabelTransformIndex = Math.Clamp(selectedLabelTransformIndex, 0, labelCount - 1);
            selectedLabelTransform = PackAdjustmentTransformResolver.GetLabelTransformAt(labelSettings, selectedLabelTransformIndex);
            transformSettings = null;

            if (transformsGroup != "labelTransform")
            {
                transformsGroup = "labelTransform";
                hasChanged = true;
            }

            if (hasChanged)
                SetTransformIndex(selectedLabelTransformIndex);
        }

        internal void SetTransformIndex(int index)
        {
            if (currentTransformScope == TransformScope.Label)
            {
                var behavior = carryManager?.GetCarried(api.World.Player.Entity, carrySlot)?.GetCarryableBehavior();
                var labelSettings = behavior?.LabelRenderSettings;
                var labelCount = PackAdjustmentTransformResolver.GetLabelTransformCount(labelSettings);

                if (labelCount <= 0)
                {
                    selectedLabelTransformIndex = 0;
                    selectedLabelTransform = null;
                    ShowMessage("Transform: 1/1 | id: labelTransform, item: unavailable");
                    return;
                }

                selectedLabelTransformIndex = Math.Clamp(index, 0, labelCount - 1);
                selectedLabelTransform = PackAdjustmentTransformResolver.GetLabelTransformAt(labelSettings, selectedLabelTransformIndex);
                transformIndex = selectedLabelTransformIndex;
                ShowMessage($"Transform: {selectedLabelTransformIndex + 1}/{labelCount} | {GetCurrentTransformInfo()}");
                return;
            }

            transformIndex = Math.Clamp(index, 0, transformSettings!.Length - 1);
            ShowMessage($"Transform: {transformIndex + 1}/{transformSettings.Length} | {GetCurrentTransformInfo()}");
        }

        internal string GetCurrentTransformInfo()
        {
            if (currentTransformScope == TransformScope.Label)
            {
                var labelItem = "unavailable";
                var carried = carryManager?.GetCarried(api.World.Player.Entity, carrySlot);
                var behavior = carried?.GetCarryableBehavior();
                var labelCount = PackAdjustmentTransformResolver.GetLabelTransformCount(behavior?.LabelRenderSettings);
                if (PackAdjustmentTransformResolver.HasCarriedLabel(api!, carried, behavior))
                {
                    var text = carried?.BlockEntityData?.GetString("text", null);
                    labelItem = !string.IsNullOrWhiteSpace(text) ? "label-text" : "label-icon";
                }
                return selectedLabelTransform == null
                    ? $"id: labelTransform {selectedLabelTransformIndex + 1}/{Math.Max(1, labelCount)}, item: unavailable"
                    : $"id: labelTransform {selectedLabelTransformIndex + 1}/{Math.Max(1, labelCount)}, item: {labelItem}";
            }

            if (currentTransformScope == TransformScope.Child && !string.IsNullOrEmpty(resolver.SelectedChildGroup))
                return $"group: {resolver.SelectedChildGroup}";

            if (transformSettings == null || transformSettings.Length == 0 || transformIndex < 0 || transformIndex >= transformSettings.Length)
                return "No transform";

            var ts = transformSettings[transformIndex];
            string id = ts.Id != null ? $"id: {ts.Id}" : "id: (none)";
            string asset = !string.IsNullOrEmpty(ts.AssetName) ? $"item: {ts.AssetName}" : "item: (none)";
            return $"{id}, {asset}";
        }

        internal AttachmentPoint? GetFrontCarryAttachPoint()
        {
            var apPose = api.World.Player.Entity?.AnimManager?.Animator?.GetAttachmentPointPose(CarryCode.FrontCarryAttachmentPoint);
            return apPose?.AttachPoint;
        }

        internal bool TryGetCurrentCarried(out CarriedBlock? carried, out BlockBehaviorCarryable? carryBehavior)
        {
            carried = carryManager?.GetCarried(api.World.Player.Entity, carrySlot);
            carryBehavior = carried?.GetCarryableBehavior();
            return carried != null && carryBehavior != null;
        }

        internal BlockBehaviorCarryable? GetCarryableBehavior(CarrySlot slot)
        {
            var carried = carryManager?.GetCarried(api.World.Player.Entity, slot);
            if (carried == null)
            {
                ShowMessage($"[PackAdjustmentHandler] GetCarryableBehavior: No item carried in {slot} slot.");
                return null;
            }

            var carryBehavior = carried.GetCarryableBehavior();
            if (carryBehavior == null)
                ShowMessage($"[PackAdjustmentHandler] GetCarryableBehavior: Carried item does not have a carryable behavior.");

            return carryBehavior;
        }

        internal SlotSettings? GetCarriedSlotSettings(CarrySlot slot)
        {
            var carried = carryManager?.GetCarried(api.World.Player.Entity, slot);
            if (carried == null)
            {
                ShowMessage($"[PackAdjustmentHandler] GetCarriedSlot: No item carried in {slot} slot.");
                return null;
            }

            return carried.GetCarryableBehavior()?.Slots[carried.Slot];
        }

        internal void InvalidateCaches()
        {
            entityCarryRenderer?.InvalidateRenderCaches();
        }

        internal void ShowMessage(string message)
        {
            api?.World?.Player?.ShowChatNotification(message);
        }
    }
}
