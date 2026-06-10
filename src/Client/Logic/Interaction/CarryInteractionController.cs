using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Client.Models;
using CarryOn.Common.Logic;
using CarryOn.Common.Models;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.Interaction
{
    public class CarryInteractionController : ICarryInteractionController
    {
        private readonly CarryInteractionValidator validator;
        private readonly CarryInteractionStateMachine stateMachine;

        public CarryInteraction Interaction => stateMachine.Interaction;
        public Vintagestory.Client.NoObf.HudElementInteractionHelp? HudHelp
        {
            get => stateMachine.HudHelp;
            set => stateMachine.HudHelp = value;
        }

        public bool RemoveInteractDelayWhileCarrying => validator.RemoveInteractDelayWhileCarrying;
        public bool AllowSprintWhileCarrying => validator.AllowSprintWhileCarrying;
        public bool BackSlotEnabled => validator.BackSlotEnabled;
        public float InteractSpeedMultiplier => validator.InteractSpeedMultiplier;

        public CarryInteractionController(ICoreClientAPI api, ICarryManager carryManager, IClientNetworkChannel clientChannel, CarryOnConfig config, Action hideOverlay, Action<float> setOverlayProgress, ClientModConfig? clientModConfig = null)
        {
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(carryManager);
            ArgumentNullException.ThrowIfNull(clientChannel);
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(hideOverlay);
            ArgumentNullException.ThrowIfNull(setOverlayProgress);

            var transferLogic = new TransferLogic(api, carryManager);
            validator = new CarryInteractionValidator(api, config, transferLogic, this);
            stateMachine = new CarryInteractionStateMachine(api, carryManager, clientChannel, hideOverlay, setOverlayProgress, validator, transferLogic, clientModConfig);
        }

        public void TryBeginInteraction(bool isInteracting, ref EnumHandling handled)
        {
            validator.TryBeginInteraction(isInteracting, ref handled);
        }

        public void TryContinueInteraction(float deltaTime)
        {
            stateMachine.TryContinueInteraction(deltaTime);
        }

        public void CancelInteraction(bool resetTimeHeld = false)
        {
            stateMachine.CancelInteraction(resetTimeHeld);
        }

        public void CompleteInteraction()
        {
            stateMachine.CompleteInteraction();
        }

        public void RefreshPlacedBlockInteractionHelp()
        {
            stateMachine.RefreshPlacedBlockInteractionHelp();
        }

        public void FlushPlacedBlockInteractionHelpRefresh()
        {
            stateMachine.FlushPlacedBlockInteractionHelpRefresh();
        }
    }
}