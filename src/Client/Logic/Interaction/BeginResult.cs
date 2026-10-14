using CarryOn.Common.Models;

namespace CarryOn.Client.Logic.Interaction
{
    /// <summary>
    /// Outcome of a begin-interaction validation pass. The validator produces a decision
    /// (handle, complete, or begin a specific interaction) that the controller mediates.
    /// </summary>
    internal enum BeginOutcome
    {
        /// <summary> The stage did not handle the event; try the next candidate. </summary>
        NotHandled,

        /// <summary> The event was consumed (optionally preventing the default) without starting an interaction. </summary>
        Handled,

        /// <summary> The event was consumed and any in-progress interaction is completed (an error was shown). </summary>
        Complete,

        /// <summary> The event was consumed and the described interaction begins. </summary>
        Begin
    }

    internal sealed class BeginResult
    {
        public static readonly BeginResult NotHandled = new(BeginOutcome.NotHandled);

        public static BeginResult Handled(bool preventDefault = true) => new(BeginOutcome.Handled, preventDefault);

        public static BeginResult Complete(bool preventDefault = true) => new(BeginOutcome.Complete, preventDefault);

        public static BeginResult Begin(CarryInteraction interaction, bool preventDefault = true)
            => new(BeginOutcome.Begin, preventDefault, interaction);

        public BeginOutcome Outcome { get; }

        public bool PreventDefault { get; }

        public CarryInteraction? Interaction { get; }

        private BeginResult(BeginOutcome outcome, bool preventDefault = false, CarryInteraction? interaction = null)
        {
            Outcome = outcome;
            PreventDefault = preventDefault;
            Interaction = interaction;
        }
    }
}
