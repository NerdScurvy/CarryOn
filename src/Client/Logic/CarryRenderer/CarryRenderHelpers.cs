using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CarryOn.API.Common.Models;
using CarryOn.Client.Models;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Client.Logic.CarryRenderer
{
    [Flags]
    internal enum RenderPhaseMask
    {
        None = 0,
        Opaque = 1,
        Translucent = 2,
        Both = Opaque | Translucent
    }

    internal static class CarryRenderHelpers
    {
        internal static readonly Vec4f DefaultTint = new(1f, 1f, 1f, 1f);

        internal static bool TryReadVec3(JsonObject json, out Vec3f value)
        {
            value = null;
            if (json == null || !json.Exists) return false;

            bool hasAnyAxis = json["x"].Exists || json["y"].Exists || json["z"].Exists;
            if (!hasAnyAxis) return false;

            value = new Vec3f(
                json["x"].AsFloat(0f),
                json["y"].AsFloat(0f),
                json["z"].AsFloat(0f)
            );
            return true;
        }

        internal static bool TryReadScale(JsonObject json, out Vec3f value)
        {
            value = null;
            if (json == null || !json.Exists) return false;

            var scaleJson = json["scale"];
            if (scaleJson.Exists)
            {
                var uniform = scaleJson.AsFloat(float.NaN);
                if (!float.IsNaN(uniform))
                {
                    value = new Vec3f(uniform, uniform, uniform);
                    return true;
                }

                if (TryReadVec3(scaleJson, out var vecScale))
                {
                    value = vecScale;
                    return true;
                }
            }

            if (json["scaleX"].Exists || json["scaleY"].Exists || json["scaleZ"].Exists)
            {
                value = new Vec3f(
                    json["scaleX"].AsFloat(1f),
                    json["scaleY"].AsFloat(1f),
                    json["scaleZ"].AsFloat(1f)
                );
                return true;
            }

            return false;
        }

        internal static CarriedRenderInfo[] CloneCarriedRenderInfos(CarriedRenderInfo[] source)
        {
            if (source == null) return null;

            var clone = new CarriedRenderInfo[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var s = source[i];
                if (s == null) continue;

                clone[i] = new CarriedRenderInfo
                {
                    RenderInfo = s.RenderInfo,
                    RenderEnabled = s.RenderEnabled,
                    TintColor = s.TintColor,
                    EnableVertexWarp = s.EnableVertexWarp,
                    AlphaTestOpaque = s.AlphaTestOpaque,
                    AlphaTestBlend = s.AlphaTestBlend,
                    NormalShaded = s.NormalShaded,
                    RgbGlowIntensity = s.RgbGlowIntensity?.Clone(),
                    RenderPass = s.RenderPass,
                    SecondaryTransform = s.SecondaryTransform?.Clone(),
                    SkipTransform = false
                };
            }

            return clone;
        }

        internal static bool IsSameCollectible(ItemStack stack, string assetName)
        {
            if (stack?.Collectible?.Code == null || string.IsNullOrEmpty(assetName)) return false;
            return string.Equals(
                stack.Collectible.Code.ToString(),
                assetName,
                StringComparison.OrdinalIgnoreCase
            );
        }

        internal static ItemStack TryGetSlotItemStackByKey(TreeAttribute containerSlots, string slotKey)
        {
            if (containerSlots == null || string.IsNullOrEmpty(slotKey))
            {
                return null;
            }

            return containerSlots.GetItemstack(slotKey);
        }

        internal static string BuildSlotStateKey(EntityAgent entity, CarrySlot slot)
        {
            return string.Concat(entity?.EntityId.ToString() ?? "0", "|", ((int)slot).ToString());
        }

        internal static string BuildFrameCacheKey(EntityAgent entity, CarriedBlock carried, string planSignature, string renderVariantSignature)
        {
            return string.Concat(
                entity?.EntityId.ToString() ?? "0", "|",
                ((int)carried.Slot).ToString(), "|",
                carried?.ItemStack?.Collectible?.Code?.ToString() ?? "none", "|",
                planSignature ?? "noplan", "|",
                renderVariantSignature ?? "novariant");
        }

        internal static string BuildRenderInfoVariantSignature(CarriedBlock carried, TreeAttribute containerSlots)
        {
            var sb = new StringBuilder(160);
            var be = carried?.BlockEntityData;

            var center = be?.GetBool("haveCenterPlacement", false) == true ? "1" : "0";
            sb.Append("center=").Append(center);

            for (int i = 0; i < 4; i++)
            {
                var key = "rotation" + i;
                sb.Append("|r").Append(i).Append('=');

                if (be?[key] == null)
                {
                    sb.Append("na");
                }
                else
                {
                    sb.Append(be.GetFloat(key).ToString("R", CultureInfo.InvariantCulture));
                }
            }

            if (containerSlots != null)
            {
                sb.Append("|slots=");
                var keys = containerSlots.Keys.OrderBy(k => k, StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    var stack = containerSlots.GetItemstack(key);
                    var code = stack?.Collectible?.Code?.ToString() ?? "null";
                    sb.Append(key).Append(':').Append(code).Append(',');
                }
            }

            return sb.ToString();
        }

        internal static string BuildTransformPlanSignature(
            CarriedBlock carried,
            string transformsGroupBase,
            string matchedResolverCode,
            string resolverCacheSignature)
        {
            var sb = new StringBuilder(192);
            sb.Append(carried?.Block?.Code?.ToString() ?? "noblock").Append('|');

            var variantType = carried?.BlockEntityData?.GetString("type", "none") ?? "none";
            sb.Append(variantType).Append('|');
            sb.Append(transformsGroupBase ?? "default").Append('|');
            sb.Append((int)carried.Slot).Append('|');
            sb.Append(carried.ItemStack?.Collectible?.Code?.ToString() ?? "nostack").Append('|');

            if (!string.IsNullOrEmpty(matchedResolverCode))
            {
                sb.Append("resolver=").Append(matchedResolverCode).Append('|');
            }

            if (!string.IsNullOrEmpty(resolverCacheSignature))
            {
                sb.Append("resolversig=").Append(resolverCacheSignature);
            }
            else
            {
                sb.Append("resolversig=none");
            }

            return sb.ToString();
        }

        internal static float[] ApplyTransform(ModelTransform transform, float[] modelMatrix, Vec3f offset)
        {
            float[] matrix = Mat4f.CloneIt(modelMatrix);
            Mat4f.Translate(matrix, matrix, offset.X, offset.Y, offset.Z);
            Mat4f.Translate(matrix, matrix, transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
            Mat4f.Translate(matrix, matrix, transform.Origin.X, transform.Origin.Y, transform.Origin.Z);
            Mat4f.RotateX(matrix, matrix, transform.Rotation.X * GameMath.DEG2RAD);
            Mat4f.RotateZ(matrix, matrix, transform.Rotation.Z * GameMath.DEG2RAD);
            Mat4f.RotateY(matrix, matrix, transform.Rotation.Y * GameMath.DEG2RAD);
            Mat4f.Scale(matrix, matrix, transform.ScaleXYZ.X, transform.ScaleXYZ.Y, transform.ScaleXYZ.Z);
            Mat4f.Translate(matrix, matrix, -transform.Origin.X, -transform.Origin.Y, -transform.Origin.Z);

            return matrix;
        }

        internal static float[] GetAttachmentPointMatrix(EntityShapeRenderer renderer, AttachmentPointAndPose attachPointAndPose)
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

        internal static bool IsSitting(EntityPlayer player)
        {
            var active = player?.AnimManager?.ActiveAnimationsByAnimCode;
            if (active == null || active.Count == 0) return false;

            foreach (var animation in active.Keys)
            {
                if (string.IsNullOrEmpty(animation)) continue;
                // Exclude our carry animations; check only for vanilla sit animations
                if (animation.StartsWith("carryon:", StringComparison.OrdinalIgnoreCase)) continue;
                if (animation.IndexOf("sit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        internal static string ResolveCarryAnimationCode(string animationCode, bool isSneaking, bool isSitting)
        {
            if (string.IsNullOrEmpty(animationCode)) return animationCode;

            if (isSitting)
            {
                if (animationCode == "carryon:holdheavy") return "carryon:holdheavysit";
                if (animationCode == "carryon:holdlight") return "carryon:holdlightsit";
            }

            if (!isSneaking) return animationCode;

            if (animationCode == "carryon:holdheavy") return "carryon:holdheavycrouch";
            if (animationCode == "carryon:holdlight") return "carryon:holdlightcrouch";

            return animationCode;
        }

        internal static RenderPhaseMask ResolveDefaultPhases(CarriedRenderInfo info)
        {
            var explicitPass = ParseRenderPass(info?.RenderPass);
            if (explicitPass.HasValue)
            {
                return explicitPass.Value;
            }

            // Use cullFaces property to determine if the model has translucency and requires two-pass rendering. This is a bit of a hack, but it avoids needing explicit metadata for render phases and allows modders to control it via their existing model properties.
            // cullFaces=false meshes are treated as split opaque+translucent
            // cullFaces=true meshes default to opaque-only
            return info?.RenderInfo?.CullFaces == false
                ? RenderPhaseMask.Both
                : RenderPhaseMask.Opaque;
        }

        internal static RenderPhaseMask? ParseRenderPass(string renderPass)
        {
            if (string.IsNullOrWhiteSpace(renderPass))
            {
                return null;
            }

            switch (renderPass.Trim().ToLowerInvariant())
            {
                case "opaque":
                    return RenderPhaseMask.Opaque;
                case "translucent":
                    return RenderPhaseMask.Translucent;
                case "both":
                    return RenderPhaseMask.Both;
                default:
                    return null;
            }
        }

        internal static bool ShouldDrawInPhase(RenderPhaseMask mask, bool translucentPhase)
        {
            return translucentPhase
                ? (mask & RenderPhaseMask.Translucent) != 0
                : (mask & RenderPhaseMask.Opaque) != 0;
        }

        internal static Vec4f SampleColorMapTint(string climateTintMap, string seasonalTintMap, BlockPos pos, ICoreClientAPI api)
        {
            if (api?.World == null) return null;
            if (string.IsNullOrEmpty(climateTintMap) && string.IsNullOrEmpty(seasonalTintMap)) return null;

            static Vec4f FromRgba(int rgba)
            {
                return new Vec4f(
                    ColorUtil.ColorR(rgba) / 255f,
                    ColorUtil.ColorG(rgba) / 255f,
                    ColorUtil.ColorB(rgba) / 255f,
                    ColorUtil.ColorA(rgba) / 255f
                );
            }

            try
            {
                var combinedRgba = api.World.ApplyColorMapOnRgba(
                    string.IsNullOrEmpty(climateTintMap) ? null : climateTintMap,
                    string.IsNullOrEmpty(seasonalTintMap) ? null : seasonalTintMap,
                    ColorUtil.ToRgba(255, 255, 255, 255),
                    pos.X,
                    pos.Y,
                    pos.Z,
                    true
                );

                return new Vec4f(
                    ColorUtil.ColorR(combinedRgba) / 255f,
                    ColorUtil.ColorG(combinedRgba) / 255f,
                    ColorUtil.ColorB(combinedRgba) / 255f,
                    ColorUtil.ColorA(combinedRgba) / 255f
                );
            }
            catch (KeyNotFoundException)
            {
            }

            Vec4f climateTint = null;
            if (!string.IsNullOrEmpty(climateTintMap))
            {
                try
                {
                    var climateRgba = api.World.ApplyColorMapOnRgba(
                        climateTintMap,
                        null,
                        ColorUtil.ToRgba(255, 255, 255, 255),
                        pos.X,
                        pos.Y,
                        pos.Z,
                        true
                    );
                    climateTint = FromRgba(climateRgba);
                }
                catch (KeyNotFoundException)
                {
                    climateTint = null;
                }
            }

            Vec4f seasonalTint = null;
            if (!string.IsNullOrEmpty(seasonalTintMap))
            {
                try
                {
                    var seasonalRgba = api.World.ApplyColorMapOnRgba(
                        null,
                        seasonalTintMap,
                        ColorUtil.ToRgba(255, 255, 255, 255),
                        pos.X,
                        pos.Y,
                        pos.Z,
                        true
                    );
                    seasonalTint = FromRgba(seasonalRgba);
                }
                catch (KeyNotFoundException)
                {
                    seasonalTint = null;
                }
            }

            if (climateTint == null) return seasonalTint;
            if (seasonalTint == null) return climateTint;

            return new Vec4f(
                climateTint.R * seasonalTint.R,
                climateTint.G * seasonalTint.G,
                climateTint.B * seasonalTint.B,
                climateTint.A * seasonalTint.A
            );
        }
    }
}
