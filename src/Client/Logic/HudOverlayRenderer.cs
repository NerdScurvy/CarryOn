using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Logic
{
    public class HudOverlayRenderer : IRenderer
    {
        private const int CircleColor = 0xCCCCCC;
        private const float CircleAlphaIn = 0.2f; // How quickly circle fades
        private const float CircleAlphaOut = 0.4f; // in and out in seconds.

        private const int CircleMaxSteps = 16;
        private const float OuterRadius = 24;
        private const float InnerRadius = 18;

        private MeshRef? circleMesh = null;

        private readonly ICoreClientAPI api;

        private float circleAlpha = 0.0f;
        private float circleProgress = 0.0f;

        public bool CircleVisible { get; set; }
        public float CircleProgress
        {
            get => this.circleProgress;
            set
            {
                this.circleProgress = GameMath.Clamp(value, 0.0f, 1.0f);
                CircleVisible = true;
            }
        }

        public HudOverlayRenderer(ICoreClientAPI api)
        {
            this.api = api;
            this.api.Event.RegisterRenderer(this, EnumRenderStage.Ortho);
            UpdateCircleMesh(1);
        }

        private void UpdateCircleMesh(float progress)
        {
            const float ringSize = InnerRadius / OuterRadius;
            const float stepSize = 1.0f / CircleMaxSteps;

            var steps = 1 + (int)Math.Ceiling(CircleMaxSteps * progress);
            var data = new MeshData(steps * 2, steps * 6, false, false, true, false);

            for (var i = 0; i < steps; i++)
            {
                var p = Math.Min(progress, i * stepSize) * Math.PI * 2;
                var x = (float)Math.Sin(p);
                var y = -(float)Math.Cos(p);

                data.AddVertexSkipTex(x, y, 0);
                data.AddVertexSkipTex(x * ringSize, y * ringSize, 0);

                if (i > 0)
                {
                    data.AddIndices(new[] { (i * 2) - 2, (i * 2) - 1, (i * 2) + 0 });
                    data.AddIndices(new[] { (i * 2) + 0, (i * 2) - 1, (i * 2) + 1 });
                }
            }

            if (this.circleMesh != null) this.api.Render.UpdateMesh(this.circleMesh, data);
            else this.circleMesh = this.api.Render.UploadMesh(data);
        }

        // IRenderer implementation

        public double RenderOrder => 0;
        public int RenderRange => 10;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            var rend = this.api.Render;
            var shader = rend.CurrentActiveShader;

            this.circleAlpha = Math.Max(0.0f, Math.Min(1.0f, this.circleAlpha
                + (deltaTime / (this.CircleVisible ? CircleAlphaIn : -CircleAlphaOut))));

            // Circle progress is set externally; alpha fade provides the transition smoothing
            if ((this.CircleProgress <= 0.0f) || (this.circleAlpha <= 0.0f)) return;
            UpdateCircleMesh(this.CircleProgress);

            const float r = ((CircleColor >> 16) & 0xFF) / 255.0f;
            const float g = ((CircleColor >> 8) & 0xFF) / 255.0f;
            const float b = (CircleColor & 0xFF) / 255.0f;
            var color = new Vec4f(r, g, b, this.circleAlpha);

            shader.Uniform("rgbaIn", color);
            shader.Uniform("extraGlow", 0);
            shader.Uniform("applyColor", 0);
            shader.Uniform("tex2d", 0);
            shader.Uniform("noTexture", 1.0f);
            shader.UniformMatrix("projectionMatrix", rend.CurrentProjectionMatrix);

            int x, y;
            if (this.api.Input.MouseGrabbed)
            {
                x = this.api.Render.FrameWidth / 2;
                y = this.api.Render.FrameHeight / 2;
            }
            else
            {
                x = this.api.Input.MouseX;
                y = this.api.Input.MouseY;
            }

            // GlPushMatrix/GlTranslate/GlScale/GlPopMatrix are deprecated in favor of direct Matrix calls,
            // but are still required here to isolate the model-view transform for the overlay shader uniform.
#pragma warning disable CS0618 // Suppress deprecation of legacy GL matrix methods
            rend.GlPushMatrix();
            rend.GlTranslate(x, y, 0);
            rend.GlScale(OuterRadius, OuterRadius, 0);
            shader.UniformMatrix("modelViewMatrix", rend.CurrentModelviewMatrix);
            rend.GlPopMatrix();
#pragma warning restore CS0618

            rend.RenderMesh(this.circleMesh);

            // Reset shader state we modified so other renderers (e.g. itemstack GUI renders)
            // are not affected by the "noTexture" flag. Leaving it at 1.0 causes
            // subsequent GUI draw calls to render white/untextured icons.
            shader.Uniform("noTexture", 0.0f);
        }

        public void Dispose()
        {
            if (this.circleMesh != null)
                this.api.Render.DeleteMesh(this.circleMesh);
        }
    }
}
