namespace LogicSim.Simulation
{
    /// <summary>
    /// Pointers to the netlist and the simulation state. Plain unmanaged data so the same kernel runs
    /// from managed code (pinned arrays) and from Burst-compiled jobs (NativeArray pointers).
    /// </summary>
    public unsafe struct KernelData
    {
        public int GateCount;
        public int NetCount;
        public int ClockCount;

        // Netlist (read-only during simulation)
        public byte* Op;
        public int* Param;
        public int* InStart;
        public int* InNets;
        public int* Out0;
        public int* Out1;
        public int* FanoutStart;
        public int* FanoutGates;
        public int* ClockGates;

        // State
        public byte* Inputs;
        public byte* GateState;
        public byte* Values;

        // Event-driven work buffers (each GateCount long, Stage buffers 2 * GateCount)
        public int* Active;
        public int* NextActive;
        public long* Stamp;
        public int* StageNet;
        public byte* StageValue;
    }

    /// <summary>Counters shared between the kernel and its host.</summary>
    public struct KernelStatus
    {
        public long Tick;
        public int ActiveCount;
        public long Evaluations;

        /// <summary>Number of ticks in the last run that evaluated at least one gate.</summary>
        public long BusyTicks;

        /// <summary>Total number of gates evaluated in the last run (for the auto mode heuristic).</summary>
        public long LastRunEvaluations;
    }

    /// <summary>
    /// Unit-delay, two-valued logic simulation. Every gate output reacts to its inputs one tick later,
    /// which models propagation delay, makes feedback loops (latches, oscillators) behave deterministically
    /// and lets all gates of a tick be evaluated independently (in parallel).
    /// </summary>
    public static unsafe class SimulationKernel
    {
        /// <summary>Evaluates one gate against <paramref name="current"/>. Updates the gate's private state.</summary>
        public static void Evaluate(ref KernelData d, byte* current, int gate, long tick, out byte out0, out byte out1)
        {
            int start = d.InStart[gate];
            int end = d.InStart[gate + 1];
            out1 = 0;
            byte v;
            switch (d.Op[gate])
            {
                case (byte)OpCode.Buffer:
                    out0 = current[d.InNets[start]];
                    return;
                case (byte)OpCode.Not:
                    out0 = (byte)(current[d.InNets[start]] ^ 1);
                    return;
                case (byte)OpCode.And:
                case (byte)OpCode.Nand:
                    v = 1;
                    for (int i = start; i < end; i++)
                        v &= current[d.InNets[i]];
                    out0 = d.Op[gate] == (byte)OpCode.And ? v : (byte)(v ^ 1);
                    return;
                case (byte)OpCode.Or:
                case (byte)OpCode.Nor:
                    v = 0;
                    for (int i = start; i < end; i++)
                        v |= current[d.InNets[i]];
                    out0 = d.Op[gate] == (byte)OpCode.Or ? v : (byte)(v ^ 1);
                    return;
                case (byte)OpCode.Xor:
                case (byte)OpCode.Xnor:
                    v = 0;
                    for (int i = start; i < end; i++)
                        v ^= current[d.InNets[i]];
                    out0 = d.Op[gate] == (byte)OpCode.Xor ? v : (byte)(v ^ 1);
                    return;
                case (byte)OpCode.Input:
                    out0 = d.Inputs[d.Param[gate]];
                    return;
                case (byte)OpCode.Clock:
                    out0 = (byte)((tick / d.Param[gate]) & 1);
                    return;
                case (byte)OpCode.Constant:
                    out0 = (byte)d.Param[gate];
                    return;
                case (byte)OpCode.DFlipFlop:
                case (byte)OpCode.TFlipFlop:
                case (byte)OpCode.JKFlipFlop:
                    out0 = EvaluateFlipFlop(ref d, current, gate, start);
                    out1 = (byte)(out0 ^ 1);
                    return;
                default:
                    out0 = 0;
                    return;
            }
        }

        private static byte EvaluateFlipFlop(ref KernelData d, byte* current, int gate, int start)
        {
            // State bit 0: Q, bit 1: clock level seen at the previous evaluation.
            byte state = d.GateState[gate];
            byte q = (byte)(state & 1);
            byte previousClock = (byte)((state >> 1) & 1);
            byte op = d.Op[gate];
            int clockPin = op == (byte)OpCode.JKFlipFlop ? 2 : 1;
            byte clock = current[d.InNets[start + clockPin]];

            if (clock == 1 && previousClock == 0)
            {
                if (op == (byte)OpCode.DFlipFlop)
                {
                    q = current[d.InNets[start]];
                }
                else if (op == (byte)OpCode.TFlipFlop)
                {
                    if (current[d.InNets[start]] == 1)
                        q ^= 1;
                }
                else
                {
                    byte j = current[d.InNets[start]];
                    byte k = current[d.InNets[start + 1]];
                    if (j == 1 && k == 1)
                        q ^= 1;
                    else if (j == 1)
                        q = 1;
                    else if (k == 1)
                        q = 0;
                }
            }

            d.GateState[gate] = (byte)(q | (clock << 1));
            return q;
        }

        /// <summary>
        /// Power-on values on cleared buffers: constants hold their value, flip-flops start with Q = 0 / nQ = 1
        /// and remember their current clock level so start-up does not produce a spurious clock edge.
        /// </summary>
        public static void InitializeState(ref KernelData d)
        {
            for (int g = 0; g < d.GateCount; g++)
            {
                byte op = d.Op[g];
                if (op == (byte)OpCode.Constant && d.Out0[g] >= 0)
                    d.Values[d.Out0[g]] = (byte)d.Param[g];
                else if (IsFlipFlop(op) && d.Out1[g] >= 0)
                    d.Values[d.Out1[g]] = 1;
            }
            for (int g = 0; g < d.GateCount; g++)
            {
                byte op = d.Op[g];
                if (!IsFlipFlop(op))
                    continue;
                int clockPin = op == (byte)OpCode.JKFlipFlop ? 2 : 1;
                byte clock = d.Values[d.InNets[d.InStart[g] + clockPin]];
                d.GateState[g] = (byte)(clock << 1);
            }
        }

        private static bool IsFlipFlop(byte op)
        {
            return op == (byte)OpCode.DFlipFlop || op == (byte)OpCode.TFlipFlop || op == (byte)OpCode.JKFlipFlop;
        }

        /// <summary>Marks a gate for evaluation in the current tick.</summary>
        public static void Activate(ref KernelData d, ref KernelStatus status, int gate)
        {
            if (d.Stamp[gate] == status.Tick)
                return;
            d.Stamp[gate] = status.Tick;
            d.Active[status.ActiveCount++] = gate;
        }

        public static void ActivateAll(ref KernelData d, ref KernelStatus status)
        {
            status.ActiveCount = 0;
            for (int g = 0; g < d.GateCount; g++)
            {
                d.Stamp[g] = status.Tick;
                d.Active[status.ActiveCount++] = g;
            }
        }

        /// <summary>
        /// Event-driven run: only gates whose inputs changed in the previous tick are evaluated.
        /// Idle periods are skipped up to the next clock edge.
        /// </summary>
        /// <param name="stopWhenStable">Return as soon as no gate is pending (used to settle a circuit).</param>
        public static void RunEventDriven(ref KernelData d, ref KernelStatus status, long ticks, bool stopWhenStable)
        {
            long end = status.Tick + ticks;
            int* activeBuffer = d.Active;
            long evaluations = 0;
            long busyTicks = 0;

            while (status.Tick < end)
            {
                long tick = status.Tick;

                for (int c = 0; c < d.ClockCount; c++)
                {
                    int gate = d.ClockGates[c];
                    if (tick % d.Param[gate] == 0)
                        Activate(ref d, ref status, gate);
                }

                if (status.ActiveCount == 0)
                {
                    if (stopWhenStable)
                        break;
                    status.Tick = NextClockEdge(ref d, tick, end);
                    continue;
                }

                // Evaluate against the current values, stage the changes, then apply them all at once.
                int staged = 0;
                int activeCount = status.ActiveCount;
                for (int i = 0; i < activeCount; i++)
                {
                    int gate = d.Active[i];
                    Evaluate(ref d, d.Values, gate, tick, out byte o0, out byte o1);
                    int n0 = d.Out0[gate];
                    if (n0 >= 0 && d.Values[n0] != o0)
                    {
                        d.StageNet[staged] = n0;
                        d.StageValue[staged++] = o0;
                    }
                    int n1 = d.Out1[gate];
                    if (n1 >= 0 && d.Values[n1] != o1)
                    {
                        d.StageNet[staged] = n1;
                        d.StageValue[staged++] = o1;
                    }
                }
                evaluations += activeCount;
                busyTicks++;

                long nextTick = tick + 1;
                int nextCount = 0;
                for (int s = 0; s < staged; s++)
                {
                    int net = d.StageNet[s];
                    d.Values[net] = d.StageValue[s];
                    int fanoutEnd = d.FanoutStart[net + 1];
                    for (int f = d.FanoutStart[net]; f < fanoutEnd; f++)
                    {
                        int gate = d.FanoutGates[f];
                        if (d.Stamp[gate] != nextTick)
                        {
                            d.Stamp[gate] = nextTick;
                            d.NextActive[nextCount++] = gate;
                        }
                    }
                }

                int* swap = d.Active;
                d.Active = d.NextActive;
                d.NextActive = swap;
                status.ActiveCount = nextCount;
                status.Tick = nextTick;

                if (stopWhenStable && nextCount == 0)
                {
                    // A clock edge exactly at the next tick would still change something.
                    bool edgePending = false;
                    for (int c = 0; c < d.ClockCount; c++)
                    {
                        if (nextTick % d.Param[d.ClockGates[c]] == 0)
                        {
                            edgePending = true;
                            break;
                        }
                    }
                    if (!edgePending)
                        break;
                }
            }

            // Keep the pending list in the caller's buffer.
            if (d.Active != activeBuffer)
            {
                for (int i = 0; i < status.ActiveCount; i++)
                    activeBuffer[i] = d.Active[i];
                d.NextActive = d.Active;
                d.Active = activeBuffer;
            }

            status.Evaluations += evaluations;
            status.LastRunEvaluations = evaluations;
            status.BusyTicks = busyTicks;
        }

        private static long NextClockEdge(ref KernelData d, long tick, long end)
        {
            long next = end;
            for (int c = 0; c < d.ClockCount; c++)
            {
                long half = d.Param[d.ClockGates[c]];
                long edge = (tick / half + 1) * half;
                if (edge < next)
                    next = edge;
            }
            return next;
        }

        /// <summary>Evaluates one gate for a full sweep: reads <paramref name="current"/>, writes <paramref name="next"/>.</summary>
        /// <returns>True when an output changed.</returns>
        public static bool SweepGate(ref KernelData d, byte* current, byte* next, int gate, long tick)
        {
            Evaluate(ref d, current, gate, tick, out byte o0, out byte o1);
            bool changed = false;
            int n0 = d.Out0[gate];
            if (n0 >= 0)
            {
                next[n0] = o0;
                changed = current[n0] != o0;
            }
            int n1 = d.Out1[gate];
            if (n1 >= 0)
            {
                next[n1] = o1;
                changed |= current[n1] != o1;
            }
            return changed;
        }

        /// <summary>Single-threaded full sweep of all gates for one tick.</summary>
        public static bool SweepTick(ref KernelData d, byte* current, byte* next, long tick)
        {
            bool changed = false;
            for (int g = 0; g < d.GateCount; g++)
                changed |= SweepGate(ref d, current, next, g, tick);
            return changed;
        }
    }
}
