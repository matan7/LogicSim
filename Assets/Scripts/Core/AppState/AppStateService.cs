using System;

namespace Core.AppState
{
    public enum ApplicationState : byte
    {
        None = 0,
        Editor = 1,
        Simulator = 2
    }

    /// <summary>Editor (editing, simulation paused) or Simulator (running, editing locked).</summary>
    public class AppStateService
    {
        public ApplicationState State { get; private set; } = ApplicationState.Editor;

        public bool IsEditing => State == ApplicationState.Editor;
        public bool IsSimulating => State == ApplicationState.Simulator;

        public event Action<ApplicationState> StateChanged;

        public void SetState(ApplicationState state)
        {
            if (state == State)
                return;
            State = state;
            StateChanged?.Invoke(state);
        }

        public void Toggle()
        {
            SetState(IsSimulating ? ApplicationState.Editor : ApplicationState.Simulator);
        }
    }
}
