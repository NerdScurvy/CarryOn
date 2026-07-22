using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;

namespace CarryOn.Common.Models
{
    internal class ReportRow
    {
        public string BlockCode { get; set; } = "";
        public CarrySlot Slot { get; set; }
        public string CarryType { get; set; } = "";
        public string Group { get; set; } = "";
        public float WalkMultiplier { get; set; } = 1f;
        public float HungerMultiplier { get; set; } = 1f;
        public float BaseWalkSpeed { get; set; }
        public float FinalWalkSpeed { get; set; }
        public float BaseHungerRate { get; set; }
        public float FinalHungerRate { get; set; }

        public Dictionary<string, string>? Variants { get; set; }

        public bool ModifierEquals(ReportRow other)
        {
            const float Epsilon = 0.0001f;
            return Group == other.Group
                && Math.Abs(WalkMultiplier - other.WalkMultiplier) < Epsilon
                && Math.Abs(HungerMultiplier - other.HungerMultiplier) < Epsilon
                && Math.Abs(BaseWalkSpeed - other.BaseWalkSpeed) < Epsilon
                && Math.Abs(FinalWalkSpeed - other.FinalWalkSpeed) < Epsilon
                && Math.Abs(BaseHungerRate - other.BaseHungerRate) < Epsilon
                && Math.Abs(FinalHungerRate - other.FinalHungerRate) < Epsilon;
        }
    }
}
