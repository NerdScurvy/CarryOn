using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.TransformGroupResolvers.HiddenBackpack
{
    /// <summary>
    /// Reads Combat Overhaul's per-wearer backpack-visibility flag from the entity's synced
    /// WatchedAttributes (key <c>combatoverhaul:hide-backpack</c>). The key is stable
    /// regardless of which mod writes it - real Combat Overhaul, or the Overhaul lib legacy
    /// compat layer that Combat Overhaul vanity-slot forks depend on - so one read covers
    /// both stacks.
    /// </summary>
    public sealed class CombatOverhaulHiddenBackpackSignal : IHiddenBackpackSignal
    {
        public const string SignalCodeName = "combatoverhaul";
        public const string CombatOverhaulModId = "combatoverhaul";
        public const string OverhaulLibLegacyCompatModId = "overhaulliblegacycompat";
        public const string HideBackpackKey = "combatoverhaul:hide-backpack";

        public string SignalCode => SignalCodeName;

        public bool IsBackpackHidden(EntityPlayer entity)
            => entity?.WatchedAttributes?.GetBool(HideBackpackKey, false) ?? false;

        public string? GetCacheSignature(EntityPlayer entity)
            => (entity?.WatchedAttributes?.GetBool(HideBackpackKey, false) ?? false) ? "1" : "0";
    }
}
