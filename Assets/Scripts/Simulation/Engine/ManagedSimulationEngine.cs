using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LogicSim.Simulation
{
    /// <summary>
    /// Single-threaded backend that runs <see cref="SimulationKernel"/> on pinned managed arrays.
    /// Used outside Unity (headless MCP server, tests) and as a fallback where jobs are unavailable.
    /// </summary>
    public sealed unsafe class ManagedSimulationEngine : ISimulationEngine
    {
        private CompiledCircuit _circuit;
        private byte[] _values;
        private byte[] _sweepBuffer;
        private byte[] _gateState;
        private byte[] _inputs;
        private long[] _stamp;
        private int[] _active;
        private int[] _nextActive;
        private int[] _stageNet;
        private byte[] _stageValue;

        private readonly List<GCHandle> _handles = new List<GCHandle>();
        private KernelData _data;
        private KernelStatus _status;
        private bool _needsFullEvaluation;
        private bool _lastSweepChanged;

        public string Name => "Managed";
        public SimulationMode Mode { get; set; } = SimulationMode.Auto;
        public SimulationMode ActiveMode { get; private set; } = SimulationMode.EventDriven;
        public CompiledCircuit Circuit => _circuit;
        public long Tick => _status.Tick;
        public bool IsStable => ActiveMode == SimulationMode.FullSweep ? !_lastSweepChanged : _status.ActiveCount == 0;
        public long TotalEvaluations => _status.Evaluations;
        public bool IsRunning => false;

        /// <summary>Gates evaluated by the last run.</summary>
        public long LastRunEvaluations => _status.LastRunEvaluations;

        public void Load(CompiledCircuit circuit)
        {
            if (circuit == null)
                throw new ArgumentNullException(nameof(circuit));
            ReleaseHandles();
            _circuit = circuit;

            int gates = circuit.GateCount;
            int nets = circuit.NetCount;
            _values = new byte[nets];
            _sweepBuffer = new byte[nets];
            _gateState = new byte[gates];
            _inputs = new byte[circuit.InputSlotCount];
            _stamp = new long[gates];
            _active = new int[gates];
            _nextActive = new int[gates];
            _stageNet = new int[gates * 2];
            _stageValue = new byte[gates * 2];

            _data = new KernelData
            {
                GateCount = gates,
                NetCount = nets,
                ClockCount = circuit.ClockGates.Length,
                Op = (byte*)Pin(circuit.Op),
                Param = (int*)Pin(circuit.Param),
                InStart = (int*)Pin(circuit.InStart),
                InNets = (int*)Pin(circuit.InNets),
                Out0 = (int*)Pin(circuit.Out0),
                Out1 = (int*)Pin(circuit.Out1),
                FanoutStart = (int*)Pin(circuit.FanoutStart),
                FanoutGates = (int*)Pin(circuit.FanoutGates),
                ClockGates = (int*)Pin(circuit.ClockGates),
                Inputs = (byte*)Pin(_inputs),
                GateState = (byte*)Pin(_gateState),
                Values = (byte*)Pin(_values),
                Active = (int*)Pin(_active),
                NextActive = (int*)Pin(_nextActive),
                Stamp = (long*)Pin(_stamp),
                StageNet = (int*)Pin(_stageNet),
                StageValue = (byte*)Pin(_stageValue)
            };
            Pin(_sweepBuffer);
            Reset();
        }

        public void Reset()
        {
            EnsureLoaded();
            Array.Clear(_values, 0, _values.Length);
            Array.Clear(_sweepBuffer, 0, _sweepBuffer.Length);
            Array.Clear(_gateState, 0, _gateState.Length);
            Array.Copy(_circuit.InitialInputs, _inputs, _inputs.Length);
            for (int i = 0; i < _stamp.Length; i++)
                _stamp[i] = -1;
            _status = default;
            _lastSweepChanged = true;
            _needsFullEvaluation = false;
            SimulationKernel.InitializeState(ref _data);
            Array.Copy(_values, _sweepBuffer, _values.Length);
            SimulationKernel.ActivateAll(ref _data, ref _status);
        }

        public void SetInput(int slot, bool value)
        {
            EnsureLoaded();
            if (slot < 0 || slot >= _inputs.Length)
                throw new ArgumentOutOfRangeException(nameof(slot));
            byte v = value ? (byte)1 : (byte)0;
            if (_inputs[slot] == v)
                return;
            _inputs[slot] = v;
            _lastSweepChanged = true;
            // A sweep evaluates every gate anyway; the pending list is rebuilt when returning to event-driven mode.
            if (!_needsFullEvaluation && Mode != SimulationMode.FullSweep)
                SimulationKernel.Activate(ref _data, ref _status, _circuit.InputSlotGate[slot]);
            else
                _needsFullEvaluation = true;
        }

        public bool GetInput(int slot) => _inputs[slot] != 0;

        public bool GetNet(int net)
        {
            if (net < 0 || net >= _values.Length)
                return false;
            return _values[net] != 0;
        }

        public void Schedule(long ticks)
        {
            EnsureLoaded();
            if (ticks <= 0)
                return;

            if (Mode == SimulationMode.FullSweep)
            {
                ActiveMode = SimulationMode.FullSweep;
                RunSweep(ticks);
                _needsFullEvaluation = true;
            }
            else
            {
                ActiveMode = SimulationMode.EventDriven;
                PrepareEventDriven();
                SimulationKernel.RunEventDriven(ref _data, ref _status, ticks, false);
            }
        }

        public void Complete()
        {
        }

        public bool Settle(long maxTicks)
        {
            EnsureLoaded();
            ActiveMode = SimulationMode.EventDriven;
            PrepareEventDriven();
            SimulationKernel.RunEventDriven(ref _data, ref _status, maxTicks, true);
            return _status.ActiveCount == 0;
        }

        private void PrepareEventDriven()
        {
            if (!_needsFullEvaluation)
                return;
            // After sweeping, the pending list is unknown: evaluating every gate once is exactly one sweep tick.
            SimulationKernel.ActivateAll(ref _data, ref _status);
            _needsFullEvaluation = false;
        }

        private void RunSweep(long ticks)
        {
            fixed (byte* a = _values)
            fixed (byte* b = _sweepBuffer)
            {
                byte* current = a;
                byte* next = b;
                bool changed = false;
                for (long i = 0; i < ticks; i++)
                {
                    changed = SimulationKernel.SweepTick(ref _data, current, next, _status.Tick);
                    _status.Tick++;
                    _status.Evaluations += _data.GateCount;
                    byte* swap = current;
                    current = next;
                    next = swap;
                }
                if (current != a)
                    Buffer.MemoryCopy(current, a, _values.Length, _values.Length);
                _lastSweepChanged = changed;
                _status.LastRunEvaluations = ticks * _data.GateCount;
            }
        }

        private IntPtr Pin(Array array)
        {
            var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
            _handles.Add(handle);
            return handle.AddrOfPinnedObject();
        }

        private void EnsureLoaded()
        {
            if (_circuit == null)
                throw new InvalidOperationException("No circuit loaded");
        }

        private void ReleaseHandles()
        {
            foreach (var handle in _handles)
                if (handle.IsAllocated)
                    handle.Free();
            _handles.Clear();
        }

        public void Dispose()
        {
            ReleaseHandles();
            _circuit = null;
        }
    }
}
