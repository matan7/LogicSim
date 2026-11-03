using System.Collections.Generic;

namespace LogicSim.Simulation
{
    /// <summary>Primitive operations evaluated by the simulation kernel.</summary>
    public enum OpCode : byte
    {
        Buffer = 0,
        Not = 1,
        And = 2,
        Or = 3,
        Nand = 4,
        Nor = 5,
        Xor = 6,
        Xnor = 7,
        Input = 8,
        Clock = 9,
        Constant = 10,
        DFlipFlop = 11,
        TFlipFlop = 12,
        JKFlipFlop = 13
    }

    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class CompileDiagnostic
    {
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }
        public int ComponentId { get; }

        public CompileDiagnostic(DiagnosticSeverity severity, string message, int componentId = 0)
        {
            Severity = severity;
            Message = message;
            ComponentId = componentId;
        }

        public override string ToString() => $"{Severity}: {Message}";
    }

    /// <summary>
    /// Flat, structure-of-arrays netlist produced by <see cref="CircuitCompiler"/>.
    /// Subcircuits are inlined. Net 0 is a constant LOW used by unconnected inputs.
    /// Every other net is driven by exactly one gate output.
    /// </summary>
    public sealed class CompiledCircuit
    {
        public const int LowNet = 0;

        public int GateCount;
        public int NetCount;

        /// <summary>Gate operation (<see cref="OpCode"/>).</summary>
        public byte[] Op;

        /// <summary>Clock half period, input slot or constant value, depending on <see cref="Op"/>.</summary>
        public int[] Param;

        /// <summary>CSR offsets into <see cref="InNets"/>; length GateCount + 1.</summary>
        public int[] InStart;
        public int[] InNets;

        /// <summary>First and second output net of each gate; -1 when unused.</summary>
        public int[] Out0;
        public int[] Out1;

        /// <summary>CSR offsets into <see cref="FanoutGates"/>; length NetCount + 1.</summary>
        public int[] FanoutStart;
        public int[] FanoutGates;

        public int[] ClockGates;

        public int InputSlotCount;

        /// <summary>Gate driven by each input slot.</summary>
        public int[] InputSlotGate;

        /// <summary>Initial values of the input slots (switch states).</summary>
        public byte[] InitialInputs;

        // Mapping of the top-level circuit to nets and slots.
        public readonly Dictionary<int, int[]> OutputNets = new Dictionary<int, int[]>();
        public readonly Dictionary<int, int[]> InputNets = new Dictionary<int, int[]>();
        public readonly Dictionary<int, int> InputSlots = new Dictionary<int, int>();

        public readonly List<CompileDiagnostic> Diagnostics = new List<CompileDiagnostic>();

        /// <summary>Circuit structure version this netlist was compiled from.</summary>
        public int SourceVersion;

        public int TotalInputConnections => InNets?.Length ?? 0;

        public bool HasErrors
        {
            get
            {
                foreach (var d in Diagnostics)
                    if (d.Severity == DiagnosticSeverity.Error)
                        return true;
                return false;
            }
        }

        public int GetOutputNet(int componentId, int pin)
        {
            if (OutputNets.TryGetValue(componentId, out var nets) && pin >= 0 && pin < nets.Length)
                return nets[pin];
            return LowNet;
        }

        public int GetInputNet(int componentId, int pin)
        {
            if (InputNets.TryGetValue(componentId, out var nets) && pin >= 0 && pin < nets.Length)
                return nets[pin];
            return LowNet;
        }

        public int GetInputSlot(int componentId)
        {
            return InputSlots.TryGetValue(componentId, out int slot) ? slot : -1;
        }
    }
}
