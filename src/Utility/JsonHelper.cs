using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Utility
{
    public static class JsonHelper
    {
        public static bool TryGetFloat(JsonObject json, string key, out float result)
        {
            result = json[key].AsFloat(float.NaN);
            return !float.IsNaN(result);
        }

        public static bool TryGetVec3f(JsonObject json, string key, out Vec3f result)
        {
            var floats = json[key].AsArray<float>();
            var success = (floats?.Length == 3);
            result = success ? new Vec3f(floats) : null;
            return success;
        }

        public static ModelTransform GetTransform(JsonObject json, ModelTransform baseTransform)
        {
            var trans = baseTransform.Clone();
            if (TryGetVec3f(json, "translation", out var t)) trans.Translation = t;
            if (TryGetVec3f(json, "rotation", out var r)) trans.Rotation = r;
            if (TryGetVec3f(json, "origin", out var o)) trans.Origin = o;
            // Try to get scale both as a Vec3f and single float - for compatibility reasons.
            if (TryGetVec3f(json, "scale", out var sv)) trans.ScaleXYZ = sv;
            if (TryGetFloat(json, "scale", out var sf)) trans.ScaleXYZ = new Vec3f(sf, sf, sf);
            return trans;
        }
    }
}
