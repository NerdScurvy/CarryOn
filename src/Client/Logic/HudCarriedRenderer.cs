using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Client.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Logic
{
    internal sealed class HudCarriedRenderer : IRenderer
    {
        private readonly ICoreClientAPI api;
        private readonly ICarryManager carryManager;
        private readonly CarryOnClientConfig config;

        // Highlight timers (runtime state, not config)
        private float handsHighlightSecondsRemaining;
        private float backHighlightSecondsRemaining;

        // Cached positioning values - update only when GUI scale or frame size changes
        private float cachedGUIScale = -1f;
        private float cachedFrameWidth = -1f;
        private float cachedFrameHeight = -1f;
        private float cachedSlotSize;
        private float cachedBackgroundSize;
        private float cachedHotbarWidth;
        private float cachedHotbarCenterX;
        private float cachedHotbarY;

        // Pre-calculated positions for multiple icons (3 left, 3 right of hotbar)
        private readonly (int x, int y)[] cachedLeftPositions = new (int, int)[3];
        private readonly (int x, int y)[] cachedRightPositions = new (int, int)[3];
        private MeshRef? highlightMesh;
        private MeshRef? rectMesh;

        // Color cache — only re-parse hex when the string changes
        private string? cachedBgHex;
        private float cachedBgAlpha;
        private Vec4f cachedBgVec = new();
        private string? cachedBrHex;
        private float cachedBrAlpha;
        private Vec4f cachedBrVec = new();
        private string? cachedHiHex;
        private float cachedHiAlpha;
        private Vec4f cachedHiVec = new();

        public HudCarriedRenderer(ICoreClientAPI api, ICarryManager carryManager, CarryOnClientConfig config)
        {
            this.api = api;
            this.carryManager = carryManager;
            this.config = config;
        }

        public double RenderOrder => 1.0;
        public int RenderRange => 10;

        private Vec4f GetCachedBgColor()
        {
            if (cachedBgHex != config.AnchorBackgroundColor || cachedBgAlpha != config.AnchorBackgroundAlpha)
            {
                ColorHelper.TryParseHex(config.AnchorBackgroundColor, config.AnchorBackgroundAlpha, out cachedBgVec);
                cachedBgHex = config.AnchorBackgroundColor;
                cachedBgAlpha = config.AnchorBackgroundAlpha;
            }
            return cachedBgVec;
        }

        private Vec4f GetCachedBrColor()
        {
            if (cachedBrHex != config.AnchorBorderColor || cachedBrAlpha != config.AnchorBorderAlpha)
            {
                ColorHelper.TryParseHex(config.AnchorBorderColor, config.AnchorBorderAlpha, out cachedBrVec);
                cachedBrHex = config.AnchorBorderColor;
                cachedBrAlpha = config.AnchorBorderAlpha;
            }
            return cachedBrVec;
        }

        private Vec4f GetCachedHiColor()
        {
            if (cachedHiHex != config.IconHighlightColor || cachedHiAlpha != config.IconHighlightAlpha)
            {
                ColorHelper.TryParseHex(config.IconHighlightColor, 1f, out cachedHiVec);
                cachedHiHex = config.IconHighlightColor;
                cachedHiAlpha = config.IconHighlightAlpha;
            }
            return cachedHiVec;
        }

        public void TriggerHandsHighlight(float seconds = HudCarried.DefaultHighlightDuration + HudCarried.HighlightFadeExtra)
            => handsHighlightSecondsRemaining = Math.Max(handsHighlightSecondsRemaining, seconds);

        public void TriggerBackHighlight(float seconds = HudCarried.DefaultHighlightDuration + HudCarried.HighlightFadeExtra)
            => backHighlightSecondsRemaining = Math.Max(backHighlightSecondsRemaining, seconds);

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Ortho) return;

            var player = this.api.World.Player;
            if (player == null) return;

            var rapi = this.api.Render;

            // Check if we need to update cached positioning (like StatusHud renderer pattern)
            float currentGUIScale = Vintagestory.API.Config.RuntimeEnv.GUIScale;
            float currentFrameWidth = rapi.FrameWidth;
            float currentFrameHeight = rapi.FrameHeight;

            if (this.cachedGUIScale != currentGUIScale ||
                this.cachedFrameWidth != currentFrameWidth ||
                this.cachedFrameHeight != currentFrameHeight)
            {
                this.UpdateCachedPositions(currentGUIScale, currentFrameWidth, currentFrameHeight);
            }

            // Decrement highlight timers
            if (handsHighlightSecondsRemaining > 0f)
                handsHighlightSecondsRemaining = Math.Max(0f, handsHighlightSecondsRemaining - deltaTime);
            if (backHighlightSecondsRemaining > 0f)
                backHighlightSecondsRemaining = Math.Max(0f, backHighlightSecondsRemaining - deltaTime);

            // Read config values directly
            var handsAnchor = config.HandsAnchor;
            var backAnchor = config.BackAnchor;

            // Parse colors from config (cached — only re-parses when hex string changes)
            var bgVec = GetCachedBgColor();
            var brVec = GetCachedBrColor();

            // Draw bounding rect(s) for anchors BEFORE rendering items so highlights render on top of backgrounds
            bool drewCombinedRect = false;
            if (backAnchor != HudCarried.Anchor.None && handsAnchor != HudCarried.Anchor.None)
            {
                int a = (int)backAnchor;
                int b = (int)handsAnchor;

                bool bothLeft = (a >= (int)HudCarried.Anchor.L1 && a <= (int)HudCarried.Anchor.L3) && (b >= (int)HudCarried.Anchor.L1 && b <= (int)HudCarried.Anchor.L3);
                bool bothRight = (a >= (int)HudCarried.Anchor.R1 && a <= (int)HudCarried.Anchor.R3) && (b >= (int)HudCarried.Anchor.R1 && b <= (int)HudCarried.Anchor.R3);

                if ((bothLeft || bothRight) && Math.Abs(a - b) == 1)
                {
                    var posA = this.GetPositionForAnchor(backAnchor);
                    var posB = this.GetPositionForAnchor(handsAnchor);

                    float centerX = (posA.x + posB.x) / 2f;
                    float centerY = (posA.y + posB.y) / 2f + 1f;
                    float width = Math.Abs(posA.x - posB.x) + this.cachedBackgroundSize;
                    float height = Math.Max(this.cachedBackgroundSize, 2f * (this.cachedFrameHeight - centerY));

                    try
                    {
                        if (config.AnchorBackgroundEnabled)
                        {
                            DrawRectFilled(rapi, centerX, centerY, width, height, bgVec);
                        }
                    }
                    catch (Exception ex) { api?.Logger?.Debug("CarryOn: Error drawing anchor background: {0}", ex.Message); }

                    if (config.AnchorBorderEnabled)
                    {
                        DrawRectOutline(rapi, centerX, centerY, width, height, Math.Max(0.5f, this.cachedSlotSize * 0.08f), brVec);
                    }

                    drewCombinedRect = true;
                }
            }

            if (!drewCombinedRect)
            {
                if (backAnchor != HudCarried.Anchor.None)
                {
                    var posB = this.GetPositionForAnchor(backAnchor);
                    float centerY = posB.y + 1f;
                    float height = Math.Max(this.cachedBackgroundSize, 2f * (this.cachedFrameHeight - centerY));
                    try
                    {
                        if (config.AnchorBackgroundEnabled)
                        {
                            DrawRectFilled(rapi, posB.x, centerY, this.cachedBackgroundSize, height, bgVec);
                        }
                    }
                    catch (Exception ex) { api?.Logger?.Debug("CarryOn: Error drawing anchor background: {0}", ex.Message); }

                    if (config.AnchorBorderEnabled)
                    {
                        DrawRectOutline(rapi, posB.x, centerY, this.cachedBackgroundSize, height, Math.Max(0.5f, this.cachedSlotSize * 0.08f), brVec);
                    }
                }

                if (handsAnchor != HudCarried.Anchor.None)
                {
                    var posH = this.GetPositionForAnchor(handsAnchor);
                    float centerY = posH.y + 1f;
                    float height = Math.Max(this.cachedBackgroundSize, 2f * (this.cachedFrameHeight - centerY));
                    try
                    {
                        if (config.AnchorBackgroundEnabled)
                        {
                            DrawRectFilled(rapi, posH.x, centerY, this.cachedBackgroundSize, height, bgVec);
                        }
                    }
                    catch (Exception ex) { api?.Logger?.Debug("CarryOn: Error drawing anchor background: {0}", ex.Message); }

                    if (config.AnchorBorderEnabled)
                    {
                        DrawRectOutline(rapi, posH.x, centerY, this.cachedBackgroundSize, height, Math.Max(0.5f, this.cachedSlotSize * 0.08f), brVec);
                    }
                }
            }

            // Render carried hands item
            var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);
            if (carriedHands != null)
            {
                RenderCarriedBlock(rapi, carriedHands, handsAnchor, handsHighlightSecondsRemaining);
            }

            // Render carried back item
            var carriedBack = carryManager.GetCarried(player.Entity, CarrySlot.Back);
            if (carriedBack != null)
            {
                RenderCarriedBlock(rapi, carriedBack, backAnchor, backHighlightSecondsRemaining);
            }
        }

        private void EnsureHighlightMesh(int steps = 32)
        {
            if (this.highlightMesh != null) return;

            int vertexCount = 1 + steps;
            int indexCount = steps * 3;
            var mesh = new MeshData(vertexCount, indexCount, false, false, true, true);

            mesh.AddVertexSkipTex(0f, 0f, 0f);

            for (int i = 0; i < steps; i++)
            {
                float a = (float)i / steps * GameMath.TWOPI;
                float x = (float)Math.Cos(a);
                float y = (float)Math.Sin(a);
                mesh.AddVertexSkipTex(x, y, 0f);
            }

            for (int i = 0; i < steps; i++)
            {
                int a = 0;
                int b = 1 + i;
                int c = 1 + ((i + 1) % steps);
                mesh.AddIndices(new[] { a, b, c });
            }

            var uvs = new float[vertexCount * 2];
            for (int i = 0; i < vertexCount; i++) { uvs[i * 2 + 0] = 0; uvs[i * 2 + 1] = 0; }
            mesh.Uv = uvs;

            var rgba = new byte[vertexCount * 4];
            rgba[0] = 255; rgba[1] = 255; rgba[2] = 255; rgba[3] = 255;
            for (int i = 1; i < vertexCount; i++)
            {
                int idx = i * 4;
                rgba[idx + 0] = 255;
                rgba[idx + 1] = 255;
                rgba[idx + 2] = 255;
                rgba[idx + 3] = 0;
            }
            mesh.Rgba = rgba;

            this.highlightMesh = this.api.Render.UploadMesh(mesh);
        }

        private void EnsureRectMesh()
        {
            if (this.rectMesh != null) return;

            var mesh = new MeshData(4, 6, false, false, true, true);
            mesh.AddVertexSkipTex(-0.5f, -0.5f, 0f);
            mesh.AddVertexSkipTex(0.5f, -0.5f, 0f);
            mesh.AddVertexSkipTex(0.5f, 0.5f, 0f);
            mesh.AddVertexSkipTex(-0.5f, 0.5f, 0f);

            mesh.AddIndices(new[] { 0, 1, 2, 0, 2, 3 });

            var uvs = new float[8];
            mesh.Uv = uvs;
            var rgba = new byte[16];
            for (int i = 0; i < 4; i++) { rgba[i * 4 + 0] = 255; rgba[i * 4 + 1] = 255; rgba[i * 4 + 2] = 255; rgba[i * 4 + 3] = 255; }
            mesh.Rgba = rgba;

            this.rectMesh = this.api.Render.UploadMesh(mesh);
        }

        private void DrawRectOutline(IRenderAPI rapi, float centerX, float centerY, float width, float height, float thickness, Vec4f color)
        {
            EnsureRectMesh();

            var shader = rapi.CurrentActiveShader;

            try
            {
#pragma warning disable CS0618
                rapi.GlPushMatrix();
#pragma warning restore CS0618

#pragma warning disable CS0618
                rapi.GlTranslate((int)centerX, (int)centerY, 0);
#pragma warning restore CS0618

                // Top bar
                rapi.GlPushMatrix();
                rapi.GlTranslate(0, -(height / 2f - thickness / 2f), 0);
                rapi.GlScale(width, thickness, 0);
                shader.UniformMatrix("modelViewMatrix", rapi.CurrentModelviewMatrix);
                shader.Uniform("rgbaIn", color);
                shader.Uniform("applyColor", 1);
                shader.Uniform("noTexture", 1.0f);
                rapi.RenderMesh(this.rectMesh);
                rapi.GlPopMatrix();

                // Bottom bar
                rapi.GlPushMatrix();
                rapi.GlTranslate(0, (height / 2f - thickness / 2f), 0);
                rapi.GlScale(width, thickness, 0);
                shader.UniformMatrix("modelViewMatrix", rapi.CurrentModelviewMatrix);
                rapi.RenderMesh(this.rectMesh);
                rapi.GlPopMatrix();

                // Left bar
                rapi.GlPushMatrix();
                rapi.GlTranslate(-(width / 2f - thickness / 2f), 0, 0);
                rapi.GlScale(thickness, height, 0);
                shader.UniformMatrix("modelViewMatrix", rapi.CurrentModelviewMatrix);
                rapi.RenderMesh(this.rectMesh);
                rapi.GlPopMatrix();

                // Right bar
                rapi.GlPushMatrix();
                rapi.GlTranslate((width / 2f - thickness / 2f), 0, 0);
                rapi.GlScale(thickness, height, 0);
                shader.UniformMatrix("modelViewMatrix", rapi.CurrentModelviewMatrix);
                rapi.RenderMesh(this.rectMesh);
                rapi.GlPopMatrix();

                shader.Uniform("applyColor", 0);
                shader.Uniform("noTexture", 0.0f);

#pragma warning disable CS0618
                rapi.GlPopMatrix();
#pragma warning restore CS0618
            }
            catch (Exception ex)
            {
                this.api.Logger.Debug("[HudCarried] Exception in DrawRectOutline: " + ex);
            }
        }

        private void DrawRectFilled(IRenderAPI rapi, float centerX, float centerY, float width, float height, Vec4f color)
        {
            EnsureRectMesh();

            var shader = rapi.CurrentActiveShader;

            try
            {
#pragma warning disable CS0618
                rapi.GlPushMatrix();
#pragma warning restore CS0618

                rapi.GlTranslate((int)centerX, (int)centerY, 0);
                rapi.GlScale(width, height, 0);
                shader.UniformMatrix("modelViewMatrix", rapi.CurrentModelviewMatrix);
                shader.Uniform("rgbaIn", color);
                shader.Uniform("applyColor", 1);
                shader.Uniform("noTexture", 1.0f);
                rapi.RenderMesh(this.rectMesh);

                shader.Uniform("applyColor", 0);
                shader.Uniform("noTexture", 0.0f);

#pragma warning disable CS0618
                rapi.GlPopMatrix();
#pragma warning restore CS0618
            }
            catch (Exception ex)
            {
                this.api.Logger.Debug("[HudCarried] Exception in DrawRectFilled: " + ex);
            }
        }

        private void DrawIconHighlight(IRenderAPI rapi, float secondsRemaining, float duration, float centerX, float centerY)
        {
            if (duration <= 0f) return;
            if (!config.IconHighlightEnabled) return;

            float total = duration;

            float mainDuration = Math.Max(0.0001f, Math.Max(0f, total - HudCarried.HighlightFadeExtra));
            float fadeDuration = HudCarried.HighlightFadeExtra;

            float timeElapsed = total - secondsRemaining;

            float progressShrink = Math.Max(0f, Math.Min(1f, timeElapsed / mainDuration));
            float easeShrink = 1f - (1f - progressShrink) * (1f - progressShrink);

            float scale = 1.35f - 0.35f * easeShrink;

            float progressFade = 0f;
            if (timeElapsed >= mainDuration)
            {
                progressFade = Math.Max(0f, Math.Min(1f, (timeElapsed - mainDuration) / Math.Max(0.0001f, fadeDuration)));
            }

            float baseAlpha = config.IconHighlightAlpha;
            float alpha = baseAlpha * (1f - progressFade);

            EnsureHighlightMesh();

            var shader = rapi.CurrentActiveShader;

            try
            {
#pragma warning disable CS0618
                rapi.GlPushMatrix();
                rapi.GlTranslate((int)centerX, (int)centerY, 0);
                float radius = this.cachedSlotSize * 0.8f * scale;
                rapi.GlScale(radius, radius, 0);
                shader.UniformMatrix("modelViewMatrix", rapi.CurrentModelviewMatrix);
#pragma warning restore CS0618

                var hiVec = GetCachedHiColor();
                float finalAlpha = hiVec.W * alpha;
                finalAlpha = Math.Max(0f, Math.Min(1f, finalAlpha));
                var col = new Vec4f(hiVec.X, hiVec.Y, hiVec.Z, finalAlpha);
                shader.Uniform("rgbaIn", col);
                shader.Uniform("applyColor", 1);
                shader.Uniform("noTexture", 1.0f);

                rapi.RenderMesh(this.highlightMesh);

                shader.Uniform("applyColor", 0);
                shader.Uniform("noTexture", 0.0f);

#pragma warning disable CS0618
                rapi.GlPopMatrix();
#pragma warning restore CS0618
            }
            catch (Exception ex)
            {
                this.api.Logger.Debug("[HudCarried] Exception in DrawIconHighlight: " + ex);
            }
        }

        private void RenderCarriedBlock(IRenderAPI rapi, CarriedBlock carriedBlock, HudCarried.Anchor anchor, float highlightSecondsRemaining)
        {
            var slot = new DummySlot(carriedBlock.ItemStack);
            var pos = this.GetPositionForAnchor(anchor);

            if (highlightSecondsRemaining > 0f)
            {
                DrawIconHighlight(rapi, highlightSecondsRemaining, HudCarried.DefaultHighlightDuration + HudCarried.HighlightFadeExtra, pos.x, pos.y);
            }

            var shader = rapi.CurrentActiveShader;
            shader.Uniform("noTexture", 0.0f);
            shader.Uniform("applyColor", 0);

            rapi.RenderItemstackToGui(slot, pos.x, pos.y, 100, this.cachedSlotSize, -1);
        }

        private void UpdateCachedPositions(float guiScale, float frameWidth, float frameHeight)
        {
            this.cachedSlotSize = (float)GuiElement.scaled(HudCarried.BaseSlotSizePixels);
            int margin = (int)GuiElement.scaled(HudCarried.GroupMarginPixels);
            int spacingBetweenIcons = (int)GuiElement.scaled(HudCarried.IconSpacingPixels);

            this.cachedHotbarWidth = (float)GuiElement.scaled(HudCarried.HotbarWidthPixels);

            this.cachedHotbarCenterX = frameWidth / 2f;

            this.cachedHotbarY = frameHeight - (float)GuiElement.scaled(HudCarried.VerticalOffsetPixels);

            this.cachedBackgroundSize = (float)GuiElement.scaled(HudCarried.BackgroundSizePixels);

            float leftStartX = this.cachedHotbarCenterX - (this.cachedHotbarWidth / 2f) - margin - this.cachedSlotSize;
            for (int i = 0; i < 3; i++)
            {
                float x = leftStartX - (i * (this.cachedSlotSize + spacingBetweenIcons));
                float y = this.cachedHotbarY;

                x += this.cachedSlotSize / 2f;

                x = Math.Max(this.cachedSlotSize / 2f, Math.Min(x, frameWidth - this.cachedSlotSize / 2f));
                y = Math.Max(this.cachedSlotSize / 2f, Math.Min(y, frameHeight - this.cachedSlotSize / 2f));

                this.cachedLeftPositions[i] = ((int)x, (int)y);
            }

            float rightStartX = this.cachedHotbarCenterX + (this.cachedHotbarWidth / 2f) + margin;
            for (int i = 0; i < 3; i++)
            {
                float x = rightStartX + (i * (this.cachedSlotSize + spacingBetweenIcons));
                float y = this.cachedHotbarY;

                x += this.cachedSlotSize / 2f;

                x = Math.Max(this.cachedSlotSize / 2f, Math.Min(x, frameWidth - this.cachedSlotSize / 2f));
                y = Math.Max(this.cachedSlotSize / 2f, Math.Min(y, frameHeight - this.cachedSlotSize / 2f));

                this.cachedRightPositions[i] = ((int)x, (int)y);
            }

            this.cachedGUIScale = guiScale;
            this.cachedFrameWidth = frameWidth;
            this.cachedFrameHeight = frameHeight;
        }

        private (int x, int y) GetPositionForAnchor(HudCarried.Anchor anchor)
        {
            switch (anchor)
            {
                case HudCarried.Anchor.L1: return this.cachedLeftPositions[0];
                case HudCarried.Anchor.L2: return this.cachedLeftPositions[1];
                case HudCarried.Anchor.L3: return this.cachedLeftPositions[2];
                case HudCarried.Anchor.R1: return this.cachedRightPositions[0];
                case HudCarried.Anchor.R2: return this.cachedRightPositions[1];
                case HudCarried.Anchor.R3: return this.cachedRightPositions[2];
                default: return ((int)this.cachedHotbarCenterX, (int)this.cachedHotbarY);
            }
        }

        public void Dispose()
        {
            if (this.highlightMesh != null)
            {
                this.api.Render.DeleteMesh(this.highlightMesh);
                this.highlightMesh = null;
            }
            if (this.rectMesh != null)
            {
                this.api.Render.DeleteMesh(this.rectMesh);
                this.rectMesh = null;
            }
        }
    }
}
