using System;
using CarryOn.API.Common.Models;

namespace CarryOn.Common.Logic
{
    internal sealed class HungerDrainRateResolver
    {
        public float Resolve(CarrySlot slot, CarryHungerRateConfig config)
        {
            var rate = slot switch
            {
                CarrySlot.Hands => config.DefaultHandsRate,
                CarrySlot.Back => config.DefaultBackRate,
                _ => 1.0f
            };

            return Math.Clamp(rate, 1.0f, 10.0f);
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
