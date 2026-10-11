namespace CarryOn.Common.Models
{
    /// <summary>Determines how back slot items are selected from backpack inventories.</summary>
    public enum BackpackSelectionMode
    {
        /// <summary>Use the last matching item found when scanning slots.</summary>
        LastFound = 0,
        /// <summary>Use the first matching item found when scanning slots.</summary>
        FirstFound,
        /// <summary>Only use an item if it is in the very first slot of the backpack.</summary>
        FirstOnly,
        /// <summary>Never compensate for a worn backpack: always resolve the no-backpack transform
        /// group. Use when backpacks are hidden by another mod or should never shift the carried block.</summary>
        None
    }
}
