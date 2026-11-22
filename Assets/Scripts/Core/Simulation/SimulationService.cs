using System;
using System.Diagnostics;
using Core.AppState;
using LogicSim.Simulation;
using LogicSim.Simulation.Jobs;

namespace Core.Simulation
{
    /// <summary>
    /// Drives the simulation in real time: schedules the frame's ticks on a worker thread in LateUpdate
    /// and collects the results at the start of the next frame (see <c>SimulationDriver</c>).
    /// </summary>
    public class SimulationService : IDisposable
    {
        public const double MinTicksPerSecond = 1;
        public const double MaxTicksPerSecond = 10_000_000;

        /// <summary>
        /// Simulation work allowed per frame. When a circuit cannot keep up with the requested speed, fewer ticks are
        /// scheduled so the app stays responsive; the simulation then runs slower than requested.
        /// </summary>
        public const double FrameBudgetSeconds = 1.0 / 30;

        private readonly AppStateService _appState;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _tickBudget;
        private bool _scheduled;
        private long _scheduledTicks;
        private double _scheduledAt;
        private double _maxTicksPerFrame;

        /// <summary>Ticks per second actually simulated recently; below <see cref="TicksPerSecond"/> when the circuit cannot keep up.</summary>
        public double ActualTicksPerSecond { get; private set; }

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
            _maxTicksPerFrame = Math.Max(1, ticksPerSecond / 60);
            Session = new SimulationSession(circuit, new JobsSimulationEngine());
            _appState.StateChanged += OnStateChanged;
        }

        private void OnStateChanged(ApplicationState state)
        {
            _tickBudget = 0;
            _paused = false;
            // Start low and let EndFrame raise the limit while the simulation keeps up.
            _maxTicksPerFrame = Math.Max(1, _ticksPerSecond / 60);
            ActualTicksPerSecond = _ticksPerSecond;
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
            // Cap the budget so a hitch does not trigger a huge catch-up, and so a slow circuit gets only as many
            // ticks as fit into the frame budget (the rest is dropped: the simulation falls behind real time).
            double limit = Math.Min(_ticksPerSecond * 0.25 + 1, _maxTicksPerFrame);
            _tickBudget = Math.Min(_tickBudget + deltaTime * _ticksPerSecond, limit);
            long ticks = (long)_tickBudget;
            if (deltaTime > 0)
                ActualTicksPerSecond += (Math.Max(0, ticks) / deltaTime - ActualTicksPerSecond) * 0.1;
            if (ticks <= 0)
                return;
            _tickBudget -= ticks;
            _scheduledTicks = ticks;
            _scheduledAt = _clock.Elapsed.TotalSeconds;
            Session.Schedule(ticks);
            _scheduled = true;
        }

        /// <summary>Collects the work started by <see cref="BeginFrame"/>.</summary>
        public void EndFrame()
        {
            if (!_scheduled)
                return;
            _scheduled = false;
            double waitStart = _clock.Elapsed.TotalSeconds;
            Session.Complete();
            double now = _clock.Elapsed.TotalSeconds;
            if (now - waitStart > 0.001)
            {
                // The frame waited for the simulation, which ran the whole time since it was scheduled: its speed
                // is known, so schedule only what fits into the budget.
                double ticksPerSecond = _scheduledTicks / Math.Max(1e-6, now - _scheduledAt);
                _maxTicksPerFrame = Math.Max(1, ticksPerSecond * FrameBudgetSeconds);
            }
            else if (_scheduledTicks >= (long)_maxTicksPerFrame)
            {
                // Limited but finished in time: allow more.
                _maxTicksPerFrame *= 1.5;
            }
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
