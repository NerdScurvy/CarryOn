using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class CarryMainRenderer
    {
        private readonly CarryRenderDispatcher dispatcher;
        private readonly ICoreClientAPI api;
        private readonly CarryLabelRenderer labelRenderer;
        private readonly List<CarryRenderDispatcher.QueuedDraw> drawsScratch = new();

        public CarryMainRenderer(ICoreClientAPI api, CarryRenderDispatcher dispatcher, CarryLabelRenderer labelRenderer)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.labelRenderer = labelRenderer ?? throw new ArgumentNullException(nameof(labelRenderer));
        }

        public void Render(EntityAgent entity, CarriedBlock carried, CarriedRenderInfo[] carriedRenderInfo,
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

            var draws = drawsScratch;
            draws.Clear();
            foreach (var info in carriedRenderInfo)
            {
                if (info == null || !info.RenderEnabled) continue;

                var drawMatrix = CarryRenderHelpers.SetupDrawMatrix(dispatcher, info, initialMatrix, attachedRootMatrix, zeroOffset, alwaysRent: true);

                draws.Add(new CarryRenderDispatcher.QueuedDraw
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

            if (renderOpaquePhase)
            {
                RenderQueuedPhase(draws, translucentPhase: false, prog, viewMat, renderer, renderRootFirst);
            }

            if (renderTranslucentPhase)
            {
                RenderQueuedPhase(draws, translucentPhase: true, prog, viewMat, renderer, renderRootFirst);
            }

            if (renderOpaquePhase && initialMatrix != null)
            {
                var labelBaseMatrix = attachedRootMatrix ?? initialMatrix;
                labelRenderer.TryRender(carried, labelBaseMatrix, viewMat, prog, entity.Pos.AsBlockPos);

                if (dispatcher.RenderAttachedBlocks && carried.HasAttachedBlocks)
                {
                    dispatcher.RenderAttachedBlockLabels(carried, attachedRootMatrix ?? initialMatrix, viewMat, prog, entity);
                }
            }

            foreach (var d in draws)
            {
                dispatcher.ReturnMatrix(d.Matrix);
            }

            prog.Stop();
        }

        private void RenderQueuedPhase(
            List<CarryRenderDispatcher.QueuedDraw> draws,
            bool translucentPhase,
            IStandardShaderProgram prog,
            float[] viewMat,
            EntityShapeRenderer renderer,
            bool renderRootFirst)
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
                DrawQueuedPass(draws, translucentPhase, drawRoots: renderRootFirst, prog, viewMat, renderer);
                DrawQueuedPass(draws, translucentPhase, drawRoots: !renderRootFirst, prog, viewMat, renderer);
            }
            finally
            {
                rapi.GLDepthMask(true);
                rapi.GlToggleBlend(false, 0);
            }
        }

        private void DrawQueuedPass(
            List<CarryRenderDispatcher.QueuedDraw> draws,
            bool translucentPhase,
            bool drawRoots,
            IStandardShaderProgram prog,
            float[] viewMat,
            EntityShapeRenderer renderer)
        {
            var rapi = api.Render;

            foreach (var d in draws)
            {
                if (!CarryRenderHelpers.ShouldDrawInPhase(d.Phases, translucentPhase)) continue;
                if (d.IsRoot != drawRoots) continue;

                var info = d.Info;

                if (info.RenderInfo.ModelRef == null || info.RenderInfo.ModelRef.Disposed)
                {
                    dispatcher.SetDisposedDetected();
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
                    prog.RgbaTint = dispatcher.GetRenderTint(info);

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
    }
}
