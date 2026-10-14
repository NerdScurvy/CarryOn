namespace CarryOn.Common.Models
{
    /// <summary>
    /// Why <c>HasPermissionAt</c> refused a cell. Carried out of the silent multi-cell scan
    /// so the caller can report the actual reason instead of a generic "no permission".
    /// <para>
    /// Internal on purpose: <c>ICarryManager.HasPermissionAt</c> is public API and its
    /// signature is unchanged. Only the two message-emitting call sites use this - the
    /// pickup and placement gates, which hold the internal service directly rather than
    /// going through the manager.
    /// </para>
    /// </summary>
    internal enum PermissionDenialReason
    {
        /// <summary>Permitted.</summary>
        None = 0,

        /// <summary>
        /// A registered event handler vetoed the cell through
        /// <c>TriggerCheckPermissionAt</c>. The handler owns the player's feedback for its
        /// own veto, so CarryOn does not add a message of its own - but it also does not go
        /// silent, because a handler that vetoes quietly is legal.
        /// </summary>
        Event,

        /// <summary>The cell is block-reinforced and the player is not in creative.</summary>
        Reinforced,

        /// <summary>
        /// A landclaim refused the cell. The gate re-invokes the check with
        /// <c>showErrorMessage: true</c> on this cell, which is what makes vanilla's own
        /// claim message appear - so CarryOn's generic message is suppressed in favour of
        /// it rather than sent alongside it.
        /// </summary>
        Claim
    }

    internal static class PermissionDenialFailure
    {
        /// <summary>
        /// The failure code a gate should report for a denial reason. The mapping is the
        /// whole point of the enum: <see cref="PermissionDenialReason.Claim"/> becomes
        /// <see cref="FailureCodes.AlreadyReported"/> because the claim message has already
        /// been sent, while <see cref="PermissionDenialReason.Event"/> deliberately stays
        /// on the generic code so a quietly-vetoing handler does not leave the player with
        /// no message at all.
        /// </summary>
        public static string ToFailureCode(this PermissionDenialReason reason) => reason switch
        {
            PermissionDenialReason.Claim => CarryCodes.FailureCodes.AlreadyReported,
            PermissionDenialReason.Reinforced => CarryCodes.FailureCodes.Reinforced,
            _ => CarryCodes.FailureCodes.NoPermission
        };
    }
}