using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal static class CarryTransformResolver
    {
        // In-place version for pooled matrix buffers
        internal static void ApplyTransformInPlace(ModelTransform transform, float[] matrix, Vec3f offset)
        {
            Mat4f.Translate(matrix, matrix, offset.X, offset.Y, offset.Z);
            ApplyTransformInPlace(transform, matrix);
        }

        internal static void ApplyTransformInPlace(ModelTransform transform, float[] matrix)
        {
            Mat4f.Translate(matrix, matrix, transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
            Mat4f.Translate(matrix, matrix, transform.Origin.X, transform.Origin.Y, transform.Origin.Z);
            Mat4f.RotateX(matrix, matrix, transform.Rotation.X * GameMath.DEG2RAD);
            Mat4f.RotateZ(matrix, matrix, transform.Rotation.Z * GameMath.DEG2RAD);
            Mat4f.RotateY(matrix, matrix, transform.Rotation.Y * GameMath.DEG2RAD);
            Mat4f.Scale(matrix, matrix, transform.ScaleXYZ.X, transform.ScaleXYZ.Y, transform.ScaleXYZ.Z);
            Mat4f.Translate(matrix, matrix, -transform.Origin.X, -transform.Origin.Y, -transform.Origin.Z);
        }

        internal static float[]? GetAttachmentPointMatrix(EntityShapeRenderer renderer, AttachmentPointAndPose attachPointAndPose)
        {
            var modelMat = renderer?.ModelMat == null ? null : Mat4f.CloneIt(renderer.ModelMat);
            var animModelMat = attachPointAndPose.AnimModelMatrix;
            Mat4f.Mul(modelMat, modelMat, animModelMat);

            var attach = attachPointAndPose.AttachPoint;
            Mat4f.Translate(modelMat, modelMat, (float)(attach.PosX / 16), (float)(attach.PosY / 16), (float)(attach.PosZ / 16));
            Mat4f.RotateX(modelMat, modelMat, (float)attach.RotationX * GameMath.DEG2RAD);
            Mat4f.RotateY(modelMat, modelMat, (float)attach.RotationY * GameMath.DEG2RAD);
            Mat4f.RotateZ(modelMat, modelMat, (float)attach.RotationZ * GameMath.DEG2RAD);

            return modelMat;
        }
    }
}
