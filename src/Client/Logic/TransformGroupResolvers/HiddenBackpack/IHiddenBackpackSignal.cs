using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.TransformGroupResolvers.HiddenBackpack
{
    /// <summary>
    /// Per-wearer backpack-visibility probe for a single cosmetic mod. Signals are pure
    /// per-entity reads over client-replicated state; mod-presence gating happens where the
    /// signal list is built (see <see cref="HiddenBackpackEntityResolver"/>).
    /// </summary>
    public interface IHiddenBackpackSignal
    {
        /// <summary>Short identifier of the mod whose state this signal reads.</summary>
        string SignalCode { get; }

        /// <summary>Returns true when the wearer's worn backpack is visually hidden.</summary>
        bool IsBackpackHidden(EntityPlayer entity);

        /// <summary>
        /// Returns a cheap change token for <see cref="IsBackpackHidden"/>, or null to opt
        /// this signal out of memoization entirely.
        /// </summary>
        string? GetCacheSignature(EntityPlayer entity);
    }
}
