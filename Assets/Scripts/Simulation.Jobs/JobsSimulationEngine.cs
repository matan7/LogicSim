using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LogicSim.Simulation.Jobs
{
    /// <summary>
    /// C# Job System backend (Burst-compiled when the Burst package is installed).
    /// Typical circuits run event-driven in one worker job per frame; very large and very active circuits
    /// switch to a full sweep that is split over all worker threads.
    /// </summary>
    public sealed unsafe class JobsSimulationEngine : ISimulationEngine
    {
        /// <summary>Gate count from which the parallel sweep may be used in <see cref="SimulationMode.Auto"/>.</summary>
        public int ParallelSweepMinGates { get; set; } = 40_000;

        /// <summary>Average fraction of gates active per tick from which a full sweep is cheaper.</summary>
        public float ParallelSweepActivity { get; set; } = 0.2f;

        /// <summary>Gates per parallel work item.</summary>
        public int ChunkSize { get; set; } = 2048;

        private CompiledCircuit _circuit;
        private NetlistArrays _netlist;
        private NativeArray<byte> _inputs;
        private NativeArray<byte> _gateState;
        private NativeArray<byte> _values;
        private NativeArray<byte> _sweepBuffer;
        private NativeArray<long> _stamp;
        private NativeArray<int> _active;
        private NativeArray<int> _nextActive;
        private NativeArray<int> _stageNet;
        private NativeArray<byte> _stageValue;
        private NativeArray<KernelStatus> _status;
        private NativeArray<byte> _sweepChanged;

        private JobHandle _handle;
        private bool _running;
        private bool _needsFullEvaluation;
        private bool _lastSweepChanged = true;
        private long _pendingSweepTicks;
        private int _framesInSweep;
        private readonly List<KeyValuePair<int, bool>> _pendingInputs = new List<KeyValuePair<int, bool>>();
        private KernelStatus _cachedStatus;

        public string Name =>
#if LOGICSIM_BURST
            "Jobs + Burst";
#else
            "Jobs";
#endif

        public SimulationMode Mode { get; set; } = SimulationMode.Auto;
        public SimulationMode ActiveMode { get; private set; } = SimulationMode.EventDriven;
        public CompiledCircuit Circuit => _circuit;
        public bool IsRunning => _running;

        // Status getters report the last completed run and never block on a running job.
        public long Tick => _cachedStatus.Tick;

        public bool IsStable
        {
            get
            {
                if (_pendingInputs.Count > 0)
                    return false;
                return ActiveMode == SimulationMode.FullSweep ? !_lastSweepChanged : _cachedStatus.ActiveCount == 0;
            }
        }

        public long TotalEvaluations => _cachedStatus.Evaluations;

        public void Load(CompiledCircuit circuit)
        {
            if (circuit == null)
                throw new ArgumentNullException(nameof(circuit));
            Complete();
            DisposeArrays();
            _circuit = circuit;

            _netlist = new NetlistArrays
            {
                GateCount = circuit.GateCount,
                NetCount = circuit.NetCount,
                ClockCount = circuit.ClockGates.Length,
                Op = Copy(circuit.Op),
                Param = Copy(circuit.Param),
                InStart = Copy(circuit.InStart),
                InNets = Copy(circuit.InNets),
                Out0 = Copy(circuit.Out0),
                Out1 = Copy(circuit.Out1),
                FanoutStart = Copy(circuit.FanoutStart),
                FanoutGates = Copy(circuit.FanoutGates),
                ClockGates = Copy(circuit.ClockGates)
            };

            int gates = circuit.GateCount;
            _inputs = new NativeArray<byte>(Math.Max(1, circuit.InputSlotCount), Allocator.Persistent);
            _gateState = new NativeArray<byte>(Math.Max(1, gates), Allocator.Persistent);
            _values = new NativeArray<byte>(circuit.NetCount, Allocator.Persistent);
            _sweepBuffer = new NativeArray<byte>(circuit.NetCount, Allocator.Persistent);
            _stamp = new NativeArray<long>(Math.Max(1, gates), Allocator.Persistent);
            _active = new NativeArray<int>(Math.Max(1, gates), Allocator.Persistent);
            _nextActive = new NativeArray<int>(Math.Max(1, gates), Allocator.Persistent);
            _stageNet = new NativeArray<int>(Math.Max(1, gates * 2), Allocator.Persistent);
            _stageValue = new NativeArray<byte>(Math.Max(1, gates * 2), Allocator.Persistent);
            _status = new NativeArray<KernelStatus>(1, Allocator.Persistent);
            _sweepChanged = new NativeArray<byte>(1, Allocator.Persistent);
            Reset();
        }

        public void Reset()
        {
            EnsureLoaded();
            Complete();
            Clear(_values);
            Clear(_gateState);
            for (int i = 0; i < _circuit.InputSlotCount; i++)
                _inputs[i] = _circuit.InitialInputs[i];
            for (int i = 0; i < _stamp.Length; i++)
                _stamp[i] = -1;
            _pendingInputs.Clear();
            _pendingSweepTicks = 0;
            _framesInSweep = 0;
            _needsFullEvaluation = false;
            _lastSweepChanged = true;

            var d = MainThreadKernelData();
            var status = default(KernelStatus);
            SimulationKernel.InitializeState(ref d);
            SimulationKernel.ActivateAll(ref d, ref status);
            _status[0] = status;
            _cachedStatus = status;
            NativeArray<byte>.Copy(_values, _sweepBuffer);
        }

        public void SetInput(int slot, bool value)
        {
            EnsureLoaded();
            if (slot < 0 || slot >= _circuit.InputSlotCount)
                throw new ArgumentOutOfRangeException(nameof(slot));
            // Applied right before the next job, so callers never touch arrays owned by a running job.
            _pendingInputs.Add(new KeyValuePair<int, bool>(slot, value));
        }

        public bool GetInput(int slot)
        {
            for (int i = _pendingInputs.Count - 1; i >= 0; i--)
                if (_pendingInputs[i].Key == slot)
                    return _pendingInputs[i].Value;
            Complete();
            return _inputs[slot] != 0;
        }

        public bool GetNet(int net)
        {
            Complete();
            if (net < 0 || net >= _values.Length)
                return false;
            return _values[net] != 0;
        }

        public void Schedule(long ticks)
        {
            EnsureLoaded();
            Complete();
            if (ticks <= 0)
                return;
            ApplyPendingInputs();

            var mode = ResolveMode();
            ActiveMode = mode;
            if (mode == SimulationMode.FullSweep)
                ScheduleSweep(ticks);
            else
                ScheduleEventDriven(ticks, false);
        }

        public void Complete()
        {
            if (!_running)
                return;
            _handle.Complete();
            _running = false;

            if (_pendingSweepTicks > 0)
            {
                var status = _status[0];
                status.Tick += _pendingSweepTicks;
                status.Evaluations += _pendingSweepTicks * _circuit.GateCount;
                status.LastRunEvaluations = _pendingSweepTicks * _circuit.GateCount;
                _status[0] = status;
                _lastSweepChanged = _sweepChanged[(int)Math.Min(_sweepChanged.Length - 1, _pendingSweepTicks - 1)] != 0;
                _pendingSweepTicks = 0;
            }
            _cachedStatus = _status[0];
        }

        public bool Settle(long maxTicks)
        {
            EnsureLoaded();
            Complete();
            ApplyPendingInputs();
            ActiveMode = SimulationMode.EventDriven;
            ScheduleEventDriven(maxTicks, true);
            Complete();
            return _cachedStatus.ActiveCount == 0;
        }

        private SimulationMode ResolveMode()
        {
            if (Mode != SimulationMode.Auto)
                return Mode;
            if (_circuit.GateCount < ParallelSweepMinGates)
                return SimulationMode.EventDriven;

            if (ActiveMode == SimulationMode.FullSweep)
            {
                // Re-measure activity with an event-driven frame from time to time.
                _framesInSweep++;
                return _framesInSweep % 30 == 0 ? SimulationMode.EventDriven : SimulationMode.FullSweep;
            }

            var status = _cachedStatus;
            if (status.BusyTicks == 0)
                return SimulationMode.EventDriven;
            double averageActive = (double)status.LastRunEvaluations / status.BusyTicks;
            return averageActive >= _circuit.GateCount * ParallelSweepActivity ? SimulationMode.FullSweep : SimulationMode.EventDriven;
        }

        private void ScheduleEventDriven(long ticks, bool stopWhenStable)
        {
            var job = new EventDrivenJob
            {
                Netlist = _netlist,
                Inputs = _inputs,
                GateState = _gateState,
                Values = _values,
                Stamp = _stamp,
                Active = _active,
                NextActive = _nextActive,
                StageNet = _stageNet,
                StageValue = _stageValue,
                Status = _status,
                Ticks = ticks,
                StopWhenStable = stopWhenStable,
                ActivateAllFirst = _needsFullEvaluation
            };
            _needsFullEvaluation = false;
            _handle = job.Schedule();
            _running = true;
        }

        private void ScheduleSweep(long ticks)
        {
            if (_sweepChanged.Length < ticks)
            {
                _sweepChanged.Dispose();
                _sweepChanged = new NativeArray<byte>((int)Math.Min(int.MaxValue, ticks), Allocator.Persistent);
            }
            Clear(_sweepChanged);

            int chunkSize = Math.Max(64, ChunkSize);
            int chunks = (_circuit.GateCount + chunkSize - 1) / chunkSize;
            long tick = _status[0].Tick;
            var dependency = default(JobHandle);
            var current = _values;
            var next = _sweepBuffer;
            for (long k = 0; k < ticks; k++)
            {
                var job = new SweepChunkJob
                {
                    Netlist = _netlist,
                    Inputs = _inputs,
                    Current = current,
                    Next = next,
                    GateState = _gateState,
                    Changed = _sweepChanged,
                    ChunkSize = chunkSize,
                    TickIndex = (int)k,
                    Tick = tick + k
                };
                dependency = job.Schedule(Math.Max(1, chunks), 1, dependency);
                var swap = current;
                current = next;
                next = swap;
            }

            // After an odd number of ticks the newest values live in the other buffer.
            if (ticks % 2 == 1)
            {
                var swap = _values;
                _values = _sweepBuffer;
                _sweepBuffer = swap;
            }

            _handle = dependency;
            _running = true;
            _pendingSweepTicks = ticks;
            _needsFullEvaluation = true;
        }

        private void ApplyPendingInputs()
        {
            if (_pendingInputs.Count == 0)
                return;
            var d = MainThreadKernelData();
            var status = _status[0];
            foreach (var change in _pendingInputs)
            {
                byte value = change.Value ? (byte)1 : (byte)0;
                if (_inputs[change.Key] == value)
                    continue;
                _inputs[change.Key] = value;
                if (!_needsFullEvaluation)
                    SimulationKernel.Activate(ref d, ref status, _circuit.InputSlotGate[change.Key]);
            }
            _pendingInputs.Clear();
            _status[0] = status;
            _cachedStatus = status;
        }

        /// <summary>Kernel view of the arrays for main-thread work (only while no job is running).</summary>
        private KernelData MainThreadKernelData()
        {
            var d = new KernelData();
            _netlist.Fill(ref d);
            d.Inputs = (byte*)_inputs.GetUnsafePtr();
            d.GateState = (byte*)_gateState.GetUnsafePtr();
            d.Values = (byte*)_values.GetUnsafePtr();
            d.Stamp = (long*)_stamp.GetUnsafePtr();
            d.Active = (int*)_active.GetUnsafePtr();
            d.NextActive = (int*)_nextActive.GetUnsafePtr();
            d.StageNet = (int*)_stageNet.GetUnsafePtr();
            d.StageValue = (byte*)_stageValue.GetUnsafePtr();
            return d;
        }

        private static NativeArray<T> Copy<T>(T[] source) where T : struct
        {
            // Zero-length NativeArrays are valid, but a length of 1 keeps pointer access simple everywhere.
            if (source.Length == 0)
                return new NativeArray<T>(1, Allocator.Persistent);
            return new NativeArray<T>(source, Allocator.Persistent);
        }

        private static void Clear<T>(NativeArray<T> array) where T : struct
        {
            UnsafeUtility.MemClear(array.GetUnsafePtr(), (long)array.Length * UnsafeUtility.SizeOf<T>());
        }

        private void EnsureLoaded()
        {
            if (_circuit == null)
                throw new InvalidOperationException("No circuit loaded");
        }

        private void DisposeArrays()
        {
            if (!_netlist.Op.IsCreated)
                return;
            _netlist.Op.Dispose();
            _netlist.Param.Dispose();
            _netlist.InStart.Dispose();
            _netlist.InNets.Dispose();
            _netlist.Out0.Dispose();
            _netlist.Out1.Dispose();
            _netlist.FanoutStart.Dispose();
            _netlist.FanoutGates.Dispose();
            _netlist.ClockGates.Dispose();
            _inputs.Dispose();
            _gateState.Dispose();
            _values.Dispose();
            _sweepBuffer.Dispose();
            _stamp.Dispose();
            _active.Dispose();
            _nextActive.Dispose();
            _stageNet.Dispose();
            _stageValue.Dispose();
            _status.Dispose();
            _sweepChanged.Dispose();
            _netlist = default;
        }

        public void Dispose()
        {
            Complete();
            DisposeArrays();
            _circuit = null;
        }
    }
}
