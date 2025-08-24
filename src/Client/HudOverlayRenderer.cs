using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace CarryOn.Client
{
    public class HudOverlayRenderer : IRenderer
    {
        private const int CircleColor = 0xCCCCCC;
        private const float CircleAlphaIn = 0.2F; // How quickly circle fades
        private const float CircleAlphaOut = 0.4F; // in and out in seconds.

        private const int CircleMaxSteps = 16;
        private const float OuterRadius = 24;
        private const float InnerRadius = 18;

        private MeshRef circleMesh = null;

        private ICoreClientAPI api;

        private float circleAlpha = 0.0F;
        private float circleProgress = 0.0F;

        public bool CircleVisible { get; set; }
        public float CircleProgress
        {
            get => this.circleProgress;
            set
            {
                this.circleProgress = GameMath.Clamp(value, 0.0F, 1.0F);
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
            const float stepSize = 1.0F / CircleMaxSteps;

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

            this.circleAlpha = Math.Max(0.0F, Math.Min(1.0F, this.circleAlpha
                + (deltaTime / (this.CircleVisible ? CircleAlphaIn : -CircleAlphaOut))));

            // TODO: Do some smoothing between frames?
            if ((this.CircleProgress <= 0.0F) || (this.circleAlpha <= 0.0F)) return;
            UpdateCircleMesh(this.CircleProgress);

            const float r = ((CircleColor >> 16) & 0xFF) / 255.0F;
            const float g = ((CircleColor >> 8) & 0xFF) / 255.0F;
            const float b = (CircleColor & 0xFF) / 255.0F;
            var color = new Vec4f(r, g, b, this.circleAlpha);

            shader.Uniform("rgbaIn", color);
            shader.Uniform("extraGlow", 0);
            shader.Uniform("applyColor", 0);
            shader.Uniform("tex2d", 0);
            shader.Uniform("noTexture", 1.0F);
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

            // These IRenderAPI methods are deprecated but not sure how to do it otherwise.
#pragma warning disable CS0618
            rend.GlPushMatrix();
            rend.GlTranslate(x, y, 0);
            rend.GlScale(OuterRadius, OuterRadius, 0);
            shader.UniformMatrix("modelViewMatrix", rend.CurrentModelviewMatrix);
            rend.GlPopMatrix();
#pragma warning restore CS0618

            rend.RenderMesh(this.circleMesh);
        }

        public void Dispose()
        {
            if (this.circleMesh != null)
                this.api.Render.DeleteMesh(this.circleMesh);
        }
    }
}
