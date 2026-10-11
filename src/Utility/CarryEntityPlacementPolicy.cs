using CarryOn.API.Common.Models;
using CarryOn.Common.Models;

namespace CarryOn.Utility
{
    /// <summary>
    /// Resolves the effective "spawn as entity on hand-placement" verdict for a carried block.
    /// The per-block signal (<see cref="CarriedBlock.SpawnAsEntityOnPlacement"/>) set by pickup
    /// handlers for custom entity blocks wins; unset signals (normal world-pickup blocks) fall
    /// back to the CarryOn config default, which opens the future "place your carried block as
    /// an entity" feature without per-mod code.
    /// </summary>
    public static class CarryEntityPlacementPolicy
    {
        public static bool ShouldSpawnAsEntityOnPlacement(CarriedBlock carriedBlock, CarriedBlockEntityConfig? config)
            => carriedBlock.SpawnAsEntityOnPlacement ?? config?.SpawnAsEntityOnPlacementByDefault ?? false;
    }
}