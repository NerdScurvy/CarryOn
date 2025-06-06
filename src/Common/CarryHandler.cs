using System.Linq;
using CarryOn.API.Common;
using CarryOn.Common.Network;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CarryOn.Common
{
    /// <summary>
    ///   Takes care of core CarryCapacity handling, such as listening to input events,
    ///   picking up, placing and swapping blocks, as well as sending and handling messages.
    /// </summary>
    public class CarryHandler
    {
        private CurrentAction _action = CurrentAction.None;
        private CarrySlot? _targetSlot = null;
        private BlockPos _selectedBlock = null;
        private float _timeHeld = 0.0F;
        public bool IsCarryOnEnabled { get; set; } = true;

        private KeyCombination CarryKeyCombination{get; set;} 

        private KeyCombination CarrySwapKeyCombination{get;set;}

        private CarrySystem CarrySystem { get; }

        public CarryHandler(CarrySystem carrySystem)
            => CarrySystem = carrySystem;

        public void InitClient()
        {
            var cApi = CarrySystem.ClientAPI;
            var input = cApi.Input;

            input.RegisterHotKey(CarrySystem.PickupKeyCode, Lang.Get(CarrySystem.ModId + ":pickup-hotkey"), CarrySystem.PickupKeyDefault);

            input.RegisterHotKey(CarrySystem.SwapBackModifierKeyCode, Lang.Get(CarrySystem.ModId + ":swap-back-hotkey"), CarrySystem.SwapBackModifierDefault);

            input.RegisterHotKey(CarrySystem.ToggleKeyCode, Lang.Get(CarrySystem.ModId + ":toggle-hotkey"), CarrySystem.ToggleDefault, altPressed: true);
            input.RegisterHotKey(CarrySystem.QuickDropKeyCode, Lang.Get(CarrySystem.ModId + ":quickdrop-hotkey"), CarrySystem.QuickDropDefault, altPressed: true, ctrlPressed: true);

            input.SetHotKeyHandler(CarrySystem.ToggleKeyCode, TriggerToggleKeyPressed);
            input.SetHotKeyHandler(CarrySystem.QuickDropKeyCode, TriggerQuickDropKeyPressed);

            CarrySystem.ClientChannel.SetMessageHandler<LockSlotsMessage>(OnLockSlotsMessage);

            cApi.Input.InWorldAction +=  OnEntityAction;
            cApi.Event.RegisterGameTickListener(OnGameTick, 0);

            cApi.Event.BeforeActiveSlotChanged +=
                (_) => OnBeforeActiveSlotChanged(CarrySystem.ClientAPI.World.Player.Entity);

            CarryKeyCombination = input.HotKeys[CarrySystem.PickupKeyCode]?.CurrentMapping;
            CarrySwapKeyCombination = input.HotKeys[CarrySystem.SwapBackModifierKeyCode]?.CurrentMapping;
        }

        public void InitServer()
        {
            CarrySystem.ServerChannel
                .SetMessageHandler<InteractMessage>(OnInteractMessage)
                .SetMessageHandler<PickUpMessage>(OnPickUpMessage)
                .SetMessageHandler<PlaceDownMessage>(OnPlaceDownMessage)
                .SetMessageHandler<SwapSlotsMessage>(OnSwapSlotsMessage)
                .SetMessageHandler<AttachMessage>(OnAttachMessage)
                .SetMessageHandler<DetachMessage>(OnDetachMessage)
                .SetMessageHandler<CarryKeyMessage>(OnCarryKeyMessage)
                .SetMessageHandler<QuickDropMessage>(OnQuickDropMessage);

            CarrySystem.ServerAPI.Event.OnEntitySpawn += OnServerEntitySpawn;
            CarrySystem.ServerAPI.Event.PlayerNowPlaying += OnServerPlayerNowPlaying;

            CarrySystem.ServerAPI.Event.BeforeActiveSlotChanged +=
                (player, _) => OnBeforeActiveSlotChanged(player.Entity);
        }

        public bool TriggerToggleKeyPressed(KeyCombination keyCombination)
        {

            IsCarryOnEnabled = !IsCarryOnEnabled;

            CarrySystem.ClientAPI.ShowChatMessage("CarryOn " + (IsCarryOnEnabled ? "Enabled" : "Disabled"));
            return true;
        }

        public bool TriggerQuickDropKeyPressed(KeyCombination keyCombination)
        {
            // Send drop message even if client shows nothing being held
            CarrySystem.ClientChannel.SendPacket(new QuickDropMessage());
            return true;
        }

        public bool IsCarryKeyPressed(bool checkMouse = false){
            var input = CarrySystem.ClientAPI.Input;
            if(checkMouse && !input.InWorldMouseButton.Right) return false;
            
            return input.KeyboardKeyState[CarryKeyCombination.KeyCode];
        }

        public bool IsCarrySwapKeyPressed(){
            return CarrySystem.ClientAPI.Input.KeyboardKeyState[CarrySwapKeyCombination.KeyCode];
        }

        public void OnServerEntitySpawn(Entity entity)
        {
            // We handle player "spawning" in OnServerPlayerJoin.
            // If we send a LockSlotsMessage at this point, the client's player is still null.
            if (entity is EntityPlayer) return;

            // Set this again so walk speed modifiers and animations can be applied.
            foreach (var carried in entity.GetCarried())
                carried.Set(entity, carried.Slot);
        }

        public void OnServerPlayerNowPlaying(IServerPlayer player)
        {
            foreach (var carried in player.Entity.GetCarried())
                carried.Set(player.Entity, carried.Slot);
        }

        /// <summary> Returns the first "action" slot (either Hands or Shoulder)
        ///           that satisfies the specified function, or null if none. </summary>
        private static CarrySlot? FindActionSlot(System.Func<CarrySlot, bool> func)
        {
            if (func(CarrySlot.Hands)) return CarrySlot.Hands;
            if (func(CarrySlot.Shoulder)) return CarrySlot.Shoulder;
            return null;
        }

        public void OnEntityAction(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            // Only handle action if it's being activated rather than deactivated.
            if (!on || !IsCarryOnEnabled) return;

            bool isInteract;
            switch (action)
            {
                // Right click (interact action) starts carry's pickup and place handling.
                case EnumEntityAction.InWorldRightMouseDown:
                    isInteract = true; break;
                // Other actions, which are prevented while holding something.
                case EnumEntityAction.InWorldLeftMouseDown:
                case EnumEntityAction.Sprint:
                    isInteract = false; break;
                default: return;
            }

            // If an action is currently ongoing, ignore the game's entity action.
            if (_action != CurrentAction.None)
            { handled = EnumHandling.PreventDefault; return; }

            var world = CarrySystem.ClientAPI.World;  
            var player = world.Player;

            // Don't do carryon interaction if player looking at an entity (i.e. raft or armor stand)
            if (player.CurrentEntitySelection != null) return;

            var selection = player.CurrentBlockSelection;

            var carriedHands = player.Entity.GetCarried(CarrySlot.Hands);
            var carriedBack = player.Entity.GetCarried(CarrySlot.Back);
            var carriedShoulder = player.Entity.GetCarried(CarrySlot.Shoulder);
            var holdingAny = carriedHands ?? carriedShoulder;

            var swapBack = IsCarrySwapKeyPressed();

            // If something is being carried in-hand, prevent RMB, LMB and sprint.
            // If still holding RMB after an action completed, prevent the default action as well.
            if ((carriedHands != null) || (isInteract && (_timeHeld > 0.0F)))
                handled = EnumHandling.PreventDefault;

            // Only continue if player is starting an interaction (right click).
            if (!isInteract || (_timeHeld > 0.0F)) return;

            // If something's being held..
            if (holdingAny != null)
            {
                // ..and aiming at block, try to place it.
                if (selection != null && !swapBack)
                {
                    // If carrying something in-hand, don't require empty hands.
                    // This shouldn't occur since nothing is supposed to go into
                    // an active slot while something is carried there. This is
                    // just in case, so a carried block can still be placed down.
                    if (!CanInteract(player.Entity, carriedHands == null))
                    {
                        selection = GetMultiblockOriginSelection(selection);

                        // Cannot pick up or put down - check for interact behavior such as open door or chest.
                        if (selection?.Block?.HasBehavior<BlockBehaviorCarryableInteract>() == true)
                        {
                            var interactBehavior = selection?.Block.GetBehavior<BlockBehaviorCarryableInteract>();
                            if (interactBehavior.CanInteract(player))
                            {
                                _action = CurrentAction.Interact;
                                _selectedBlock = selection.Position;
                            }
                        }
                        else
                        {
                            handled = EnumHandling.PreventDefault;
                        }
                        return;
                    }
                    
                    _selectedBlock = GetPlacedPosition(world, selection, holdingAny.Block);
                    if (_selectedBlock == null) return;

                    _action = CurrentAction.PlaceDown;
                    _targetSlot = holdingAny.Slot;
                }
                // If something's being held and aiming at nothing, try to put held block on back.
                else
                {
                    if (!ModConfig.BackSlotEnabled) return;
                    // Check to make sure that player is sneaking empty-handed,
                    // is not already carrying something in the back slot, and
                    // the currently held block can be equipped on the back.
                    if (!CanInteract(player.Entity, true) || (carriedBack != null) ||
                        (holdingAny.Behavior.Slots[CarrySlot.Back] == null))
                    {
                        return;
                    }

                    _action = CurrentAction.SwapBack;
                    _targetSlot = holdingAny.Slot;
                }
            }
            // If nothing's being held..
            else if (CanInteract(player.Entity, true))
            {
                if (selection != null) selection = GetMultiblockOriginSelection(selection);
                // ..and aiming at carryable block, try to pick it up.
                if ((selection?.Block != null) && (_targetSlot = FindActionSlot(slot => selection.Block.IsCarryable(slot))) != null && !swapBack)
                {
                    _action = CurrentAction.PickUp;
                    _selectedBlock = selection.Position;
                }
                // ..and aiming at nothing or non-carryable block, try to grab block on back.
                else if ((carriedBack != null) &&
                         (_targetSlot = FindActionSlot(slot => carriedBack.Behavior.Slots[slot] != null)) != null)
                {
                    _action = CurrentAction.SwapBack;
                }
                else
                {
                    return;
                }
            }
            else
            {
                return;
            }

            // Run this once to for validation. May reset action to None.
            _timeHeld = 0.0F;
            OnGameTick(0.0F);
            // Prevent default action. Don't want to interact with blocks.
            handled = EnumHandling.PreventDefault;
        }

        public void OnGameTick(float deltaTime)
        {
            if (!IsCarryOnEnabled) return;

            var world = CarrySystem.ClientAPI.World;
            var player = world.Player;            
            var input = CarrySystem.ClientAPI.Input;

            if (!input.MouseButton.Right) { CancelInteraction(true); return; }

            // One-shot sent carry key status to server.
            if (input.IsHotKeyPressed(CarrySystem.PickupKeyCode))
            {
                if (!player.Entity.IsCarryKeyHeld())
                {
                    CarrySystem.ClientChannel.SendPacket(new CarryKeyMessage(true));
                    player.Entity.SetCarryKeyHeld(true);
                }
            }
            else
            {
                if (player.Entity.IsCarryKeyHeld())
                {
                    CarrySystem.ClientChannel.SendPacket(new CarryKeyMessage(false));
                    player.Entity.SetCarryKeyHeld(false);
                }
            }

            if (_action == CurrentAction.None) return;


            // TODO: Only allow close blocks to be picked up.
            // TODO: Don't allow the block underneath to change?

            if (_action != CurrentAction.Interact && !CanInteract(player.Entity, (_action != CurrentAction.PlaceDown) || (_targetSlot != CarrySlot.Hands)))
            { CancelInteraction(); return; }

            var carriedTarget = _targetSlot.HasValue ? player.Entity.GetCarried(_targetSlot.Value) : null;
            var holdingAny = player.Entity.GetCarried(CarrySlot.Hands)
                             ?? player.Entity.GetCarried(CarrySlot.Shoulder);
            BlockSelection selection = null;
            BlockBehaviorCarryable carryBehavior = null;
            BlockBehaviorCarryableInteract interactBehavior = null;
            switch (_action)
            {
                case CurrentAction.Interact:
                case CurrentAction.PickUp:
                case CurrentAction.PlaceDown:

                    // Ensure the player hasn't in the meantime
                    // picked up / placed down something somehow.
                    if ((_action == CurrentAction.PickUp) == (holdingAny != null))
                    { CancelInteraction(); return; }

                    selection = (_action == CurrentAction.PlaceDown) ? player.CurrentBlockSelection : GetMultiblockOriginSelection(player.CurrentBlockSelection);

                    var position = (_action == CurrentAction.PlaceDown)
                        ? GetPlacedPosition(world, player?.CurrentBlockSelection, carriedTarget.Block)
                        : selection?.Position;

                    // Make sure the player is still looking at the same block.
                    if (_selectedBlock != position)
                    { CancelInteraction(); return; }

                    if (_action == CurrentAction.Interact)
                    {
                        interactBehavior = selection?.Block.GetBehavior<BlockBehaviorCarryableInteract>();
                        break;
                    }
                    // Get the block behavior from either the block
                    // to be picked up or the currently carried block.
                    carryBehavior = (_action == CurrentAction.PickUp)
                        ? selection?.Block?.GetBehaviorOrDefault(BlockBehaviorCarryable.Default)
                        : carriedTarget?.Behavior;
                    break;

                case CurrentAction.SwapBack:
                    if (!ModConfig.BackSlotEnabled) return;

                    var carriedBack = player.Entity.GetCarried(CarrySlot.Back);
                    // Ensure that the player hasn't in the meantime
                    // put something in that slot / on their back.
                    if ((carriedTarget != null) == (carriedBack != null))
                    { CancelInteraction(); return; }

                    carryBehavior = (carriedTarget != null) ? carriedTarget.Behavior : carriedBack.Behavior;
                    // Make sure the block to swap can still be put in that slot.
                    if (carryBehavior.Slots[_targetSlot.Value] == null) return;

                    break;

                default: return;
            }

            float requiredTime;
            if (_action == CurrentAction.Interact)
            {
                requiredTime = interactBehavior?.InteractDelay ?? CarrySystem.InteractSpeedDefault;
            }
            else
            {
                requiredTime = carryBehavior?.InteractDelay ?? CarrySystem.PickUpSpeedDefault;
                switch (_action)
                {
                    case CurrentAction.PlaceDown: requiredTime *= CarrySystem.PlaceSpeedDefault; break;
                    case CurrentAction.SwapBack: requiredTime *= CarrySystem.SwapSpeedDefault; break;
                }
            }

            _timeHeld += deltaTime;
            var progress = _timeHeld / requiredTime;
            CarrySystem.HudOverlayRenderer.CircleProgress = progress;
            if (progress <= 1.0F) return;

            switch (_action)
            {
                case CurrentAction.Interact:
                    if (selection?.Block?.OnBlockInteractStart(world, player, selection) == true)
                        CarrySystem.ClientChannel.SendPacket(new InteractMessage(selection.Position));
                    break;
                case CurrentAction.PickUp:
                    if (player.Entity.Carry(selection.Position, _targetSlot.Value))
                        CarrySystem.ClientChannel.SendPacket(new PickUpMessage(selection.Position, _targetSlot.Value));
                    break;
                case CurrentAction.PlaceDown:
                    if (PlaceDown(player, carriedTarget, selection, out var placedAt))
                        CarrySystem.ClientChannel.SendPacket(new PlaceDownMessage(_targetSlot.Value, selection, placedAt));
                    break;
                case CurrentAction.SwapBack:
                    if (player.Entity.Swap(_targetSlot.Value, CarrySlot.Back))
                        CarrySystem.ClientChannel.SendPacket(new SwapSlotsMessage(CarrySlot.Back, _targetSlot.Value));
                    break;
            }

            CancelInteraction();
        }

        public void CancelInteraction(bool resetTimeHeld = false)
        {
            _action = CurrentAction.None;
            _targetSlot = null;
            CarrySystem.HudOverlayRenderer.CircleVisible = false;
            if (resetTimeHeld) _timeHeld = 0.0F;
        }

        public EnumHandling OnBeforeActiveSlotChanged(EntityAgent entity)
        {
            // If the player is carrying something in their hands,
            // prevent them from changing their active hotbar slot.
            return (entity.GetCarried(CarrySlot.Hands) != null)
                ? EnumHandling.PreventDefault
                : EnumHandling.PassThrough;
        }

        public void OnCarryKeyMessage(IServerPlayer player, CarryKeyMessage message){
            player.Entity.Api.Logger.VerboseDebug($"CarryKey: {player.PlayerName}={message.IsCarryKeyHeld}");
            player.Entity.SetCarryKeyHeld(message.IsCarryKeyHeld);
        }

        public void OnLockSlotsMessage(LockSlotsMessage message)
        {
            var player = CarrySystem.ClientAPI.World.Player;
            var hotbar = player.InventoryManager.GetHotbarInventory();
            for (var i = 0; i < hotbar.Count; i++)
            {
                if (message.HotbarSlots?.Contains(i) == true)
                    LockedItemSlot.Lock(hotbar[i]);
                else LockedItemSlot.Restore(hotbar[i]);
            }
        }

        public void SendLockSlotsMessage(IServerPlayer player)
        {
            var hotbar = player.InventoryManager.GetHotbarInventory();
            var slots = Enumerable.Range(0, hotbar.Count).Where(i => hotbar[i] is LockedItemSlot).ToList();
            CarrySystem.ServerChannel.SendPacket(new LockSlotsMessage(slots), player);
        }
        public static void SendLockSlotsMessage(EntityPlayer player)
        {
            if ((player == null) || (player.World.PlayerByUid(player.PlayerUID) is not IServerPlayer serverPlayer)) return;
            var system = player.World.Api.ModLoader.GetModSystem<CarrySystem>();
            system.CarryHandler.SendLockSlotsMessage(serverPlayer);
        }

        private void OnInteractMessage(IServerPlayer player, InteractMessage message)
        {
            var world = player.Entity.World;
            var block = world.BlockAccessor.GetBlock(message.Position);

            // Check block has interact behavior serverside
            if (block?.HasBlockBehavior<BlockBehaviorCarryableInteract>() == true)
            {
                var behavior = block.GetBehavior<BlockBehaviorCarryableInteract>();

                if (behavior.CanInteract(player))
                {
                    var blockSelection = player.CurrentBlockSelection.Clone();
                    blockSelection.Position = message.Position;
                    blockSelection.Block = block;
                    // TODO: add event hook here
                    block?.OnBlockInteractStart(world, player, blockSelection);
                }
            }
        }

        public void OnPickUpMessage(IServerPlayer player, PickUpMessage message)
        {
            // FIXME: Do at least some validation of this data.

            var carried = player.Entity.GetCarried(message.Slot);
            if ((message.Slot == CarrySlot.Back) || (carried != null) ||
                !CanInteract(player.Entity, true) ||
                !player.Entity.Carry(message.Position, message.Slot))
            {
                InvalidCarry(player, message.Position);
            }
        }

        public void OnPlaceDownMessage(IServerPlayer player, PlaceDownMessage message)
        {
            // FIXME: Do at least some validation of this data.

            var carried = player.Entity.GetCarried(message.Slot);
            if ((message.Slot == CarrySlot.Back) || (carried == null) ||
                !CanInteract(player.Entity, message.Slot != CarrySlot.Hands) ||
                !PlaceDown(player, carried, message.Selection, out var placedAt))
            {
                InvalidCarry(player, message.PlacedAt);
            }
            // If succeeded, but by chance the client's projected placement isn't
            // the same as the server's, re-sync the block at the client's position.
            else if (placedAt != message.PlacedAt)
            {
                player.Entity.World.BlockAccessor.MarkBlockDirty(message.PlacedAt);
            }
        }

        public void OnSwapSlotsMessage(IServerPlayer player, SwapSlotsMessage message)
        {
            if (!ModConfig.BackSlotEnabled) return;

            if ((message.First == message.Second) || (message.First != CarrySlot.Back) ||
                !CanInteract(player.Entity, true) ||
                !player.Entity.Swap(message.First, message.Second))
            {
                player.Entity.WatchedAttributes.MarkPathDirty(CarriedBlock.AttributeId);
            }
        }

        public void OnAttachMessage(IServerPlayer player, AttachMessage message)
        {
            CarrySystem.Api.World.Logger.Debug("OnAttach");
            // TODO: Why am I validating the target entity here? Is CurrentEntitySelection even valid on the server?
            var targetEntity = player.CurrentEntitySelection?.Entity;
            if (targetEntity != null && targetEntity.EntityId == message.TargetEntityId)
            {
                var attachableBehavior = targetEntity.GetBehavior<EntityBehaviorAttachable>();

                if (attachableBehavior != null)
                {

                    // Check player is carrying block
                    var carriedBlock = player.Entity.GetCarried(CarrySlot.Hands);

                    if (carriedBlock == null) return;

                    var blockEntityData = carriedBlock?.BlockEntityData;

                    if (blockEntityData == null) return; // Probably should log this

                    var type = blockEntityData.GetString("type");
                    var sourceInventory = blockEntityData.GetTreeAttribute("inventory");
                    var block = carriedBlock?.Block;

                    var targetSlot = attachableBehavior.GetSlotFromSelectionBoxIndex(message.SlotIndex);
                    var apname = targetEntity.GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes[message.SlotIndex]?.AttachPoint?.Code;

                    var seatableBehavior = targetEntity.GetBehavior<EntityBehaviorSeatable>();
                    bool isOccupied = false;
                    if (seatableBehavior != null){
                        var seatId = seatableBehavior.SeatConfigs.Where(s => s.APName == apname).FirstOrDefault()?.SeatId;
                        isOccupied = seatableBehavior.Seats.Where(s => s.SeatId == seatId).FirstOrDefault()?.Passenger != null;
                    }
                    

                    if (targetSlot == null || !targetSlot.Empty || isOccupied){
                        CarrySystem.ServerAPI.SendIngameError(player, "occupied", Lang.Get("Target slot is occupied"));
                        CarrySystem.Api.Logger.Log(EnumLogType.Debug, "Target Slot is occupied!");
                        return;
                    }

                    var sourceItemSlot = (ItemSlot)new DummySlot(null);
                    sourceItemSlot.Itemstack = carriedBlock.ItemStack.Clone();
                    TreeAttribute attr = sourceItemSlot.Itemstack.Attributes as TreeAttribute;

                    if (attr == null)
                    {
                        CarrySystem.ServerAPI.SendIngameError(player, "invalid", Lang.Get("Source item is invalid"));
                        CarrySystem.Api.Logger.Log(EnumLogType.Debug, "Source item is invalid!");
                        return;                        
                    }

                    attr.SetString("type", type);



                    var backpack = ConvertBlockInventoryToBackpack(blockEntityData.GetTreeAttribute("inventory"));

                   // backpack.SetAttribute("slots", slots);

                    attr.SetAttribute("backpack", backpack);



                    //var itemSlot = new ItemSlot(inv);
                    //itemSlot.Itemstack = new ItemStack(System.Api.World.GetBlock(3899), 1);

                    if (!targetSlot.CanTakeFrom(sourceItemSlot)) return;

                    var iai = sourceItemSlot.Itemstack.Collectible.GetCollectibleInterface<IAttachedInteractions>();
                    if (iai?.OnTryAttach(sourceItemSlot, message.SlotIndex, targetEntity) == false) return;

                    var ial = sourceItemSlot.Itemstack?.Collectible.GetCollectibleInterface<IAttachedListener>();

                    var moved = sourceItemSlot.TryPutInto(targetEntity.World, targetSlot) > 0;
                    if (moved)
                    {
                        attachableBehavior.storeInv();

                        targetEntity.MarkShapeModified();
                        targetEntity.World.BlockAccessor.GetChunkAtBlockPos(targetEntity.ServerPos.AsBlockPos).MarkModified();

                        // Remove held block from player
                        CarriedBlock.Remove(player.Entity, CarrySlot.Hands);
                        
                    }else{
                        CarrySystem.ServerAPI.SendIngameError(player, "unmoved", Lang.Get("Something went wrong attaching block"));
                    }
                }
            }
        }

        // TODO: Looks like backpack format may have changed, so this may not work as expected. Will cause crash if open container on boat when first slot is empty and others are not.
// Boat Container has no slots if empty
        public static ITreeAttribute ConvertBlockInventoryToBackpack(ITreeAttribute blockInventory)
        {
            if (blockInventory == null) return new TreeAttribute(); // graceful fallback

            var backpack = new TreeAttribute();
            var slotCount = blockInventory.GetAsInt("qslots");
            var slots = blockInventory.GetTreeAttribute("slots");

            // create backpack slots and copy items
            var backpackSlots = new TreeAttribute();
            for (int i = 0; i < slotCount; i++)
            {
                var slotKey = "slot-" + i;

                var itemstack = slots.GetItemstack(i.ToString());
                backpackSlots.SetItemstack(slotKey, itemstack);

            }

            backpack.SetAttribute("slots", backpackSlots);
            return backpack;
        }

        public static IAttribute ConvertBackpackToBlockInventory(ITreeAttribute backpack)
        {
            var inventory = new TreeAttribute();
            if (backpack != null)
            {
                var backpackSlots = backpack["slots"] as ITreeAttribute;
                var count = backpackSlots.Count();

                inventory.SetInt("qslots", count);
                var slotsAttribute = new TreeAttribute();

                for (var i = 0; i < count; i++)
                {
                    var value = backpackSlots.Values[i];
                    if (value?.GetValue() == null) continue;
                    slotsAttribute.SetAttribute(i.ToString(), value.Clone());
                }

                inventory.SetAttribute("slots", slotsAttribute);
            }

            return inventory;
        }

        public void OnDetachMessage(IServerPlayer player, DetachMessage message)
        {

            /* TODO
                - Validate message.SlotIndex against attachableBehavior.Inventory.Count.
                - Ensure the player is close enough and has line-of-sight if that is a design constraint.
                - Double-check sourceSlot?.Inventory is indeed part of targetEntity to avoid tampering.
                - Validate hotbar lock / slot cooldown
            */

            CarrySystem.Api.World.Logger.Debug("OnDetach");
            var targetEntity = player.CurrentEntitySelection?.Entity;
            if (targetEntity != null && targetEntity.EntityId == message.TargetEntityId)
            {
                var attachableBehavior = targetEntity.GetBehavior<EntityBehaviorAttachable>();

                if (attachableBehavior != null)
                {

                    var sourceSlot = attachableBehavior.GetSlotFromSelectionBoxIndex(message.SlotIndex);

                    if (!sourceSlot.CanTake()) return;

                    var block = sourceSlot?.Itemstack?.Block;
                    if (block == null) return;

                    if (!block.HasBehavior<BlockBehaviorCarryable>()) return;

                    // Player hands must be empty - active/offhand slot and carryon hands slot
                    // If active slot has an item then it will prevent block from being placed
                    var carriedBlock = player?.Entity?.GetCarried(CarrySlot.Hands);
                    if (carriedBlock != null) return;

                    //var inventory = player.InventoryManager.OpenedInventories.Find(f=>f.InventoryID == $"mountedbaginv-{message.SlotIndex}-{message.TargetEntityId}");
                    //                if(inventory != null) player.InventoryManager.CloseInventory(inventory);

                    var itemstack = sourceSlot?.Itemstack;

                    var sourceBackpack = itemstack?.Attributes?["backpack"] as ITreeAttribute;

                    var destInventory = ConvertBackpackToBlockInventory(sourceBackpack);

                    var blockEntityData = new TreeAttribute();
                    blockEntityData.SetString("blockCode", block.Code.ToShortString());
                    blockEntityData.SetAttribute("inventory", destInventory);
                    blockEntityData.SetString("forBlockCode", block.Code.ToShortString());
                    blockEntityData.SetString("type", itemstack.Attributes.GetString("type"));

                    // Clone itemstack and remove inventory attribute since it will be stored as blockdata
                    var itemstackCopy = itemstack.Clone();
                    itemstackCopy.Attributes.Remove("backpack");

                    carriedBlock = new CarriedBlock(CarrySlot.Hands, itemstackCopy, blockEntityData);
                    carriedBlock.Set(player.Entity, CarrySlot.Hands);

                    var sound = block?.Sounds.Place ?? new AssetLocation("sounds/player/build");
                    CarrySystem.Api.World.PlaySoundAt(sound, targetEntity, player, true, 16);


                    itemstack?.Collectible.GetCollectibleInterface<IAttachedListener>()?.OnDetached(sourceSlot, message.SlotIndex, targetEntity, player.Entity);

                    EntityBehaviorAttachableCarryable.ClearCachedSlotStorage(CarrySystem.Api, message.SlotIndex, sourceSlot, targetEntity);
                    sourceSlot.Itemstack = null;
                    attachableBehavior.storeInv();



                    targetEntity.MarkShapeModified();
                    targetEntity.World.BlockAccessor.GetChunkAtBlockPos(targetEntity.ServerPos.AsBlockPos).MarkModified();
                }

            }

        }
        public void OnQuickDropMessage(IServerPlayer player, QuickDropMessage message){
            CarrySlot[] fromHands = new []{CarrySlot.Hands, CarrySlot.Shoulder};

            player.Entity.DropCarried(fromHands, 1, 2);

        }

        /// <summary>
        /// Checks if entity can begin interaction with carryable item that is in the world or in hand slot
        /// Their left and right hands be empty.
        /// </summary>
        /// <param name="entityAgent"></param>
        /// <param name="requireEmptyHanded"></param>
        /// <returns></returns>
        public bool CanDoCarryAction(EntityAgent entityAgent, bool requireEmptyHanded)
        {
            var isEmptyHanded = entityAgent.RightHandItemSlot.Empty && entityAgent.LeftHandItemSlot.Empty;
            if (!isEmptyHanded && requireEmptyHanded) return false;

            if (entityAgent is not EntityPlayer entityPlayer) return true;

            // Active slot must be main hotbar (This excludes the backpack slots)
            var activeHotbarSlot = entityPlayer.Player.InventoryManager.ActiveHotbarSlotNumber;
            return (activeHotbarSlot >= 0) && (activeHotbarSlot < 10);
        }

        /// <summary>
        ///   Returns whether the specified entity has the required prerequisites
        ///   to interact using CarryOn: Must be sneaking with an empty hand.
        ///   Also tests for whether a valid hotbar slot is currently selected.
        /// </summary>
        public bool CanInteract(EntityAgent entityAgent, bool requireEmptyHanded)
        {
            if(entityAgent.Api.Side == EnumAppSide.Client){
                if(!IsCarryKeyPressed(true)){
                    return false;
                }
            }

            return CanDoCarryAction(entityAgent, requireEmptyHanded);
        }

        public bool PlaceDown(IPlayer player, CarriedBlock carried,
                                     BlockSelection selection, out BlockPos placedAt)
        {
            var clickedBlock = player.Entity.World.BlockAccessor.GetBlock(selection.Position);

            // Clone the selection, because we don't
            // want to affect what is sent to the server.
            selection = selection.Clone();

            if (clickedBlock.IsReplacableBy(carried.Block))
            {
                selection.Face = BlockFacing.UP;
                selection.HitPosition.Y = 0.5;
            }
            else
            {
                selection.Position.Offset(selection.Face);
                selection.DidOffset = true;
            }

            placedAt = selection.Position;
            return player.PlaceCarried(selection, carried.Slot);
        }

        /// <summary> Called when a player picks up or places down an invalid block,
        ///           requiring it to get notified about the action being rejected. </summary>
        private void InvalidCarry(IServerPlayer player, BlockPos pos)
        {
            player.Entity.World.BlockAccessor.MarkBlockDirty(pos);
            player.Entity.WatchedAttributes.MarkPathDirty(CarriedBlock.AttributeId);
            player.Entity.WatchedAttributes.MarkPathDirty("stats/walkspeed");
            SendLockSlotsMessage(player);
        }

        /// <summary> Returns the position that the specified block would
        ///           be placed at for the specified block selection. </summary>
        private static BlockPos GetPlacedPosition(
            IWorldAccessor world, BlockSelection selection, Block block)
        {
            if (selection == null) return null;
            var position = selection.Position.Copy();
            var clickedBlock = world.BlockAccessor.GetBlock(position);
            if (!clickedBlock.IsReplacableBy(block))
            {
                position.Offset(selection.Face);
                var replacedBlock = world.BlockAccessor.GetBlock(position);
                if (!replacedBlock.IsReplacableBy(block)) return null;
            }
            return position;
        }

        /// <summary>Get the block position for the main block within for a multiblock structure</summary>
        private BlockPos GetMultiblockOrigin(BlockPos position, BlockMultiblock multiblock)
        {
            if (position == null) return null;

            if (multiblock != null)
            {
                var multiPosition = position.Copy();
                multiPosition.Add(multiblock.OffsetInv);
                return multiPosition;
            }
            return position;
        }

        /// <summary>Create a new block selection pointing to the main block within a multiblock structure</summary>
        private BlockSelection GetMultiblockOriginSelection(BlockSelection blockSelection)
        {
            if (blockSelection?.Block is BlockMultiblock multiblock)
            {
                var world = CarrySystem.Api.World;
                var position = GetMultiblockOrigin(blockSelection.Position, multiblock);
                var block = world.BlockAccessor.GetBlock(position);
                var selection = blockSelection.Clone();
                selection.Position = position;
                selection.Block = block;

                return selection;
            }
            return blockSelection;
        }

        private enum CurrentAction
        {
            None,
            PickUp,
            PlaceDown,
            SwapBack,
            Interact
        }
    }
}
