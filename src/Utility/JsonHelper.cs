using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CarryOn.Utility
{
    public static class JsonHelper
    {
        public static bool TryGetBool(JsonObject json, string key, out bool result)
        {
            if (!json.KeyExists(key))
            {
                result = false;
                return false;
            }
            result = json[key].AsBool();
            return true;
        }

        public static bool TryGetString(JsonObject json, string key, out string result)
        {
            if (!json.KeyExists(key))
            {
                result = null;
                return false;
            }
            result = json[key].AsString();
            return result != null;
        }        
        public static bool TryGetInt(JsonObject json, string key, out int result)
        {
            if (!json.KeyExists(key))
            {
                result = 0;
                return false;
            }
            result = json[key].AsInt();
            return true;
        }

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

        public static bool TryGetVec3i(JsonObject json, string key, out Vec3i result)
        {
            var ints = json[key].AsArray<int>();
            var success = (ints?.Length == 3);
            result = success ? new Vec3i(ints[0], ints[1], ints[2]) : null;
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
