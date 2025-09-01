using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using CarryOn.API.Common;

namespace CarryOn.Common
{

    public class EntityBehaviorAttachableCarryable : EntityBehavior, ICustomInteractionHelpPositioning
    {

        public readonly ICoreAPI Api;


        public EntityBehaviorAttachableCarryable(Entity entity) : base(entity)
        {
            Api = entity.World.Api;
        }

        private EntityBehaviorAttachable _behaviorAttachable;

        public int GetSlotIndex(int selBoxIndex)
        {
            if (selBoxIndex <= 0) return 0;
            _behaviorAttachable ??= entity.GetBehavior<EntityBehaviorAttachable>();
            
            return _behaviorAttachable?.GetSlotIndexFromSelectionBoxIndex(selBoxIndex - 1) ?? 0;
        }

        public ItemSlot GetItemSlot(int slotIndex)
        {
            return (slotIndex >= 0 && slotIndex < _behaviorAttachable?.Inventory.Count) ? _behaviorAttachable?.Inventory[slotIndex] : null;
        }

        public bool IsItemSlotEmpty(ItemSlot itemSlot)
        {
            if (itemSlot != null)
            {
                return itemSlot.Empty;
            }
            return false;
        }


        public static string Name { get; }
            = $"{CarrySystem.ModId}:attachablecarryable";

        public override string PropertyName() => Name;

        public bool TransparentCenter => false;

        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled)
        {

            if (es.SelectionBoxIndex == 0) return null;

            var behaviorAttachable = es.Entity.GetBehavior<EntityBehaviorAttachable>();

            if (behaviorAttachable == null) return null;

            var slotIndex = es.SelectionBoxIndex - 1;


            var targetSlot = behaviorAttachable.GetSlotFromSelectionBoxIndex(slotIndex);

            // Slot will be null for selections that don't contain storage slots - i.e. seats
            if (targetSlot == null) return null;

            var carryableStacks = AttachableCarryableInteractionHelp.GetInteractionItemStacks(Api, es.Entity, slotIndex, targetSlot);


            string langCode = null;

            // If slot has an item then check if block is carryable
            if (!targetSlot.Empty)
            {
                if (targetSlot.Itemstack.Block?.GetBehavior<BlockBehaviorCarryable>() != null)
                {
                    langCode = CarrySystem.ModId + ":blockhelp-detach";
                }
            }
            else
            {
                langCode = CarrySystem.ModId + ":blockhelp-attach";
            }

            if (langCode == null) return null;

            return [ new WorldInteraction()
                        {
                            ActionLangCode = langCode,
                            Itemstacks = targetSlot.Empty?carryableStacks?.ToArray():null,
                            MouseButton = EnumMouseButton.Right,
                            HotKeyCode = "carryonpickupkey",
                            RequireFreeHand = true
                        }];
        }

        public Vec3d GetInteractionHelpPosition()
        {
            var capi = entity.Api as ICoreClientAPI;
            if (capi.World.Player.CurrentEntitySelection == null) return null;

            var selebox = capi.World.Player.CurrentEntitySelection.SelectionBoxIndex - 1;
            if (selebox < 0) return null;

            return entity.GetBehavior<EntityBehaviorSelectionBoxes>().GetCenterPosOfBox(selebox)?.Add(0, 0.5, 0);
        }

        public void OnAttachmentToggled(bool isAttached, EntityAgent byEntity, ItemSlot itemslot, int targetSlotIndex)
        {
            // This will close the containers inventory on detach
            var attachedListener = itemslot?.Itemstack?.Collectible?.GetCollectibleInterface<IAttachedListener>();
            if (attachedListener != null)
            {
                if (isAttached)
                    attachedListener.OnAttached(itemslot, targetSlotIndex, entity, byEntity);
                else
                    attachedListener.OnDetached(itemslot, targetSlotIndex, entity, byEntity);
            }
            
            entity.MarkShapeModified();
            // Tell server to save this chunk to disk again
            entity.World.BlockAccessor.GetChunkAtBlockPos(entity.ServerPos.AsBlockPos).MarkModified();
            if (!isAttached)
            {
                ClearCachedSlotStorage(Api, targetSlotIndex, itemslot, entity);
            }
        }

        public static void ClearCachedSlotStorage(ICoreAPI api, int slotIndex, ItemSlot slot, Entity targetEntity)
        {
            if (slotIndex < 0 || targetEntity == null || slot?.Itemstack == null) return;
            ObjectCacheUtil.Delete(api, "att-cont-workspace-" + slotIndex.ToString() + "-" + targetEntity.EntityId.ToString() + "-" + slot.Itemstack.Id.ToString());
        }

    }

    public class AttachableCarryableInteractionHelp
    {
        public static List<ItemStack> GetInteractionItemStacks(ICoreAPI api, Entity entity, int slotIndex, ItemSlot slot)
        {
            var carryableKey = "carryable-stack-" + entity.Code + "-" + slotIndex;
            var carryableStacks = ObjectCacheUtil.TryGet<List<ItemStack>>(api, carryableKey);
            if (carryableStacks == null)
            {
                // Grab the cached attachable stacks as a base.
                var attachableKey = "interactionhelp-attachable-" + entity.Code + "-" + slotIndex;
                var attachableStacks = ObjectCacheUtil.TryGet<List<ItemStack>>(api, attachableKey);

                if (attachableStacks == null || attachableStacks?.Count == 0)
                {
                    return null;
                }

                carryableStacks = ObjectCacheUtil.GetOrCreate(api, carryableKey, () =>
                {
                    var stacks = new List<ItemStack>();
                    foreach (var item in attachableStacks)
                    {
                        if (item.Block != null)
                        {
                            if (item.Block.IsCarryable())
                            {
                                stacks.Add(item.Clone());
                            }
                        }
                    }
                    return stacks;
                });

            }
            return carryableStacks;
        }
    }
}
