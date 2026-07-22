namespace CarryOn.Common.Models
{
    /// <summary>Controls how carried items are dropped when they cannot be placed.</summary>
    public enum DropMode
    {
        /// <summary>Drop the carried items directly into the world.</summary>
        Items = 0,
        /// <summary>Spawn as a block entity only when placement fails.</summary>
        EntityOnFailedPlacement,
        /// <summary>Always spawn as a block entity when dropped.</summary>
        EntityAlways
    }
}
