using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CarryOn.Client.Logic.CarryRenderer
{
    public sealed class CarryAnimationSync
    {
        private readonly Dictionary<long, HashSet<string>> activeHandCarryAnimationsByEntityId = new();
        private readonly Dictionary<long, HashSet<string>> knownHandCarryAnimationsByEntityId = new();

        private static HashSet<string> GetOrCreateSet(Dictionary<long, HashSet<string>> dict, long entityId)
        {
            if (!dict.TryGetValue(entityId, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                dict[entityId] = set;
            }
            return set;
        }

        public void SyncCarryAnimations(EntityPlayer player)
        {
            if (player == null) return;
            var trackedAnimations = GetOrCreateSet(activeHandCarryAnimationsByEntityId, player.EntityId);
            var knownAnimations = GetOrCreateSet(knownHandCarryAnimationsByEntityId, player.EntityId);

            var isSneaking = player.Controls?.Sneak ?? false;
            var isSitting = CarryAnimationResolver.IsSitting(player);
            var desiredAnimations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var handsCarried = player.GetCarried(CarrySlot.Hands);

            if (handsCarried != null)
            {
                var slotSettings = handsCarried.GetCarryableBehavior()?.Slots?[handsCarried.Slot];
                if (slotSettings != null)
                {
                    knownAnimations.UnionWith(CarryAnimationResolver.GetHandAnimationCodes(slotSettings));
                    var animationCode = CarryAnimationResolver.ResolveHandsAnimation(slotSettings, isSneaking, isSitting);
                    if (!string.IsNullOrWhiteSpace(animationCode))
                    {
                        desiredAnimations.Add(animationCode);
                    }
                }
            }

            foreach (var animationCode in desiredAnimations.Except(trackedAnimations))
                player.StartAnimation(animationCode);

            foreach (var animationCode in trackedAnimations.Except(desiredAnimations).ToList())
                player.StopAnimation(animationCode);

            var activeByCode = player.AnimManager?.ActiveAnimationsByAnimCode;
            if (activeByCode != null && activeByCode.Count > 0)
            {
                foreach (var animationCode in activeByCode.Keys.Where(code => knownAnimations.Contains(code)).ToList())
                {
                    if (!desiredAnimations.Contains(animationCode))
                    {
                        player.StopAnimation(animationCode);
                    }
                }
            }

            trackedAnimations.Clear();
            trackedAnimations.UnionWith(desiredAnimations);
        }

        public void CleanupStaleAnimations(HashSet<long> seenEntityIds)
        {
            foreach (var entityId in this.activeHandCarryAnimationsByEntityId.Keys.Where(id => !seenEntityIds.Contains(id)).ToList())
            {
                this.activeHandCarryAnimationsByEntityId.Remove(entityId);
                this.knownHandCarryAnimationsByEntityId.Remove(entityId);
            }
        }
    }
}
