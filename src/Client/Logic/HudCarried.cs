using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Logic
{
    public class HudCarried : IDisposable
    {
        private readonly ICoreClientAPI api;
        private readonly ICarryManager carryManager;
        private IRenderer? renderer;
        // Toggle to show debug icons (can be controlled by client chat command)
        public static bool ShowDebugIcons { get; set; } = false;

        // Anchor positions available for GUI placement
        public enum Anchor
        {
            None = 0,
            L1,
            L2,
            L3,
            R1,
            R2,
            R3
        }

        public static readonly Anchor HandsAnchorDefault = Anchor.L1;
        public static readonly Anchor BackAnchorDefault = Anchor.R1;

        public static readonly float AnchorBackgroundAlphaDefault = 0.4f;
        public static readonly string AnchorBackgroundColorDefault = "#E4C4A6";

        public static readonly float AnchorBorderAlphaDefault = 0.75f;
        public static readonly string AnchorBorderColorDefault = "#45372D";

        public static readonly string IconHighlightColorDefault = "#FFFFFF";
        public static readonly float IconHighlightAlphaDefault = 0.8f;


        // Current assignments for hands and back; defaults: Hands -> None (blank), Back -> R1
        public static Anchor HandsAnchor { get; set; } = HandsAnchorDefault;
        public static Anchor BackAnchor { get; set; } = BackAnchorDefault;

        public static bool AnchorBackgroundEnabled { get; set; } = true;
        public static float AnchorBackgroundAlpha { get; set; } = AnchorBackgroundAlphaDefault;
        public static string AnchorBackgroundColor { get; set; } = AnchorBackgroundColorDefault;
        // Cached parsed colors (Vec4f) to avoid reparsing on every frame
        public static Vec4f AnchorBackgroundVec { get; private set; } = new();

        // Border (outline) options
        public static bool AnchorBorderEnabled { get; set; } = true;
        public static float AnchorBorderAlpha { get; set; } = AnchorBorderAlphaDefault;
        public static string AnchorBorderColor { get; set; } = AnchorBorderColorDefault;
        public static Vec4f AnchorBorderVec { get; private set; } = new();

        // Icon highlight options
        public static bool IconHighlightEnabled { get; set; } = true;
        public static string IconHighlightColor { get; set; } = IconHighlightColorDefault;
        public static float IconHighlightAlpha { get; set; } = IconHighlightAlphaDefault;
        public static Vec4f IconHighlightVec { get; private set; } = new();

        // Highlight timers (seconds remaining). When > 0 the corresponding icon will be tinted.
        // Trigger these from other client-side code when the player starts interacting with that carried item.
        public static float HandsHighlightSecondsRemaining { get; private set; } = 0f;
        public static float BackHighlightSecondsRemaining { get; private set; } = 0f;

        // Default highlight duration (seconds)
        public const float DefaultHighlightDuration = 1.0f;
        // Extra time after the main shrink where the alpha will fade out (seconds)
        public const float HighlightFadeExtra = 0.4f;

        // Layout constants (unscaled pixels)
        private const float BaseSlotSizePixels = 32.0f;
        private const float GroupMarginPixels = 16.0f;
        private const float IconSpacingPixels = 16.0f;
        private const float HotbarWidthPixels = 850.0f;
        private const float VerticalOffsetPixels = 36.0f;
        private const float BackgroundSizePixels = 64.0f;

        // Trigger the hands highlight for the given duration (seconds). Use when the player starts interacting with the hands-carried item.
        public static void TriggerHandsHighlight(float seconds = DefaultHighlightDuration + HighlightFadeExtra)
        {
            HandsHighlightSecondsRemaining = Math.Max(HandsHighlightSecondsRemaining, seconds);
        }

        // Trigger the back highlight for the given duration (seconds). Use when the player starts interacting with the back-carried item.
        public static void TriggerBackHighlight(float seconds = DefaultHighlightDuration + HighlightFadeExtra)
        {
            BackHighlightSecondsRemaining = Math.Max(BackHighlightSecondsRemaining, seconds);
        }

        public HudCarried(ICoreClientAPI api, ICarryManager carryManager)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.carryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
            this.renderer = new HudCarriedRenderer(api, carryManager);
            // Initialize parsed color cache
            UpdateParsedColors();
            this.api.Event.RegisterRenderer(this.renderer, EnumRenderStage.Ortho);
        }

        // Update cached parsed colors from the configured hex strings and alphas.
        public static void UpdateParsedColors()
        {
            // If any of the colors fail to parse, the ColorHelper will set them to magenta (#FF00FF) to indicate an error.
            // Anchor background
            ColorHelper.TryParseHex(AnchorBackgroundColor, AnchorBackgroundAlpha, out var bg);
            AnchorBackgroundVec = bg;

            // Anchor border
            ColorHelper.TryParseHex(AnchorBorderColor, AnchorBorderAlpha, out var br);
            AnchorBorderVec = br;

            // Icon highlight
            ColorHelper.TryParseHex(IconHighlightColor, IconHighlightAlpha, out var hi);
            IconHighlightVec = hi;
        }

        public void Dispose()
        {
            if (this.renderer != null)
            {
                this.api.Event.UnregisterRenderer(this.renderer, EnumRenderStage.Ortho);
                ((HudCarriedRenderer)this.renderer).Dispose();
                this.renderer = null;
            }
        }
        private class HudCarriedRenderer : IRenderer
        {
            private readonly ICoreClientAPI api;
            private readonly ICarryManager carryManager;
            
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

            public HudCarriedRenderer(ICoreClientAPI api, ICarryManager carryManager)
            {
                this.api = api;
                this.carryManager = carryManager;
            }

            public double RenderOrder => 1.0; // Higher than default (0) to render on top
            public int RenderRange => 10;

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
                if (HudCarried.HandsHighlightSecondsRemaining > 0f)
                {
                    HudCarried.HandsHighlightSecondsRemaining = Math.Max(0f, HudCarried.HandsHighlightSecondsRemaining - deltaTime);
                }
                if (HudCarried.BackHighlightSecondsRemaining > 0f)
                {
                    HudCarried.BackHighlightSecondsRemaining = Math.Max(0f, HudCarried.BackHighlightSecondsRemaining - deltaTime);
                }

                // Draw bounding rect(s) for anchors BEFORE rendering items so highlights render on top of backgrounds
                var backAnchorRect = HudCarried.BackAnchor;
                var handsAnchorRect = HudCarried.HandsAnchor;

                bool drewCombinedRect = false;
                if (backAnchorRect != Anchor.None && handsAnchorRect != Anchor.None)
                {
                    int a = (int)backAnchorRect;
                    int b = (int)handsAnchorRect;

                    bool bothLeft = (a >= (int)Anchor.L1 && a <= (int)Anchor.L3) && (b >= (int)Anchor.L1 && b <= (int)Anchor.L3);
                    bool bothRight = (a >= (int)Anchor.R1 && a <= (int)Anchor.R3) && (b >= (int)Anchor.R1 && b <= (int)Anchor.R3);

                    if ((bothLeft || bothRight) && Math.Abs(a - b) == 1)
                    {
                        var posA = this.GetPositionForAnchor(backAnchorRect);
                        var posB = this.GetPositionForAnchor(handsAnchorRect);

                        float centerX = (posA.x + posB.x) / 2f;
                        float centerY = (posA.y + posB.y) / 2f + 1f;
                        float width = Math.Abs(posA.x - posB.x) + this.cachedBackgroundSize;
                        float height = Math.Max(this.cachedBackgroundSize, 2f * (this.cachedFrameHeight - centerY));

                        try
                        {
                            if (AnchorBackgroundEnabled)
                            {
                                DrawRectFilled(rapi, centerX, centerY, width, height, HudCarried.AnchorBackgroundVec);
                            }
                        }
                        catch (Exception ex) { api?.Logger?.Debug("CarryOn: Error drawing anchor background: {0}", ex.Message); }

                        if (AnchorBorderEnabled)
                        {
                            DrawRectOutline(rapi, centerX, centerY, width, height, Math.Max(0.5f, this.cachedSlotSize * 0.08f), HudCarried.AnchorBorderVec);
                        }

                        drewCombinedRect = true;
                    }
                }

                if (!drewCombinedRect)
                {
                    if (backAnchorRect != Anchor.None)
                    {
                        var posB = this.GetPositionForAnchor(backAnchorRect);
                        float centerY = posB.y + 1f;
                        float height = Math.Max(this.cachedBackgroundSize, 2f * (this.cachedFrameHeight - centerY));
                        try
                        {
                            if (AnchorBackgroundEnabled)
                            {
                                DrawRectFilled(rapi, posB.x, centerY, this.cachedBackgroundSize, height, HudCarried.AnchorBackgroundVec);
                            }
                        }
                        catch (Exception ex) { api?.Logger?.Debug("CarryOn: Error drawing anchor background: {0}", ex.Message); }

                        if (AnchorBorderEnabled)
                        {
                            DrawRectOutline(rapi, posB.x, centerY, this.cachedBackgroundSize, height, Math.Max(0.5f, this.cachedSlotSize * 0.08f), HudCarried.AnchorBorderVec);
                        }
                    }

                    if (handsAnchorRect != Anchor.None)
                    {
                        var posH = this.GetPositionForAnchor(handsAnchorRect);
                        float centerY = posH.y + 1f;
                        float height = Math.Max(this.cachedBackgroundSize, 2f * (this.cachedFrameHeight - centerY));
                        try
                        {
                            if (AnchorBackgroundEnabled)
                            {
                                DrawRectFilled(rapi, posH.x, centerY, this.cachedBackgroundSize, height, HudCarried.AnchorBackgroundVec);
                            }
                        }
                        catch (Exception ex) { api?.Logger?.Debug("CarryOn: Error drawing anchor background: {0}", ex.Message); }

                        if (AnchorBorderEnabled)
                        {
                            DrawRectOutline(rapi, posH.x, centerY, this.cachedBackgroundSize, height, Math.Max(0.5f, this.cachedSlotSize * 0.08f), HudCarried.AnchorBorderVec);
                        }
                    }
                }

                // Render carried hands item (default position L1 -> first left position)
                var carriedHands = carryManager.GetCarried(player.Entity, CarrySlot.Hands);
                if (carriedHands != null)
                {
                    RenderCarriedBlock(rapi, carriedHands, HudCarried.HandsAnchor, HudCarried.HandsHighlightSecondsRemaining);
                }
				
                // Render carried back item (for now, just show in first right position)
                var carriedBack = carryManager.GetCarried(player.Entity, CarrySlot.Back);

                if (carriedBack != null)
                {
                    RenderCarriedBlock(rapi, carriedBack, HudCarried.BackAnchor, HudCarried.BackHighlightSecondsRemaining);
                }

            }

            private void EnsureHighlightMesh(int steps = 32)
            {
                if (this.highlightMesh != null) return;

                // Build a filled circle as a triangle fan: center vertex + outer ring
                int vertexCount = 1 + steps; // center + outer
                int indexCount = steps * 3;
                var mesh = new MeshData(vertexCount, indexCount, false, false, true, true);

                // center vertex
                mesh.AddVertexSkipTex(0f, 0f, 0f);

                // outer ring vertices (unit circle), clockwise
                for (int i = 0; i < steps; i++)
                {
                    float a = (float)i / steps * GameMath.TWOPI;
                    float x = (float)Math.Cos(a);
                    float y = (float)Math.Sin(a);
                    mesh.AddVertexSkipTex(x, y, 0f);
                }

                // Indices for triangle fan from center (0)
                for (int i = 0; i < steps; i++)
                {
                    int a = 0;
                    int b = 1 + i;
                    int c = 1 + ((i + 1) % steps);
                    mesh.AddIndices(new[] { a, b, c });
                }

                // UVs (not used) - provide defaults
                var uvs = new float[vertexCount * 2];
                for (int i = 0; i < vertexCount; i++) { uvs[i * 2 + 0] = 0; uvs[i * 2 + 1] = 0; }
                mesh.Uv = uvs;

                // Colors RGBA per-vertex: center opaque white, outer vertices transparent white to get a soft fade
                var rgba = new byte[vertexCount * 4];
                // center
                rgba[0] = 255; rgba[1] = 255; rgba[2] = 255; rgba[3] = 255;
                for (int i = 1; i < vertexCount; i++)
                {
                    int idx = i * 4;
                    rgba[idx + 0] = 255;
                    rgba[idx + 1] = 255;
                    rgba[idx + 2] = 255;
                    rgba[idx + 3] = 0; // fully transparent at edges
                }
                mesh.Rgba = rgba;

                this.highlightMesh = this.api.Render.UploadMesh(mesh);
            }

            private void EnsureRectMesh()
            {
                if (this.rectMesh != null) return;

                // Unit quad centered at origin (-0.5..0.5)
                var mesh = new MeshData(4, 6, false, false, true, true);
                mesh.AddVertexSkipTex(-0.5f, -0.5f, 0f);
                mesh.AddVertexSkipTex(0.5f, -0.5f, 0f);
                mesh.AddVertexSkipTex(0.5f, 0.5f, 0f);
                mesh.AddVertexSkipTex(-0.5f, 0.5f, 0f);

                mesh.AddIndices(new[] { 0, 1, 2, 0, 2, 3 });

                // UVs and colors not needed for solid color quads
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

                    // We'll draw four quads: top, bottom, left, right
                    // Top
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
                    shader.Uniform("noTexture", 1.0F);
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

                    // Reset shader toggles
                    shader.Uniform("applyColor", 0);
                    shader.Uniform("noTexture", 0.0F);

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
                    shader.Uniform("noTexture", 1.0F);
                    rapi.RenderMesh(this.rectMesh);

                    // Reset shader toggles
                    shader.Uniform("applyColor", 0);
                    shader.Uniform("noTexture", 0.0F);

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
                if (!IconHighlightEnabled) return;

                // We separate the animation into two phases:
                // 1) Shrink phase: duration seconds (progressShrink 0->1)
                // 2) Fade phase: extra seconds (HighlightFadeExtra) where alpha goes to 0
                float total = duration; // duration passed in already includes the extra fade time when triggered

                // Ensure we don't divide by zero
                float mainDuration = Math.Max(0.0001f, Math.Max(0f, total - HighlightFadeExtra));
                float fadeDuration = HighlightFadeExtra;

                // timeElapsed from start
                float timeElapsed = total - secondsRemaining;

                // Shrink progress clamped 0..1
                float progressShrink = Math.Max(0f, Math.Min(1f, timeElapsed / mainDuration));
                // Ease out for shrink
                float easeShrink = 1f - (1f - progressShrink) * (1f - progressShrink);

                // Scale from 1.35 -> 1.0 as shrink progress goes from 0->1
                float scale = 1.35f - 0.35f * easeShrink;

                // Fade progress (0 = opaque, 1 = fully faded)
                float progressFade = 0f;
                if (timeElapsed >= mainDuration)
                {
                    progressFade = Math.Max(0f, Math.Min(1f, (timeElapsed - mainDuration) / Math.Max(0.0001f, fadeDuration)));
                }

                // Alpha eases out during the fade phase; use configured IconHighlightAlpha as base
                float baseAlpha = IconHighlightAlpha;
                float alpha = baseAlpha * (1f - progressFade);

                EnsureHighlightMesh();

                var shader = rapi.CurrentActiveShader;

                try
                {
                    // Use GUI matrix transforms similar to HudOverlayRenderer to position and scale
#pragma warning disable CS0618
                    rapi.GlPushMatrix();
                    rapi.GlTranslate((int)centerX, (int)centerY, 0);
                    // Make the highlight slightly larger than the slot so it surrounds the icon
                    float radius = this.cachedSlotSize * 0.8f * scale;
                    rapi.GlScale(radius, radius, 0);
                    shader.UniformMatrix("modelViewMatrix", rapi.CurrentModelviewMatrix);
#pragma warning restore CS0618

                    // Tint using cached parsed color and computed animated alpha
                    var baseColor = HudCarried.IconHighlightVec;
                    // combine parsed color alpha (from parsed config) with animated alpha
                    float finalAlpha = baseColor.W * alpha;
                    finalAlpha = Math.Max(0f, Math.Min(1f, finalAlpha));
                    var col = new Vec4f(baseColor.X, baseColor.Y, baseColor.Z, finalAlpha);
                    shader.Uniform("rgbaIn", col);
                    shader.Uniform("applyColor", 1);
                    shader.Uniform("noTexture", 1.0F);

                    rapi.RenderMesh(this.highlightMesh);

                    // Reset shader state toggles
                    shader.Uniform("applyColor", 0);
                    shader.Uniform("noTexture", 0.0F);

#pragma warning disable CS0618
                    rapi.GlPopMatrix();
#pragma warning restore CS0618
                }
                catch (Exception ex)
                {
                    this.api.Logger.Debug("[HudCarried] Exception in DrawIconHighlight: " + ex);
                }
            }

            // Helper to render a carried block (draw highlight then the item)
            private void RenderCarriedBlock(IRenderAPI rapi, CarriedBlock carriedBlock, Anchor anchor, float highlightSecondsRemaining)
            {
                var slot = new DummySlot(carriedBlock.ItemStack);
                var pos = this.GetPositionForAnchor(anchor);

                // If highlight active, draw highlight. Note: we pass the main duration (DefaultHighlightDuration)
                // but the triggers include the extra fade time, so DrawIconHighlight expects the total passed
                if (highlightSecondsRemaining > 0f)
                {
                    DrawIconHighlight(rapi, highlightSecondsRemaining, DefaultHighlightDuration + HighlightFadeExtra, pos.x, pos.y);
                }

                var shader = rapi.CurrentActiveShader;
                shader.Uniform("noTexture", 0.0F);
                shader.Uniform("applyColor", 0);

                // Render the item.
                rapi.RenderItemstackToGui(slot, pos.x, pos.y, 100, this.cachedSlotSize, -1);
            }

            private void UpdateCachedPositions(float guiScale, float frameWidth, float frameHeight)
            {
                // Use GuiElement.scaled() for proper GUI scaling like StatusHud renderer
                this.cachedSlotSize = (float)GuiElement.scaled(BaseSlotSizePixels);
                int margin = (int)GuiElement.scaled(GroupMarginPixels);
                int spacingBetweenIcons = (int)GuiElement.scaled(IconSpacingPixels);

                this.cachedHotbarWidth = (float)GuiElement.scaled(HotbarWidthPixels);
                
                // Center of screen and hotbar position
                this.cachedHotbarCenterX = frameWidth / 2f; 

                // Vertical positioning: place the center of icons at 36 pixels from bottom (GUI-scaled)
                // Note: RenderItemstackToGui expects center coordinates, so cachedHotbarY represents the icon center Y
                this.cachedHotbarY = frameHeight - (float)GuiElement.scaled(VerticalOffsetPixels);

                // Cache background size for anchor outlines
                this.cachedBackgroundSize = (float)GuiElement.scaled(BackgroundSizePixels);

                // Calculate positions to the left of hotbar (right to left: closest to farthest)
                // Start from the left edge of hotbar, move left by margin, then place icons
                float leftStartX = this.cachedHotbarCenterX - (this.cachedHotbarWidth / 2f) - margin - this.cachedSlotSize;
                for (int i = 0; i < 3; i++)
                {
                    float x = leftStartX - (i * (this.cachedSlotSize + spacingBetweenIcons));
                    // Y is the icon center; keep it exactly at cachedHotbarY
                    float y = this.cachedHotbarY;

                    // The render API centers the item at the provided pos. Convert our top-left calculations to center-based
                    // by adding half the slot size so the renderer draws the icon where we expect on X.
                    x += this.cachedSlotSize / 2f;

                    // Boundary clamping - keep icon center inside frame
                    x = Math.Max(this.cachedSlotSize / 2f, Math.Min(x, frameWidth - this.cachedSlotSize / 2f));
                    y = Math.Max(this.cachedSlotSize / 2f, Math.Min(y, frameHeight - this.cachedSlotSize / 2f));
                    
                    this.cachedLeftPositions[i] = ((int)x, (int)y);
                }

                // Calculate positions to the right of hotbar (left to right: closest to farthest)
                // Start from the right edge of hotbar, move right by margin, then place icons
                float rightStartX = this.cachedHotbarCenterX + (this.cachedHotbarWidth / 2f) + margin;
                for (int i = 0; i < 3; i++)
                {
                    float x = rightStartX + (i * (this.cachedSlotSize + spacingBetweenIcons));

                    // Y is the icon center; keep it exactly at cachedHotbarY
                    float y = this.cachedHotbarY;

                    // Convert top-left calculation to renderer's center-based coordinates
                    x += this.cachedSlotSize / 2f;

                    // Boundary clamping - keep icon center inside frame
                    x = Math.Max(this.cachedSlotSize / 2f, Math.Min(x, frameWidth - this.cachedSlotSize / 2f));
                    y = Math.Max(this.cachedSlotSize / 2f, Math.Min(y, frameHeight - this.cachedSlotSize / 2f));

                    this.cachedRightPositions[i] = ((int)x, (int)y);
                }
                
                // Update cached values
                this.cachedGUIScale = guiScale;
                this.cachedFrameWidth = frameWidth;
                this.cachedFrameHeight = frameHeight;
            }

            private (int x, int y) GetPositionForAnchor(Anchor anchor)
            {
                switch (anchor)
                {
                    case Anchor.L1: return this.cachedLeftPositions[0];
                    case Anchor.L2: return this.cachedLeftPositions[1];
                    case Anchor.L3: return this.cachedLeftPositions[2];
                    case Anchor.R1: return this.cachedRightPositions[0];
                    case Anchor.R2: return this.cachedRightPositions[1];
                    case Anchor.R3: return this.cachedRightPositions[2];
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
}