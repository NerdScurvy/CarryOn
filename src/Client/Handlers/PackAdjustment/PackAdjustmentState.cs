using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using CarryOn.Client.Logic.CarryRenderer;
using CarryOn.Client.Logic.TransformTemplates;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CarryOn.Client.Handlers.PackAdjustment
{
    internal enum AdjustmentMode { Translation, Rotation, Scale, Origin }
    internal enum TransformScope { Parent, Child, Label }

    internal sealed class PackAdjustmentState
    {
        private readonly ICoreClientAPI api;
        private readonly ICarryManager? carryManager;
        private readonly EntityCarryRenderer? entityCarryRenderer;

        public PackAdjustmentState(ICoreClientAPI api, ICarryManager? carryManager, EntityCarryRenderer? entityCarryRenderer)
        {
            this.api = api;
            this.carryManager = carryManager;
            this.entityCarryRenderer = entityCarryRenderer;
            Resolver = new PackAdjustmentTransformResolver(api, carryManager);
        }

        public PackAdjustmentTransformResolver Resolver { get; }

        internal TransformTemplateManager? TemplateManager
        {
            get => Resolver.TemplateManager;
            set
            {
                Resolver.TemplateManager = value;
                // Every manager placement goes through here (boot and the carryability
                // snapshot swap), so the session cannot outlive the manager it was
                // materialized from: a new manager resets the derived session state.
                ResetForTemplateManagerSwap();
            }
        }

        public AdjustmentMode CurrentMode { get; set; } = AdjustmentMode.Translation;
        public CarrySlot CarrySlot { get; set; } = CarrySlot.Back;
        public bool FrontCarryAttachmentMode { get; set; }
        public TransformScope CurrentTransformScope { get; set; } = TransformScope.Parent;
        public bool ScaleAllAxesMode { get; set; }
        public int TransformIndex { get; set; }

        public Block? Block { get; set; }

        /// <summary>
        /// Identity key for the current carry's pose source. Blocks use <see cref="Block"/>; item
        /// carries (null Block, e.g. a bag held in hands) use the item's collectible code so pose
        /// changes between different bags are detected and the write-through goes to the right
        /// item-code override.
        /// </summary>
        public string? ItemCode { get; set; }
        public string? TransformsGroup { get; set; }
        public TransformSettings[]? TransformSettings { get; set; }
        public ModelTransform? SelectedLabelTransform { get; set; }
        public int SelectedLabelTransformIndex { get; set; }

        public void SetTransformIndex(int index)
        {
            if (CurrentTransformScope == TransformScope.Label)
            {
                TryGetCurrentCarryBehavior(out var behavior);
                var labelSettings = behavior?.LabelRenderSettings;
                var labelCount = PackAdjustmentTransformResolver.GetLabelTransformCount(labelSettings);

                if (labelCount <= 0)
                {
                    SelectedLabelTransformIndex = 0;
                    SelectedLabelTransform = null;
                    ShowMessage($"Transform: 1/1 | id: {CarryCodes.LabelTransformGroup}, item: unavailable");
                    return;
                }

                SelectedLabelTransformIndex = Math.Clamp(index, 0, labelCount - 1);
                SelectedLabelTransform = PackAdjustmentTransformResolver.GetLabelTransformAt(labelSettings, SelectedLabelTransformIndex);
                TransformIndex = SelectedLabelTransformIndex;
                ShowMessage($"Transform: {SelectedLabelTransformIndex + 1}/{labelCount} | {GetCurrentTransformInfo()}");
                return;
            }

            if (TransformSettings == null || TransformSettings.Length == 0) return;
            TransformIndex = Math.Clamp(index, 0, TransformSettings.Length - 1);
            ShowMessage($"Transform: {TransformIndex + 1}/{TransformSettings.Length} | {GetCurrentTransformInfo()}");
        }

        public string GetCurrentTransformInfo()
        {
            if (CurrentTransformScope == TransformScope.Label)
            {
                var labelItem = "unavailable";
                var carried = carryManager?.GetCarried(api.World.Player.Entity, CarrySlot);
                var behavior = carried?.GetCarryableBehavior();
                var labelCount = PackAdjustmentTransformResolver.GetLabelTransformCount(behavior?.LabelRenderSettings);
                if (PackAdjustmentTransformResolver.HasCarriedLabel(api, carried, behavior))
                {
                    var text = carried?.BlockEntityData?.GetString("text", null);
                    labelItem = !string.IsNullOrWhiteSpace(text) ? "label-text" : "label-icon";
                }
                return SelectedLabelTransform == null
                    ? $"id: {CarryCodes.LabelTransformGroup} {SelectedLabelTransformIndex + 1}/{Math.Max(1, labelCount)}, item: unavailable"
                    : $"id: {CarryCodes.LabelTransformGroup} {SelectedLabelTransformIndex + 1}/{Math.Max(1, labelCount)}, item: {labelItem}";
            }

            if (CurrentTransformScope == TransformScope.Child && !string.IsNullOrEmpty(Resolver.SelectedChildGroup))
                return $"group: {Resolver.SelectedChildGroup}";

            if (TransformSettings == null || TransformSettings.Length == 0 || TransformIndex < 0 || TransformIndex >= TransformSettings.Length)
                return "No transform";

            var ts = TransformSettings[TransformIndex];
            string id = ts.Id != null ? $"id: {ts.Id}" : "id: (none)";
            string asset = !string.IsNullOrEmpty(ts.AssetName) ? $"item: {ts.AssetName}" : "item: (none)";
            return $"{id}, {asset}";
        }

        public bool SaveTransformSettings(BlockBehaviorCarryable behavior)
        {
            if (TransformsGroup == null || TransformSettings == null || TemplateManager == null) return false;

            if (Block != null)
            {
                TemplateManager.SetSettings(Block, TransformsGroup, TransformSettings);
            }
            else if (!string.IsNullOrEmpty(ItemCode))
            {
                TemplateManager.SetItemSettings(ItemCode, TransformsGroup, TransformSettings);
            }
            else
            {
                return false;
            }

            InvalidateCaches();
            return true;
        }

        public bool TryGetCurrentCarryBehavior(out BlockBehaviorCarryable? carryBehavior)
        {
            carryBehavior = carryManager?.GetCarried(api.World.Player.Entity, CarrySlot)?.GetCarryableBehavior();
            return carryBehavior != null;
        }

        public BlockBehaviorCarryable? GetCarryableBehavior(CarrySlot slot)
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

        public AttachmentPoint? GetFrontCarryAttachPoint()
        {
            var apPose = api.World.Player.Entity?.AnimManager?.Animator?.GetAttachmentPointPose(CarryCodes.FrontCarryAttachmentPoint);
            return apPose?.AttachPoint;
        }

        public void InvalidateCaches()
        {
            entityCarryRenderer?.InvalidateRenderCaches();
        }

        /// <summary>
        /// Clears the session state materialized from the previous template manager.
        /// <see cref="PackAdjustmentHandler.UpdateOnChange"/> only re-resolves
        /// <see cref="TransformSettings"/> when the carry identity or the resolved
        /// group name changed, so after a manager swap the editor would otherwise hold
        /// a settings array materialized from the <b>old</b> manager - and
        /// <see cref="SaveTransformSettings"/> would write it back through the new
        /// one. Called from the <see cref="TemplateManager"/> setter, so no swap site
        /// can forget it; on boot's first assignment the state is already fresh, so
        /// the reset is a no-op.
        /// </summary>
        public void ResetForTemplateManagerSwap()
        {
            Block = null;
            ItemCode = null;
            TransformsGroup = null;
            TransformSettings = null;
            SelectedLabelTransform = null;
            SelectedLabelTransformIndex = 0;
            Resolver.ClearChildGroups();
        }

        public void ShowMessage(string message)
        {
            api?.World?.Player?.ShowChatNotification(message);
        }
    }
}
