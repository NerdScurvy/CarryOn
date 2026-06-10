using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Client.Models
{
    public record CarriedRenderInfo
    {
        public required ItemRenderInfo RenderInfo { get; set; }
        public bool RenderEnabled { get; set; } = true;
        public Vec4f? TintColor { get; set; }
        public Vec4f? RgbGlowIntensity { get; set; }
        public bool EnableVertexWarp { get; set; } = false;
        public float? AlphaTestOpaque { get; set; }
        public float? AlphaTestBlend { get; set; }
        public bool? NormalShaded { get; set; }
        public string? RenderPass { get; set; }
        public ModelTransform? SecondaryTransform { get; set; }
        public bool SkipTransform { get; set; } = false;
        public bool IsAttachedRoot { get; set; } = false;
        public bool IsAttachedBlock { get; set; } = false;
    }
}