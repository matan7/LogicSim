using System.Linq;
using LogicSim.Simulation;
using Newtonsoft.Json.Linq;

namespace LogicSim.Mcp
{
    // Inspection tools: get_circuit, list_component_types, get_selection.
    public sealed partial class LogicSimTools
    {
        private McpToolResult GetCircuit(JObject args)
        {
            bool values = Bool(args, "include_values", false);
            ISignalReader signals = null;
            string valuesNote = null;
            if (values)
                signals = CurrentSignals(out valuesNote);
            var result = new JObject
            {
                ["name"] = _workspace.DocumentName,
                ["level"] = DescribeLevel(),
                ["components"] = new JArray(Circuit.Components.Select(c => Describe(c, signals))),
                ["wires"] = new JArray(Circuit.Wires.Select(w => DescribeWire(w, signals))),
                ["subcircuits"] = new JArray(Circuit.Library.Definitions.Select(DescribeDefinition)),
                ["counts"] = new JObject { ["components"] = Circuit.ComponentCount, ["wires"] = Circuit.WireCount }
            };
            if (valuesNote != null)
                result["values_note"] = valuesNote;
            return McpToolResult.Ok(result);
        }

        /// <summary>Values for the current level, or null with the reason why they are not available.</summary>
        private ISignalReader CurrentSignals(out string unavailableReason)
        {
            var session = _workspace.Session;
            session.EnsureCompiled();
            unavailableReason = null;
            if (Hierarchy.IsAtRoot)
                return session;
            var level = Hierarchy.Current;
            if (Hierarchy.IsModified(level))
            {
                unavailableReason = "The open IC has changes that are not applied yet (close_subcircuit or run_simulation applies them).";
                return null;
            }
            var path = Hierarchy.GetInstancePath(level);
            if (path == null)
            {
                unavailableReason = "The IC was opened from the library, not through an instance, so it has no values.";
                return null;
            }
            var reader = session.CreateReader(path);
            if (reader is InstanceSignalReader instance && !instance.IsValid)
            {
                unavailableReason = "The instance is not part of the compiled circuit.";
                return null;
            }
            return reader;
        }

        private JObject DescribeLevel()
        {
            var levels = Hierarchy.Levels;
            var path = new JArray(levels.Select(l => Hierarchy.GetName(l)));
            var entry = new JObject { ["path"] = path, ["depth"] = Hierarchy.Depth };
            if (!Hierarchy.IsAtRoot)
            {
                var current = Hierarchy.Current;
                entry["subcircuit"] = current.DefinitionId;
                entry["modified"] = Hierarchy.IsModified(current);
                if (current.InstanceId != 0)
                    entry["instance"] = current.InstanceId;
            }
            return entry;
        }

        private McpToolResult ListComponentTypes(JObject args)
        {
            var types = new JArray();
            foreach (var spec in ComponentSpec.All)
            {
                var entry = new JObject
                {
                    ["type"] = spec.Kind.ToString(),
                    ["name"] = spec.DisplayName,
                    ["category"] = spec.Category.ToString(),
                    ["description"] = spec.Description,
                    ["outputs"] = new JArray(Enumerable.Range(0, spec.OutputCount).Select(spec.GetOutputName))
                };
                if (spec.HasVariableInputs)
                {
                    entry["inputs"] = $"{spec.MinInputs}-{spec.MaxInputs} (default {spec.DefaultInputs}), named in0..inN";
                }
                else
                {
                    entry["inputs"] = new JArray(Enumerable.Range(0, spec.DefaultInputs).Select(spec.GetInputName));
                }
                if (spec.Kind == ComponentKind.Clock)
                    entry["properties"] = "half_period (ticks, default " + CircuitComponent.DefaultClockHalfPeriod + ")";
                else if (spec.Kind == ComponentKind.Constant)
                    entry["properties"] = "value (true/false)";
                else if (spec.Kind == ComponentKind.Subcircuit)
                    entry["properties"] = "subcircuit (definition id); pins come from the definition";
                else if (spec.Kind == ComponentKind.Neuron)
                    entry["properties"] = "weights (one per input), threshold";
                else if (spec.Kind == ComponentKind.SpikingNeuron)
                    entry["properties"] = "weights, threshold, leak (0-1), reset, refractory (ticks)";
                else if (spec.Kind == ComponentKind.NeuralBlock)
                {
                    entry["properties"] = "layers (e.g. [3, 8, 2]), activation (sigmoid/tanh/relu), seed; pins in0.. and out0.. follow the layers";
                    entry["inputs"] = $"1-{NeuralNetwork.MaxInputs} (from layers)";
                }
                types.Add(entry);
            }
            return McpToolResult.Ok(new JObject { ["types"] = types });
        }

        private McpToolResult GetSelection(JObject args)
        {
            var selected = _workspace.SelectedComponentIds.Where(id => Circuit.TryGetComponent(id, out _)).Select(id => Circuit.GetComponent(id));
            return McpToolResult.Ok(new JObject { ["components"] = new JArray(selected.Select(c => Describe(c, null))) });
        }
    }
}
