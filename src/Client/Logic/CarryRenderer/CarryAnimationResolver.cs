using System;
using System.Collections.Generic;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal static class CarryAnimationResolver
    {
        private sealed class DefaultAnimationSet
        {
            public string? Sit { get; init; }
            public string? Crouch { get; init; }
        }

        private static readonly IReadOnlyDictionary<string, DefaultAnimationSet> DefaultSets
            = new Dictionary<string, DefaultAnimationSet>(StringComparer.OrdinalIgnoreCase)
            {
                [CarryOnCode("holdheavy")] = new DefaultAnimationSet
                {
                    Sit = CarryOnCode("holdheavysit"),
                    Crouch = CarryOnCode("holdheavycrouch")
                },
                [CarryOnCode("holdlight")] = new DefaultAnimationSet
                {
                    Sit = CarryOnCode("holdlightsit"),
                    Crouch = CarryOnCode("holdlightcrouch")
                }
            };

        internal static bool IsSitting(EntityPlayer player)
        {
            return (player?.Controls?.FloorSitting ?? false) || player?.MountedOn != null;
        }

        internal static string ResolveHandsAnimation(SlotSettings? slotSettings, bool isSneaking, bool isSitting)
        {
            if (slotSettings == null) return string.Empty;

            var baseAnimation = slotSettings.Animation;
            if (string.IsNullOrWhiteSpace(baseAnimation)) return string.Empty;

            if (isSitting)
            {
                if (!string.IsNullOrWhiteSpace(slotSettings.AnimationSit))
                {
                    return slotSettings.AnimationSit;
                }

                if (DefaultSets.TryGetValue(baseAnimation, out var set) && !string.IsNullOrWhiteSpace(set.Sit))
                {
                    return set.Sit;
                }
            }

            if (!isSneaking) return baseAnimation;

            if (!string.IsNullOrWhiteSpace(slotSettings.AnimationCrouch))
            {
                return slotSettings.AnimationCrouch;
            }

            if (DefaultSets.TryGetValue(baseAnimation, out var crouchSet) && !string.IsNullOrWhiteSpace(crouchSet.Crouch))
            {
                return crouchSet.Crouch;
            }

            return baseAnimation;
        }

        internal static HashSet<string> GetHandAnimationCodes(SlotSettings? slotSettings)
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (slotSettings == null) return codes;

            var baseAnimation = slotSettings.Animation;
            if (string.IsNullOrWhiteSpace(baseAnimation)) return codes;

            codes.Add(baseAnimation);

            if (!string.IsNullOrWhiteSpace(slotSettings.AnimationSit))
            {
                codes.Add(slotSettings.AnimationSit);
            }

            if (!string.IsNullOrWhiteSpace(slotSettings.AnimationCrouch))
            {
                codes.Add(slotSettings.AnimationCrouch);
            }

            if (DefaultSets.TryGetValue(baseAnimation, out var set))
            {
                if (!string.IsNullOrWhiteSpace(set.Sit)) codes.Add(set.Sit);
                if (!string.IsNullOrWhiteSpace(set.Crouch)) codes.Add(set.Crouch);
            }

            return codes;
        }
    }
}