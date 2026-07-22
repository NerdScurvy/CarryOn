namespace CarryOn.Common.Models
{
    /// <summary>Controls who can pick up dropped carried block entities.</summary>
    public enum PickupAccess
    {
        /// <summary>Any player can pick up the dropped entity.</summary>
        Anyone = 0,
        /// <summary>Only the player who dropped it can pick it up.</summary>
        OwnerOnly,
        /// <summary>The owner has priority, but others can pick it up after a timeout.</summary>
        OwnerFirst
    }
}
