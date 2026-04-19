using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Models
{
    public class CarriedRenderInfo
    {
        public ItemRenderInfo RenderInfo { get; set; }

        public bool RenderEnabled { get; set; } = true;

        public Vec4f TintColor { get; set; } = null;

        public string SeasonalTintMap { get; set; } = null;

        public bool EnableVertexWarp { get; set; } = false;

        public float? AlphaTestOpaque { get; set; } = null;

        public float? AlphaTestBlend { get; set; } = null;

        public bool? NormalShaded { get; set; } = null;

        public string RenderPass { get; set; } = null;

        public ModelTransform SecondaryTransform { get; set; } = null;

        public bool SkipTransform { get; set; } = false;
    }
}