using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Models
{
    public class TransformGroupSettings
    {
        public string AssetName { get; set; }

        public string Id { get; set; }

        public EnumAssetType AssetType { get; set; } = EnumAssetType.None;

        public float? TranslationX { get; set; } = null;
        public float? TranslationY { get; set; } = null;
        public float? TranslationZ { get; set; } = null;

        public float? RotationX { get; set; } = null;
        public float? RotationY { get; set; } = null;
        public float? RotationZ { get; set; } = null;

        public float? ScaleX { get; set; } = null;
        public float? ScaleY { get; set; } = null;
        public float? ScaleZ { get; set; } = null;

        public float? OriginX { get; set; } = null;
        public float? OriginY { get; set; } = null;
        public float? OriginZ { get; set; } = null;

        public bool? CullFaces { get; set; } = null;

        public float? AlphaTestOpaque { get; set; } = null;

        public float? AlphaTestBlend { get; set; } = null;

        public bool? NormalShaded { get; set; } = null;

        public string RenderPass { get; set; } = null;

        public Vec4f TintColor { get; set; } = null;

        public string ClimateTintMap { get; set; } = null;

        public string SeasonalTintMap { get; set; } = null;

        public bool Enabled { get; set; } = true;

        public void SetTranslation(float? x, float? y, float? z)
        {
            TranslationX = x;
            TranslationY = y;
            TranslationZ = z;
        }

        public void SetTranslation(Vec3f translation)
        {
            SetTranslation(translation.X, translation.Y, translation.Z);
        }

        public void SetRotation(float? x, float? y, float? z)
        {
            RotationX = x;
            RotationY = y;
            RotationZ = z;
        }

        public void SetRotation(Vec3f rotation)
        {
            SetRotation(rotation.X, rotation.Y, rotation.Z);
        }

        public void SetScale(float? x, float? y, float? z)
        {
            ScaleX = x;
            ScaleY = y;
            ScaleZ = z;
        }

        public void SetScale(Vec3f scale)
        {
            SetScale(scale.X, scale.Y, scale.Z);
        }

        public void SetOrigin(float? x, float? y, float? z)
        {
            OriginX = x;
            OriginY = y;
            OriginZ = z;
        }

        public void SetOrigin(Vec3f origin)
        {
            SetOrigin(origin.X, origin.Y, origin.Z);
        }

        public Vec3f GetTranslation(Vec3f defaultValue = null)
        {
            if (defaultValue == null) defaultValue = new Vec3f(0, 0, 0);
            return new Vec3f(
                TranslationX ?? defaultValue.X,
                TranslationY ?? defaultValue.Y,
                TranslationZ ?? defaultValue.Z
            );
        }

        public Vec3f GetRotation(Vec3f defaultValue = null)
        {
            if (defaultValue == null) defaultValue = new Vec3f(0, 0, 0);
            return new Vec3f(
                RotationX ?? defaultValue.X,
                RotationY ?? defaultValue.Y,
                RotationZ ?? defaultValue.Z
            );
        }

        public Vec3f GetScale(Vec3f defaultValue = null)
        {
            if (defaultValue == null) defaultValue = new Vec3f(0, 0, 0);
            return new Vec3f(
                ScaleX ?? defaultValue.X,
                ScaleY ?? defaultValue.Y,
                ScaleZ ?? defaultValue.Z
            );
        }

        public Vec3f GetOrigin(Vec3f defaultValue = null)
        {
            if (defaultValue == null) defaultValue = new Vec3f(0, 0, 0);
            return new Vec3f(
                OriginX ?? defaultValue.X,
                OriginY ?? defaultValue.Y,
                OriginZ ?? defaultValue.Z
            );
        }

        /// <summary>
        /// Converts this TransformGroupSettings instance to a TransformSettings instance, applying default values from a provided ModelTransform if necessary.
        /// This is used to produce the final flattened transform settings that will be applied to the model.
        /// </summary>
        /// <param name="defaultTransform">An optional ModelTransform instance providing default values for any missing transform properties.</param>
        /// <returns>A TransformSettings instance with all properties populated.</returns>
        public TransformSettings ToTransformSettings(ModelTransform defaultTransform = null)
        {
            var outSetting = new TransformSettings
            {
                Id = Id,
                AssetType = AssetType,
                AssetName = AssetName,
                CullFaces = CullFaces,
                AlphaTestOpaque = AlphaTestOpaque,
                AlphaTestBlend = AlphaTestBlend,
                NormalShaded = NormalShaded,
                RenderPass = RenderPass,
                TintColor = TintColor,
                ClimateTintMap = ClimateTintMap,
                SeasonalTintMap = SeasonalTintMap,
                Enabled = Enabled
            };

            bool hasTransform =
                TranslationX.HasValue || TranslationY.HasValue || TranslationZ.HasValue ||
                RotationX.HasValue || RotationY.HasValue || RotationZ.HasValue ||
                ScaleX.HasValue || ScaleY.HasValue || ScaleZ.HasValue ||
                OriginX.HasValue || OriginY.HasValue || OriginZ.HasValue;

            if (hasTransform)
            {
                outSetting.Transform = new ModelTransform
                {
                    Translation = new Vec3f(
                        TranslationX ?? defaultTransform?.Translation.X ?? 0f,
                        TranslationY ?? defaultTransform?.Translation.Y ?? 0f,
                        TranslationZ ?? defaultTransform?.Translation.Z ?? 0f),
                    Rotation = new Vec3f(
                        RotationX ?? defaultTransform?.Rotation.X ?? 0f,
                        RotationY ?? defaultTransform?.Rotation.Y ?? 0f,
                        RotationZ ?? defaultTransform?.Rotation.Z ?? 0f),
                    ScaleXYZ = new Vec3f(
                        ScaleX ?? defaultTransform?.ScaleXYZ.X ?? 1f,
                        ScaleY ?? defaultTransform?.ScaleXYZ.Y ?? 1f,
                        ScaleZ ?? defaultTransform?.ScaleXYZ.Z ?? 1f),
                    Origin = new Vec3f(
                        OriginX ?? defaultTransform?.Origin.X ?? 0.5f,
                        OriginY ?? defaultTransform?.Origin.Y ?? 0.5f,
                        OriginZ ?? defaultTransform?.Origin.Z ?? 0.5f)
                };
            }

            return outSetting;
        }

        /// <summary>
        /// Merges the current TransformGroupSettings instance with the overlay settings, with overlay values taking precedence.
        /// </summary>
        /// <param name="overlay"> The TransformGroupSettings instance whose values should override the current instance where provided. </param>
        /// <returns> A new TransformGroupSettings instance containing the merged values from the current instance and the overlay. </returns>
        public TransformGroupSettings MergeOverlay(TransformGroupSettings overlay)
        {
            var m = Clone();

            if (overlay == null) return m;

            if (!string.IsNullOrWhiteSpace(overlay.Id)) m.Id = overlay.Id;

            if (overlay.AssetType != EnumAssetType.None)
            {
                m.AssetType = overlay.AssetType;
                m.AssetName = overlay.AssetName;
            }
            else if (!string.IsNullOrWhiteSpace(overlay.AssetName))
            {
                m.AssetName = overlay.AssetName;
            }

            if (overlay.TranslationX.HasValue) m.TranslationX = overlay.TranslationX;
            if (overlay.TranslationY.HasValue) m.TranslationY = overlay.TranslationY;
            if (overlay.TranslationZ.HasValue) m.TranslationZ = overlay.TranslationZ;

            if (overlay.RotationX.HasValue) m.RotationX = overlay.RotationX;
            if (overlay.RotationY.HasValue) m.RotationY = overlay.RotationY;
            if (overlay.RotationZ.HasValue) m.RotationZ = overlay.RotationZ;

            if (overlay.ScaleX.HasValue) m.ScaleX = overlay.ScaleX;
            if (overlay.ScaleY.HasValue) m.ScaleY = overlay.ScaleY;
            if (overlay.ScaleZ.HasValue) m.ScaleZ = overlay.ScaleZ;

            if (overlay.OriginX.HasValue) m.OriginX = overlay.OriginX;
            if (overlay.OriginY.HasValue) m.OriginY = overlay.OriginY;
            if (overlay.OriginZ.HasValue) m.OriginZ = overlay.OriginZ;

            if (overlay.CullFaces.HasValue) m.CullFaces = overlay.CullFaces;
            if (overlay.AlphaTestOpaque.HasValue) m.AlphaTestOpaque = overlay.AlphaTestOpaque;
            if (overlay.AlphaTestBlend.HasValue) m.AlphaTestBlend = overlay.AlphaTestBlend;
            if (overlay.NormalShaded.HasValue) m.NormalShaded = overlay.NormalShaded;
            if (!string.IsNullOrWhiteSpace(overlay.RenderPass)) m.RenderPass = overlay.RenderPass;

            if (overlay.TintColor != null) m.TintColor = overlay.TintColor;
            if (!string.IsNullOrEmpty(overlay.ClimateTintMap)) m.ClimateTintMap = overlay.ClimateTintMap;
            if (!string.IsNullOrEmpty(overlay.SeasonalTintMap)) m.SeasonalTintMap = overlay.SeasonalTintMap;

            // Note: TransformGroupSettings.Enabled is non-nullable bool, so we just apply the overlay value directly without checking for HasValue like the other properties.
            m.Enabled = overlay.Enabled;

            return m;
        }    

        /// <summary>
        /// Merges the current TransformGroupSettings instance with the relative values from the overlay settings. This is used for "tilde" prefixed adjustment groups where the values are meant to be added on top of the current instance rather than replacing them.
        /// </summary>
        /// <param name="relative">The overlay TransformGroupSettings instance containing relative adjustments.</param>
        /// <returns>A new TransformGroupSettings instance with merged values.</returns>
        public TransformGroupSettings MergeRelative(TransformGroupSettings relative)
        {
            var m = Clone();
            if (relative == null) return m;

            if (relative.TranslationX.HasValue) m.TranslationX = (m.TranslationX ?? 0f) + relative.TranslationX.Value;
            if (relative.TranslationY.HasValue) m.TranslationY = (m.TranslationY ?? 0f) + relative.TranslationY.Value;
            if (relative.TranslationZ.HasValue) m.TranslationZ = (m.TranslationZ ?? 0f) + relative.TranslationZ.Value;

            if (relative.RotationX.HasValue) m.RotationX = (m.RotationX ?? 0f) + relative.RotationX.Value;
            if (relative.RotationY.HasValue) m.RotationY = (m.RotationY ?? 0f) + relative.RotationY.Value;
            if (relative.RotationZ.HasValue) m.RotationZ = (m.RotationZ ?? 0f) + relative.RotationZ.Value;

            if (relative.ScaleX.HasValue) m.ScaleX = (m.ScaleX ?? 0f) + relative.ScaleX.Value;
            if (relative.ScaleY.HasValue) m.ScaleY = (m.ScaleY ?? 0f) + relative.ScaleY.Value;
            if (relative.ScaleZ.HasValue) m.ScaleZ = (m.ScaleZ ?? 0f) + relative.ScaleZ.Value;

            if (relative.OriginX.HasValue) m.OriginX = (m.OriginX ?? 0f) + relative.OriginX.Value;
            if (relative.OriginY.HasValue) m.OriginY = (m.OriginY ?? 0f) + relative.OriginY.Value;
            if (relative.OriginZ.HasValue) m.OriginZ = (m.OriginZ ?? 0f) + relative.OriginZ.Value;

            return m;
        }        

        public TransformGroupSettings Clone()
        {
            return new TransformGroupSettings
            {
                Id = Id,
                AssetType = AssetType,
                AssetName = AssetName,
                TranslationX = TranslationX,
                TranslationY = TranslationY,
                TranslationZ = TranslationZ,
                RotationX = RotationX,
                RotationY = RotationY,
                RotationZ = RotationZ,
                ScaleX = ScaleX,
                ScaleY = ScaleY,
                ScaleZ = ScaleZ,
                OriginX = OriginX,
                OriginY = OriginY,
                OriginZ = OriginZ,
                CullFaces = CullFaces,
                AlphaTestOpaque = AlphaTestOpaque,
                AlphaTestBlend = AlphaTestBlend,
                NormalShaded = NormalShaded,
                RenderPass = RenderPass,
                TintColor = TintColor,
                ClimateTintMap = ClimateTintMap,
                SeasonalTintMap = SeasonalTintMap,
                Enabled = Enabled
            };
        }

    }
    
}