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
        JKFlipFlop = 13,

        /// <summary>Weighted sum of the inputs compared with a threshold.</summary>
        Perceptron = 14,

        /// <summary>Leaky integrate-and-fire neuron with state in <see cref="CompiledCircuit.NeuronParams"/>.</summary>
        Spiking = 15,

        /// <summary>Table lookup: the inputs form an index into <see cref="CompiledCircuit.Lut"/> (neural blocks).</summary>
        Lut = 16
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

        /// <summary>Neuron weight of every input connection (parallel to <see cref="InNets"/>; length 1 without neurons).</summary>
        public float[] Weights;

        /// <summary>Four values per neuron: threshold, leak, reset potential, refractory ticks.</summary>
        public float[] NeuronParams;

        public int NeuronCount;

        /// <summary>Lookup tables of neural blocks; a Lut gate's Param is its first word.</summary>
        public ulong[] Lut;

        // Mapping of the top-level circuit to nets and slots.
        public readonly Dictionary<int, int[]> OutputNets = new Dictionary<int, int[]>();
        public readonly Dictionary<int, int[]> InputNets = new Dictionary<int, int[]>();
        public readonly Dictionary<int, int> InputSlots = new Dictionary<int, int>();

        public readonly List<CompileDiagnostic> Diagnostics = new List<CompileDiagnostic>();

        /// <summary>Circuit structure version this netlist was compiled from.</summary>
        public int SourceVersion;

        /// <summary>Subcircuit library version this netlist was compiled from.</summary>
        public int LibraryVersion;

        /// <summary>The document level; nested scopes are the IC instances.</summary>
        public CompiledScope RootScope;

        private NetResolver _probeResolver;

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

        /// <summary>Scope of the IC instance reached by following <paramref name="instancePath"/> from the document. Null if it does not exist.</summary>
        public CompiledScope FindScope(IReadOnlyList<int> instancePath)
        {
            var scope = RootScope;
            if (instancePath == null)
                return scope;
            foreach (var instanceId in instancePath)
            {
                if (scope == null || !scope.Children.TryGetValue(instanceId, out scope))
                    return null;
            }
            return scope;
        }

        public int GetOutputNet(CompiledScope scope, int componentId, int pin)
        {
            if (scope == null || scope.IsRoot)
                return GetOutputNet(componentId, pin);
            return (_probeResolver ?? (_probeResolver = new NetResolver(this, compiling: false))).ResolveOutput(scope, componentId, pin);
        }

        public int GetInputNet(CompiledScope scope, int componentId, int pin)
        {
            if (scope == null || scope.IsRoot)
                return GetInputNet(componentId, pin);
            return (_probeResolver ?? (_probeResolver = new NetResolver(this, compiling: false))).ResolveInput(scope, componentId, pin);
        }
    }

    /// <summary>One circuit instance in the compiled hierarchy: the document or an IC instance inside it.</summary>
    public sealed class CompiledScope
    {
        public int Index { get; }
        public Circuit Circuit { get; }
        public CompiledScope Parent { get; }

        /// <summary>Component id of the instance in the parent scope (0 for the document).</summary>
        public int InstanceId { get; }

        public SubcircuitDefinition Definition { get; }

        public bool IsRoot => Parent == null;

        internal readonly Dictionary<int, CompiledScope> Children = new Dictionary<int, CompiledScope>();
        internal Dictionary<int, int[]> PrimitiveOutputs = new Dictionary<int, int[]>();
        internal Dictionary<(int component, int pin, bool input), int> Memo;

        /// <summary>Neuron index of each Neuron/SpikingNeuron in this scope.</summary>
        internal readonly Dictionary<int, int> Neurons = new Dictionary<int, int>();

        public bool TryGetNeuron(int componentId, out int neuron) => Neurons.TryGetValue(componentId, out neuron);

        internal CompiledScope(int index, Circuit circuit, CompiledScope parent, int instanceId, SubcircuitDefinition definition)
        {
            Index = index;
            Circuit = circuit;
            Parent = parent;
            InstanceId = instanceId;
            Definition = definition;
        }

        public bool TryGetChild(int instanceId, out CompiledScope child) => Children.TryGetValue(instanceId, out child);
    }
}
