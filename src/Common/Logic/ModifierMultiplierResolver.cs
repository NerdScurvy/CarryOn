using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.Common;

namespace CarryOn.Common.Logic
{
    internal static class ModifierMultiplierResolver
    {
        public static float ResolveMultiplier(
            Block? block,
            ModifierMultipliersConfig? multipliers,
            CarrySlot slot)
        {
            if (multipliers == null) return 1.0f;

            var globalValue = slot switch
            {
                CarrySlot.Hands => multipliers.Global?.Hands,
                CarrySlot.Back => multipliers.Global?.Back,
                _ => null
            };
            var multiplier = globalValue ?? 1.0f;

            if (block != null && multipliers.ByBlockMaterial?.Count > 0)
            {
                var blockMaterial = block.BlockMaterial;
                foreach (var entry in multipliers.ByBlockMaterial)
                {
                    if (entry?.Material == null) continue;

                    if (entry.Material.Value == blockMaterial)
                    {
                        var materialValue = slot switch
                        {
                            CarrySlot.Hands => entry.Hands,
                            CarrySlot.Back => entry.Back,
                            _ => null
                        };
                        if (materialValue.HasValue)
                            multiplier = materialValue.Value;
                        break;
                    }
                }
            }

            return multiplier;
        }
    }
}
