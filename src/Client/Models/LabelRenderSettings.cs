#nullable disable
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace CarryOn.Client.Models
{
    public class LabelRenderSettings
    {
        public ModelTransform Transform { get; set; }
        public List<ModelTransform> AdditionalTransforms { get; set; }
        public int? MaxWidth { get; set; }
        public int? MaxHeight { get; set; }
        public int? IconPixelSize { get; set; }
        public float? IconScale { get; set; }
        public bool IconFromInventory { get; set; }
        public string FontName { get; set; }
        public string VerticalAlign { get; set; }
        public bool? BoldFont { get; set; }
    }
}