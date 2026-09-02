using System;

namespace YUIFramework
{
    internal sealed class UIContextStateMachine
    {
        public UIContextState State { get; private set; } = UIContextState.Unloaded;
        public Exception LastFailure { get; private set; }

        public event Action<UIContextState, UIContextState> StateChanged;

        public void TransitionTo(UIContextState next)
        {
            if (State == next)
            {
                return;
            }

            if (!UIContextStateGraph.CanTransition(State, next))
            {
                throw new InvalidOperationException(
                    $"Invalid UI lifecycle transition: {State} -> {next}.");
            }

            var previous = State;
            State = next;
            StateChanged?.Invoke(previous, next);
        }

        public void RecordFailure(Exception exception, bool enterFaultedState)
        {
            LastFailure = exception ?? throw new ArgumentNullException(nameof(exception));
            if (enterFaultedState)
            {
                TransitionTo(UIContextState.Faulted);
            }
        }
    }
}
