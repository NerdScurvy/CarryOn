using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Common.Logic;
using CarryOn.Utility;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class CarryRenderInfoBuilder(ICoreClientAPI api, bool renderAttachedBlocks = true)
    {
        private readonly Dictionary<AssetLocation, MultiTextureMeshRef> attachedBlockMeshCache = new();

        internal bool RenderAttachedBlocks { get => renderAttachedBlocks; set => renderAttachedBlocks = value; }

        public void Dispose()
        {
            foreach (var meshRef in attachedBlockMeshCache.Values)
            {
                meshRef?.Dispose();
            }
            attachedBlockMeshCache.Clear();
        }

        internal CarriedRenderInfo[] BuildFromPlan(CarriedBlock carried, CachedTransformPlan? plan, TreeAttribute? containerSlots = null)
        {
            var world = api.World;
            if (carried == null || world == null)
            {
                return Array.Empty<CarriedRenderInfo>();
            }

            var carriedBlock = carried;
            var carryBehavior = carriedBlock.GetCarryableBehavior();

            // Resolve the render block variant if RootRenderVariant is configured
            Block? renderVariantBlock = null;
            ItemStack renderStack = carriedBlock.ItemStack;
            if (carryBehavior != null && !string.IsNullOrEmpty(carryBehavior.RootRenderVariant))
            {
                renderVariantBlock = CarryRotationHelper.ResolveRenderBlock(
                    world, carriedBlock, carryBehavior.RootRenderVariant, carryBehavior.RootRenderFacing);
                if (renderVariantBlock != null)
                {
                    renderStack = new ItemStack(renderVariantBlock);
                    renderStack.ResolveBlockOrItem(world);
                }
            }

            var slot = new DummySlot(renderStack);
            var baseRenderInfo = api.Render.GetItemStackRenderInfo(slot, EnumItemRenderTarget.Ground, 0);

            if (renderVariantBlock != null)
            {
                var worldMeshRef = GetOrCreateBlockMeshRef(renderVariantBlock);
                if (worldMeshRef != null)
                    baseRenderInfo.ModelRef = worldMeshRef;
            }

            if (carryBehavior == null || plan == null || plan.EffectiveSettings == null || plan.EffectiveSettings.Length == 0)
            {
                var fallback = new CarriedRenderInfo { RenderInfo = baseRenderInfo };
                fallback.RenderInfo.Transform = carryBehavior?.DefaultTransform?.Clone() ?? new ModelTransform();
                return new[] { fallback };
            }

            var renderInfoList = new List<CarriedRenderInfo>();
            var playerPos = api.World?.Player?.Entity?.Pos;

            foreach (var effective in plan.EffectiveSettings)
            {
                if (effective == null) continue;

                var setting = effective.Setting;
                if (setting == null) continue;

                Vec4f? customTint = null;
                if (!string.IsNullOrEmpty(setting.ClimateTintMap) || !string.IsNullOrEmpty(setting.SeasonalTintMap))
                {
                    if (playerPos != null)
                    {
                        customTint = CarryRenderHelpers.SampleColorMapTint(
                            setting.ClimateTintMap,
                            setting.SeasonalTintMap,
                            new BlockPos((int)playerPos.X, (int)playerPos.Y, (int)playerPos.Z),
                            api);
                    }
                }

                ItemRenderInfo targetRenderInfo;

                // Resolve source stack directly via resolver-provided slot key
                ItemStack? slotItemStack = null;
                if (containerSlots != null && !string.IsNullOrEmpty(effective.SourceSlotKey))
                {
                    slotItemStack = CarryRenderHelpers.TryGetSlotItemStackByKey(containerSlots, effective.SourceSlotKey);
                    if (slotItemStack != null && slotItemStack.Collectible == null)
                    {
                        slotItemStack.ResolveBlockOrItem(api.World);
                    }
                }

                ItemStack? beDataItemStack = null;
                if (!string.IsNullOrWhiteSpace(setting.BlockEntityDataItemStackPath))
                {
                    beDataItemStack = CarryRenderHelpers.TryGetItemStackByPath(
                        carriedBlock.BlockEntityData,
                        setting.BlockEntityDataItemStackPath,
                        world);
                }

                var disableIfItemStack = !string.IsNullOrWhiteSpace(setting.DisableIfItemStackPath)
                    && CarryRenderHelpers.TryGetItemStackByPath(
                        carriedBlock.BlockEntityData,
                        setting.DisableIfItemStackPath,
                        world) != null;

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
                    targetRenderInfo = api.Render.GetItemStackRenderInfo(
                        new DummySlot(slotItemStack),
                        EnumItemRenderTarget.Ground,
                        0);
                }
                else if (!string.IsNullOrEmpty(setting.AssetName))
                {
                    CollectibleObject? itemOrBlock = null;
                    var assetLocation = new AssetLocation(setting.AssetName);
                    
                    switch (setting.AssetType)
                    {
                        case EnumAssetType.Item:
                            itemOrBlock = world.GetItem(assetLocation);
                            break;
                        case EnumAssetType.Block:
                            itemOrBlock = world.GetBlock(assetLocation);
                            break;
                    }

                    if (itemOrBlock == null) continue;

                    var itemStack = new ItemStack(itemOrBlock);
                    targetRenderInfo = api.Render.GetItemStackRenderInfo(
                        new DummySlot(itemStack),
                        EnumItemRenderTarget.Ground,
                        0);
                }
                else if (beDataItemStack != null)
                {
                    targetRenderInfo = api.Render.GetItemStackRenderInfo(
                        new DummySlot(beDataItemStack),
                        EnumItemRenderTarget.Ground,
                        0);
                }
                else
                {
                    targetRenderInfo = api.Render.GetItemStackRenderInfo(
                        slot,
                        EnumItemRenderTarget.Ground,
                        0);

                    if (renderVariantBlock != null)
                    {
                        var worldMeshRef = GetOrCreateBlockMeshRef(renderVariantBlock);
                        if (worldMeshRef != null)
                            targetRenderInfo.ModelRef = worldMeshRef;
                    }
                }

                if (setting.CullFaces.HasValue)
                {
                    targetRenderInfo.CullFaces = setting.CullFaces.Value;
                }

                var (resolvedTransform, secondaryTransform) = ResolveDisplayTransforms(
                    carriedBlock,
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
                    RenderEnabled = (setting.Enabled ?? true) && !disableIfItemStack,
                    IsAttachedRoot = setting.IsAttachedRoot
                });
            }

            AppendAttachedBlockRenderInfos(carriedBlock, renderInfoList);

            return renderInfoList.Count > 0 ? renderInfoList.ToArray() : new[] { new CarriedRenderInfo { RenderInfo = baseRenderInfo } };
        }

        internal void AppendAttachedBlockRenderInfos(CarriedBlock carried, List<CarriedRenderInfo> renderInfoList)
        {
            if (!renderAttachedBlocks) return;
            if (carried == null || !carried.HasAttachedBlocks) return;

            var attachedBlocks = carried.AttachedBlocks;
            if (attachedBlocks == null || attachedBlocks.Count == 0) return;

            var world = api.World;
            if (world == null) return;

            var defaultFacing = carried.GetCarryableBehavior()?.RootRenderFacing;
            int offsetSteps = CarryRotationHelper.GetOriginalToModelDefaultSteps(carried, defaultFacing);

            foreach (var attached in attachedBlocks)
            {
                if (attached == null) continue;

                var offset = CarryRotationHelper.RotateOffset(attached.RelativeOffset, offsetSteps);

                var originalCode = attached.OriginalBlockCode;
                Block? variantBlock = null;
                if (originalCode != null)
                {
                    var rotatedFace = attached.OriginalLocalFace != null
                        ? CarryRotationHelper.RotateFacing(attached.OriginalLocalFace, offsetSteps)
                        : null;

                    if (rotatedFace != null)
                    {
                        variantBlock = CarryRotationHelper.GetRotatedVariantBlock(world, originalCode, rotatedFace);
                    }
                    variantBlock ??= world.GetBlock(originalCode);
                }

                ItemStack childStack;
                if (variantBlock != null)
                {
                    childStack = new ItemStack(variantBlock);
                    childStack.ResolveBlockOrItem(world);
                }
                else
                {
                    childStack = attached.ItemStack;
                    if (childStack.Collectible == null)
                        childStack.ResolveBlockOrItem(world);
                }

                var childRenderInfo = api.Render.GetItemStackRenderInfo(new DummySlot(childStack), EnumItemRenderTarget.Ground, 0);

                MultiTextureMeshRef? worldMeshRef = null;
                if (variantBlock != null)
                {
                    worldMeshRef = GetOrCreateBlockMeshRef(variantBlock);
                }

                if (worldMeshRef != null)
                {
                    childRenderInfo.ModelRef = worldMeshRef;
                }

                var transform = new ModelTransform();
                transform.EnsureDefaultValues();
                transform.Translation.Set(offset.X, offset.Y, offset.Z);

                childRenderInfo.Transform = transform;

                renderInfoList.Add(new CarriedRenderInfo
                {
                    RenderInfo = childRenderInfo,
                    SkipTransform = false,
                    RenderEnabled = true,
                    IsAttachedBlock = true
                });
            }
        }

        private MultiTextureMeshRef? GetOrCreateBlockMeshRef(Block block)
        {
            if (attachedBlockMeshCache.TryGetValue(block.Code, out var cached))
            {
                return cached;
            }

            var meshData = api.TesselatorManager.GetDefaultBlockMesh(block);
            if (meshData == null) return null;

            var meshRef = api.Render.UploadMultiTextureMesh(meshData);
            attachedBlockMeshCache[block.Code] = meshRef;
            return meshRef;
        }

        private static (ModelTransform primary, ModelTransform? secondary) ResolveDisplayTransforms(
            CarriedBlock carried,
            EffectiveTransformSetting effective,
            TransformSettings setting,
            BlockBehaviorCarryable carryBehavior,
            ItemStack? slotItemStack)
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

        private static ModelTransform? TryGetOnDisplayTransform(ItemStack? stack)
        {
            var displayTransform = stack?.Collectible?.Attributes?["onDisplayTransform"];
            if (displayTransform == null || !displayTransform.Exists)
            {
                return null;
            }

            var transform = new ModelTransform();
            transform.EnsureDefaultValues();

            if (CarryRenderHelpers.TryReadVec3(displayTransform["translation"], out var translation) && translation != null)
            {
                transform.Translation.Set(translation.X, translation.Y, translation.Z);
            }

            if (CarryRenderHelpers.TryReadVec3(displayTransform["rotation"], out var rotation) && rotation != null)
            {
                transform.Rotation.Set(rotation.X, rotation.Y, rotation.Z);
            }

            if (CarryRenderHelpers.TryReadVec3(displayTransform["origin"], out var origin) && origin != null)
            {
                transform.Origin.Set(origin.X, origin.Y, origin.Z);
            }

            if (CarryRenderHelpers.TryReadScale(displayTransform, out var scale) && scale != null)
            {
                transform.ScaleXYZ.Set(scale.X, scale.Y, scale.Z);
            }

            return transform;
        }
    }
}
