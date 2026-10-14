using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Client.Logic.CarryRenderer;
using CarryOn.Client.Logic.TransformTemplates;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Client;

namespace CarryOn.Client.Handlers.PackAdjustment
{
    public class PackAdjustmentHandler
    {
        private const string HotKeyTranslation = "carryon-packadj-translation";
        private const string HotKeyRotation = "carryon-packadj-rotation";
        private const string HotKeyScale = "carryon-packadj-scale";
        private const string HotKeyOrigin = "carryon-packadj-origin";
        private const string HotKeyYPlus = "carryon-packadj-y-plus";
        private const string HotKeyYMinus = "carryon-packadj-y-minus";
        private const string HotKeyXPlus = "carryon-packadj-x-plus";
        private const string HotKeyXMinus = "carryon-packadj-x-minus";
        private const string HotKeyZPlus = "carryon-packadj-z-plus";
        private const string HotKeyZMinus = "carryon-packadj-z-minus";
        private const string HotKeyAmountPlus = "carryon-packadj-amount-plus";
        private const string HotKeyAmountMinus = "carryon-packadj-amount-minus";
        private const string HotKeyTransformNext = "carryon-packadj-transform-next";
        private const string HotKeyTransformPrev = "carryon-packadj-transform-prev";
        private const string HotKeyLog = "carryon-packadj-log";
        private const string HotKeyInsert = "carryon-packadj-insert";
        private const string HotKeyDelete = "carryon-packadj-delete";
        private const string HotKeyToggle = "carryon-packadj-toggle";
        private const string HotKeyToggleBackpack = "carryon-packadj-togglebackpack";
        private const string HotKeyToggleScope = "carryon-packadj-togglescope";

        private readonly ICoreClientAPI api;
        private readonly ICarryManager? carryManager;
        private readonly CarryOnConfig? config;
        private readonly PackAdjustmentState state;

        private PackAdjustmentEditor editor = null!;
        private PackAdjustmentLogger logger = null!;

        internal TransformTemplateManager? TemplateManager
        {
            get => state.TemplateManager;
            set => state.TemplateManager = value;
        }

        public PackAdjustmentHandler(ICoreClientAPI api, ICarryManager? carryManager, CarryOnConfig? config, EntityCarryRenderer? entityCarryRenderer)
        {
            this.api = api;
            this.carryManager = carryManager;
            this.config = config;
            state = new PackAdjustmentState(api, carryManager, entityCarryRenderer);
        }

        public void InitClient()
        {
            editor = new PackAdjustmentEditor(api, state);
            logger = new PackAdjustmentLogger(api, state);
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
            input.RegisterHotKey(HotKeyTranslation, "Pack Adjust: Translation Mode", GlKeys.V, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyRotation, "Pack Adjust: Rotation Mode", GlKeys.B, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyScale, "Pack Adjust: Scale Mode", GlKeys.N, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyOrigin, "Pack Adjust: Origin Mode", GlKeys.Comma, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyYPlus, "Pack Adjust: Y Axis Plus", GlKeys.PageUp, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyYMinus, "Pack Adjust: Y Axis Minus", GlKeys.PageDown, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyXPlus, "Pack Adjust: X Axis Plus", GlKeys.Left, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyXMinus, "Pack Adjust: X Axis Minus", GlKeys.Right, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyZPlus, "Pack Adjust: Z Axis Plus", GlKeys.Up, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyZMinus, "Pack Adjust: Z Axis Minus", GlKeys.Down, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyAmountPlus, "Pack Adjust: Increase Amount", GlKeys.Quote, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyAmountMinus, "Pack Adjust: Decrease Amount", GlKeys.Semicolon, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyTransformNext, "Pack Adjust: Next Transform", GlKeys.BracketRight, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyTransformPrev, "Pack Adjust: Previous Transform", GlKeys.BracketLeft, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyLog, "Pack Adjust: Log Values", GlKeys.BackSlash, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyInsert, "Pack Adjust: Add Transform", GlKeys.Insert, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyDelete, "Pack Adjust: Remove Transform", GlKeys.Delete, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyToggle, "Pack Adjust: Toggle Transform", GlKeys.End, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyToggleBackpack, "Pack Adjust: Toggle Backpack/Hands", GlKeys.Home, HotkeyType.GUIOrOtherControls);
            input.RegisterHotKey(HotKeyToggleScope, "Pack Adjust: Toggle Transform Scope", GlKeys.P, HotkeyType.GUIOrOtherControls);

            input.SetHotKeyHandler(HotKeyTranslation, (kc) => { SetMode(AdjustmentMode.Translation); return true; });
            input.SetHotKeyHandler(HotKeyRotation, (kc) => { SetMode(AdjustmentMode.Rotation); return true; });
            input.SetHotKeyHandler(HotKeyScale, (kc) => { SetMode(AdjustmentMode.Scale); return true; });
            input.SetHotKeyHandler(HotKeyOrigin, (kc) => { SetMode(AdjustmentMode.Origin); return true; });
            input.SetHotKeyHandler(HotKeyYPlus, (kc) => { editor.AdjustAxis('Y', +1); return true; });
            input.SetHotKeyHandler(HotKeyYMinus, (kc) => { editor.AdjustAxis('Y', -1); return true; });
            input.SetHotKeyHandler(HotKeyXPlus, (kc) => { editor.AdjustAxis('X', +1); return true; });
            input.SetHotKeyHandler(HotKeyXMinus, (kc) => { editor.AdjustAxis('X', -1); return true; });
            input.SetHotKeyHandler(HotKeyZPlus, (kc) => { editor.AdjustAxis('Z', +1); return true; });
            input.SetHotKeyHandler(HotKeyZMinus, (kc) => { editor.AdjustAxis('Z', -1); return true; });
            input.SetHotKeyHandler(HotKeyTransformNext, (kc) => { editor.AdjustTransformIndex(+1); return true; });
            input.SetHotKeyHandler(HotKeyTransformPrev, (kc) => { editor.AdjustTransformIndex(-1); return true; });
            input.SetHotKeyHandler(HotKeyLog, (kc) => { logger.LogCurrentValues(); return true; });
            input.SetHotKeyHandler(HotKeyInsert, (kc) => { editor.AddTransform(); return true; });
            input.SetHotKeyHandler(HotKeyDelete, (kc) => { editor.RemoveTransform(); return true; });
            input.SetHotKeyHandler(HotKeyToggle, (kc) => { editor.ToggleTransform(); return true; });
            input.SetHotKeyHandler(HotKeyToggleBackpack, (kc) => { ToggleCarrySlot(); return true; });
            input.SetHotKeyHandler(HotKeyToggleScope, (kc) => { ToggleTransformScope(); return true; });

            api.Event.RegisterGameTickListener(OnGameTick, 100);
        }

        private void OnGameTick(float _) => UpdateOnChange();

        private void ToggleCarrySlot()
        {
            if (state.FrontCarryAttachmentMode)
            {
                state.FrontCarryAttachmentMode = false;
                state.CarrySlot = CarrySlot.Back;
                state.ShowMessage("Carry Slot: Back");
            }
            else if (state.CarrySlot == CarrySlot.Back)
            {
                state.CarrySlot = CarrySlot.Hands;
                state.ShowMessage("Carry Slot: Hands");
            }
            else
            {
                state.FrontCarryAttachmentMode = true;
                state.ShowMessage("Mode: FrontCarry Attachment Point");
            }

            state.Resolver.ClearChildGroups();
        }

        private void ToggleTransformScope()
        {
            var includeLabelScope = false;
            if (TryGetCurrentCarried(out var carried, out var carryBehavior))
                includeLabelScope = PackAdjustmentTransformResolver.HasCarriedLabel(api, carried, carryBehavior);

            var entityPlayer = api.World.Player.Entity;
            if (entityPlayer == null || config == null) return;
            var baseTransformsGroup = entityPlayer?.ResolveCarryTransformGroupBase(config, state.CarrySlot);

            switch (state.CurrentTransformScope)
            {
                case TransformScope.Parent:
                    state.CurrentTransformScope = TransformScope.Child;
                    if (carried != null && carryBehavior != null && !string.IsNullOrEmpty(baseTransformsGroup))
                        state.Resolver.RefreshAvailableChildGroups(carried, carryBehavior, baseTransformsGroup);
                    break;
                case TransformScope.Child:
                    if (!state.Resolver.AdvanceChildGroup())
                    {
                        state.CurrentTransformScope = includeLabelScope ? TransformScope.Label : TransformScope.Parent;
                        if (state.CurrentTransformScope != TransformScope.Child)
                        {
                            state.Resolver.ClearChildGroups();
                        }
                    }
                    break;
                default:
                    state.CurrentTransformScope = TransformScope.Parent;
                    state.Resolver.ClearChildGroups();
                    break;
            }

            state.TransformsGroup = null;
            if (state.CurrentTransformScope != TransformScope.Label)
            {
                state.SelectedLabelTransform = null;
                state.SelectedLabelTransformIndex = 0;
            }

            state.ShowMessage($"Transform Scope: {state.CurrentTransformScope} | {state.GetCurrentTransformInfo()}");
        }

        private void SetMode(AdjustmentMode mode)
        {
            if (mode == AdjustmentMode.Scale && state.CurrentMode == AdjustmentMode.Scale)
            {
                state.ScaleAllAxesMode = !state.ScaleAllAxesMode;
                state.ShowMessage("Mode: Scale (" + (state.ScaleAllAxesMode ? "All Axes" : "Single Axis") + ")");
                return;
            }

            state.CurrentMode = mode;
            state.ShowMessage("Mode: " + (mode == AdjustmentMode.Scale
                ? "Scale (" + (state.ScaleAllAxesMode ? "All Axes" : "Single Axis") + ")"
                : mode.ToString()));
        }

        private void UpdateOnChange()
        {
            if (state.FrontCarryAttachmentMode) return;

            bool hasChanged = false;
            var entityPlayer = api.World.Player.Entity;
            if (entityPlayer == null || config == null) return;
            var carried = carryManager?.GetCarried(entityPlayer, state.CarrySlot);
            if (carried == null) return;

            var itemCode = carried.Block?.Code?.ToString() ?? carried.ItemStack.Collectible?.Code?.ToString();
            if (carried.Block != state.Block || itemCode != state.ItemCode)
            {
                state.ShowMessage(carried.Block != null ? "Carried Block changed" : "Carried Item changed");
                state.Block = carried.Block;
                state.ItemCode = itemCode;
                hasChanged = true;
                state.Resolver.ClearChildGroups();
            }

            var baseTransformsGroup = entityPlayer?.ResolveCarryTransformGroupBase(config!, state.CarrySlot);
            var carryBehavior = carried.GetCarryableBehavior();
            if (carryBehavior == null)
            {
                state.ShowMessage("No carryable behavior found");
                return;
            }

            if (state.CurrentTransformScope == TransformScope.Label)
            {
                UpdateLabelScope(carried, carryBehavior, ref hasChanged);
                return;
            }

            string resolvedGroupName = state.Resolver.ResolveTransformsGroup(carried, carryBehavior, baseTransformsGroup ?? CarryCodes.DefaultTransformGroup, state.CurrentTransformScope);

            if (state.TemplateManager?.TransformGroupExists(carried, resolvedGroupName) != true)
                resolvedGroupName = baseTransformsGroup ?? CarryCodes.DefaultTransformGroup;
            if (state.TemplateManager?.TransformGroupExists(carried, resolvedGroupName) != true)
                resolvedGroupName = CarryCodes.DefaultTransformGroup;

            if (hasChanged || resolvedGroupName != state.TransformsGroup)
            {
                state.TransformsGroup = resolvedGroupName;
                hasChanged = true;
                state.ShowMessage("Transform Group: " + state.TransformsGroup);
            }

            if (hasChanged)
            {
                // Item carries (null Block) use BlockBehaviorCarryable.Default, whose empty Slots
                // would trip this gate; the item-code pose registry replaces it as the source of
                // truth for which group/settings apply.
                if (carried.Block != null && carryBehavior.Slots[carried.Slot] == null)
                {
                    state.ShowMessage("Invalid carry slot");
                    return;
                }

                TransformSettings[]? settings = null;
                state.TemplateManager?.TryGetSettings(carried, state.TransformsGroup, out settings);
                if (settings == null || settings.Length == 0)
                    settings = [new TransformSettings() { Transform = carryBehavior.DefaultTransform.Clone() }];

                state.TransformSettings = settings;

                state.SetTransformIndex(0);
            }
        }

        private void UpdateLabelScope(CarriedBlock carried, BlockBehaviorCarryable carryBehavior, ref bool hasChanged)
        {
            if (!PackAdjustmentTransformResolver.HasCarriedLabel(api, carried, carryBehavior))
            {
                state.CurrentTransformScope = TransformScope.Parent;
                state.SelectedLabelTransform = null;
                state.SelectedLabelTransformIndex = 0;
                state.TransformsGroup = null;
                state.ShowMessage("Label transform unavailable, switched to Parent scope");
                return;
            }

            var labelSettings = carryBehavior.LabelRenderSettings;
            var labelCount = PackAdjustmentTransformResolver.GetLabelTransformCount(labelSettings);
            if (labelCount <= 0)
            {
                state.CurrentTransformScope = TransformScope.Parent;
                state.SelectedLabelTransform = null;
                state.SelectedLabelTransformIndex = 0;
                state.TransformsGroup = null;
                state.ShowMessage("Label transform unavailable, switched to Parent scope");
                return;
            }

            state.SelectedLabelTransformIndex = Math.Clamp(state.SelectedLabelTransformIndex, 0, labelCount - 1);
            state.SelectedLabelTransform = PackAdjustmentTransformResolver.GetLabelTransformAt(labelSettings, state.SelectedLabelTransformIndex);
            state.TransformSettings = null;

            if (state.TransformsGroup != CarryCodes.LabelTransformGroup)
            {
                state.TransformsGroup = CarryCodes.LabelTransformGroup;
                hasChanged = true;
            }

            if (hasChanged)
                state.SetTransformIndex(state.SelectedLabelTransformIndex);
        }

        private bool TryGetCurrentCarried(out CarriedBlock? carried, out BlockBehaviorCarryable? carryBehavior)
        {
            carried = carryManager?.GetCarried(api.World.Player.Entity, state.CarrySlot);
            carryBehavior = carried?.GetCarryableBehavior();
            return carried != null && carryBehavior != null;
        }
    }
}
