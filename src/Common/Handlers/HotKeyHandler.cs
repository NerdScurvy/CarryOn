using System;
using CarryOn.Common.Network;
using Vintagestory.API.Client;
using static CarryOn.CarrySystem;
using Vintagestory.API.Server;
using static CarryOn.API.Common.Models.CarryCode;
using CarryOn.API.Common.Models;


namespace CarryOn.Common.Handlers
{
    public class HotKeyHandler
    {
        private ICoreClientAPI clientApi;

        public ICoreClientAPI ClientApi => clientApi;

        private readonly CarrySystem carrySystem;

        public bool IsCarryOnEnabled => this.carrySystem.CarryOnEnabled;

        public HotKeyHandler(CarrySystem carrySystem)
        {
            if (carrySystem == null) throw new ArgumentNullException(nameof(carrySystem));
            this.carrySystem = carrySystem;
        }

        public void InitClient(ICoreClientAPI api)
        {
            this.clientApi = api ?? throw new ArgumentNullException(nameof(api));

            this.carrySystem.ClientChannel
                .RegisterMessageType<QuickDropMessage>()
                .RegisterMessageType<PlayerAttributeUpdateMessage>();



            var input = api.Input;

            input.RegisterHotKey(HotKeyCode.Toggle, GetLang("toggle-hotkey"), Default.FunctionKeybind, altPressed: true);
            input.RegisterHotKey(HotKeyCode.QuickDrop, GetLang("quickdrop-hotkey"), Default.FunctionKeybind);
            input.RegisterHotKey(HotKeyCode.QuickDropAll, GetLang("quickdropall-hotkey"), Default.FunctionKeybind, altPressed: true, ctrlPressed: true);
            input.RegisterHotKey(HotKeyCode.ToggleDoubleTapDismount, GetLang("toggle-double-tap-dismount-hotkey"), Default.FunctionKeybind, ctrlPressed: true);

            input.SetHotKeyHandler(HotKeyCode.Toggle, TriggerToggleKeyPressed);
            input.SetHotKeyHandler(HotKeyCode.QuickDrop, TriggerQuickDropKeyPressed);
            input.SetHotKeyHandler(HotKeyCode.QuickDropAll, TriggerQuickDropAllKeyPressed);
            input.SetHotKeyHandler(HotKeyCode.ToggleDoubleTapDismount, TriggerToggleDoubleTapDismountKeyPressed);

        }

        public void InitServer(ICoreServerAPI api)
        {
            this.carrySystem.ServerChannel
                .RegisterMessageType<QuickDropMessage>()
                .RegisterMessageType<PlayerAttributeUpdateMessage>()
                .SetMessageHandler<QuickDropMessage>(OnQuickDropMessage)
                .SetMessageHandler<PlayerAttributeUpdateMessage>(OnPlayerAttributeUpdateMessage);
        }

        private bool IsCursorActive()
        {
            return !ClientApi.Input.MouseGrabbed;
        }

        /// <summary>
        /// Triggers the action to toggle client side CarryOn behavior when the specified key combination is pressed.
        /// </summary>
        /// <param name="keyCombination"></param>
        /// <returns></returns>
        public bool TriggerToggleKeyPressed(KeyCombination keyCombination)
        {
            if (IsCursorActive()) return false;

            this.carrySystem.CarryOnEnabled = !IsCarryOnEnabled;
            ClientApi.ShowChatMessage(GetLang("carryon-" + (IsCarryOnEnabled ? "enabled" : "disabled")));
            return true;
        }

        /// <summary>
        /// Triggers the quick drop action when the specified key combination is pressed.
        /// </summary>
        /// <param name="keyCombination"></param>
        /// <returns></returns>
        public bool TriggerQuickDropKeyPressed(KeyCombination keyCombination)
        {
            if (IsCursorActive()) return false;

            // Send drop message even if client shows nothing being held
            this.carrySystem.ClientChannel.SendPacket(new QuickDropMessage() { CarrySlots = [CarrySlot.Hands] });
            return true;
        }


        /// <summary>
        /// Triggers the quick drop action when the specified key combination is pressed.
        /// </summary>
        /// <param name="keyCombination"></param>
        /// <returns></returns>
        public bool TriggerQuickDropAllKeyPressed(KeyCombination keyCombination)
        {
            if (IsCursorActive()) return false;

            if (ClientApi.World?.Player == null) return false;
            //if (ClientApi.OpenedGuis..IsDialogOpen == true || ClientApi.Gui?.IsChatOpen == true) return false;

            // Send drop message even if client shows nothing being held
            this.carrySystem.ClientChannel.SendPacket(new QuickDropMessage() { CarrySlots = [CarrySlot.Hands, CarrySlot.Back] });
            return true;
        }

        /// <summary>
        /// Triggers the double-tap dismount toggle when the specified key combination is pressed.
        /// </summary>
        /// <param name="keyCombination"></param>
        /// <returns></returns>
        private bool TriggerToggleDoubleTapDismountKeyPressed(KeyCombination keyCombination)
        {
            if (IsCursorActive()) return false;
            if (ClientApi?.World?.Player?.Entity == null) return false;
            var playerEntity = ClientApi.World.Player.Entity;
            var isEnabled = playerEntity.WatchedAttributes.GetBool(AttributeKey.Watched.EntityDoubleTapDismountEnabled, false);

            // Toggle the opposite state 
            playerEntity.WatchedAttributes.SetBool(AttributeKey.Watched.EntityDoubleTapDismountEnabled, !isEnabled);

            this.carrySystem.ClientChannel.SendPacket(new PlayerAttributeUpdateMessage(AttributeKey.Watched.EntityDoubleTapDismountEnabled, !isEnabled, true));

            ClientApi.ShowChatMessage(GetLang("double-tap-dismount-" + (!isEnabled ? "enabled" : "disabled")));
            return true;
        }

        /// <summary>
        /// Handles the quick drop action for a player.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        public void OnQuickDropMessage(IServerPlayer player, QuickDropMessage message)
        {
            carrySystem.CarryManager.DropCarried(player.Entity, message.CarrySlots, 2);
        }

        /// <summary>
        /// Handles player attribute updates.
        /// Currently only updates the double-tap dismount attribute which toggles the feature for the player.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        private void OnPlayerAttributeUpdateMessage(IServerPlayer player, PlayerAttributeUpdateMessage message)
        {
            var playerEntity = player.Entity;
            if (message.AttributeKey == null)
            {
                return;
            }

            if (message.AttributeKey == AttributeKey.Watched.EntityDoubleTapDismountEnabled && message.IsWatchedAttribute)
            {
                if (message.BoolValue.HasValue)
                {
                    playerEntity.WatchedAttributes.SetBool(message.AttributeKey, message.BoolValue.Value);
                }
                else
                {
                    playerEntity.WatchedAttributes.RemoveAttribute(message.AttributeKey);
                }

                return;
            }

            playerEntity.Api.Logger.Warning($"Received PlayerAttributeUpdateMessage with unknown attribute key: {message.AttributeKey}");
        }
    }
}