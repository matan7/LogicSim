using System;
using Core.AppState;
using LogicSim.Simulation;
using LogicSim.Simulation.Jobs;

namespace Core.Simulation
{
    /// <summary>
    /// Drives the simulation in real time: schedules the frame's ticks on a worker thread in Update
    /// and collects the results in LateUpdate (see <c>SimulationDriver</c>).
    /// </summary>
    public class SimulationService : IDisposable
    {
        public const double MinTicksPerSecond = 1;
        public const double MaxTicksPerSecond = 10_000_000;

        private readonly AppStateService _appState;
        private double _tickBudget;

        public SimulationSession Session { get; }
        public ISimulationEngine Engine => Session.Engine;

        public bool IsRunning => _appState.IsSimulating;

        private double _ticksPerSecond;

        public double TicksPerSecond
        {
            get => _ticksPerSecond;
            set
            {
                _ticksPerSecond = Math.Max(MinTicksPerSecond, Math.Min(MaxTicksPerSecond, value));
                SpeedChanged?.Invoke();
            }
        }

        /// <summary>While simulating, stops the clock so the circuit can be stepped tick by tick.</summary>
        public bool Paused
        {
            get => _paused;
            set
            {
                if (_paused == value)
                    return;
                _paused = value;
                _tickBudget = 0;
                SpeedChanged?.Invoke();
            }
        }

        private bool _paused;

        /// <summary>Raised after new values are available (every simulated frame, step or reset).</summary>
        public event Action ValuesUpdated;

        public event Action SpeedChanged;

        public SimulationService(Circuit circuit, AppStateService appState, double ticksPerSecond)
        {
            _appState = appState;
            _ticksPerSecond = ticksPerSecond;
            Session = new SimulationSession(circuit, new JobsSimulationEngine());
            _appState.StateChanged += OnStateChanged;
        }

        private void OnStateChanged(ApplicationState state)
        {
            _tickBudget = 0;
            _paused = false;
            if (state == ApplicationState.Simulator)
            {
                // Start from power-on state each time the simulation is entered.
                Session.Reset();
            }
            ValuesUpdated?.Invoke();
        }

        /// <summary>Starts this frame's work. Does nothing while editing.</summary>
        public void BeginFrame(float deltaTime)
        {
            if (!IsRunning || _paused)
                return;
            // Cap the budget so a hitch does not trigger a huge catch-up.
            _tickBudget = Math.Min(_tickBudget + deltaTime * _ticksPerSecond, _ticksPerSecond * 0.25 + 1);
            long ticks = (long)_tickBudget;
            if (ticks <= 0)
                return;
            _tickBudget -= ticks;
            Session.Schedule(ticks);
        }

        public void EndFrame()
        {
            Session.Complete();
            if (IsRunning)
                ValuesUpdated?.Invoke();
        }

        public void Step(long ticks)
        {
            Session.Run(ticks);
            ValuesUpdated?.Invoke();
        }

        public void Reset()
        {
            Session.Reset();
            ValuesUpdated?.Invoke();
        }

        /// <summary>Notifies views after values changed outside the frame loop (e.g. MCP calls).</summary>
        public void NotifyValuesChanged() => ValuesUpdated?.Invoke();

        public void Dispose()
        {
            _appState.StateChanged -= OnStateChanged;
            Session.Dispose();
        }
    }
}
