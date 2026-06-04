using CarryOn.Common.Models;

namespace CarryOn.Client.Logic.Interaction
{
    internal interface ICarryInteractionController
    {
        CarryInteraction Interaction { get; }
        void CancelInteraction(bool resetTimeHeld = false);
        void CompleteInteraction();
    }
}