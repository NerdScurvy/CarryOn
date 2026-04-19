using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Models
{
    public class TransformSettings
    {
        public string AssetName { get; set; }

        public string Id { get; set; }

        public EnumAssetType AssetType { get; set; } = EnumAssetType.None;

        public ModelTransform Transform { get; set; }

        public bool? CullFaces { get; set; } = null;

        public float? AlphaTestOpaque { get; set; } = null;

        public float? AlphaTestBlend { get; set; } = null;

        public bool? NormalShaded { get; set; } = null;

        public string RenderPass { get; set; } = null;

        public Vec4f TintColor { get; set; } = null;

        public string ClimateTintMap { get; set; } = null;

        public string SeasonalTintMap { get; set; } = null;

        public bool? Enabled { get; set; } = true;

        public TransformSettings Clone()
        {
            return new TransformSettings
            {
                AssetType = this.AssetType,
                AssetName = this.AssetName,
                Id = this.Id,
                Transform = this.Transform?.Clone(),
                CullFaces = this.CullFaces,
                AlphaTestOpaque = this.AlphaTestOpaque,
                AlphaTestBlend = this.AlphaTestBlend,
                NormalShaded = this.NormalShaded,
                RenderPass = this.RenderPass,
                TintColor = this.TintColor,
                ClimateTintMap = this.ClimateTintMap,
                SeasonalTintMap = this.SeasonalTintMap,
                Enabled = this.Enabled,
            };
        }
    }
}