using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
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
        private CarryOnConfig config;
        private readonly CarryRenderCacheManager cacheManager;
        private readonly CarryFirstPersonRenderer firstPersonRenderer;
        private readonly CarriedLabelRenderer labelRenderer;
        private readonly bool renderAttachedBlocks;

        public CarryRenderDispatcher(ICoreClientAPI api, ICarryManager carryManager, CarryOnConfig config, CarryRenderCacheManager cacheManager, CarryFirstPersonRenderer firstPersonRenderer, CarriedLabelRenderer labelRenderer, bool renderAttachedBlocks = true)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.carryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            this.firstPersonRenderer = firstPersonRenderer ?? throw new ArgumentNullException(nameof(firstPersonRenderer));
            this.labelRenderer = labelRenderer ?? throw new ArgumentNullException(nameof(labelRenderer));
            this.renderAttachedBlocks = renderAttachedBlocks;
            this.RenderAttachedBlocks = renderAttachedBlocks;
        }

        public void UpdateConfig(CarryOnConfig newConfig)
        {
            this.config = newConfig;
        }

        private const float FirstPersonVerticalOffset = -0.05F;

        private static readonly Dictionary<CarrySlot, Dictionary<string, SlotRenderSettings>> RenderSettings = CreateRenderSettings();
        private static Dictionary<CarrySlot, Dictionary<string, SlotRenderSettings>> CreateRenderSettings() => new() {
            { CarrySlot.Hands    , new Dictionary<string, SlotRenderSettings> {
                    { "hands", new SlotRenderSettings(CarryCode.FrontCarryAttachmentPoint, -0.3F, -0.6F, -0.5F) } } },

            { CarrySlot.Back     , new Dictionary<string, SlotRenderSettings> {
                    { "backpack-none", new SlotRenderSettings("Back", -0.3F, -0.6F, -0.5F) },
                    { "backpack-small", new SlotRenderSettings("Back", -0.2F, -0.6F, -0.5F) },
                    { "backpack-large", new SlotRenderSettings("Back", -0.025F, -0.6F, -0.5F) }
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

        private record QueuedDraw(
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

        private float[] RentMatrix() => matrixPool.Count > 0 ? matrixPool.Pop() : new float[16];

        private Vec4f GetRenderTint(CarriedRenderInfo info)
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
            var animator = entity.AnimManager.Animator;

            if (renderer == null) return;

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

            string transformGroupName = entity.ResolveCarryTransformGroupBase(config, carried.Slot);

            var renderSettings = RenderSettings?[carried.Slot]?[transformGroupName];
            if (renderSettings == null) return;

            var carriedRenderInfo = cacheManager.GetRenderInfoCached(entity, carried, transformGroupName);
            if (carriedRenderInfo == null || carriedRenderInfo.Length == 0) return;

            float[] modelMat;
            if (inHands && isFirstPerson && !isImmersiveFirstPerson && !isShadowPass)
            {
                modelMat = firstPersonRenderer.GetFirstPersonHandsMatrix(entity, viewMat, deltaTime, renderTick);
                Mat4f.Translate(modelMat, modelMat, 0.0F, FirstPersonVerticalOffset, 0.0F);
            }
            else
            {
                if (animator == null) return;
                AttachmentPointAndPose? attachPointAndPose = animator.GetAttachmentPointPose(renderSettings.AttachmentPoint);
                if (attachPointAndPose == null) return;
                var attachmentPointMatrix = CarryRenderHelpers.GetAttachmentPointMatrix(renderer, attachPointAndPose);
                if (attachmentPointMatrix == null) return;
                modelMat = attachmentPointMatrix;
            }

            float[] initialMatrix = RentMatrix();

            var initial = carriedRenderInfo[0];
            initial.SkipTransform = true;
            Array.Copy(modelMat, initialMatrix, 16);
            CarryRenderHelpers.ApplyTransformInPlace(initial.RenderInfo.Transform, initialMatrix, renderSettings.Offset);

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
                    CarryRenderHelpers.ApplyTransformInPlace(info.RenderInfo.Transform, attachedRootMatrix, ZeroOffset);
                    break;
                }
            }

            var zeroOffset = ZeroOffset;

            if (isShadowPass)
            {
                RenderCarriedShadowPass(carriedRenderInfo, initialMatrix, attachedRootMatrix, zeroOffset, renderer);
            }
            else
            {
                RenderCarriedMainPass(entity, carried, carriedRenderInfo, initialMatrix, attachedRootMatrix, zeroOffset,
                                      stage, deferHandsOpaqueUntilAfterOit, viewMat, renderer);
            }

            if (attachedRootMatrix != null)
                matrixPool.Push(attachedRootMatrix);

            matrixPool.Push(initialMatrix);
        }

        private void RenderCarriedShadowPass(CarriedRenderInfo[] carriedRenderInfo, float[] initialMatrix, float[]? attachedRootMatrix, Vec3f zeroOffset, EntityShapeRenderer renderer)
        {
            var rapi = api.Render;
            var prog = rapi.CurrentActiveShader;

            foreach (var info in carriedRenderInfo)
            {
                if (!info.RenderEnabled) continue;

                if (info.RenderInfo.ModelRef == null || info.RenderInfo.ModelRef.Disposed)
                {
                    disposedDetected = true;
                    continue;
                }

                float[] matrix;
                bool rentedMatrix = false;
                if (info.SkipTransform)
                {
                    matrix = initialMatrix;
                }
                else
                {
                    matrix = RentMatrix();
                    rentedMatrix = true;
                    if (info.IsAttachedBlock && attachedRootMatrix != null)
                        Array.Copy(attachedRootMatrix, matrix, 16);
                    else
                        Array.Copy(initialMatrix, matrix, 16);
                    CarryRenderHelpers.ApplyTransformInPlace(info.RenderInfo.Transform, matrix, zeroOffset);
                    if (info.SecondaryTransform != null)
                    {
                        CarryRenderHelpers.ApplyTransformInPlace(info.SecondaryTransform, matrix, zeroOffset);
                    }
                }

                var shadowMatrix = RentMatrix();
                Array.Copy(matrix, shadowMatrix, 16);
                Mat4f.Mul(shadowMatrix, rapi.CurrentShadowProjectionMatrix, shadowMatrix);

                bool disabledCull = false;
                if (!info.RenderInfo.CullFaces)
                {
                    rapi.GlDisableCullFace();
                    disabledCull = true;
                }

                try
                {
                    prog.BindTexture2D("tex2d", info.RenderInfo.TextureId, 0);
                    prog.UniformMatrix("mvpMatrix", shadowMatrix);
                    prog.Uniform("origin", renderer.OriginPos);

                    rapi.RenderMultiTextureMesh(info.RenderInfo.ModelRef, "tex2d");
                }
                finally
                {
                    if (disabledCull)
                    {
                        rapi.GlEnableCullFace();
                    }
                }

                matrixPool.Push(shadowMatrix);
                if (rentedMatrix) matrixPool.Push(matrix);
            }
        }

        private void RenderCarriedMainPass(EntityAgent entity, CarriedBlock carried, CarriedRenderInfo[] carriedRenderInfo,
                                           float[] initialMatrix, float[]? attachedRootMatrix, Vec3f zeroOffset,
                                           EnumRenderStage stage, bool deferHandsOpaqueUntilAfterOit,
                                           float[] viewMat, EntityShapeRenderer renderer)
        {
            var rapi = api.Render;
            var renderOpaquePhase = stage == EnumRenderStage.Opaque || (stage == EnumRenderStage.AfterOIT && deferHandsOpaqueUntilAfterOit);
            var renderTranslucentPhase = stage == EnumRenderStage.AfterOIT;

            if (!renderOpaquePhase && !renderTranslucentPhase)
            {
                return;
            }

            var prog = rapi.PreparedStandardShader((int)entity.Pos.X, (int)entity.Pos.Y, (int)entity.Pos.Z);

            var draws = new List<QueuedDraw>(carriedRenderInfo.Length);
            foreach (var info in carriedRenderInfo)
            {
                if (!info.RenderEnabled) continue;

                var drawMatrix = RentMatrix();
                if (info.IsAttachedBlock && attachedRootMatrix != null)
                    Array.Copy(attachedRootMatrix, drawMatrix, 16);
                else
                    Array.Copy(initialMatrix, drawMatrix, 16);
                if (!info.SkipTransform)
                {
                    CarryRenderHelpers.ApplyTransformInPlace(info.RenderInfo.Transform, drawMatrix, zeroOffset);
                    if (info.SecondaryTransform != null)
                    {
                        CarryRenderHelpers.ApplyTransformInPlace(info.SecondaryTransform, drawMatrix, zeroOffset);
                    }
                }

                draws.Add(new QueuedDraw
                (
                    Info: info,
                    Matrix: drawMatrix,
                    IsRoot: info.SkipTransform,
                    Phases: CarryRenderHelpers.ResolveDefaultPhases(info),
                    AlphaTestOpaque: info.AlphaTestOpaque ?? 0.5f,
                    AlphaTestBlend: info.AlphaTestBlend ?? 0.15f
                ));
            }

            var renderRootFirst = carried.GetCarryableBehavior()?.RenderRootFirst ?? false;
            var roots = new List<QueuedDraw>();
            var nonRoots = new List<QueuedDraw>();
            foreach (var d in draws)
            {
                if (d.IsRoot) roots.Add(d);
                else nonRoots.Add(d);
            }
            draws = renderRootFirst
                ? [.. roots, .. nonRoots]
                : [.. nonRoots, .. roots];

            if (renderOpaquePhase)
            {
                RenderQueuedPhase(draws, translucentPhase: false, prog, viewMat, renderer);
            }

            if (renderTranslucentPhase)
            {
                RenderQueuedPhase(draws, translucentPhase: true, prog, viewMat, renderer);
            }

            if (renderOpaquePhase && initialMatrix != null)
            {
                var labelBaseMatrix = attachedRootMatrix ?? initialMatrix;
                labelRenderer.TryRender(carried, labelBaseMatrix, viewMat, prog, entity.Pos.AsBlockPos);

                if (RenderAttachedBlocks && carried.HasAttachedBlocks)
                {
                    RenderAttachedBlockLabels(carried, attachedRootMatrix ?? initialMatrix, viewMat, prog, entity);
                }
            }

            foreach (var d in draws)
            {
                matrixPool.Push(d.Matrix);
            }

            prog.Stop();
        }

        private void RenderQueuedPhase(
            List<QueuedDraw> draws,
            bool translucentPhase,
            IStandardShaderProgram prog,
            float[] viewMat,
            EntityShapeRenderer renderer)
        {
            var rapi = api.Render;

            if (translucentPhase)
            {
                rapi.GLDepthMask(false);
                rapi.GlToggleBlend(true, 0);
            }
            else
            {
                rapi.GLDepthMask(true);
                rapi.GlToggleBlend(false, 0);
            }

            try
            {
                foreach (var d in draws)
                {
                    if (!CarryRenderHelpers.ShouldDrawInPhase(d.Phases, translucentPhase)) continue;

                    var info = d.Info;

                    if (info.RenderInfo.ModelRef == null || info.RenderInfo.ModelRef.Disposed)
                    {
                        disposedDetected = true;
                        continue;
                    }

                    bool disabledCull = false;

                    try
                    {
                        if (!info.RenderInfo.CullFaces)
                        {
                            rapi.GlDisableCullFace();
                            disabledCull = true;
                        }

                        prog.Tex2D = info.RenderInfo.TextureId;
                        prog.ViewMatrix = viewMat;
                        prog.ModelMatrix = d.Matrix;
                        prog.DontWarpVertices = info.EnableVertexWarp ? 2 : 1;
                        prog.RgbaTint = GetRenderTint(info);

                        prog.AlphaTest = translucentPhase ? d.AlphaTestBlend : d.AlphaTestOpaque;

                        prog.NormalShaded = info.NormalShaded.HasValue ? (info.NormalShaded.Value ? 1 : 0) : 1;
                        prog.RgbaGlowIn = info.RgbGlowIntensity ?? new Vec4f(0f, 0f, 0f, 0f);

                        rapi.RenderMultiTextureMesh(info.RenderInfo.ModelRef, "tex");
                    }
                    finally
                    {
                        if (disabledCull) rapi.GlEnableCullFace();
                    }
                }
            }
            finally
            {
                rapi.GLDepthMask(true);
                rapi.GlToggleBlend(false, 0);
            }
        }

        private void RenderAttachedBlockLabels(CarriedBlock carried, float[] initialMatrix, float[] viewMat, IStandardShaderProgram prog, EntityAgent entity)
        {
            if (carried.AttachedBlocks == null) return;

            var world = api.World;
            if (world == null) return;

            var defaultFacing = carried.GetCarryableBehavior()?.RootRenderFacing;
            int offsetSteps = CarryRotationHelper.GetOriginalToModelDefaultSteps(carried, defaultFacing);

            foreach (var attached in carried.AttachedBlocks)
            {
                if (attached == null) continue;

                var offset = CarryRotationHelper.RotateOffset(attached.RelativeOffset, offsetSteps);

                var rotatedFace = attached.OriginalLocalFace != null
                    ? CarryRotationHelper.RotateFacing(attached.OriginalLocalFace, offsetSteps)
                    : null;

                var childMatrix = RentMatrix();
                Array.Copy(initialMatrix, childMatrix, 16);

                var offsetTransform = new ModelTransform();
                offsetTransform.EnsureDefaultValues();
                offsetTransform.Translation.Set(offset.X, offset.Y, offset.Z);
                if (rotatedFace != null)
                {
                    var facingDegrees = -CarryRotationHelper.FacingToYRotationDegrees(rotatedFace);
                    offsetTransform.Origin.Set(0.5f, 0.5f, 0.5f);
                    offsetTransform.Rotation.Y = facingDegrees;
                }
                CarryRenderHelpers.ApplyTransformInPlace(offsetTransform, childMatrix);

                CarriedBlock labelCarriedBlock = attached.CarriedBlock;
                var originalCode = attached.OriginalBlockCode;
                if (originalCode != null)
                {
                    Block? variantBlock = rotatedFace != null
                        ? CarryRotationHelper.GetRotatedVariantBlock(world, originalCode, rotatedFace)
                        : null;
                    variantBlock ??= world.GetBlock(originalCode);

                    if (variantBlock != null)
                    {
                        var variantStack = new ItemStack(variantBlock);
                        variantStack.ResolveBlockOrItem(world);
                        labelCarriedBlock = new CarriedBlock(
                            CarrySlot.Attached,
                            variantStack,
                            attached.BlockEntityData,
                            null,
                            variantBlock.Code,
                            attached.OriginalMeshAngle
                        );
                    }
                }

                var behavior = labelCarriedBlock.GetCarryableBehavior();
                var attachedTransform = behavior?.LabelRenderSettings?.AttachedTransform;
                if (attachedTransform != null)
                {
                    CarryRenderHelpers.ApplyTransformInPlace(attachedTransform, childMatrix);
                }

                labelRenderer.TryRender(labelCarriedBlock, childMatrix, viewMat, prog, entity.Pos.AsBlockPos);
                matrixPool.Push(childMatrix);
            }
        }
    }
}
