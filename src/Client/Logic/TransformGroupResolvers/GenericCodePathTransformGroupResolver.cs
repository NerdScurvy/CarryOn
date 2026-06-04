using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.TransformGroupResolvers
{
    /// <summary>
    /// Generic fallback resolver that derives transform group candidates from block code path segments.
    /// Example: mymod:fancybox-iron-west + baseGroup "hands" =>
    /// hands-fancybox-iron-west, hands-fancybox-iron, hands-fancybox.
    /// </summary>
    public class GenericCodePathTransformGroupResolver : ICarriedTransformGroupResolver
    {
        public string ResolverCode => "codepath";

        public bool TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup, out CarriedGroupResolution? resolution)
        {
            resolution = null;

            if (carried?.Block?.Code == null || string.IsNullOrWhiteSpace(baseGroup))
            {
                return false;
            }

            var codePath = carried.Block.Code.Path;
            if (string.IsNullOrWhiteSpace(codePath))
            {
                return false;
            }

            var parts = codePath.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            var normalizedBaseGroup = baseGroup.ToLowerInvariant();
            var candidates = new List<string>(parts.Length);

            for (var i = parts.Length; i >= 1; i--)
            {
                var suffix = string.Join('-', parts, 0, i).ToLowerInvariant();
                candidates.Add(normalizedBaseGroup + "-" + suffix);
            }

            resolution = new CarriedGroupResolution
            {
                PrimaryGroupCandidates = candidates
            };

            return true;
        }

        public string? GetCacheSignature(ICoreAPI api, CarriedBlock carried, string baseGroup, CarriedGroupResolution? resolution)
        {
            // Static derivation from block code and base group only; no extra signature needed.
            return string.Empty;
        }
    }
}