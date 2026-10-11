using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.Client.Logic;
using CarryOn.Client.Logic.Interaction;
using CarryOn.Common.Handlers.CarryHandlers;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Logic;
using CarryOn.Common.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Util;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Client.Handlers.CarryHandlers
{
    /// <summary>
    /// Client-side composition root for the carry interaction pipeline: owns the
    /// <see cref="CarryInteractionController"/> and <see cref="CarryClientHandler"/>,
    /// registers client network handlers and hotkeys, and forwards HUD-help/config wiring.
    /// </summary>
    public class CarryClientHandlers : IDisposable
    {
        private readonly ICarryManager carryManager;
        private readonly IConfigProvider configProvider;
        private readonly Func<bool> isCarryOnEnabled;

        private ICoreClientAPI? api;
        private CarryClientHandler? clientHandler;
        private bool lastSameKeyConflict;
        private bool lastComboConflict;
        private bool active;

        public CarryInteractionController InteractionLogic { get; private set; } = null!;

        internal CarryClientHandlers(ICarryManager carryManager, IConfigProvider configProvider, Func<bool> isCarryOnEnabled)
        {
            this.carryManager = carryManager;
            this.configProvider = configProvider;
            this.isCarryOnEnabled = isCarryOnEnabled;
        }

        public void Init(
            ICoreClientAPI api,
            IClientNetworkChannel clientChannel,
            Action hideOverlay,
            Action<float> setOverlayProgress,
            Action triggerHandsHighlight,
            Action triggerBackHighlight,
            ClientModConfig? clientModConfig = null)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));

            if (api.Input == null)
                throw new InvalidOperationException("CarryClientHandlers.Init requires Input to be initialized.");

            ArgumentNullException.ThrowIfNull(clientChannel);
            ArgumentNullException.ThrowIfNull(hideOverlay);
            ArgumentNullException.ThrowIfNull(setOverlayProgress);
            ArgumentNullException.ThrowIfNull(triggerHandsHighlight);
            ArgumentNullException.ThrowIfNull(triggerBackHighlight);

            var interactionLogic = new CarryInteractionController(
                api, carryManager, configProvider, clientChannel,
                hideOverlay, setOverlayProgress, triggerHandsHighlight, triggerBackHighlight, clientModConfig);
            InteractionLogic = interactionLogic;

            clientHandler = new CarryClientHandler(carryManager, configProvider, isCarryOnEnabled);
            clientHandler.Init(api, interactionLogic);

            CarryNetworkSetup.RegisterClient(clientChannel)
                .SetMessageHandler<LockSlotsMessage>(clientHandler.OnLockSlotsMessage);

            api.Input.RegisterHotKey(HotKeyCodes.Pickup, LocalizationHelper.GetLang("pickup-hotkey"), Defaults.PickupKeybind);
            api.Input.RegisterHotKey(HotKeyCodes.SwapBackModifier, LocalizationHelper.GetLang("swap-back-hotkey"), Defaults.SwapBackModifierKeybind);

            active = true;
            ValidateKeybinds();
            Vintagestory.Client.NoObf.ClientSettings.Inst.AddKeyCombinationUpdatedWatcher(OnKeyMappingUpdated);
        }

        /// <summary>
        /// Validates the carry-critical keybindings (Pick Up and Swap to Back) and warns the
        /// player about configurations CarryOn cannot support:
        /// 1. Both bound to the same primary key - makes carry and swap back indistinguishable.
        /// 2. Bound as a multi-key combo (modifier flags set) - CarryOn's input polling reads only
        ///    the primary key, so a combo would be silently half-detected.
        /// Runs on startup and whenever a key mapping changes (see <see cref="OnKeyMappingUpdated"/>).
        /// Chat messages are only shown on state transitions so a persistent conflict toasts once,
        /// not on every change.
        /// </summary>
        private void ValidateKeybinds()
        {
            if (!active) return;

            var input = api?.Input;
            if (input == null) return;

            var pickup = input.HotKeys.Get(HotKeyCodes.Pickup);
            var swapBack = input.HotKeys.Get(HotKeyCodes.SwapBackModifier);
            if (pickup?.CurrentMapping == null || swapBack?.CurrentMapping == null) return;

            var pickupMapping = pickup.CurrentMapping;
            var swapBackMapping = swapBack.CurrentMapping;

            bool sameKeyConflict = pickupMapping.KeyCode == swapBackMapping.KeyCode;
            bool comboConflict = pickupMapping.Ctrl || pickupMapping.Shift || pickupMapping.Alt
                || swapBackMapping.Ctrl || swapBackMapping.Shift || swapBackMapping.Alt;

            if (sameKeyConflict && !lastSameKeyConflict)
                api?.ShowChatMessage(LocalizationHelper.GetLang("keybind-conflict-same-key"));
            if (comboConflict && !lastComboConflict)
                api?.ShowChatMessage(LocalizationHelper.GetLang("keybind-single-key-only"));

            if (sameKeyConflict)
                api?.Logger.Warning("CarryOn: Carry and Swap Back keys are both bound to the same key (" + pickupMapping + ")");
            if (comboConflict)
                api?.Logger.Warning("CarryOn: Carry or Swap Back key is bound as a multi-key combo which is not supported: " + pickupMapping + " / " + swapBackMapping);

            lastSameKeyConflict = sameKeyConflict;
            lastComboConflict = comboConflict;
        }

        /// <summary>
        /// Called by <c>ClientSettings</c> whenever any key mapping is changed (fired from
        /// <c>SetKeyMapping</c> inside the Controls dialog for every rebind). The game's own
        /// <c>Event.HotkeysChanged</c> only fires for a handful of built-in keys (Sneak/Sprint/
        /// Shift/Ctrl-clicks, mouse buttons), never for a mod hotkey rebind, so this watcher is
        /// the reliable event-driven way to re-validate carry keys on in-game rebind.
        /// </summary>
        private void OnKeyMappingUpdated(string key, KeyCombination value)
        {
            if (key != HotKeyCodes.Pickup && key != HotKeyCodes.SwapBackModifier) return;
            ValidateKeybinds();
        }

        internal void SetHudHelp(Vintagestory.Client.NoObf.HudElementInteractionHelp? hudHelp)
        {
            clientHandler?.SetHudHelp(hudHelp);
        }

        public void RefreshConfigCache()
        {
            clientHandler?.RefreshConfigCache();
        }

        public void Dispose()
        {
            active = false;
            api = null;

            clientHandler?.Dispose();
            clientHandler = null;
        }
    }
}
