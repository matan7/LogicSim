using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LogicSim.Simulation
{
    public sealed class TruthTableRow
    {
        public bool[] Inputs { get; }
        public bool[] Outputs { get; }

        /// <summary>False when the circuit did not settle (oscillation or very deep logic).</summary>
        public bool Stable { get; }

        public TruthTableRow(bool[] inputs, bool[] outputs, bool stable)
        {
            Inputs = inputs;
            Outputs = outputs;
            Stable = stable;
        }
    }

    public sealed class TruthTable
    {
        public IReadOnlyList<string> InputNames { get; }
        public IReadOnlyList<string> OutputNames { get; }
        public IReadOnlyList<TruthTableRow> Rows { get; }

        public TruthTable(IReadOnlyList<string> inputNames, IReadOnlyList<string> outputNames, IReadOnlyList<TruthTableRow> rows)
        {
            InputNames = inputNames;
            OutputNames = outputNames;
            Rows = rows;
        }

        public bool AllStable => Rows.All(r => r.Stable);

        /// <summary>Plain text table, e.g. for a UI panel or an MCP response.</summary>
        public string ToText()
        {
            var columns = InputNames.Concat(OutputNames).ToList();
            var widths = columns.Select(c => Math.Max(1, c.Length)).ToList();
            var sb = new StringBuilder();
            for (int i = 0; i < columns.Count; i++)
            {
                if (i == InputNames.Count)
                    sb.Append("| ");
                sb.Append(columns[i].PadRight(widths[i])).Append(' ');
            }
            sb.AppendLine();
            foreach (var row in Rows)
            {
                var values = row.Inputs.Concat(row.Outputs).ToList();
                for (int i = 0; i < values.Count; i++)
                {
                    if (i == InputNames.Count)
                        sb.Append("| ");
                    sb.Append((values[i] ? "1" : "0").PadRight(widths[i])).Append(' ');
                }
                if (!row.Stable)
                    sb.Append(" (unstable)");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    public static class TruthTableGenerator
    {
        public const int MaxInputs = 12;
        public const long DefaultMaxTicksPerRow = 10000;

        /// <summary>Default inputs: top-level switches, buttons and input pins, top to bottom.</summary>
        public static List<CircuitComponent> DefaultInputs(Circuit circuit)
        {
            return circuit.Components.Where(c => c.Spec.IsInterfaceInput)
                .OrderByDescending(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Id).ToList();
        }

        /// <summary>Default outputs: top-level bulbs and output pins, top to bottom.</summary>
        public static List<CircuitComponent> DefaultOutputs(Circuit circuit)
        {
            return circuit.Components.Where(c => c.Spec.IsInterfaceOutput)
                .OrderByDescending(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Id).ToList();
        }

        /// <summary>
        /// Evaluates every input combination from a reset state (combinational behaviour).
        /// The first input is the most significant bit of the row index.
        /// </summary>
        public static TruthTable Generate(Circuit circuit, IList<CircuitComponent> inputs = null, IList<CircuitComponent> outputs = null,
            long maxTicksPerRow = DefaultMaxTicksPerRow)
        {
            inputs = inputs ?? DefaultInputs(circuit);
            outputs = outputs ?? DefaultOutputs(circuit);
            if (inputs.Count > MaxInputs)
                throw new CircuitException($"Truth tables are limited to {MaxInputs} inputs");
            foreach (var input in inputs)
                if (!input.Spec.IsInterfaceInput)
                    throw new CircuitException($"{input} is not an input");

            var compiled = CircuitCompiler.Compile(circuit);
            var slots = inputs.Select(c => compiled.GetInputSlot(c.Id)).ToArray();
            var rows = new List<TruthTableRow>();

            using (var engine = new ManagedSimulationEngine())
            {
                engine.Load(compiled);
                int rowCount = 1 << inputs.Count;
                for (int row = 0; row < rowCount; row++)
                {
                    engine.Reset();
                    var inputValues = new bool[inputs.Count];
                    for (int i = 0; i < inputs.Count; i++)
                    {
                        inputValues[i] = ((row >> (inputs.Count - 1 - i)) & 1) != 0;
                        if (slots[i] >= 0)
                            engine.SetInput(slots[i], inputValues[i]);
                    }

                    bool stable = engine.Settle(maxTicksPerRow);
                    var outputValues = new bool[outputs.Count];
                    for (int o = 0; o < outputs.Count; o++)
                        outputValues[o] = ReadDisplayValue(circuit, compiled, engine, outputs[o]);
                    rows.Add(new TruthTableRow(inputValues, outputValues, stable));
                }
            }

            return new TruthTable(inputs.Select(NameOf).ToList(), outputs.Select(NameOf).ToList(), rows);
        }

        private static bool ReadDisplayValue(Circuit circuit, CompiledCircuit compiled, ISimulationEngine engine, CircuitComponent component)
        {
            if (component.Spec.IsSink)
                return engine.GetNet(compiled.GetInputNet(component.Id, 0));
            if (circuit.GetOutputCount(component) > 0)
                return engine.GetNet(compiled.GetOutputNet(component.Id, 0));
            return false;
        }

        private static string NameOf(CircuitComponent component)
        {
            return string.IsNullOrEmpty(component.Label) ? component.Kind.ToString() + component.Id : component.Label;
        }
    }
}
