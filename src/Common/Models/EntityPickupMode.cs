namespace CarryOn.Common.Models
{
    /// <summary>Controls which input combination triggers whole-entity pickup.</summary>
    public enum EntityPickupMode
    {
        /// <summary>Right mouse only - no modifier key required (default).</summary>
        Interact = 0,
        /// <summary>Requires holding the Pick Up / Put Down hotkey while pressing right mouse.</summary>
        PickupHotkey
    }
}
