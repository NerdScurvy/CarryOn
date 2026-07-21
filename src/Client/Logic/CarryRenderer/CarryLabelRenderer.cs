using System;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal sealed class CarryLabelRenderer : IDisposable
    {
        private const int IconLabelColor = unchecked((int)0xFFFFFFFF);
        private const float IconLabelSize = 0.45f;
        private const float MaxLabelWidth = 1.80f;
        private const float MaxLabelHeight = 0.45f;
        private const float FontHeightOffset = 0.12f;
        private const float VisualClampWidth = 1.20f;
        private const float MarginFactor = 0.95f;

        private readonly ICoreClientAPI api;
        private readonly CarryLabelManager labelManager;
        private MeshRef? labelQuad;

        internal CarryLabelRenderer(ICoreClientAPI api)
        {
            this.api = api;
            this.labelManager = new CarryLabelManager(api);
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
            if (labelRenderSettings == null) return;

            var labelTransform = labelRenderSettings.Transform;
            if (labelTransform == null) return; // No label transform => no label

            var render = this.api.Render;
            if (render == null) return;

            var text = beData.GetString("text", null);

            ItemStack? labelStack = null;

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

            var additionalTransforms = labelRenderSettings!.AdditionalTransforms ?? [];
            int transformCount = 1 + (additionalTransforms?.Count ?? 0);

            for (int ti = 0; ti < transformCount; ti++)
            {
                if (ti == 0)
                {
                    var transform = labelTransform;

                    var modelMat = Mat4f.CloneIt(initialMatrix);
                    CarryTransformResolver.ApplyTransformInPlace(transform, modelMat);

                    if (labelStack?.Collectible != null)
                    {
                        var iconLabel = this.labelManager.GetItemIconLabel(labelStack, IconLabelColor, labelRenderSettings);
                        if (iconLabel.ready && iconLabel.mesh != null)
                        {
                            Mat4f.Translate(modelMat, modelMat, -0.5f, -0.5f, 0f);
                            Mat4f.Scale(modelMat, modelMat, IconLabelSize, IconLabelSize, 1f);

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

                            render.GlToggleBlend(true, EnumBlendMode.PremultipliedAlpha);
                            render.GlEnableCullFace();
                            render.GLEnableDepthTest();
                            render.GLDepthMask(false);
                            render.RenderMesh(iconLabel.mesh);
                            render.GLDepthMask(true);
                            render.GlToggleBlend(false);
                            continue;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(text)) continue;

                    // Decide wrap width in pixels relative to max world width mapped to 1024 texture width
                    // Use narrower wrap width similar to sign text area
                    var wrapWidthPx = labelRenderSettings?.MaxWidth ?? 200;
                    var label = this.labelManager.GetLabel(text, color, fontSize, labelRenderSettings, wrapWidthPx);
                    if (label == null) continue; // silent fail
                    var (tex, w, h) = label.Value;
                    float aspect = (h == 0) ? 1f : (float)w / h;
                    float targetHeight = Math.Min(MaxLabelHeight, FontHeightOffset + (fontSize / 64f));
                    float targetWidth = targetHeight * aspect;
                    if (targetWidth > MaxLabelWidth)
                    {
                        float scaleDown = MaxLabelWidth / targetWidth;
                        targetWidth = MaxLabelWidth;
                        targetHeight *= scaleDown;
                    }
                    // Additional clamp to keep from visually exceeding chest face too much
                    if (targetWidth > VisualClampWidth)
                    {
                        float scaleDown = VisualClampWidth / targetWidth;
                        targetWidth = VisualClampWidth;
                        targetHeight *= scaleDown;
                    }
                    // Add internal margins (shrink a bit more after fit)
                    targetWidth *= MarginFactor;
                    targetHeight *= MarginFactor;
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

                    render.GLDepthMask(false);
                    render.GlToggleBlend(true, EnumBlendMode.PremultipliedAlpha);
                    render.GlDisableCullFace();
                    render.RenderMesh(this.labelQuad);
                    render.GlEnableCullFace();
                    render.GLDepthMask(true);
                    render.GlToggleBlend(false, 0);
                    continue;
                }

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
