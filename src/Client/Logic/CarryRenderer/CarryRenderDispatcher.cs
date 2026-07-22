using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Common.Interfaces;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class CarryRenderDispatcher
    {
        private readonly ICoreClientAPI api;
        private readonly ICarryManager carryManager;
        private readonly IConfigProvider configProvider;
        private readonly CarryRenderCacheManager cacheManager;
        private readonly CarryFirstPersonTransform firstPersonRenderer;
        private readonly CarryLabelRenderer labelRenderer;
        private readonly CarryShadowRenderer shadowRenderer;
        private readonly CarryMainRenderer mainRenderer;
        private readonly CarryLabelDispatch labelDispatch;
        private readonly bool renderAttachedBlocks;

        public CarryRenderDispatcher(ICoreClientAPI api, ICarryManager carryManager, CarryRenderCacheManager cacheManager, CarryFirstPersonTransform firstPersonRenderer, CarryLabelRenderer labelRenderer, bool renderAttachedBlocks = true)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.carryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
            this.configProvider = carryManager as IConfigProvider ?? throw new ArgumentException("carryManager must implement IConfigProvider", nameof(carryManager));
            this.cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            this.firstPersonRenderer = firstPersonRenderer ?? throw new ArgumentNullException(nameof(firstPersonRenderer));
            this.labelRenderer = labelRenderer ?? throw new ArgumentNullException(nameof(labelRenderer));
            this.renderAttachedBlocks = renderAttachedBlocks;
            this.RenderAttachedBlocks = renderAttachedBlocks;
            this.shadowRenderer = new CarryShadowRenderer(api, this);
            this.mainRenderer = new CarryMainRenderer(api, this, labelRenderer);
            this.labelDispatch = new CarryLabelDispatch(api, this, labelRenderer);
        }

        private const float FirstPersonVerticalOffset = -0.05f;

        private static readonly Dictionary<CarrySlot, Dictionary<string, SlotRenderSettings>> RenderSettings = CreateRenderSettings();
        private static Dictionary<CarrySlot, Dictionary<string, SlotRenderSettings>> CreateRenderSettings() => new() {
            { CarrySlot.Hands    , new Dictionary<string, SlotRenderSettings> {
                    { "hands", new SlotRenderSettings(CarryCodes.FrontCarryAttachmentPoint, -0.3f, -0.6f, -0.5f) } } },

            { CarrySlot.Back     , new Dictionary<string, SlotRenderSettings> {
                    { "backpack-none", new SlotRenderSettings("Back", -0.3f, -0.6f, -0.5f) },
                    { "backpack-small", new SlotRenderSettings("Back", -0.2f, -0.6f, -0.5f) },
                    { "backpack-large", new SlotRenderSettings("Back", -0.025f, -0.6f, -0.5f) }
                }
            }
        };

        private record SlotRenderSettings
        {
            public string AttachmentPoint { get; }
            public Vec3f Offset { get; }
            public SlotRenderSettings(string attachmentPoint, float xOffset, float yOffset, float zOffset)
            { AttachmentPoint = attachmentPoint; Offset = new Vec3f(xOffset, yOffset, zOffset); }
        }

        internal record QueuedDraw(
            CarriedRenderInfo Info,
            float[] Matrix,
            bool IsRoot,
            RenderPhaseMask Phases,
            float AlphaTestOpaque,
            float AlphaTestBlend
        );

        private const float PlantTintBrightnessBoost = 1.12f;
        private readonly Vec4f plantTintScratch = new(1f, 1f, 1f, 1f);

        private readonly Stack<float[]> matrixPool = new();
        internal bool RenderAttachedBlocks { get; set; }
        private object? lastCameraMatrixRef;
        private float[] cachedViewMat = new float[16];
        private static readonly Vec3f ZeroOffset = new(0, 0, 0);
        private bool disposedDetected;

        public void ClearMatrixPool()
        {
            matrixPool.Clear();
        }

        internal float[] RentMatrix() => matrixPool.Count > 0 ? matrixPool.Pop() : new float[16];

        internal void ReturnMatrix(float[] matrix) => matrixPool.Push(matrix);

        internal void SetDisposedDetected() => disposedDetected = true;

        internal Vec4f GetRenderTint(CarriedRenderInfo info)
        {
            var sourceTint = info.TintColor ?? CarryRenderHelpers.DefaultTint;
            if (!info.EnableVertexWarp)
            {
                return sourceTint;
            }

            plantTintScratch.X = Math.Min(1f, sourceTint.X * PlantTintBrightnessBoost);
            plantTintScratch.Y = Math.Min(1f, sourceTint.Y * PlantTintBrightnessBoost);
            plantTintScratch.Z = Math.Min(1f, sourceTint.Z * PlantTintBrightnessBoost);
            plantTintScratch.W = sourceTint.W;
            return plantTintScratch;
        }

        public void RenderAllCarried(EntityAgent entity, float deltaTime, EnumRenderStage stage, bool isShadowPass, long renderTick)
        {
            var allCarried = carryManager.GetAllCarried(entity).ToList();
            if (allCarried.Count == 0) return;

            disposedDetected = false;

            var player = api.World.Player;
            var isLocalPlayer = entity == player.Entity;
            var isFirstPerson = isLocalPlayer && (player.CameraMode == EnumCameraMode.FirstPerson);
            var isImmersiveFirstPerson = player.ImmersiveFpMode;

            var renderer = (EntityShapeRenderer)entity.Properties.Client.Renderer;
            var animator = entity.AnimManager?.Animator;

            if (renderer == null || animator == null) return;

            foreach (var carried in allCarried)
            {
                RenderCarried(entity, carried, deltaTime,
                              isFirstPerson, isImmersiveFirstPerson,
                              stage, isShadowPass, renderer, animator, renderTick);
            }

            if (disposedDetected)
            {
                cacheManager.InvalidateAll();
            }
        }

        private void RenderCarried(EntityAgent entity, CarriedBlock carried, float deltaTime,
                                   bool isFirstPerson, bool isImmersiveFirstPerson, EnumRenderStage stage, bool isShadowPass,
                                   EntityShapeRenderer renderer, IAnimator animator, long renderTick)
        {
            var inHands = carried.Slot == CarrySlot.Hands;
            if (!inHands && isFirstPerson && !isShadowPass) return;

            var deferHandsOpaqueUntilAfterOit = inHands && isFirstPerson && !isImmersiveFirstPerson;
            if (!isShadowPass)
            {
                if (stage == EnumRenderStage.Opaque && deferHandsOpaqueUntilAfterOit) return;
            }

            var cam = api.Render.CameraMatrixOrigin;
            if (!ReferenceEquals(cam, lastCameraMatrixRef))
            {
                for (int i = 0; i < 16 && i < cam.Length; i++) cachedViewMat[i] = (float)cam[i];
                lastCameraMatrixRef = cam;
            }
            var viewMat = cachedViewMat;

            string transformGroupName = entity.ResolveCarryTransformGroupBase(configProvider.Config, carried.Slot);

            var renderSettings = RenderSettings?[carried.Slot]?[transformGroupName];
            if (renderSettings == null) return;

            var carriedRenderInfo = cacheManager.GetRenderInfoCached(entity, carried, transformGroupName);
            if (carriedRenderInfo == null || carriedRenderInfo.Length == 0) return;

            float[] modelMat;
            if (inHands && isFirstPerson && !isImmersiveFirstPerson && !isShadowPass)
            {
                modelMat = firstPersonRenderer.GetFirstPersonHandsMatrix(entity, viewMat, deltaTime, renderTick);
                Mat4f.Translate(modelMat, modelMat, 0.0f, FirstPersonVerticalOffset, 0.0f);
            }
            else
            {
                if (animator == null) return;
                AttachmentPointAndPose? attachPointAndPose = animator.GetAttachmentPointPose(renderSettings.AttachmentPoint);
                if (attachPointAndPose == null) return;
                var attachmentPointMatrix = CarryTransformResolver.GetAttachmentPointMatrix(renderer, attachPointAndPose);
                if (attachmentPointMatrix == null) return;
                modelMat = attachmentPointMatrix;
            }

            float[] initialMatrix = RentMatrix();

            var initial = carriedRenderInfo[0];
            initial.SkipTransform = true;
            Array.Copy(modelMat, initialMatrix, 16);
            CarryTransformResolver.ApplyTransformInPlace(initial.RenderInfo.Transform, initialMatrix, renderSettings.Offset);

            var renderRootFirst = carried.GetCarryableBehavior()?.RenderRootFirst ?? false;
            if (carriedRenderInfo.Length > 1 && !renderRootFirst)
            {
                carriedRenderInfo = carriedRenderInfo.Skip(1).Append(initial).ToArray();
            }

            // Find attached root entry: its transform is the base for attached blocks/labels
            float[]? attachedRootMatrix = null;
            foreach (var info in carriedRenderInfo)
            {
                if (info.IsAttachedRoot && info.RenderEnabled)
                {
                    attachedRootMatrix = RentMatrix();
                    Array.Copy(initialMatrix, attachedRootMatrix, 16);
                    CarryTransformResolver.ApplyTransformInPlace(info.RenderInfo.Transform, attachedRootMatrix, ZeroOffset);
                    break;
                }
            }

            var zeroOffset = ZeroOffset;

            if (isShadowPass)
            {
                shadowRenderer.Render(carriedRenderInfo, initialMatrix, attachedRootMatrix, zeroOffset, renderer);
            }
            else
            {
                mainRenderer.Render(entity, carried, carriedRenderInfo, initialMatrix, attachedRootMatrix, zeroOffset,
                                    stage, deferHandsOpaqueUntilAfterOit, viewMat, renderer);
            }

            if (attachedRootMatrix != null)
                matrixPool.Push(attachedRootMatrix);

            matrixPool.Push(initialMatrix);
        }

        internal void RenderAttachedBlockLabels(CarriedBlock carried, float[] initialMatrix, float[] viewMat, IStandardShaderProgram prog, EntityAgent entity)
        {
            labelDispatch.Render(carried, initialMatrix, viewMat, prog, entity);
        }
    }
}
