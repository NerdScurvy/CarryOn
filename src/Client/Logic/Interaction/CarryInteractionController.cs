using System;
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

        public CarryInteractionController(ICoreClientAPI api, CarrySystem carrySystem)
        {
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(carrySystem);

            var transferLogic = new TransferLogic(api, carrySystem);
            validator = new CarryInteractionValidator(api, carrySystem, transferLogic, this);
            stateMachine = new CarryInteractionStateMachine(api, carrySystem, validator, transferLogic);
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