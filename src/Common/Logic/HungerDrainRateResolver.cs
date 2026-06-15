using System;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;

namespace CarryOn.Common.Logic
{
    internal sealed class HungerDrainRateResolver
    {
        public float Resolve(CarrySlot slot, CarryHungerRateConfig config, SlotSettings? slotSettings = null)
        {
            // Per-block override from JSON takes priority — stored as modifier directly
            if (slotSettings?.HungerModifier.HasValue == true)
                return Math.Clamp(slotSettings.HungerModifier.Value, 0.0f, 9.0f);

            // Fall back to config defaults — stored as modifier directly
            var modifier = slot switch
            {
                CarrySlot.Hands => config.DefaultHandsModifier,
                CarrySlot.Back => config.DefaultBackModifier,
                _ => 0.0f
            };

            return Math.Clamp(modifier, 0.0f, 9.0f);
        }

        public bool IsEnabled(CarrySlot slot, CarryHungerRateConfig config)
        {
            return slot switch
            {
                CarrySlot.Hands => config.HandsEnabled,
                CarrySlot.Back => config.BackEnabled,
                _ => false
            };
        }
    }
}
