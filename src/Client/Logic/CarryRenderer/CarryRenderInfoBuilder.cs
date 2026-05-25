using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class CarryRenderInfoBuilder
    {
        private readonly ICoreClientAPI api;

        internal CarryRenderInfoBuilder(ICoreClientAPI api)
        {
            this.api = api;
        }

        internal CarriedRenderInfo[] BuildFromPlan(CarriedBlock carried, CachedTransformPlan plan, TreeAttribute containerSlots = null)
        {
            var slot = new DummySlot(carried.ItemStack);
            var baseRenderInfo = this.api.Render.GetItemStackRenderInfo(slot, EnumItemRenderTarget.Ground, 0);
            var carryBehavior = carried.GetCarryableBehavior();

            if (carryBehavior == null || plan == null || plan.EffectiveSettings == null || plan.EffectiveSettings.Length == 0)
            {
                var fallback = new CarriedRenderInfo { RenderInfo = baseRenderInfo };
                fallback.RenderInfo.Transform = carryBehavior?.DefaultTransform?.Clone() ?? new ModelTransform();
                return new[] { fallback };
            }

            var renderInfoList = new List<CarriedRenderInfo>();
            var playerPos = this.api.World?.Player?.Entity?.Pos;

            foreach (var effective in plan.EffectiveSettings)
            {
                var setting = effective?.Setting;
                if (setting == null) continue;

                Vec4f customTint = null;
                if (!string.IsNullOrEmpty(setting.ClimateTintMap) || !string.IsNullOrEmpty(setting.SeasonalTintMap))
                {
                    if (playerPos != null)
                    {
                        customTint = CarryRenderHelpers.SampleColorMapTint(
                            setting.ClimateTintMap,
                            setting.SeasonalTintMap,
                            new BlockPos((int)playerPos.X, (int)playerPos.Y, (int)playerPos.Z),
                            this.api);
                    }
                }

                ItemRenderInfo targetRenderInfo;

                // Resolve source stack directly via resolver-provided slot key
                ItemStack slotItemStack = null;
                if (containerSlots != null && !string.IsNullOrEmpty(effective.SourceSlotKey))
                {
                    slotItemStack = CarryRenderHelpers.TryGetSlotItemStackByKey(containerSlots, effective.SourceSlotKey);
                    if (slotItemStack != null && slotItemStack.Collectible == null)
                    {
                        slotItemStack.ResolveBlockOrItem(this.api.World);
                    }
                }

                ItemStack beDataItemStack = null;
                if (!string.IsNullOrWhiteSpace(setting.BlockEntityDataItemStackPath))
                {
                    beDataItemStack = CarryRenderHelpers.TryGetItemStackByPath(
                        carried?.BlockEntityData,
                        setting.BlockEntityDataItemStackPath,
                        this.api.World);
                }

                var disableIfItemStack = !string.IsNullOrWhiteSpace(setting.DisableIfItemStackPath)
                    && CarryRenderHelpers.TryGetItemStackByPath(
                        carried?.BlockEntityData,
                        setting.DisableIfItemStackPath,
                        this.api.World) != null;

                bool useSlotStack =
                    slotItemStack != null &&
                    (
                        string.IsNullOrEmpty(setting.AssetName) ||
                        CarryRenderHelpers.IsSameCollectible(slotItemStack, setting.AssetName)
                    );

                // If this transform explicitly requests a BE-driven stack source and none exists,
                // do not implicitly fall back to the carried root stack unless another explicit
                // source (slot match or item/block asset) is available.
                var hasBeItemPath = !string.IsNullOrWhiteSpace(setting.BlockEntityDataItemStackPath);
                var hasExplicitAsset = !string.IsNullOrWhiteSpace(setting.AssetName);
                if (hasBeItemPath && beDataItemStack == null && !useSlotStack && !hasExplicitAsset)
                {
                    continue;
                }

                if (useSlotStack)
                {
                    targetRenderInfo = this.api.Render.GetItemStackRenderInfo(
                        new DummySlot(slotItemStack),
                        EnumItemRenderTarget.Ground,
                        0);
                }
                else if (!string.IsNullOrEmpty(setting.AssetName))
                {
                    CollectibleObject itemOrBlock = null;
                    var assetLocation = new AssetLocation(setting.AssetName);

                    switch (setting.AssetType)
                    {
                        case EnumAssetType.Item:
                            itemOrBlock = this.api.World.GetItem(assetLocation);
                            break;
                        case EnumAssetType.Block:
                            itemOrBlock = this.api.World.GetBlock(assetLocation);
                            break;
                    }

                    if (itemOrBlock == null) continue;

                    var itemStack = new ItemStack(itemOrBlock);
                    targetRenderInfo = this.api.Render.GetItemStackRenderInfo(
                        new DummySlot(itemStack),
                        EnumItemRenderTarget.Ground,
                        0);
                }
                else if (beDataItemStack != null)
                {
                    targetRenderInfo = this.api.Render.GetItemStackRenderInfo(
                        new DummySlot(beDataItemStack),
                        EnumItemRenderTarget.Ground,
                        0);
                }
                else
                {
                    targetRenderInfo = this.api.Render.GetItemStackRenderInfo(
                        slot,
                        EnumItemRenderTarget.Ground,
                        0);
                }

                if (setting.CullFaces.HasValue)
                {
                    targetRenderInfo.CullFaces = setting.CullFaces.Value;
                }

                var (resolvedTransform, secondaryTransform) = ResolveDisplayTransforms(
                    carried,
                    effective,
                    setting,
                    carryBehavior,
                    slotItemStack
                );

                targetRenderInfo.Transform = resolvedTransform;

                renderInfoList.Add(new CarriedRenderInfo
                {
                    RenderInfo = targetRenderInfo,
                    TintColor = customTint ?? setting.TintColor,
                    RgbGlowIntensity = setting.RgbGlowIntensity ?? new Vec4f(0, 0, 0, 0),
                    EnableVertexWarp = effective.EnableVertexWarp,
                    AlphaTestOpaque = setting.AlphaTestOpaque,
                    AlphaTestBlend = setting.AlphaTestBlend,
                    NormalShaded = setting.NormalShaded,
                    RenderPass = setting.RenderPass,
                    SecondaryTransform = secondaryTransform,
                    RenderEnabled = (setting.Enabled ?? true) && !disableIfItemStack
                });
            }

            return renderInfoList.Count > 0 ? renderInfoList.ToArray() : new[] { new CarriedRenderInfo { RenderInfo = baseRenderInfo } };
        }

        private static (ModelTransform primary, ModelTransform secondary) ResolveDisplayTransforms(
            CarriedBlock carried,
            EffectiveTransformSetting effective,
            TransformSettings setting,
            BlockBehaviorCarryable carryBehavior,
            ItemStack slotItemStack)
        {
            var baseTransform =
                setting.Transform?.Clone()
                ?? carryBehavior?.DefaultTransform?.Clone()
                ?? new ModelTransform();

            if (effective?.ApplyDisplaySlotYaw == true && !string.IsNullOrEmpty(effective.SourceSlotKey))
            {
                var slotYawOverrideDeg = TryGetDisplaySlotYawDegrees(
                    carried,
                    effective.SourceSlotKey,
                    effective.ApplyDisplayCaseYawOffset);
                if (slotYawOverrideDeg.HasValue)
                {
                    baseTransform.Rotation.Y = slotYawOverrideDeg.Value;
                }
            }

            if (effective?.ApplyOnDisplayTransform != true)
            {
                return (baseTransform, null);
            }

            var itemDisplayTransform = TryGetOnDisplayTransform(slotItemStack);
            if (itemDisplayTransform == null) return (baseTransform, null);

            return (baseTransform, itemDisplayTransform);
        }

        private static float? TryGetDisplaySlotYawDegrees(CarriedBlock carried, string slotKey, bool applyDisplayCaseYawOffset)
        {
            if (carried?.BlockEntityData == null || string.IsNullOrEmpty(slotKey))
            {
                return null;
            }

            var attrName = "rotation" + slotKey;
            var radians = carried.BlockEntityData.GetFloat(attrName, 0f);  // default 0 when absent
            var yawDeg = radians * GameMath.RAD2DEG;

            if (applyDisplayCaseYawOffset)
            {
                yawDeg += 45f;  // Vanilla displaycase slot yaw basis offset
            }

            return yawDeg;
        }

        private static ModelTransform TryGetOnDisplayTransform(ItemStack stack)
        {
            var displayTransform = stack?.Collectible?.Attributes?["onDisplayTransform"];
            if (displayTransform == null || !displayTransform.Exists)
            {
                return null;
            }

            var transform = new ModelTransform();
            transform.EnsureDefaultValues();

            if (CarryRenderHelpers.TryReadVec3(displayTransform["translation"], out var translation))
            {
                transform.Translation.Set(translation.X, translation.Y, translation.Z);
            }

            if (CarryRenderHelpers.TryReadVec3(displayTransform["rotation"], out var rotation))
            {
                transform.Rotation.Set(rotation.X, rotation.Y, rotation.Z);
            }

            if (CarryRenderHelpers.TryReadVec3(displayTransform["origin"], out var origin))
            {
                transform.Origin.Set(origin.X, origin.Y, origin.Z);
            }

            if (CarryRenderHelpers.TryReadScale(displayTransform, out var scale))
            {
                transform.ScaleXYZ.Set(scale.X, scale.Y, scale.Z);
            }

            return transform;
        }
    }
}
