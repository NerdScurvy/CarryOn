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

        internal static bool TryReadVec3(JsonObject json, out Vec3f? value)
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

        internal static bool TryReadScale(JsonObject json, out Vec3f? value)
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
            if (source == null) return Array.Empty<CarriedRenderInfo>();

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
                    SkipTransform = false,
                    IsAttachedRoot = s.IsAttachedRoot,
                    IsAttachedBlock = s.IsAttachedBlock
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

        internal static ItemStack? TryGetSlotItemStackByKey(TreeAttribute containerSlots, string slotKey)
        {
            if (containerSlots == null || string.IsNullOrEmpty(slotKey))
            {
                return null;
            }

            return containerSlots.GetItemstack(slotKey);
        }

        internal static ItemStack? TryGetItemStackByPath(ITreeAttribute? root, string path, IWorldAccessor world)
        {
            if (root == null || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var parts = path.Split('.');
            if (parts.Length == 0)
            {
                return null;
            }

            ITreeAttribute current = root;
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i]?.Trim();
                if (string.IsNullOrEmpty(part) || current == null)
                {
                    return null;
                }

                var isLast = i == parts.Length - 1;
                if (isLast)
                {
                    var stack = current.GetItemstack(part, null);
                    if (stack == null && current[part] is ItemstackAttribute itemAttr)
                    {
                        stack = itemAttr.GetValue() as ItemStack;
                    }

                    if (stack?.Collectible == null)
                    {
                        stack?.ResolveBlockOrItem(world);
                    }

                    return stack;
                }

                current = current.GetTreeAttribute(part);
            }

            return null;
        }

        internal static string BuildSlotStateKey(EntityAgent entity, CarrySlot slot)
        {
            return string.Concat(entity?.EntityId.ToString() ?? "0", "|", ((int)slot).ToString());
        }

        internal static string BuildFrameCacheKey(EntityAgent entity, CarriedBlock carried, string? planSignature, string? renderVariantSignature)
        {
            return string.Concat(
                entity?.EntityId.ToString() ?? "0", "|",
                ((int)carried.Slot).ToString(), "|",
                carried?.ItemStack?.Collectible?.Code?.ToString() ?? "none", "|",
                planSignature ?? "noplan", "|",
                renderVariantSignature ?? "novariant");
        }

        internal static string BuildRenderInfoVariantSignature(CarriedBlock carried, TreeAttribute? containerSlots, IReadOnlyList<EffectiveTransformSetting> effectiveSettings, IWorldAccessor world, string? defaultRenderVariant = null)
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

            if (effectiveSettings != null && effectiveSettings.Count > 0)
            {
                sb.Append("|bepaths=");
                for (var i = 0; i < effectiveSettings.Count; i++)
                {
                    AppendBeStackPathSignature(sb, be, world, effectiveSettings[i]?.Setting?.DisableIfItemStackPath, "disable");
                    AppendBeStackPathSignature(sb, be, world, effectiveSettings[i]?.Setting?.BlockEntityDataItemStackPath, "render");
                }
            }

            if (carried?.OriginalBlockCode != null)
            {
                sb.Append("|origCode=").Append(carried.OriginalBlockCode.ToString());
            }

            if (carried?.OriginalMeshAngle.HasValue == true)
            {
                sb.Append("|origMeshAngle=").Append(carried.OriginalMeshAngle.Value.ToString("R", CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(defaultRenderVariant))
            {
                sb.Append("|renderVariant=").Append(defaultRenderVariant);
            }

            if (carried?.HasAttachedBlocks == true && carried.AttachedBlocks != null)
            {
                sb.Append("|attached=").Append(carried.AttachedBlocks.Count);
                foreach (var attached in carried.AttachedBlocks)
                {
                    if (attached == null) continue;
                    sb.Append('|').Append(attached.ItemStack?.Collectible?.Code?.ToString() ?? "null");
                    sb.Append(',').Append(attached.RelativeOffset.X).Append(',').Append(attached.RelativeOffset.Y).Append(',').Append(attached.RelativeOffset.Z);
                    sb.Append(',').Append(attached.OriginalLocalFace?.Code ?? "null");
                }
            }

            return sb.ToString();
        }

        private static void AppendBeStackPathSignature(StringBuilder sb, ITreeAttribute? be, IWorldAccessor world, string? path, string kind)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var stack = TryGetItemStackByPath(be, path, world);
            sb.Append(kind).Append(':').Append(path).Append(':').Append(BuildItemStackContentSignature(stack)).Append(',');
        }

        private static string BuildItemStackContentSignature(ItemStack? stack)
        {
            if (stack == null)
            {
                return "null";
            }

            var raw = new StringBuilder(256)
                .Append((int)stack.Class).Append('|')
                .Append(stack.Id).Append('|')
                .Append(stack.StackSize).Append('|')
                .Append(stack.Collectible?.Code?.ToString() ?? "nocode");

            if (stack.Attributes != null)
            {
                if (stack.Attributes is TreeAttribute treeAttributes)
                {
                    raw.Append("|a:").Append(SerializeTreeAttribute(treeAttributes));
                }
 
            }

            return ComputeFnv1a64Hex(raw.ToString());
        }

        private static string SerializeTreeAttribute(TreeAttribute tree, int depth = 0)
        {
            if (tree == null) return "null";
            if (depth > 16) return "<max-depth>";

            var keys = tree.Keys?.OrderBy(k => k, StringComparer.Ordinal) ?? Enumerable.Empty<string>();
            var sb = new StringBuilder(256);
            sb.Append('{');

            foreach (var key in keys)
            {
                sb.Append(key).Append('=');
                sb.Append(SerializeAttribute(tree[key], depth + 1));
                sb.Append(';');
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static string SerializeAttribute(IAttribute attribute, int depth)
        {
            if (attribute == null) return "null";
            if (depth > 16) return "<max-depth>";

            if (attribute is TreeAttribute nestedTree)
            {
                return SerializeTreeAttribute(nestedTree, depth + 1);
            }

            if (attribute is ItemstackAttribute itemstackAttribute)
            {
                var nestedStack = itemstackAttribute.GetValue() as ItemStack;
                return "itemstack(" + BuildItemStackContentSignature(nestedStack) + ")";
            }

            var value = attribute.GetValue();
            if (value == null)
            {
                return attribute.GetType().Name + "(null)";
            }

            if (value is byte[] bytes)
            {
                return attribute.GetType().Name + "(b64:" + Convert.ToBase64String(bytes) + ")";
            }

            if (value is Array arr && value is not char[])
            {
                var values = new List<string>(arr.Length);
                foreach (var element in arr)
                {
                    values.Add(element?.ToString() ?? "null");
                }

                return attribute.GetType().Name + "([" + string.Join(",", values) + "])";
            }

            return attribute.GetType().Name + "(" + value + ")";
        }

        private static string ComputeFnv1a64Hex(string text)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offset;
            for (var i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= prime;
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        internal static string BuildTransformPlanSignature(
            CarriedBlock? carried,
            string transformsGroupBase,
            string? matchedResolverCode,
            string? resolverCacheSignature)
        {
            var sb = new StringBuilder(192);
            sb.Append(carried?.Block?.Code?.ToString() ?? "noblock").Append('|');

            var variantType = carried?.BlockEntityData?.GetString("type", "none") ?? "none";
            sb.Append(variantType).Append('|');
            sb.Append(transformsGroupBase ?? CarryCode.DefaultTransformGroup).Append('|');
            sb.Append((int?)carried?.Slot ?? 0).Append('|');
            sb.Append(carried?.ItemStack?.Collectible?.Code?.ToString() ?? "nostack").Append('|');

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

        internal static RenderPhaseMask? ParseRenderPass(string? renderPass)
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

        internal static Vec4f? SampleColorMapTint(string? climateTintMap, string? seasonalTintMap, BlockPos pos, ICoreClientAPI api)
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

            Vec4f? climateTint = null;
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

            Vec4f? seasonalTint = null;
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
