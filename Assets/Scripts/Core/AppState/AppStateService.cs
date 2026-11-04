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

        /// <summary>Raised before the state changes (e.g. to write back open IC edits before the simulation starts).</summary>
        public event Action<ApplicationState> StateChanging;

        public event Action<ApplicationState> StateChanged;

        public void SetState(ApplicationState state)
        {
            if (state == State)
                return;
            StateChanging?.Invoke(state);
            State = state;
            StateChanged?.Invoke(state);
        }

        public void Toggle()
        {
            SetState(IsSimulating ? ApplicationState.Editor : ApplicationState.Simulator);
        }
    }
}
