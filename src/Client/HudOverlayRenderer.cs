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

        private MeshRef _circleMesh = null;

        private ICoreClientAPI Api { get; }

        private float _circleAlpha = 0.0F;
        private float _circleProgress = 0.0F;

        public bool CircleVisible { get; set; }
        public float CircleProgress
        {
            get => _circleProgress;
            set
            {
                _circleProgress = GameMath.Clamp(value, 0.0F, 1.0F);
                CircleVisible = true;
            }
        }

        public HudOverlayRenderer(ICoreClientAPI api)
        {
            Api = api;
            Api.Event.RegisterRenderer(this, EnumRenderStage.Ortho);
            UpdateCirceMesh(1);
        }

        private void UpdateCirceMesh(float progress)
        {
            const float ringSize = (float)InnerRadius / OuterRadius;
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

            if (_circleMesh != null) Api.Render.UpdateMesh(_circleMesh, data);
            else _circleMesh = Api.Render.UploadMesh(data);
        }

        // IRenderer implementation

        public double RenderOrder => 0;
        public int RenderRange => 10;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            var rend = Api.Render;
            var shader = rend.CurrentActiveShader;

            _circleAlpha = Math.Max(0.0F, Math.Min(1.0F, _circleAlpha
                + (deltaTime / (CircleVisible ? CircleAlphaIn : -CircleAlphaOut))));

            // TODO: Do some smoothing between frames?
            if ((CircleProgress <= 0.0F) || (_circleAlpha <= 0.0F)) return;
            UpdateCirceMesh(CircleProgress);

            const float r = ((CircleColor >> 16) & 0xFF) / 255.0F;
            const float g = ((CircleColor >> 8) & 0xFF) / 255.0F;
            const float b = (CircleColor & 0xFF) / 255.0F;
            var color = new Vec4f(r, g, b, _circleAlpha);

            shader.Uniform("rgbaIn", color);
            shader.Uniform("extraGlow", 0);
            shader.Uniform("applyColor", 0);
            shader.Uniform("tex2d", 0);
            shader.Uniform("noTexture", 1.0F);
            shader.UniformMatrix("projectionMatrix", rend.CurrentProjectionMatrix);

            int x, y;
            if (Api.Input.MouseGrabbed)
            {
                x = Api.Render.FrameWidth / 2;
                y = Api.Render.FrameHeight / 2;
            }
            else
            {
                x = Api.Input.MouseX;
                y = Api.Input.MouseY;
            }

            // These IRenderAPI methods are deprecated but not sure how to do it otherwise.
#pragma warning disable CS0618
            rend.GlPushMatrix();
            rend.GlTranslate(x, y, 0);
            rend.GlScale(OuterRadius, OuterRadius, 0);
            shader.UniformMatrix("modelViewMatrix", rend.CurrentModelviewMatrix);
            rend.GlPopMatrix();
#pragma warning restore CS0618

            rend.RenderMesh(_circleMesh);
        }

        public void Dispose()
        {
            if (_circleMesh != null)
                Api.Render.DeleteMesh(_circleMesh);
        }
    }
}
