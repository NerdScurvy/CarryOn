using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CarryOn.Client.Logic.TransformGroupResolvers.HiddenBackpack
{
    /// <summary>
    /// Entity-level resolver that downgrades the back-slot base transform group to the
    /// no-backpack group when a cosmetic mod hides the wearer's backpack, so the carried
    /// block's render position tracks each wearer's own hidden-pack state instead of a
    /// global selection mode. Delegates to per-mod <see cref="IHiddenBackpackSignal"/>
    /// probes and only ever downgrades to <see cref="CarryCodes.BackpackGroupNone"/>,
    /// never upgrades.
    /// </summary>
    public sealed class HiddenBackpackEntityResolver : IEntityTransformGroupResolver
    {
        public const string ResolverCodeName = "hidden-backpack";

        private readonly IReadOnlyList<IHiddenBackpackSignal> signals;

        // Memoizes the hidden decision per wearer, keyed by the combined signal change
        // token, so expensive probes (Vanity's reflection call, Hide Gear's JSON parse)
        // run only when the underlying state changes. ConditionalWeakTable auto-evicts
        // when the entity is collected - no manual cleanup.
        private readonly ConditionalWeakTable<EntityPlayer, MemoEntry> hiddenMemo = new();

        public HiddenBackpackEntityResolver(ICoreAPI api)
            : this(BuildDefaultSignals(api))
        {
        }

        public HiddenBackpackEntityResolver(IReadOnlyList<IHiddenBackpackSignal> signals)
        {
            ArgumentNullException.ThrowIfNull(signals);
            this.signals = signals;
        }

        public string ResolverCode => ResolverCodeName;

        public bool TryResolve(ICoreAPI api, EntityAgent entity, CarrySlot carrySlot, string baseGroup, out string? group)
        {
            group = null;
            if (carrySlot != CarrySlot.Back || entity is not EntityPlayer entityPlayer) return false;

            bool hidden;
            var signature = BuildCacheSignature(entityPlayer);
            if (signature == null)
            {
                // A signal opted out of memoization: re-evaluate every call.
                hidden = ComputeHidden(entityPlayer);
            }
            else
            {
                var memo = hiddenMemo.GetValue(entityPlayer, static _ => new MemoEntry());
                if (!memo.HasSignature || memo.Signature != signature)
                {
                    memo.Hidden = ComputeHidden(entityPlayer);
                    memo.Signature = signature;
                    memo.HasSignature = true;
                }
                hidden = memo.Hidden;
            }

            if (!hidden) return false;
            group = CarryCodes.BackpackGroupNone;
            return true;
        }

        private static IReadOnlyList<IHiddenBackpackSignal> BuildDefaultSignals(ICoreAPI api)
        {
            ArgumentNullException.ThrowIfNull(api);

            var modLoader = api.ModLoader;
            var signals = new List<IHiddenBackpackSignal>();

            if (modLoader.IsModEnabled(HideGearHiddenBackpackSignal.ModId))
            {
                signals.Add(new HideGearHiddenBackpackSignal());
            }

            if (modLoader.IsModEnabled(CombatOverhaulHiddenBackpackSignal.CombatOverhaulModId)
                || modLoader.IsModEnabled(CombatOverhaulHiddenBackpackSignal.OverhaulLibLegacyCompatModId))
            {
                signals.Add(new CombatOverhaulHiddenBackpackSignal());
            }

            if (modLoader.IsModEnabled(VanityHiddenBackpackSignal.ModId))
            {
                signals.Add(new VanityHiddenBackpackSignal(api.Logger));
            }

            return signals;
        }

        private bool ComputeHidden(EntityPlayer entityPlayer)
        {
            foreach (var signal in signals)
            {
                if (signal.IsBackpackHidden(entityPlayer)) return true;
            }
            return false;
        }

        private string? BuildCacheSignature(EntityPlayer entityPlayer)
        {
            if (signals.Count == 0) return string.Empty;

            string? combined = null;
            foreach (var signal in signals)
            {
                var part = signal.GetCacheSignature(entityPlayer);
                if (part == null) return null;
                combined = combined == null ? part : combined + "|" + part;
            }
            return combined;
        }

        private sealed class MemoEntry
        {
            public string? Signature;
            public bool Hidden;
            public bool HasSignature;
        }
    }
}
