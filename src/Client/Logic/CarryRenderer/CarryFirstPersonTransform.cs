using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Logic.CarryRenderer
{
    public sealed class CarryFirstPersonTransform
    {
        private const long WobbleResetTickThreshold = 10;
        private const float WobbleSpeed = 5.0f;
        private const float YawSmoothing = 0.075f;
        private const float FirstPersonHandOffsetY = -0.35f;
        private const float FirstPersonHandOffsetZ = -0.20f;
        private const float FirstPersonDepthOffset = -0.20f;

        private long lastTickHandsRendered;
        private float moveWobble;
        private float lastYaw;
        private float yawDifference;

        public float[] GetFirstPersonHandsMatrix(EntityAgent entity, float[] viewMat, float deltaTime, long renderTick)
        {
            var modelMat = Mat4f.Invert(Mat4f.Create(), viewMat);

            if (renderTick - this.lastTickHandsRendered > WobbleResetTickThreshold)
            {
                this.moveWobble = 0;
                this.lastYaw = entity.Pos.Yaw;
                this.yawDifference = 0;
            }
            this.lastTickHandsRendered = renderTick;

            if (entity.Controls.TriesToMove)
            {
                var moveSpeed = entity.Controls.MovespeedMultiplier * (float)entity.GetWalkSpeedMultiplier();
                this.moveWobble += moveSpeed * deltaTime * WobbleSpeed;
            }
            else
            {
                var target = (float)(System.Math.Round(this.moveWobble / System.Math.PI) * System.Math.PI);
                var speed = deltaTime * (0.2f + (System.Math.Abs(target - this.moveWobble) * 4));
                if (System.Math.Abs(target - this.moveWobble) < speed) this.moveWobble = target;
                else this.moveWobble += System.Math.Sign(target - this.moveWobble) * speed;
            }
            this.moveWobble %= GameMath.PI * 2;

            var moveWobbleOffsetX = GameMath.Sin(this.moveWobble + GameMath.PI) * 0.03f;
            var moveWobbleOffsetY = GameMath.Sin(this.moveWobble * 2) * 0.02f;

            this.yawDifference += GameMath.AngleRadDistance(this.lastYaw, entity.Pos.Yaw);
            this.yawDifference *= (1 - YawSmoothing);
            this.lastYaw = entity.Pos.Yaw;

            var yawRotation = -this.yawDifference / 2;
            var pitchRotation = (entity.Pos.Pitch - GameMath.PI) / 4;

            Mat4f.RotateY(modelMat, modelMat, yawRotation);
            Mat4f.Translate(modelMat, modelMat, 0.0f, FirstPersonHandOffsetY, FirstPersonHandOffsetZ);
            Mat4f.RotateY(modelMat, modelMat, -yawRotation);
            Mat4f.RotateX(modelMat, modelMat, pitchRotation / 2);
            Mat4f.Translate(modelMat, modelMat, 0.0f, 0.0f, FirstPersonDepthOffset);
            Mat4f.RotateX(modelMat, modelMat, pitchRotation);
            Mat4f.RotateY(modelMat, modelMat, yawRotation);

            Mat4f.Translate(modelMat, modelMat, moveWobbleOffsetX, moveWobbleOffsetY, 0.0f);
            Mat4f.RotateY(modelMat, modelMat, 90.0f * GameMath.DEG2RAD);

            return modelMat;
        }
    }
}
