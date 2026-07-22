namespace CarryOn.Common.Models
{
    /// <summary>Represents the type of carry interaction being performed.</summary>
    public enum CarryAction
    {
        /// <summary>No carry action in progress.</summary>
        None = 0,
        /// <summary>The carry action has completed.</summary>
        Done,
        /// <summary>Picking up a block from the world.</summary>
        PickUp,
        /// <summary>Placing a carried block back into the world.</summary>
        PlaceDown,
        /// <summary>Swap carried block to or from back.</summary>
        SwapBack,
        /// <summary>Interacting with block entity while carried block in hands.</summary>
        Interact,
        /// <summary>Attaching a carried block to an entity slot.</summary>
        Attach,
        /// <summary>Detaching an block from entity slot to carry it.</summary>
        Detach,
        /// <summary>Putting a carried item into a block entity.</summary>
        Put,
        /// <summary>Taking a block from a block entity to carry.</summary>
        Take,
        /// <summary>Picking up dropped block entity from the world to carry.</summary>
        PickupEntity
    }
}
