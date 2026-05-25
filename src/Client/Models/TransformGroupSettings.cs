using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Models
{
    public record TransformGroupSettings(
        string Id = null,
        EnumAssetType AssetType = EnumAssetType.None,
        string AssetName = null,
        string DisableIfItemStackPath = null,
        string BlockEntityDataItemStackPath = null,
        float? TranslationX = null,
        float? TranslationY = null,
        float? TranslationZ = null,
        float? RotationX = null,
        float? RotationY = null,
        float? RotationZ = null,
        float? ScaleX = null,
        float? ScaleY = null,
        float? ScaleZ = null,
        float? OriginX = null,
        float? OriginY = null,
        float? OriginZ = null,
        bool? CullFaces = null,
        float? AlphaTestOpaque = null,
        float? AlphaTestBlend = null,
        bool? NormalShaded = null,
        string RenderPass = null,
        Vec4f TintColor = null,
        string ClimateTintMap = null,
        string SeasonalTintMap = null,
        float? GlowIntensity = null,
        bool Enabled = true
    )
    {

        // Deep clone (for reference types)
        public TransformGroupSettings DeepClone() => this with
        {
            TintColor = TintColor?.Clone()
        };

        // MergeOverlay and MergeRelative return new records
        public TransformGroupSettings MergeOverlay(TransformGroupSettings overlay)
        {
            if (overlay == null) return this;

            // For reference types, clone if needed (e.g., TintColor)
            var tintColor = overlay.TintColor != null ? overlay.TintColor.Clone() : this.TintColor?.Clone();

            return new TransformGroupSettings(
                Id: !string.IsNullOrWhiteSpace(overlay.Id) ? overlay.Id : this.Id,
                AssetType: overlay.AssetType != EnumAssetType.None ? overlay.AssetType : this.AssetType,
                AssetName: overlay.AssetType != EnumAssetType.None
                    ? overlay.AssetName
                    : (!string.IsNullOrWhiteSpace(overlay.AssetName) ? overlay.AssetName : this.AssetName),
                DisableIfItemStackPath: !string.IsNullOrWhiteSpace(overlay.DisableIfItemStackPath) ? overlay.DisableIfItemStackPath : this.DisableIfItemStackPath,
                BlockEntityDataItemStackPath: !string.IsNullOrWhiteSpace(overlay.BlockEntityDataItemStackPath) ? overlay.BlockEntityDataItemStackPath : this.BlockEntityDataItemStackPath,
                TranslationX: overlay.TranslationX ?? this.TranslationX,
                TranslationY: overlay.TranslationY ?? this.TranslationY,
                TranslationZ: overlay.TranslationZ ?? this.TranslationZ,
                RotationX: overlay.RotationX ?? this.RotationX,
                RotationY: overlay.RotationY ?? this.RotationY,
                RotationZ: overlay.RotationZ ?? this.RotationZ,
                ScaleX: overlay.ScaleX ?? this.ScaleX,
                ScaleY: overlay.ScaleY ?? this.ScaleY,
                ScaleZ: overlay.ScaleZ ?? this.ScaleZ,
                OriginX: overlay.OriginX ?? this.OriginX,
                OriginY: overlay.OriginY ?? this.OriginY,
                OriginZ: overlay.OriginZ ?? this.OriginZ,
                CullFaces: overlay.CullFaces ?? this.CullFaces,
                AlphaTestOpaque: overlay.AlphaTestOpaque ?? this.AlphaTestOpaque,
                AlphaTestBlend: overlay.AlphaTestBlend ?? this.AlphaTestBlend,
                NormalShaded: overlay.NormalShaded ?? this.NormalShaded,
                RenderPass: !string.IsNullOrWhiteSpace(overlay.RenderPass) ? overlay.RenderPass : this.RenderPass,
                TintColor: tintColor,
                ClimateTintMap: !string.IsNullOrEmpty(overlay.ClimateTintMap) ? overlay.ClimateTintMap : this.ClimateTintMap,
                SeasonalTintMap: !string.IsNullOrEmpty(overlay.SeasonalTintMap) ? overlay.SeasonalTintMap : this.SeasonalTintMap,
                GlowIntensity: overlay.GlowIntensity ?? this.GlowIntensity,
                Enabled: overlay.Enabled // always use overlay's value (non-nullable)
            );
        }

        public TransformGroupSettings MergeRelative(TransformGroupSettings relative)
        {
            if (relative == null) return this;

            // For reference types, clone if needed (e.g., TintColor)
            var tintColor = this.TintColor?.Clone();

            // For each property, add relative if present, else keep base value
            float? AddOrKeep(float? baseVal, float? relVal) =>
                relVal.HasValue ? (baseVal ?? 0f) + relVal.Value : baseVal;

            return new TransformGroupSettings(
                Id: this.Id,
                AssetType: this.AssetType,
                AssetName: this.AssetName,
                DisableIfItemStackPath: this.DisableIfItemStackPath,
                BlockEntityDataItemStackPath: this.BlockEntityDataItemStackPath,
                TranslationX: AddOrKeep(this.TranslationX, relative.TranslationX),
                TranslationY: AddOrKeep(this.TranslationY, relative.TranslationY),
                TranslationZ: AddOrKeep(this.TranslationZ, relative.TranslationZ),
                RotationX: AddOrKeep(this.RotationX, relative.RotationX),
                RotationY: AddOrKeep(this.RotationY, relative.RotationY),
                RotationZ: AddOrKeep(this.RotationZ, relative.RotationZ),
                ScaleX: AddOrKeep(this.ScaleX, relative.ScaleX),
                ScaleY: AddOrKeep(this.ScaleY, relative.ScaleY),
                ScaleZ: AddOrKeep(this.ScaleZ, relative.ScaleZ),
                OriginX: AddOrKeep(this.OriginX, relative.OriginX),
                OriginY: AddOrKeep(this.OriginY, relative.OriginY),
                OriginZ: AddOrKeep(this.OriginZ, relative.OriginZ),
                CullFaces: this.CullFaces,
                AlphaTestOpaque: this.AlphaTestOpaque,
                AlphaTestBlend: this.AlphaTestBlend,
                NormalShaded: this.NormalShaded,
                RenderPass: this.RenderPass,
                TintColor: tintColor,
                ClimateTintMap: this.ClimateTintMap,
                SeasonalTintMap: this.SeasonalTintMap,
                GlowIntensity: AddOrKeep(this.GlowIntensity, relative.GlowIntensity),
                Enabled: this.Enabled
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
            // Compute the transform if any transform property is set
            bool hasTransform =
                TranslationX.HasValue || TranslationY.HasValue || TranslationZ.HasValue ||
                RotationX.HasValue || RotationY.HasValue || RotationZ.HasValue ||
                ScaleX.HasValue || ScaleY.HasValue || ScaleZ.HasValue ||
                OriginX.HasValue || OriginY.HasValue || OriginZ.HasValue;

            ModelTransform transform = null;
            if (hasTransform)
            {
                transform = new ModelTransform
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

            // Convert GlowIntensity to RGB vector if present
            Vec4f rgbGlow = GlowIntensity.HasValue
                ? new Vec4f(GlowIntensity.Value, GlowIntensity.Value, GlowIntensity.Value, GlowIntensity.Value)
                : null;

            // Construct the TransformSettings record at the end
            return new TransformSettings(
                Id: Id,
                AssetType: AssetType,
                AssetName: AssetName,
                DisableIfItemStackPath: DisableIfItemStackPath,
                BlockEntityDataItemStackPath: BlockEntityDataItemStackPath,
                CullFaces: CullFaces,
                AlphaTestOpaque: AlphaTestOpaque,
                AlphaTestBlend: AlphaTestBlend,
                NormalShaded: NormalShaded,
                RenderPass: RenderPass,
                TintColor: TintColor,
                ClimateTintMap: ClimateTintMap,
                SeasonalTintMap: SeasonalTintMap,
                Enabled: Enabled,
                RgbGlowIntensity: rgbGlow ?? new Vec4f(0, 0, 0, 0),
                Transform: transform
            );
        }
    }
}