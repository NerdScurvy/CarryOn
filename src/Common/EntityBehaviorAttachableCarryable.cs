using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using CarryOn.API.Common;
using CarryOn.Common.Network;
using System.Diagnostics;

namespace CarryOn.Common
{

    public class AttachCarryInteraction
    {
        public float TimeHeld { get; set; }
        public bool IsHeld { get; set; }
        public bool WasReleased { get; set; }

        public int SlotIndex { get; set; }
        public ItemSlot Slot { get; set; }

        public EntityAgent ByEntity { get; set; }

    }

    public class EntityBehaviorAttachableCarryable : EntityBehavior, ICustomInteractionHelpPositioning
    {

        private ICoreAPI Api;

        private CarrySystem CarrySystem { get; }

        private AttachCarryInteraction Interaction { get; }

        public EntityBehaviorAttachableCarryable(Entity entity) : base(entity)
        {
            Api = entity.World.Api;
            CarrySystem = Api.ModLoader.GetModSystem<CarrySystem>();
            Interaction = new AttachCarryInteraction
            {
                TimeHeld = 0.0F,
                IsHeld = false,
                WasReleased = true
            };
        }

        private EntityBehaviorAttachable _behaviorAttachable;

        private int GetSlotIndex(int selBoxIndex)
        {
            if (selBoxIndex <= 0) return 0;
            _behaviorAttachable ??= entity.GetBehavior<EntityBehaviorAttachable>();
            return _behaviorAttachable.GetSlotIndexFromSelectionBoxIndex(selBoxIndex - 1);
        }

        private ItemSlot GetItemSlot(int slotIndex)
        {
            return (slotIndex >= 0 && slotIndex < _behaviorAttachable?.Inventory.Count) ? _behaviorAttachable?.Inventory[slotIndex] : null;
        }

        private bool IsItemSlotEmpty(ItemSlot itemSlot)
        {
            // TODO: Is returning a false value correct when itemSlot is null?
            if (itemSlot != null)
            {
                return itemSlot.Empty;
            }
            return false;
        }

        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {
            // Fall back to default interaction only when CarryOn is disabled
            if (!CarrySystem.CarryHandler.IsCarryOnEnabled)
            {
                base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
                return;
            }

            //TODO: prevent spinner if player cannot pickup or putdown block

            var byPlayer = byEntity as EntityPlayer;
            if (byPlayer == null) return;
            int selBoxIndex = byPlayer?.EntitySelection?.SelectionBoxIndex ?? -1;
            int slotIndex = GetSlotIndex(selBoxIndex);
            var slot = GetItemSlot(slotIndex);

            if (Api.Side == EnumAppSide.Server)
            {
                if (IsItemSlotEmpty(slot) && byEntity.GetCarried(CarrySlot.Hands) == null) return;
                if (byEntity.IsCarryKeyHeld()) handled = EnumHandling.PreventSubsequent;
                return;
            }

            if (!CarrySystem.CarryHandler.IsCarryKeyPressed(true))
            {
                Interaction.WasReleased = true;
            }

            // Exit here and allow OnGameTick to handle the pickup interaction
            if (!Interaction.WasReleased)
            {
                handled = EnumHandling.PreventSubsequent;
                return;
            }

            // If the slot is empty and the player is not carrying anything, or not ready to do carry action then exit early
            if ((IsItemSlotEmpty(slot) && byEntity.GetCarried(CarrySlot.Hands) == null) || !byEntity.CanDoCarryAction(true)) return;
            handled = EnumHandling.PreventSubsequent;
            if (mode == EnumInteractMode.Interact && CarrySystem.CarryHandler.IsCarryKeyPressed())
            {
                var entityPlayer = byEntity as EntityPlayer;
                var inventory = entityPlayer.Player.InventoryManager.OpenedInventories.Find(f => f.InventoryID == $"mountedbaginv-{slotIndex}-{entity.EntityId}");
                if (inventory != null) entityPlayer.Player.InventoryManager.CloseInventory(inventory);

                var carried = entityPlayer.GetCarried(CarrySlot.Hands);

                if (slot == null) return;

                if ((slot.Empty && carried == null) || (!slot.Empty && carried != null))
                {
                    return;
                }

                Interaction.SlotIndex = slotIndex;
                Interaction.Slot = slot;
                Interaction.ByEntity = byEntity;
                Interaction.TimeHeld = 0.0F;
                Interaction.IsHeld = true;
                Interaction.WasReleased = false;
                return;
            }
            else
            {
                Interaction.WasReleased = true;
                handled = EnumHandling.PassThrough;

            }
            if (mode == EnumInteractMode.Interact && CarrySystem.ClientAPI.Input.IsHotKeyPressed("sneak"))
            {

                handled = EnumHandling.PreventDefault;
            }
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
        }

        public override void OnGameTick(float deltaTime)
        {

            if (Api.Side == EnumAppSide.Server || !CarrySystem.CarryHandler.IsCarryOnEnabled) return;

            if (!Interaction.WasReleased)
            {

                var interactHeld = CarrySystem.CarryHandler.IsCarryKeyPressed(true);

                if (!interactHeld)
                {
                    Interaction.WasReleased = true;
                    CancelInteraction(true);
                    return;
                }

            }
            if (Interaction.Slot == null || !Interaction.IsHeld) return;

            Interaction.TimeHeld += deltaTime;
            var progress = Interaction.TimeHeld / CarrySystem.InteractSpeedDefault;

            CarrySystem.HudOverlayRenderer.CircleProgress = progress;

            if (progress <= 1.0F) return;

            var attachedListener = Interaction.Slot?.Itemstack?.Collectible?.GetCollectibleInterface<IAttachedListener>();

            if (Interaction.Slot.Empty)
            {
                // Try to place block on boat
                CarrySystem.ClientChannel.SendPacket(new AttachMessage(entity.EntityId, Interaction.SlotIndex));
                attachedListener?.OnAttached(Interaction.Slot, Interaction.SlotIndex, entity, Interaction.ByEntity);
                OnAttachmentToggled(Interaction.ByEntity, Interaction.Slot);
            }
            else
            {
                // Try to pickup block from boat
                CarrySystem.ClientChannel.SendPacket(new DetachMessage(entity.EntityId, Interaction.SlotIndex));
                attachedListener?.OnDetached(Interaction.Slot, Interaction.SlotIndex, entity, Interaction.ByEntity);
                OnAttachmentToggled(Interaction.ByEntity, Interaction.Slot);

                // Clear cached inventory
                // TODO: Does this need to be updated on other players?
                ClearCachedSlotStorage(Api, Interaction.SlotIndex, Interaction.Slot, entity);
            }

            CancelInteraction();
        }


        public void CancelInteraction(bool resetTimeHeld = false)
        {
            if (Api.Side == EnumAppSide.Client)
            {
                CarrySystem.HudOverlayRenderer.CircleVisible = false;
            }
            Interaction.IsHeld = false;
            Interaction.Slot = null;
            if (resetTimeHeld) Interaction.TimeHeld = 0.0F;
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


            //  List<ItemStack> stacks = new List<ItemStack>();

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

        private void OnAttachmentToggled(EntityAgent byEntity, ItemSlot itemslot)
        {
            var sound = itemslot.Itemstack?.Block?.Sounds.Place ?? new AssetLocation("sounds/player/build");
            Api.World.PlaySoundAt(sound, entity, (byEntity as EntityPlayer).Player, true, 16);
            entity.MarkShapeModified();
            // Tell server to save this chunk to disk again
            entity.World.BlockAccessor.GetChunkAtBlockPos(entity.ServerPos.AsBlockPos).MarkModified();
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
