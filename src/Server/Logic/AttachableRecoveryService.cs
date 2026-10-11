using System;
using System.Text;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CarryOn.Server.Logic
{
    /// <summary>
    /// Server-side recovery sweep that strips broken/unrenderable attachments from entities with an
    /// attachable inventory (boats, packs, mounts). Reuses the same attach gates that now prevent the
    /// corruption on new attachments.
    /// Carryable blocks are restored as <see cref="EntityCarriedBlock"/> (contents preserved) near the
    /// invoking player; anything else is dropped as loose items, including any carried contents.
    /// </summary>
    public class AttachableRecoveryService
    {
        private readonly ICoreServerAPI api;

        public AttachableRecoveryService(ICoreServerAPI api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
        }

        /// <summary>
        /// Scans entities with an attachable inventory, optionally limited to <paramref name="radius"/>
        /// blocks around <paramref name="origin"/>. Slot contents that fail the attach gates are removed:
        /// carryable blocks spawn as carried block entities (contents preserved) at <paramref name="dropPos"/>,
        /// everything else is dropped as items at <paramref name="dropPos"/> (falling back to the entity's
        /// own position). Returns a human-readable summary for the invoking player.
        /// </summary>
        public string Sweep(Vec3d? origin, float radius, string? ownerUid, Vec3d? dropPos)
        {
            var summary = new StringBuilder();
            int entitiesChecked = 0;
            int slotsCleaned = 0;
            int carriedBlocksSpawned = 0;
            int itemsDropped = 0;

            var carrySystem = api.ModLoader.GetModSystem<CarrySystem>();

            foreach (var entity in api.World.LoadedEntities.Values)
            {
                var attachable = entity.GetBehavior<EntityBehaviorAttachable>();
                if (attachable == null) continue;

                if (origin != null && radius > 0 && entity.Pos.DistanceTo(origin) > radius) continue;

                entitiesChecked++;
                bool changed = false;

                for (int i = 0; i < attachable.Inventory.Count; i++)
                {
                    var slot = attachable.Inventory[i];
                    if (slot.Empty) continue;

                    var stack = slot.Itemstack;
                    if (stack == null) continue;

                    if (AttachableGateValidator.IsAttachable(entity, stack)
                        && !AttachableGateValidator.IsRetrieveOnlyStorage(stack))
                    {
                        continue;
                    }

                    var position = dropPos ?? entity.Pos.XYZ;
                    if (stack.Block?.HasBehavior<BlockBehaviorCarryable>() == true)
                    {
                        var carriedBlock = BuildCarriedBlock(stack);
                        if (carriedBlock != null)
                        {
                            carrySystem?.CarriedBlockEntityService
                                ?.SpawnCarriedBlockEntityWithGravity(carriedBlock, ownerUid ?? "", position);
                            carriedBlocksSpawned++;
                        }
                        else
                        {
                            api.World.SpawnItemEntity(stack, position, new Vec3d(0, 0.1, 0));
                            itemsDropped++;
                        }
                    }
                    else
                    {
                        api.World.SpawnItemEntity(stack, position, new Vec3d(0, 0.1, 0));
                        itemsDropped++;
                        DropContents(stack, position);
                    }

                    api.World.Logger.Audit($"[CarryOn] Recovery removed {stack} from slot of entity {entity.EntityId} {entity.GetName()} at {entity.Pos.AsBlockPos}");
                    slot.Itemstack = null;
                    slotsCleaned++;
                    changed = true;
                }

                if (!changed) continue;

                attachable.storeInv();
                // Valid on the server: flags the entity as shape-modified so its updated attachment
                // state is re-synced to clients (the same helper is used by the server-side attach/
                // detach paths in EntityBehaviorAttachableCarryable). No client shape access here.
                AttachableEntitySync.MarkDirty(entity);
            }

            summary.Append($"{entitiesChecked} attachable entit{(entitiesChecked == 1 ? "y" : "ies")} checked, ");
            summary.Append($"{slotsCleaned} broken slot{(slotsCleaned == 1 ? "" : "s")} cleaned, ");
            summary.Append($"{carriedBlocksSpawned} carried block{(carriedBlocksSpawned == 1 ? "" : "s")} restored, ");
            summary.Append($"{itemsDropped} item{(itemsDropped == 1 ? "" : "s")} dropped to the ground.");
            return summary.ToString();
        }

        /// <summary>
        /// Reconstructs a <see cref="CarriedBlock"/> from a slot itemstack, converting the carried
        /// backpack back into block-entity inventory data (mirrors the detach path used by
        /// <see cref="EntityBehaviorAttachableCarryable.TryDetach"/> via <see cref="AttachableCarryConversion"/>).
        /// The conversion strips the <c>carryonbackup</c> attribute, so the restored entity carries
        /// only the live block-entity data.
        /// </summary>
        private static CarriedBlock? BuildCarriedBlock(ItemStack stack)
        {
            return AttachableCarryConversion.ItemStackToCarriedBlock(stack);
        }

        /// <summary>
        /// Drops any carried contents of a non-carryable slot item (stored in its backpack attribute)
        /// so nothing is lost when the block itself cannot be restored as a carried block entity.
        /// </summary>
        private void DropContents(ItemStack stack, Vec3d position)
        {
            var sourceBackpack = stack.Attributes?["backpack"] as ITreeAttribute;
            if (sourceBackpack == null || sourceBackpack.Count == 0) return;

            var inventory = InventoryConverter.ConvertBackpackToBlockInventory(sourceBackpack);
            var slots = inventory?.GetTreeAttribute("slots");
            if (slots == null) return;

            foreach (var value in slots.Values)
            {
                if (value?.GetValue() is not ItemStack item) continue;
                api.World.SpawnItemEntity(item, position, new Vec3d(0, 0.1, 0));
            }
        }
    }
}
