using System;
using System.Collections.Generic;

namespace CarryOn.Common.Models
{
    public class SlotSettings
    {
        public string? Animation { get; set; }
        public string? AnimationSit { get; set; }
        public string? AnimationCrouch { get; set; }
        public float WalkSpeedModifier { get; set; } = 0.0F;
        public IDictionary<string, float> WalkSpeedModifierByType { get; set; }
            = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public IDictionary<string, float> WalkSpeedModifierByGroup { get; set; }
            = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public string? EnabledCondition { get; set; }
        public string?[]? ExcludedTypes { get; set; }
    }
}
