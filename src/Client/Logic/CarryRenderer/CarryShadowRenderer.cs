using System;
using CarryOn.Client.Models;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class CarryShadowRenderer
    {
        private readonly CarryRenderDispatcher dispatcher;
        private readonly ICoreClientAPI api;

        public CarryShadowRenderer(ICoreClientAPI api, CarryRenderDispatcher dispatcher)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void Render(CarriedRenderInfo[] carriedRenderInfo, float[] initialMatrix, float[]? attachedRootMatrix, Vec3f zeroOffset, EntityShapeRenderer renderer)
        {
            var rapi = api.Render;
            var prog = rapi.CurrentActiveShader;

            foreach (var info in carriedRenderInfo)
            {
                if (!info.RenderEnabled) continue;

                if (info.RenderInfo.ModelRef == null || info.RenderInfo.ModelRef.Disposed)
                {
                    dispatcher.SetDisposedDetected();
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
                    matrix = dispatcher.RentMatrix();
                    rentedMatrix = true;
                    if (info.IsAttachedBlock && attachedRootMatrix != null)
                        Array.Copy(attachedRootMatrix, matrix, 16);
                    else
                        Array.Copy(initialMatrix, matrix, 16);
                    CarryTransformResolver.ApplyTransformInPlace(info.RenderInfo.Transform, matrix, zeroOffset);
                    if (info.SecondaryTransform != null)
                    {
                        CarryTransformResolver.ApplyTransformInPlace(info.SecondaryTransform, matrix, zeroOffset);
                    }
                }

                var shadowMatrix = dispatcher.RentMatrix();
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

                dispatcher.ReturnMatrix(shadowMatrix);
                if (rentedMatrix) dispatcher.ReturnMatrix(matrix);
            }
        }
    }
}
