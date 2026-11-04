using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
#if LOGICSIM_BURST
using Unity.Burst;
#endif

namespace LogicSim.Simulation.Jobs
{
    /// <summary>Read-only netlist arrays shared by all simulation jobs.</summary>
    public struct NetlistArrays
    {
        // Explicit counts: empty arrays are allocated with length 1.
        public int GateCount;
        public int NetCount;
        public int ClockCount;
        [ReadOnly] public NativeArray<byte> Op;
        [ReadOnly] public NativeArray<int> Param;
        [ReadOnly] public NativeArray<int> InStart;
        [ReadOnly] public NativeArray<int> InNets;
        [ReadOnly] public NativeArray<int> Out0;
        [ReadOnly] public NativeArray<int> Out1;
        [ReadOnly] public NativeArray<int> FanoutStart;
        [ReadOnly] public NativeArray<int> FanoutGates;
        [ReadOnly] public NativeArray<int> ClockGates;
        public int NeuronCount;
        [ReadOnly] public NativeArray<float> Weights;
        [ReadOnly] public NativeArray<float> NeuronParams;
        [ReadOnly] public NativeArray<ulong> Lut;

        internal unsafe void Fill(ref KernelData d)
        {
            d.GateCount = GateCount;
            d.NetCount = NetCount;
            d.ClockCount = ClockCount;
            d.Op = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Op);
            d.Param = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Param);
            d.InStart = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(InStart);
            d.InNets = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(InNets);
            d.Out0 = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Out0);
            d.Out1 = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Out1);
            d.FanoutStart = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(FanoutStart);
            d.FanoutGates = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(FanoutGates);
            d.ClockGates = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(ClockGates);
            d.NeuronCount = NeuronCount;
            d.Weights = (float*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Weights);
            d.NeuronParams = (float*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(NeuronParams);
            d.Lut = (ulong*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Lut);
        }
    }

    /// <summary>
    /// Event-driven simulation of many ticks in a single job. Runs on a worker thread while the main
    /// thread renders; Burst compiles the shared <see cref="SimulationKernel"/> to native code.
    /// </summary>
#if LOGICSIM_BURST
    // Strict float mode keeps neuron sums identical to the managed backend.
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
#endif
    public struct EventDrivenJob : IJob
    {
        public NetlistArrays Netlist;
        [ReadOnly] public NativeArray<byte> Inputs;
        public NativeArray<byte> GateState;
        public NativeArray<byte> Values;
        public NativeArray<long> Stamp;
        public NativeArray<int> Active;
        public NativeArray<int> NextActive;
        public NativeArray<int> StageNet;
        public NativeArray<byte> StageValue;
        public NativeArray<float> NeuronV;
        public NativeArray<int> NeuronCounter;
        public NativeArray<int> Requeue;
        public NativeArray<KernelStatus> Status;
        public long Ticks;
        public bool StopWhenStable;
        public bool ActivateAllFirst;

        public unsafe void Execute()
        {
            var d = new KernelData();
            Netlist.Fill(ref d);
            d.Inputs = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Inputs);
            d.GateState = (byte*)GateState.GetUnsafePtr();
            d.Values = (byte*)Values.GetUnsafePtr();
            d.Stamp = (long*)Stamp.GetUnsafePtr();
            d.Active = (int*)Active.GetUnsafePtr();
            d.NextActive = (int*)NextActive.GetUnsafePtr();
            d.StageNet = (int*)StageNet.GetUnsafePtr();
            d.StageValue = (byte*)StageValue.GetUnsafePtr();
            d.NeuronV = (float*)NeuronV.GetUnsafePtr();
            d.NeuronCounter = (int*)NeuronCounter.GetUnsafePtr();
            d.Requeue = (int*)Requeue.GetUnsafePtr();

            var status = Status[0];
            if (ActivateAllFirst)
                SimulationKernel.ActivateAll(ref d, ref status);
            SimulationKernel.RunEventDriven(ref d, ref status, Ticks, StopWhenStable);
            Status[0] = status;
        }
    }

    /// <summary>
    /// One tick of a full sweep, parallelized over chunks of gates. Chained jobs alternate the two value buffers.
    /// </summary>
#if LOGICSIM_BURST
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
#endif
    public struct SweepChunkJob : IJobParallelFor
    {
        public NetlistArrays Netlist;
        [ReadOnly] public NativeArray<byte> Inputs;
        [ReadOnly] public NativeArray<byte> Current;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Next;
        [NativeDisableParallelForRestriction] public NativeArray<byte> GateState;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Changed;

        // Each neuron is written only by the work item that owns its gate.
        [NativeDisableParallelForRestriction] public NativeArray<float> NeuronV;
        [NativeDisableParallelForRestriction] public NativeArray<int> NeuronCounter;
        public int ChunkSize;
        public int TickIndex;
        public long Tick;

        public unsafe void Execute(int chunk)
        {
            var d = new KernelData();
            Netlist.Fill(ref d);
            d.Inputs = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Inputs);
            d.GateState = (byte*)GateState.GetUnsafePtr();
            d.NeuronV = (float*)NeuronV.GetUnsafePtr();
            d.NeuronCounter = (int*)NeuronCounter.GetUnsafePtr();
            byte* current = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Current);
            byte* next = (byte*)Next.GetUnsafePtr();

            int start = chunk * ChunkSize;
            int end = start + ChunkSize;
            if (end > d.GateCount)
                end = d.GateCount;
            bool changed = false;
            for (int g = start; g < end; g++)
                changed |= SimulationKernel.SweepGate(ref d, current, next, g, Tick);
            // Benign race: every writer stores the same value.
            if (changed)
                Changed[TickIndex] = 1;
        }
    }
}
