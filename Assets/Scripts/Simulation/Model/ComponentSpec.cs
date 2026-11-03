using System;
using System.Collections.Generic;

namespace LogicSim.Simulation
{
    /// <summary>
    /// Static description of a component kind: pin layout, naming and role in the simulation.
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
        }

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
                new ComponentSpec(ComponentKind.Buffer, "Buffer", ComponentCategory.Gate, "Copies its input to the output.", 1, 1, 1, new[] { "in" }, output),
                new ComponentSpec(ComponentKind.Not, "NOT", ComponentCategory.Gate, "Inverts its input.", 1, 1, 1, new[] { "in" }, output),
                new ComponentSpec(ComponentKind.And, "AND", ComponentCategory.Gate, "1 when all inputs are 1.", 1, MaxGateInputs, 2, null, output),
                new ComponentSpec(ComponentKind.Or, "OR", ComponentCategory.Gate, "1 when any input is 1.", 1, MaxGateInputs, 2, null, output),
                new ComponentSpec(ComponentKind.Nand, "NAND", ComponentCategory.Gate, "0 when all inputs are 1.", 1, MaxGateInputs, 2, null, output),
                new ComponentSpec(ComponentKind.Nor, "NOR", ComponentCategory.Gate, "1 when all inputs are 0.", 1, MaxGateInputs, 2, null, output),
                new ComponentSpec(ComponentKind.Xor, "XOR", ComponentCategory.Gate, "1 when an odd number of inputs is 1.", 1, MaxGateInputs, 2, null, output),
                new ComponentSpec(ComponentKind.Xnor, "XNOR", ComponentCategory.Gate, "1 when an even number of inputs is 1.", 1, MaxGateInputs, 2, null, output),

                new ComponentSpec(ComponentKind.Switch, "Switch", ComponentCategory.Input, "Toggle input. Becomes an input pin when used inside a subcircuit.", 0, 0, 0, null, output, isInterfaceInput: true),
                new ComponentSpec(ComponentKind.Button, "Button", ComponentCategory.Input, "Momentary input, 1 only while pressed. Becomes an input pin inside a subcircuit.", 0, 0, 0, null, output, isInterfaceInput: true),
                new ComponentSpec(ComponentKind.Clock, "Clock", ComponentCategory.Input, "Square wave that toggles every 'halfPeriod' ticks.", 0, 0, 0, null, output),
                new ComponentSpec(ComponentKind.Constant, "Constant", ComponentCategory.Input, "Constant 0 or 1.", 0, 0, 0, null, output),

                new ComponentSpec(ComponentKind.Bulb, "Bulb", ComponentCategory.Output, "Lights up when its input is 1. Becomes an output pin inside a subcircuit.", 1, 1, 1, new[] { "in" }, Array.Empty<string>(), isInterfaceOutput: true),
                new ComponentSpec(ComponentKind.HexDisplay, "Hex display", ComponentCategory.Output, "Shows a 4-bit value (b0 is the least significant bit).", 4, 4, 4, new[] { "b0", "b1", "b2", "b3" }, Array.Empty<string>()),

                new ComponentSpec(ComponentKind.DFlipFlop, "D flip-flop", ComponentCategory.Sequential, "Stores D on the rising edge of CLK.", 2, 2, 2, new[] { "D", "CLK" }, new[] { "Q", "nQ" }),
                new ComponentSpec(ComponentKind.TFlipFlop, "T flip-flop", ComponentCategory.Sequential, "Toggles on the rising edge of CLK when T is 1.", 2, 2, 2, new[] { "T", "CLK" }, new[] { "Q", "nQ" }),
                new ComponentSpec(ComponentKind.JKFlipFlop, "JK flip-flop", ComponentCategory.Sequential, "J sets, K resets, both toggle on the rising edge of CLK.", 3, 3, 3, new[] { "J", "K", "CLK" }, new[] { "Q", "nQ" }),

                new ComponentSpec(ComponentKind.PinIn, "Input pin", ComponentCategory.Subcircuit, "Subcircuit input. At the top level it behaves like an externally driven input.", 0, 0, 0, null, output, isInterfaceInput: true),
                new ComponentSpec(ComponentKind.PinOut, "Output pin", ComponentCategory.Subcircuit, "Subcircuit output. At the top level it behaves like a probe.", 1, 1, 1, new[] { "in" }, Array.Empty<string>(), isInterfaceOutput: true),
                new ComponentSpec(ComponentKind.Subcircuit, "Subcircuit", ComponentCategory.Subcircuit, "Instance of a custom circuit. Pins come from its definition.", 0, 0, 0, null, Array.Empty<string>()),
            };

            var dict = new Dictionary<ComponentKind, ComponentSpec>();
            foreach (var spec in list)
                dict.Add(spec.Kind, spec);
            return dict;
        }
    }
}
