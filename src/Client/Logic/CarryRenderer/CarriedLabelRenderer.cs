using System;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class CarriedLabelRenderer : IDisposable
    {
        private readonly ICoreClientAPI api;
        private readonly CarriedLabelManager labelManager;
        private MeshRef labelQuad;

        internal CarriedLabelRenderer(ICoreClientAPI api)
        {
            this.api = api;
            this.labelManager = new CarriedLabelManager(api);
        }

        public void Dispose()
        {
            labelManager?.Dispose();
            if (labelQuad != null)
            {
                api.Render.DeleteMesh(labelQuad);
                labelQuad = null;
            }
        }

        internal void TryRender(CarriedBlock carried, float[] initialMatrix, float[] viewMat, IStandardShaderProgram prog, BlockPos lightPos)
        {
            if (carried == null) return;
            var beData = carried.BlockEntityData;
            if (beData == null) return;

            var behavior = carried.GetCarryableBehavior();
            var labelRenderSettings = behavior?.LabelRenderSettings;
            var labelTransform = labelRenderSettings?.Transform;
            if (labelTransform == null) return; // No label transform => no label

            var text = beData.GetString("text", null);

            ItemStack labelStack = null;

            if (labelRenderSettings?.IconFromInventory == true
                && beData["inventory"] is TreeAttribute inventory
                && inventory["slots"] is TreeAttribute slots)
            {
                foreach (var slotValue in slots.Values)
                {
                    if (slotValue is not ItemstackAttribute itemAttr)
                    {
                        continue;
                    }

                    labelStack = itemAttr.GetValue() as ItemStack;
                    if (labelStack?.Collectible != null)
                    {
                        break;
                    }
                }
            }

            labelStack ??= beData.GetItemstack("labelStack", null);
            labelStack?.ResolveBlockOrItem(this.api.World);

            if (string.IsNullOrWhiteSpace(text) && labelStack?.Collectible == null)
            {
                return;
            }

            int color = beData.GetInt("color", unchecked((int)0xFFFFFFFF));
            float fontSize = beData?.GetFloat("fontSize", 20f) ?? 20f;
            EnsureLabelQuad();

            var additionalTransforms = labelRenderSettings.AdditionalTransforms;
            int transformCount = 1 + (additionalTransforms?.Count ?? 0);

            for (int ti = 0; ti < transformCount; ti++)
            {
                var transform = ti == 0 ? labelTransform : additionalTransforms[ti - 1];
                if (transform == null) continue;

                var modelMat = Mat4f.CloneIt(initialMatrix);
                Mat4f.Translate(modelMat, modelMat, transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
                Mat4f.Translate(modelMat, modelMat, transform.Origin.X, transform.Origin.Y, transform.Origin.Z);
                Mat4f.RotateX(modelMat, modelMat, transform.Rotation.X * GameMath.DEG2RAD);
                Mat4f.RotateZ(modelMat, modelMat, transform.Rotation.Z * GameMath.DEG2RAD);
                Mat4f.RotateY(modelMat, modelMat, transform.Rotation.Y * GameMath.DEG2RAD);
                Mat4f.Scale(modelMat, modelMat, transform.ScaleXYZ.X, transform.ScaleXYZ.Y, transform.ScaleXYZ.Z);
                Mat4f.Translate(modelMat, modelMat, -transform.Origin.X, -transform.Origin.Y, -transform.Origin.Z);

                if (labelStack?.Collectible != null)
                {
                    const int iconColor = unchecked((int)0xFFFFFFFF);
                    var iconLabel = this.labelManager.GetItemIconLabel(labelStack, iconColor, labelRenderSettings);
                    if (iconLabel.ready && iconLabel.mesh != null)
                    {
                        const float iconWidth = 0.45f;
                        const float iconHeight = 0.45f;

                        Mat4f.Translate(modelMat, modelMat, -0.5f, -0.5f, 0f);
                        Mat4f.Scale(modelMat, modelMat, iconWidth, iconHeight, 1f);

                        prog.Tex2D = iconLabel.textureId;
                        prog.AlphaTest = 0.0f;
                        prog.ViewMatrix = viewMat;
                        prog.ModelMatrix = modelMat;
                        prog.DontWarpVertices = 1;
                        prog.RgbaTint = CarryRenderHelpers.DefaultTint;
                        prog.NormalShaded = 0;
                        prog.ExtraGodray = 0f;
                        prog.SsaoAttn = 0f;
                        prog.OverlayOpacity = 0f;
                        prog.AddRenderFlags = 0;

                        if (this.api?.World?.BlockAccessor != null && lightPos != null)
                        {
                            prog.RgbaLightIn = this.api.World.BlockAccessor.GetLightRGBs(lightPos);
                        }

                        api.Render.GlToggleBlend(true, EnumBlendMode.PremultipliedAlpha);
                        this.api.Render.GlEnableCullFace();
                        this.api.Render.GLEnableDepthTest();
                        this.api.Render.GLDepthMask(false);
                        this.api.Render.RenderMesh(iconLabel.mesh);
                        this.api.Render.GLDepthMask(true);
                        this.api.Render.GlToggleBlend(false);
                        continue;
                    }
                }

                if (string.IsNullOrWhiteSpace(text)) continue;

                const float maxWidth = 1.80f; // doubled capacity
                const float maxHeight = 0.45f; // 1.5x vertical capacity
                // Decide wrap width in pixels relative to max world width mapped to 1024 texture width
                // Use narrower wrap width similar to sign text area
                var wrapWidthPx = labelRenderSettings?.MaxWidth ?? 200;
                var label = this.labelManager.GetLabel(text, color, fontSize, labelRenderSettings, wrapWidthPx);
                if (label == null) continue; // silent fail
                var (tex, w, h) = label.Value;
                float aspect = (h == 0) ? 1f : (float)w / h;
                float targetHeight = Math.Min(maxHeight, 0.12f + (fontSize / 64f));
                float targetWidth = targetHeight * aspect;
                if (targetWidth > maxWidth)
                {
                    float scaleDown = maxWidth / targetWidth;
                    targetWidth = maxWidth;
                    targetHeight *= scaleDown;
                }
                // Additional clamp to keep from visually exceeding chest face too much
                const float visualClamp = 1.20f;
                if (targetWidth > visualClamp)
                {
                    float scaleDown = visualClamp / targetWidth;
                    targetWidth = visualClamp;
                    targetHeight *= scaleDown;
                }
                // Add internal margins (shrink a bit more after fit)
                const float marginFactor = 0.95f; // allow more usable space with larger quad
                targetWidth *= marginFactor;
                targetHeight *= marginFactor;
                Mat4f.Translate(modelMat, modelMat, -0.5f, -0.5f, 0f);
                Mat4f.Scale(modelMat, modelMat, targetWidth, targetHeight, 1f);

                prog.Tex2D = tex.TextureId;
                prog.AlphaTest = 0.05f;
                prog.ViewMatrix = viewMat;
                prog.ModelMatrix = modelMat;
                prog.DontWarpVertices = 1;
                prog.RgbaTint = CarryRenderHelpers.DefaultTint;
                prog.NormalShaded = 0;
                prog.ExtraGodray = 0f;
                prog.SsaoAttn = 0f;
                prog.OverlayOpacity = 0f;
                prog.AddRenderFlags = 0;

                if (this.api?.World?.BlockAccessor != null && lightPos != null)
                {
                    prog.RgbaLightIn = this.api.World.BlockAccessor.GetLightRGBs(lightPos);
                }

                this.api.Render.GLDepthMask(false);
                this.api.Render.GlToggleBlend(true, EnumBlendMode.PremultipliedAlpha);
                this.api.Render.GlDisableCullFace();
                this.api.Render.RenderMesh(this.labelQuad);
                this.api.Render.GlEnableCullFace();
                this.api.Render.GLDepthMask(true);
                this.api.Render.GlToggleBlend(false, 0);
            }
        }

        private void EnsureLabelQuad()
        {
            if (this.labelQuad != null) return;
            var mesh = new MeshData(4, 6, false, false, true, true);
            // Add vertices (positions only) - using AddVertexSkipTex like other code
            mesh.AddVertexSkipTex(0, 0, 0); // 0
            mesh.AddVertexSkipTex(1, 0, 0); // 1
            mesh.AddVertexSkipTex(1, 1, 0); // 2
            mesh.AddVertexSkipTex(0, 1, 0); // 3
            mesh.AddIndices(new[] { 0, 1, 2, 0, 2, 3 });
            // Provide UVs
            mesh.Uv = new float[] { 0, 1, 1, 1, 1, 0, 0, 0 };
            // Colors as bytes RGBA per vertex
            mesh.Rgba = new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 };
            this.labelQuad = api.Render.UploadMesh(mesh);
        }
    }
}
