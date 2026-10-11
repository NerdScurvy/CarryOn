using System;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Models;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Logic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.Interaction
{
    public class CarryInteractionController : ICarryInteractionController
    {
        private readonly CarryInteractionValidator validator;
        private readonly CarryInteractionStateMachine stateMachine;

        public CarryInteraction Interaction => stateMachine.Interaction;
        internal Vintagestory.Client.NoObf.HudElementInteractionHelp? HudHelp
        {
            get => stateMachine.HudHelp;
            set => stateMachine.HudHelp = value;
        }

        public CarryInteractionController(ICoreClientAPI api, ICarryManager carryManager, IConfigProvider configProvider, IClientNetworkChannel clientChannel, Action hideOverlay, Action<float> setOverlayProgress, Action triggerHandsHighlight, Action triggerBackHighlight, ClientModConfig? clientModConfig = null)
        {
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(carryManager);
            ArgumentNullException.ThrowIfNull(configProvider);
            ArgumentNullException.ThrowIfNull(clientChannel);
            ArgumentNullException.ThrowIfNull(hideOverlay);
            ArgumentNullException.ThrowIfNull(setOverlayProgress);
            ArgumentNullException.ThrowIfNull(triggerHandsHighlight);
            ArgumentNullException.ThrowIfNull(triggerBackHighlight);

            var transferLogic = new TransferLogic(api, carryManager);
            validator = new CarryInteractionValidator(api, carryManager, configProvider, transferLogic, this);
            stateMachine = new CarryInteractionStateMachine(api, carryManager, clientChannel, hideOverlay, setOverlayProgress, triggerHandsHighlight, triggerBackHighlight, validator, clientModConfig);
        }

        public void TryBeginInteraction(bool isInteracting, ref EnumHandling handled)
        {
            var result = validator.TryBeginInteraction(isInteracting);

            if (result.PreventDefault)
            {
                handled = EnumHandling.PreventDefault;
            }

            switch (result.Outcome)
            {
                case BeginOutcome.Begin:
                    stateMachine.BeginInteraction(result.Interaction);
                    break;
                case BeginOutcome.Complete:
                    stateMachine.CompleteInteraction();
                    break;
            }
        }

        public void RefreshConfigCache()
        {
            validator.RefreshConfigCache();
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
