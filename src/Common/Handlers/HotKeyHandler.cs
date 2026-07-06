using System;
using CarryOn.Client.Models;
using CarryOn.Common.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Logic;
using static CarryOn.Common.Models.CarryCode;

namespace CarryOn.Common.Handlers
{
    /// <summary>
    /// Handles hotkey registrations and actions for toggling CarryOn behavior and quick dropping carried blocks.
    /// </summary>
    public class HotKeyHandler(ICarryManager carryManager)
    {
        private ICoreClientAPI? clientApi;
        private IClientNetworkChannel? clientChannel;
        private ClientModConfig? clientModConfig;

        public ICoreClientAPI? ClientApi => clientApi;

        public void InitClient(ICoreClientAPI api, IClientNetworkChannel clientChannel, ClientModConfig clientModConfig)
        {
            this.clientApi = api ?? throw new ArgumentNullException(nameof(api));
            this.clientChannel = clientChannel ?? throw new ArgumentNullException(nameof(clientChannel));
            this.clientModConfig = clientModConfig ?? throw new ArgumentNullException(nameof(clientModConfig));

            clientChannel
                .RegisterMessageType<QuickDropMessage>()
                .RegisterMessageType<PlayerAttributeUpdateMessage>();

            var input = api.Input;

            input.RegisterHotKey(HotKeyCode.Toggle, LocalizationHelper.GetLang("toggle-hotkey"), Default.FunctionKeybind, altPressed: true);
            input.RegisterHotKey(HotKeyCode.QuickDrop, LocalizationHelper.GetLang("quickdrop-hotkey"), Default.FunctionKeybind);
            input.RegisterHotKey(HotKeyCode.QuickDropAll, LocalizationHelper.GetLang("quickdropall-hotkey"), Default.FunctionKeybind, altPressed: true, ctrlPressed: true);
            input.RegisterHotKey(HotKeyCode.ToggleDoubleTapDismount, LocalizationHelper.GetLang("toggle-double-tap-dismount-hotkey"), Default.FunctionKeybind, ctrlPressed: true);

            input.SetHotKeyHandler(HotKeyCode.Toggle, TriggerToggleKeyPressed);
            input.SetHotKeyHandler(HotKeyCode.QuickDrop, TriggerQuickDropKeyPressed);
            input.SetHotKeyHandler(HotKeyCode.QuickDropAll, TriggerQuickDropAllKeyPressed);
            input.SetHotKeyHandler(HotKeyCode.ToggleDoubleTapDismount, TriggerToggleDoubleTapDismountKeyPressed);

        }

        public void InitServer(ICoreServerAPI api, IServerNetworkChannel serverChannel)
        {
            ArgumentNullException.ThrowIfNull(serverChannel);

            serverChannel
                .RegisterMessageType<QuickDropMessage>()
                .RegisterMessageType<PlayerAttributeUpdateMessage>()
                .SetMessageHandler<QuickDropMessage>(OnQuickDropMessage)
                .SetMessageHandler<PlayerAttributeUpdateMessage>(OnPlayerAttributeUpdateMessage);
        }

        /// <summary>
        /// Checks if the cursor is active (not grabbed) to prevent hotkey actions from triggering while the player is interacting with UI.
        /// </summary>
        /// <returns> True if the cursor is active, false otherwise. </returns>
        private bool IsCursorActive()
        {
            return clientApi?.Input != null && !clientApi.Input.MouseGrabbed;
        }

        /// <summary>
        /// Triggers the action to toggle client side CarryOn behavior when the specified key combination is pressed.
        /// </summary>
        /// <param name="keyCombination"> The key combination that was pressed. </param>
        /// <returns> True if the action was successfully triggered, false otherwise. </returns>
        public bool TriggerToggleKeyPressed(KeyCombination keyCombination)
        {
            var api = clientApi;
            if (api == null || IsCursorActive()) return false;
            var config = this.clientModConfig?.Config;

            config?.CarryOnEnabled = !config.CarryOnEnabled;
            api.ShowChatMessage(LocalizationHelper.GetLang("carryon-" + (config?.CarryOnEnabled == true ? "enabled" : "disabled")));
            return true;
        }

        /// <summary>
        /// Triggers the quick drop action when the specified key combination is pressed.
        /// </summary>
        /// <param name="keyCombination"> The key combination that was pressed. </param>
        /// <returns> True if the action was successfully triggered, false otherwise. </returns>
        public bool TriggerQuickDropKeyPressed(KeyCombination keyCombination)
        {
            if (clientApi == null || IsCursorActive()) return false;
            var clientChannel = this.clientChannel;
            if (clientChannel == null) return false;

            // Send drop message even if client shows nothing being held
            clientChannel.SendPacket(new QuickDropMessage(carrySlots: [CarrySlot.Hands]));
            return true;
        }


        /// <summary>
        /// Triggers the quick drop all action when the specified key combination is pressed.
        /// </summary>
        /// <param name="keyCombination"> The key combination that was pressed. </param>
        /// <returns> True if the action was successfully triggered, false otherwise. </returns>
        public bool TriggerQuickDropAllKeyPressed(KeyCombination keyCombination)
        {
            var api = clientApi;
            if (api == null || IsCursorActive()) return false;
            var clientChannel = this.clientChannel;
            if (clientChannel == null) return false;

            if (api.World?.Player == null) return false;

            // Send drop message even if client shows nothing being held
            clientChannel.SendPacket(new QuickDropMessage(carrySlots: [CarrySlot.Hands, CarrySlot.Back]));
            return true;
        }

        /// <summary>
        /// Triggers the double-tap dismount toggle when the specified key combination is pressed.
        /// </summary>
        /// <param name="keyCombination"> The key combination that was pressed. </param>
        /// <returns> True if the action was successfully triggered, false otherwise. </returns>
        private bool TriggerToggleDoubleTapDismountKeyPressed(KeyCombination keyCombination)
        {
            var api = clientApi;
            if (api == null || IsCursorActive()) return false;
            var clientChannel = this.clientChannel;
            if (clientChannel == null) return false;
            if (api.World?.Player?.Entity == null) return false;
            var playerEntity = api.World.Player.Entity;
            var isEnabled = playerEntity.WatchedAttributes.GetBool(AttributeKey.Watched.EntityDoubleTapDismountEnabled, false);

            // Toggle the opposite state 
            playerEntity.WatchedAttributes.SetBool(AttributeKey.Watched.EntityDoubleTapDismountEnabled, !isEnabled);

            clientChannel.SendPacket(new PlayerAttributeUpdateMessage(AttributeKey.Watched.EntityDoubleTapDismountEnabled, !isEnabled, true));

            api.ShowChatMessage(LocalizationHelper.GetLang("double-tap-dismount-" + (!isEnabled ? "enabled" : "disabled")));
            return true;
        }

        /// <summary>
        /// Handles the quick drop action for a player.
        /// </summary>
        /// <param name="player"> The player who triggered the quick drop action. </param>
        /// <param name="message"> The message containing the quick drop details. </param>
        public void OnQuickDropMessage(IServerPlayer player, QuickDropMessage message)
        {
            var entity = player?.Entity;
            var carrySlots = message?.CarrySlots;
            if (entity == null || carrySlots == null) return;

            carryManager.DropCarried(entity, carrySlots, 2);
        }

        /// <summary>
        /// Handles player attribute updates.
        /// Currently only updates the double-tap dismount attribute which toggles the feature for the player.
        /// </summary>
        /// <param name="player"> The player whose attributes are being updated. </param>
        /// <param name="message"> The message containing the attribute update details. </param>
        private void OnPlayerAttributeUpdateMessage(IServerPlayer player, PlayerAttributeUpdateMessage message)
        {
            var playerEntity = player.Entity;
            if (playerEntity == null) return;
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