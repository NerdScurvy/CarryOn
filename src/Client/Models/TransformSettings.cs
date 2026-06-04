#nullable disable
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Models
{
    public record TransformSettings(
        string AssetName = null,
        string Id = null,
        EnumAssetType AssetType = EnumAssetType.None,
        string DisableIfItemStackPath = null,
        string BlockEntityDataItemStackPath = null,
        ModelTransform Transform = null,
        bool? CullFaces = null,
        float? AlphaTestOpaque = null,
        float? AlphaTestBlend = null,
        bool? NormalShaded = null,
        string RenderPass = null,
        Vec4f TintColor = null,
        string ClimateTintMap = null,
        string SeasonalTintMap = null,
        Vec4f RgbGlowIntensity = null,
        bool? Enabled = true
        )
    {
        public TransformSettings DeepClone() => this with
        {
            Transform = this.Transform?.Clone(),
            RgbGlowIntensity = this.RgbGlowIntensity?.Clone(),
            TintColor = this.TintColor?.Clone()
        };


        public TransformSettings DeepCloneWithDefaults(
            string defaultAssetName,
            CarriedGroupAssetType defaultAssetType)
        {
            // Determine the new AssetType and AssetName up front
            var assetType = this.AssetType;
            var assetName = this.AssetName;

            if (this.AssetType == EnumAssetType.None
                && !string.IsNullOrEmpty(defaultAssetName)
                && defaultAssetType != CarriedGroupAssetType.None)
            {
                assetName = defaultAssetName;
                assetType = defaultAssetType == CarriedGroupAssetType.Item
                    ? EnumAssetType.Item
                    : EnumAssetType.Block;
            }

            // Return a new record with all deep-cloned reference types and updated values
            return this with
            {
                AssetName = assetName,
                AssetType = assetType,
                DisableIfItemStackPath = this.DisableIfItemStackPath,
                BlockEntityDataItemStackPath = this.BlockEntityDataItemStackPath,
                Transform = this.Transform?.Clone(),
                RgbGlowIntensity = this.RgbGlowIntensity?.Clone(),
                TintColor = this.TintColor?.Clone()
            };
        }
    }
}