using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicSim.Simulation
{
    /// <summary>
    /// Static description of a component kind: pin layout, naming, role in the simulation, and its settings. It is the
    /// one place per kind that the model, the file format, the MCP tools and the inspector read their per-kind
    /// behavior from (only the visuals live in the views).
    /// </summary>
    public sealed class ComponentSpec
    {
        public ComponentKind Kind { get; }
        public string DisplayName { get; }
        public ComponentCategory Category { get; }
        public string Description { get; }
        public int MinInputs { get; }
        public int MaxInputs { get; }
        public int DefaultInputs { get; }
        public int OutputCount { get; }

        /// <summary>Component is driven by the user (or by the parent circuit when used inside a subcircuit).</summary>
        public bool IsInterfaceInput { get; }

        /// <summary>Component exposes a value to the user (or to the parent circuit when used inside a subcircuit).</summary>
        public bool IsInterfaceOutput { get; }

        /// <summary>Component only reads values and produces no outputs.</summary>
        public bool IsSink => OutputCount == 0;

        public bool HasVariableInputs => MinInputs != MaxInputs;

        /// <summary>
        /// Kernel opcode when the component compiles to one primitive gate; null for pins, displays, subcircuits and
        /// neural blocks, which the compiler handles separately.
        /// </summary>
        public OpCode? SimulationOp { get; private set; }

        /// <summary>Extra names accepted for this kind in files and MCP calls (lower case, no separators).</summary>
        public IReadOnlyList<string> Aliases { get; private set; } = Array.Empty<string>();

        /// <summary>Description of the properties that are not settings (neuron weights, layers, ...), or null.</summary>
        public string PropertiesHelp { get; private set; }

        /// <summary>Scalar settings of this kind (see <see cref="ComponentSetting"/>), in inspector order.</summary>
        public IReadOnlyList<ComponentSetting> Settings => _settings;

        /// <summary>Settings and other properties described for agents, or null when the kind has none.</summary>
        public string AllPropertiesHelp
        {
            get
            {
                var parts = _settings.Where(s => s.InDescription).Select(s => s.Describe()).ToList();
                if (PropertiesHelp != null)
                    parts.Add(PropertiesHelp);
                return parts.Count == 0 ? null : string.Join("; ", parts);
            }
        }

        public ComponentSetting FindSetting(string name) => _settings.FirstOrDefault(s => s.Name == name);

        private readonly List<ComponentSetting> _settings = new List<ComponentSetting>();

        // Pins that depend on the component (its definition, network or settings); null uses the fixed layout above.
        private Func<CircuitComponent, Circuit, int> _inputCount;
        private Func<CircuitComponent, Circuit, int> _outputCount;
        private Func<CircuitComponent, Circuit, int, string> _inputName;
        private Func<CircuitComponent, Circuit, int, string> _outputName;

        private readonly string[] _inputNames;
        private readonly string[] _outputNames;

        private ComponentSpec(ComponentKind kind, string displayName, ComponentCategory category, string description,
            int minInputs, int maxInputs, int defaultInputs, string[] inputNames, string[] outputNames,
            bool isInterfaceInput = false, bool isInterfaceOutput = false)
        {
            Kind = kind;
            DisplayName = displayName;
            Category = category;
            Description = description;
            MinInputs = minInputs;
            MaxInputs = maxInputs;
            DefaultInputs = defaultInputs;
            OutputCount = outputNames.Length;
            IsInterfaceInput = isInterfaceInput;
            IsInterfaceOutput = isInterfaceOutput;
            _inputNames = inputNames;
            _outputNames = outputNames;
            if (HasVariableInputs)
                _settings.Add(ComponentSetting.Integer("inputs", "inputs", "Inputs", "number of inputs", minInputs, maxInputs, defaultInputs,
                    p => p.InputCount, (ref ComponentProperties p, int value) => p.InputCount = value).AddsPins().NotDescribed());
        }

        /// <summary>Input pins of a placed component.</summary>
        public int GetInputCount(CircuitComponent component, Circuit circuit)
            => _inputCount?.Invoke(component, circuit) ?? (HasVariableInputs ? component.InputCount : DefaultInputs);

        public int GetOutputCount(CircuitComponent component, Circuit circuit) => _outputCount?.Invoke(component, circuit) ?? OutputCount;

        public string GetInputName(CircuitComponent component, Circuit circuit, int index)
            => _inputName?.Invoke(component, circuit, index) ?? GetInputName(index);

        public string GetOutputName(CircuitComponent component, Circuit circuit, int index)
            => _outputName?.Invoke(component, circuit, index) ?? GetOutputName(index);

        public string GetInputName(int index)
        {
            if (_inputNames != null && index < _inputNames.Length)
                return _inputNames[index];
            return "in" + index;
        }

        public string GetOutputName(int index)
        {
            if (index < _outputNames.Length)
                return _outputNames[index];
            return "out" + index;
        }

        public const int MaxGateInputs = 8;

        private ComponentSpec Op(OpCode op)
        {
            SimulationOp = op;
            return this;
        }

        private ComponentSpec Alias(params string[] names)
        {
            Aliases = names;
            return this;
        }

        private ComponentSpec Props(string help)
        {
            PropertiesHelp = help;
            return this;
        }

        private ComponentSpec Setting(params ComponentSetting[] settings)
        {
            _settings.AddRange(settings);
            return this;
        }

        private ComponentSpec Pins(Func<CircuitComponent, Circuit, int> inputCount, Func<CircuitComponent, Circuit, int> outputCount,
            Func<CircuitComponent, Circuit, int, string> inputName = null, Func<CircuitComponent, Circuit, int, string> outputName = null)
        {
            _inputCount = inputCount;
            _outputCount = outputCount;
            _inputName = inputName;
            _outputName = outputName;
            return this;
        }

        private static ComponentSetting ValueFlag(string label, string help)
            => ComponentSetting.Flag("value", "value", label, help, p => p.Value ? 1 : 0, (ref ComponentProperties p, int value) => p.Value = value != 0);

        private static readonly Dictionary<ComponentKind, ComponentSpec> _specs = Build();

        public static IEnumerable<ComponentSpec> All => _specs.Values;

        public static ComponentSpec Get(ComponentKind kind)
        {
            if (_specs.TryGetValue(kind, out var spec))
                return spec;
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown component kind");
        }

        private static Dictionary<ComponentKind, ComponentSpec> Build()
        {
            var output = new[] { "out" };
            var list = new[]
            {
                new ComponentSpec(ComponentKind.Buffer, "Buffer", ComponentCategory.Gate, "Copies its input to the output.", 1, 1, 1, new[] { "in" }, output).Op(OpCode.Buffer),
                new ComponentSpec(ComponentKind.Not, "NOT", ComponentCategory.Gate, "Inverts its input.", 1, 1, 1, new[] { "in" }, output).Op(OpCode.Not).Alias("inverter"),
                new ComponentSpec(ComponentKind.And, "AND", ComponentCategory.Gate, "1 when all inputs are 1.", 1, MaxGateInputs, 2, null, output).Op(OpCode.And),
                new ComponentSpec(ComponentKind.Or, "OR", ComponentCategory.Gate, "1 when any input is 1.", 1, MaxGateInputs, 2, null, output).Op(OpCode.Or),
                new ComponentSpec(ComponentKind.Nand, "NAND", ComponentCategory.Gate, "0 when all inputs are 1.", 1, MaxGateInputs, 2, null, output).Op(OpCode.Nand),
                new ComponentSpec(ComponentKind.Nor, "NOR", ComponentCategory.Gate, "1 when all inputs are 0.", 1, MaxGateInputs, 2, null, output).Op(OpCode.Nor),
                new ComponentSpec(ComponentKind.Xor, "XOR", ComponentCategory.Gate, "1 when an odd number of inputs is 1.", 1, MaxGateInputs, 2, null, output).Op(OpCode.Xor),
                new ComponentSpec(ComponentKind.Xnor, "XNOR", ComponentCategory.Gate, "1 when an even number of inputs is 1.", 1, MaxGateInputs, 2, null, output).Op(OpCode.Xnor),

                new ComponentSpec(ComponentKind.Switch, "Switch", ComponentCategory.Input, "Toggle input. Becomes an input pin when used inside a subcircuit.", 0, 0, 0, null, output, isInterfaceInput: true).Alias("toggle", "input")
                    .Setting(ValueFlag("State", "on or off; toggled by clicking it in the simulation").NotInInspector()),
                new ComponentSpec(ComponentKind.Button, "Button", ComponentCategory.Input, "Momentary input, 1 only while pressed. Becomes an input pin inside a subcircuit.", 0, 0, 0, null, output, isInterfaceInput: true),
                new ComponentSpec(ComponentKind.Clock, "Clock", ComponentCategory.Input, "Square wave that toggles every 'halfPeriod' ticks.", 0, 0, 0, null, output).Op(OpCode.Clock)
                    .Setting(ComponentSetting.Integer("half_period", "halfPeriod", "Half period", "ticks the output stays 0 or 1", 1, 1_000_000_000,
                        CircuitComponent.DefaultClockHalfPeriod, p => p.ClockHalfPeriod, (ref ComponentProperties p, int value) => p.ClockHalfPeriod = value)),
                new ComponentSpec(ComponentKind.Constant, "Constant", ComponentCategory.Input, "Constant 0 or 1.", 0, 0, 0, null, output).Op(OpCode.Constant).Setting(ValueFlag("Value", "the output")),

                new ComponentSpec(ComponentKind.Bulb, "Bulb", ComponentCategory.Output, "Lights up when its input is 1. Becomes an output pin inside a subcircuit.", 1, 1, 1, new[] { "in" }, Array.Empty<string>(), isInterfaceOutput: true).Alias("led", "lamp", "output"),
                new ComponentSpec(ComponentKind.HexDisplay, "Hex display", ComponentCategory.Output, "Shows a 4-bit value (b0 is the least significant bit).", 4, 4, 4, new[] { "b0", "b1", "b2", "b3" }, Array.Empty<string>()).Alias("hex", "display"),

                new ComponentSpec(ComponentKind.DFlipFlop, "D flip-flop", ComponentCategory.Sequential, "Stores D on the rising edge of CLK.", 2, 2, 2, new[] { "D", "CLK" }, new[] { "Q", "nQ" }).Op(OpCode.DFlipFlop).Alias("dff"),
                new ComponentSpec(ComponentKind.TFlipFlop, "T flip-flop", ComponentCategory.Sequential, "Toggles on the rising edge of CLK when T is 1.", 2, 2, 2, new[] { "T", "CLK" }, new[] { "Q", "nQ" }).Op(OpCode.TFlipFlop).Alias("tff"),
                new ComponentSpec(ComponentKind.JKFlipFlop, "JK flip-flop", ComponentCategory.Sequential, "J sets, K resets, both toggle on the rising edge of CLK.", 3, 3, 3, new[] { "J", "K", "CLK" }, new[] { "Q", "nQ" }).Op(OpCode.JKFlipFlop).Alias("jkff"),

                new ComponentSpec(ComponentKind.PinIn, "Input pin", ComponentCategory.Subcircuit, "Subcircuit input. At the top level it behaves like an externally driven input.", 0, 0, 0, null, output, isInterfaceInput: true)
                    .Setting(ValueFlag("State", "value at the top level; toggled by clicking it in the simulation").NotInInspector()),
                new ComponentSpec(ComponentKind.PinOut, "Output pin", ComponentCategory.Subcircuit, "Subcircuit output. At the top level it behaves like a probe.", 1, 1, 1, new[] { "in" }, Array.Empty<string>(), isInterfaceOutput: true),
                new ComponentSpec(ComponentKind.Subcircuit, "Subcircuit", ComponentCategory.Subcircuit, "Instance of a custom circuit. Pins come from its definition.", 0, 0, 0, null, Array.Empty<string>()).Alias("ic").Props("subcircuit (definition id); pins come from the definition")
                    .Pins((c, circuit) => circuit.Library.TryGet(c.SubcircuitId, out var d) ? d.InputComponentIds.Count : 0,
                        (c, circuit) => circuit.Library.TryGet(c.SubcircuitId, out var d) ? d.OutputComponentIds.Count : 0,
                        (c, circuit, pin) => circuit.Library.TryGet(c.SubcircuitId, out var d) && pin < d.InputNames.Count ? d.InputNames[pin] : null,
                        (c, circuit, pin) => circuit.Library.TryGet(c.SubcircuitId, out var d) && pin < d.OutputNames.Count ? d.OutputNames[pin] : null),

                new ComponentSpec(ComponentKind.Neuron, "Neuron", ComponentCategory.Neural,
                    "Perceptron: 1 when the weighted sum of its inputs reaches the threshold. Properties: weights, threshold.",
                    1, NeuronParameters.MaxInputs, 2, null, output).Op(OpCode.Perceptron).Alias("perceptron").Props("weights (one per input), threshold"),
                new ComponentSpec(ComponentKind.SpikingNeuron, "Spiking neuron", ComponentCategory.Neural,
                    "Leaky integrate-and-fire neuron: high inputs add their weight to a potential that leaks every tick; " +
                    "reaching the threshold emits a one-tick spike, resets the potential and ignores inputs for 'refractory' ticks.",
                    1, NeuronParameters.MaxInputs, 2, null, output).Op(OpCode.Spiking).Alias("lif", "spiking", "spike").Props("weights, threshold, leak (0-1), reset, refractory (ticks)"),
                new ComponentSpec(ComponentKind.NeuralBlock, "Neural block", ComponentCategory.Neural,
                    "Small neural network (MLP) on binary inputs. Pins come from its layers; it is compiled into lookup tables, " +
                    "so it costs one gate per output. Train it from a truth table or another IC.",
                    0, 0, 0, null, new[] { "out0" }).Alias("mlp", "nn", "network", "neuralnetwork").Props("layers (e.g. [3, 8, 2]), activation (sigmoid/tanh/relu), seed; pins in0.. and out0.. follow the layers")
                    .Pins((c, _) => c.Network?.InputCount ?? 0, (c, _) => c.Network?.OutputCount ?? 0),
            };

            var dict = new Dictionary<ComponentKind, ComponentSpec>();
            foreach (var spec in list)
                dict.Add(spec.Kind, spec);
            return dict;
        }
    }
}
