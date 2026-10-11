using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using Vintagestory.API.Common;

namespace CarryOn.Utility
{
    public static class PlayerBackpackExtensions
    {
        // 2f6: the backpack slot scan only ever looks at the first 4 slots, so a cache entry only
        // needs to snapshot those. Invalidation uses the ItemStack references of the scanned slots:
        // an ItemStack's item code cannot change on a live instance, so unchanged references mean the
        // resolved slot is still correct (a stale cache can never produce the wrong transform group).
        private const int BackpackScanSlotCount = 4;

        private sealed class BackpackGroupCacheEntry
        {
            public IInventory? Inventory;
            public readonly ItemStack?[] Snapshot = new ItemStack?[BackpackScanSlotCount];
            public int ResolvedSlotIndex = -1;
        }

        private static readonly ConditionalWeakTable<IPlayer, Dictionary<BackpackSelectionMode, BackpackGroupCacheEntry>> backpackGroupCache = new();

        public static ItemSlot? GetRenderedBackpackSlot(this IPlayer player, BackpackSelectionMode mode = BackpackSelectionMode.LastFound)
        {
            if (mode == BackpackSelectionMode.None) return null;

            var backpackInv = player.InventoryManager.GetOwnInventory("backpack");
            if (backpackInv == null) return null;

            if (mode == BackpackSelectionMode.FirstOnly)
            {
                var slot = backpackInv[0];
                return slot?.Itemstack != null &&
                       slot.Itemstack.ItemAttributes?["attachableToEntity"]?["categoryCode"]?.AsString() == "backpack"
                    ? slot : null;
            }

            var matchingSlots = backpackInv
                 .Take(4)
                 .Where(slot => slot?.Itemstack != null &&
                             slot?.Itemstack?.ItemAttributes?["attachableToEntity"]?["categoryCode"]?.AsString() == "backpack")
                 .ToList();

            return mode switch
            {
                BackpackSelectionMode.FirstFound => matchingSlots.FirstOrDefault(),
                _ => matchingSlots.LastOrDefault()
            };
        }

        public static string? GetRenderedBackpackItemCode(this IPlayer player, BackpackSelectionMode mode = BackpackSelectionMode.LastFound)
        {
            var renderedItemSlot = player.GetRenderedBackpackSlot(mode);
            return renderedItemSlot?.Itemstack?.Item?.Code?.ToString();
        }

        public static string ResolveCarryTransformGroupBase(this EntityPlayer entityPlayer, CarryOnConfig config, CarrySlot carrySlot)
        {
            if (carrySlot == CarrySlot.Hands)
            {
                return "hands";
            }

            var player = entityPlayer?.Player;
            if (player == null)
            {
                return CarryCodes.BackpackGroupNone;
            }

            var selectionMode = config?.CarryOptions?.BackpackSelectionMode ?? BackpackSelectionMode.LastFound;

            // None short-circuits before the mapping lookup: without this the value would fall through
            // the selection switch's default arm and behave like LastFound.
            if (selectionMode == BackpackSelectionMode.None)
            {
                return CarryCodes.BackpackGroupNone;
            }

            var backpackInv = player.InventoryManager.GetOwnInventory("backpack");
            if (backpackInv == null)
            {
                return CarryCodes.BackpackGroupNone;
            }

            // The resolved group is re-derived from config.BackpackMapping every call (so config
            // reloads apply immediately); only the resolved slot index is cached. The cache is valid
            // while the snapshot matches the scanned slots' ItemStack references.
            // ConditionalWeakTable auto-evicts when the IPlayer is GC'd - no manual cleanup needed.
            var playerCache = backpackGroupCache.GetOrCreateValue(player);
            if (!playerCache.TryGetValue(selectionMode, out var entry))
            {
                entry = new BackpackGroupCacheEntry();
                playerCache[selectionMode] = entry;
            }

            int resolvedSlotIndex;
            if (entry.ResolvedSlotIndex < 0
                || !ReferenceEquals(entry.Inventory, backpackInv)
                || !SnapshotMatches(backpackInv, entry.Snapshot))
            {
                resolvedSlotIndex = ScanBackpackSlots(backpackInv, selectionMode);
                entry.Inventory = backpackInv;
                entry.ResolvedSlotIndex = resolvedSlotIndex;
                CaptureSnapshot(backpackInv, entry.Snapshot);
            }
            else
            {
                resolvedSlotIndex = entry.ResolvedSlotIndex;
            }

            if (resolvedSlotIndex < 0 || resolvedSlotIndex >= backpackInv.Count)
            {
                return CarryCodes.BackpackGroupNone;
            }

            var backpackItemCode = backpackInv[resolvedSlotIndex]?.Itemstack?.Item?.Code?.ToString();
            if (!string.IsNullOrEmpty(backpackItemCode)
                && (config?.BackpackMapping?.TryGetValue(backpackItemCode, out var backpackType) ?? false)
                && !string.IsNullOrEmpty(backpackType))
            {
                return CarryCodes.BackpackGroupPrefix + backpackType;
            }

            return CarryCodes.BackpackGroupNone;
        }

        public static string ResolveCarryTransformGroupBase(this EntityAgent entity, CarryOnConfig config, CarrySlot carrySlot)
        {
            if (entity is EntityPlayer entityPlayer)
            {
                return entityPlayer.ResolveCarryTransformGroupBase(config, carrySlot);
            }

            return CarryCodes.DefaultTransformGroup;
        }

        private static int ScanBackpackSlots(IInventory backpackInv, BackpackSelectionMode mode)
        {
            var scanCount = Math.Min(backpackInv.Count, BackpackScanSlotCount);

            if (mode == BackpackSelectionMode.FirstOnly)
            {
                return scanCount > 0 && backpackInv[0].IsBagItem() ? 0 : -1;
            }

            var foundIndex = -1;
            for (var i = 0; i < scanCount; i++)
            {
                if (backpackInv[i].IsBagItem())
                {
                    foundIndex = i;
                    if (mode == BackpackSelectionMode.FirstFound) break;
                }
            }

            return foundIndex;
        }

        private static void CaptureSnapshot(IInventory backpackInv, ItemStack?[] snapshot)
        {
            var scanCount = Math.Min(backpackInv.Count, BackpackScanSlotCount);
            for (var i = 0; i < scanCount; i++)
            {
                snapshot[i] = backpackInv[i]?.Itemstack;
            }

            for (var i = scanCount; i < snapshot.Length; i++)
            {
                snapshot[i] = null;
            }
        }

        private static bool SnapshotMatches(IInventory backpackInv, ItemStack?[] snapshot)
        {
            var scanCount = Math.Min(backpackInv.Count, BackpackScanSlotCount);
            for (var i = 0; i < scanCount; i++)
            {
                if (!ReferenceEquals(backpackInv[i]?.Itemstack, snapshot[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
