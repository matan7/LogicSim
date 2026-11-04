using System;
using System.Globalization;
using System.Linq;
using LogicSim.Simulation;
using Newtonsoft.Json.Linq;

namespace LogicSim.Mcp
{
    // Argument parsing, component and pin lookup, result descriptions.
    public sealed partial class LogicSimTools
    {
        private CircuitComponent CreateComponent(JObject spec, out bool hasPosition)
        {
            var typeText = (string)spec["type"];
            if (!CircuitSerializer.TryParseKind(typeText, out var kind))
                throw new McpToolException($"Unknown component type '{typeText}'. Use list_component_types.");
            hasPosition = spec["x"] != null && spec["y"] != null;
            float x = spec["x"] != null ? (float)spec["x"] : 0;
            float y = spec["y"] != null ? (float)spec["y"] : 0;
            // build_circuit keys double as labels so later calls can refer to the same names.
            var label = (string)spec["label"] ?? (string)spec["key"];

            CircuitComponent component;
            if (kind == ComponentKind.Subcircuit)
            {
                var id = (string)spec["subcircuit"];
                if (string.IsNullOrEmpty(id) || !Circuit.Library.TryGet(id, out _))
                    throw new McpToolException($"Unknown subcircuit '{id}'. Use list_subcircuits.");
                if (!Hierarchy.CanPlace(id))
                    throw new McpToolException($"'{id}' cannot be placed here: the open IC would contain itself");
                component = ComponentFactory.CreateSubcircuit(id, x, y, label);
            }
            else
            {
                component = ComponentFactory.Create(kind, x, y, label,
                    inputs: spec["inputs"] != null ? (int)spec["inputs"] : (int?)null,
                    clockHalfPeriod: spec["half_period"] != null ? (int)spec["half_period"] : (int?)null,
                    value: spec["value"] != null ? ParseBool(spec["value"]) : (bool?)null,
                    rotation: spec["rotation"] != null ? (int)spec["rotation"] : 0);
                var properties = component.GetProperties();
                try
                {
                    ApplyNeuralArguments(component, spec, ref properties);
                }
                catch (CircuitException exception)
                {
                    throw new McpToolException(exception.Message);
                }
                if (properties.Neuron != null && component.IsNeuron)
                    ComponentFactory.SetNeuron(component, properties.Neuron);
                if (properties.Network != null && kind == ComponentKind.NeuralBlock)
                    ComponentFactory.SetNetwork(component, properties.Network);
            }
            return component;
        }

        private float NextFreeX()
        {
            return Circuit.ComponentCount == 0 ? 0 : (float)Math.Ceiling(Circuit.Components.Max(c => c.X) + AutoLayout.ColumnSpacing);
        }

        private CircuitComponent ResolveComponent(JToken token) => ResolveComponent(Circuit, token);

        private CircuitComponent ResolveComponent(Circuit circuit, JToken token)
        {
            if (token == null)
                throw new McpToolException("Missing component reference");
            if (token.Type == JTokenType.Integer)
            {
                int id = (int)token;
                if (circuit.TryGetComponent(id, out var byId))
                    return byId;
                throw new McpToolException($"Component {id} does not exist");
            }
            return ResolveComponent(circuit, (string)token);
        }

        private CircuitComponent ResolveComponent(string reference) => ResolveComponent(Circuit, reference);

        private CircuitComponent ResolveComponent(Circuit circuit, string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                throw new McpToolException("Missing component reference");
            var text = reference.Trim().TrimStart('#');
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && circuit.TryGetComponent(id, out var byId))
                return byId;
            var byLabel = circuit.FindByLabel(reference.Trim());
            if (byLabel != null)
                return byLabel;
            throw new McpToolException($"No component with id or label '{reference}'");
        }

        /// <summary>Parses "component.pin"; the pin defaults to the first output or the first free input.</summary>
        /// <param name="checkCircuit">Treat inputs wired in the current circuit as taken (false when the circuit is being replaced).</param>
        private PinRef ParsePin(string text, bool output, Func<string, CircuitComponent> lookup, Func<CircuitComponent, int, bool> isTaken, bool checkCircuit = true)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new McpToolException("Missing pin reference");
            text = text.Trim();

            CircuitComponent component = null;
            string pinName = null;
            int dot = text.LastIndexOf('.');
            if (dot > 0)
            {
                try
                {
                    component = lookup(text.Substring(0, dot));
                    pinName = text.Substring(dot + 1);
                }
                catch (McpToolException)
                {
                    component = null;
                }
            }
            if (component == null)
            {
                component = lookup(text);
                pinName = null;
            }

            int count = output ? Circuit.GetOutputCount(component) : Circuit.GetInputCount(component);
            if (count == 0)
                throw new McpToolException($"{component} has no {(output ? "outputs" : "inputs")}");

            int pin;
            if (pinName == null)
            {
                pin = 0;
                if (!output)
                {
                    pin = -1;
                    for (int i = 0; i < count; i++)
                    {
                        bool taken = (isTaken != null && isTaken(component, i)) || (checkCircuit && Circuit.GetWireInto(component.Id, i) != null);
                        if (!taken)
                        {
                            pin = i;
                            break;
                        }
                    }
                    if (pin < 0)
                        throw new McpToolException($"All inputs of {component} are connected");
                }
            }
            else
            {
                pin = Circuit.FindPin(component, pinName, output);
                if (pin < 0)
                {
                    var names = Enumerable.Range(0, count).Select(i => output ? Circuit.GetOutputName(component, i) : Circuit.GetInputName(component, i));
                    throw new McpToolException($"{component} has no {(output ? "output" : "input")} '{pinName}' (available: {string.Join(", ", names)})");
                }
            }
            return new PinRef(component.Id, pin);
        }

        private (CircuitComponent component, int pin, bool output) ParseAnyPin(string text)
        {
            int dot = text.LastIndexOf('.');
            if (dot > 0)
            {
                var component = ResolveComponent(text.Substring(0, dot));
                var name = text.Substring(dot + 1);
                int pin = Circuit.FindPin(component, name, true);
                if (pin >= 0)
                    return (component, pin, true);
                pin = Circuit.FindPin(component, name, false);
                if (pin >= 0)
                    return (component, pin, false);
                throw new McpToolException($"{component} has no pin '{name}'");
            }
            var single = ResolveComponent(text);
            return Circuit.GetOutputCount(single) > 0 ? (single, 0, true) : (single, 0, false);
        }

        private JObject SignalSummary(Circuit circuit, ISignalReader session)
        {
            var inputs = new JObject();
            var outputs = new JObject();
            var displays = new JObject();
            foreach (var component in circuit.Components.OrderByDescending(c => c.Y).ThenBy(c => c.X))
            {
                if (component.Spec.IsInterfaceInput)
                    inputs[NameOf(component)] = session.GetOutputValue(component.Id, 0) ? 1 : 0;
                else if (component.Spec.IsInterfaceOutput)
                    outputs[NameOf(component)] = session.GetInputPinValue(component.Id, 0) ? 1 : 0;
                else if (component.Kind == ComponentKind.HexDisplay)
                    displays[NameOf(component)] = session.GetInputBits(component).ToString("X");
            }
            var result = new JObject { ["inputs"] = inputs, ["outputs"] = outputs };
            if (displays.HasValues)
                result["displays"] = displays;
            return result;
        }

        private void AddDiagnostics(JObject result)
        {
            var compiled = _workspace.Session.Compiled;
            if (compiled != null && compiled.Diagnostics.Count > 0)
                result["diagnostics"] = new JArray(compiled.Diagnostics.Select(d => d.ToString()));
        }

        private JObject Describe(CircuitComponent component, ISignalReader signals)
        {
            bool values = signals != null;
            var entry = new JObject
            {
                ["id"] = component.Id,
                ["type"] = component.Kind.ToString(),
                ["x"] = component.X,
                ["y"] = component.Y
            };
            if (!string.IsNullOrEmpty(component.Label))
                entry["label"] = component.Label;
            if (component.Rotation != 0)
                entry["rotation"] = component.Rotation;
            if (component.Kind == ComponentKind.Clock)
                entry["half_period"] = component.ClockHalfPeriod;
            if (component.Kind == ComponentKind.Constant || component.Kind == ComponentKind.Switch || component.Kind == ComponentKind.PinIn)
                entry["value"] = component.Value ? 1 : 0;
            if (component.Kind == ComponentKind.Subcircuit)
                entry["subcircuit"] = component.SubcircuitId;
            if (component.IsNeuron && component.Neuron != null)
            {
                entry["weights"] = new JArray(component.Neuron.Weights);
                entry["threshold"] = component.Neuron.Threshold;
                if (component.Kind == ComponentKind.SpikingNeuron)
                {
                    entry["leak"] = component.Neuron.Leak;
                    entry["reset"] = component.Neuron.Reset;
                    entry["refractory"] = component.Neuron.Refractory;
                }
                if (signals != null && signals.TryGetNeuronPotential(component, out float potential))
                    entry["potential"] = Math.Round(potential, 4);
            }
            if (component.Kind == ComponentKind.NeuralBlock && component.Network != null)
            {
                entry["layers"] = new JArray(component.Network.LayerSizes);
                entry["activation"] = component.Network.Activation.ToString().ToLowerInvariant();
            }

            int inputCount = Circuit.GetInputCount(component);
            if (inputCount > 0)
            {
                var inputs = new JArray();
                for (int pin = 0; pin < inputCount; pin++)
                {
                    var pinEntry = new JObject { ["pin"] = pin, ["name"] = Circuit.GetInputName(component, pin) };
                    var wire = Circuit.GetWireInto(component.Id, pin);
                    if (wire != null)
                    {
                        var source = Circuit.GetComponent(wire.From.ComponentId);
                        pinEntry["from"] = $"{source.Id}.{Circuit.GetOutputName(source, wire.From.Pin)}";
                    }
                    if (values)
                        pinEntry["value"] = signals.GetInputPinValue(component.Id, pin) ? 1 : 0;
                    inputs.Add(pinEntry);
                }
                entry["inputs"] = inputs;
            }

            int outputCount = Circuit.GetOutputCount(component);
            if (outputCount > 0)
            {
                var outputs = new JArray();
                for (int pin = 0; pin < outputCount; pin++)
                {
                    var pinEntry = new JObject { ["pin"] = pin, ["name"] = Circuit.GetOutputName(component, pin) };
                    if (values)
                        pinEntry["value"] = signals.GetOutputValue(component.Id, pin) ? 1 : 0;
                    outputs.Add(pinEntry);
                }
                entry["outputs"] = outputs;
            }
            return entry;
        }

        private JObject DescribeWire(Wire wire, ISignalReader signals)
        {
            var from = Circuit.GetComponent(wire.From.ComponentId);
            var to = Circuit.GetComponent(wire.To.ComponentId);
            var entry = new JObject
            {
                ["id"] = wire.Id,
                ["from"] = $"{from.Id}.{Circuit.GetOutputName(from, wire.From.Pin)}",
                ["to"] = $"{to.Id}.{Circuit.GetInputName(to, wire.To.Pin)}"
            };
            if (signals != null)
                entry["value"] = signals.GetWireValue(wire) ? 1 : 0;
            return entry;
        }

        private static JObject DescribeDefinition(SubcircuitDefinition definition)
        {
            return new JObject
            {
                ["id"] = definition.Id,
                ["name"] = definition.Name,
                ["inputs"] = new JArray(definition.InputNames),
                ["outputs"] = new JArray(definition.OutputNames),
                ["components"] = definition.Circuit.ComponentCount
            };
        }

        private static string NameOf(CircuitComponent component)
        {
            return string.IsNullOrEmpty(component.Label) ? $"{component.Kind}#{component.Id}" : component.Label;
        }

        private static bool ParseBool(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Boolean: return (bool)token;
                case JTokenType.Integer: return (long)token != 0;
                case JTokenType.Float: return (double)token != 0;
                case JTokenType.String:
                    var text = ((string)token).Trim().ToLowerInvariant();
                    if (text == "1" || text == "true" || text == "on" || text == "high")
                        return true;
                    if (text == "0" || text == "false" || text == "off" || text == "low")
                        return false;
                    break;
            }
            throw new McpToolException($"'{token}' is not a boolean (use 0/1 or true/false)");
        }

        private static bool Bool(JObject args, string name, bool fallback)
        {
            var token = args[name];
            return token == null || token.Type == JTokenType.Null ? fallback : ParseBool(token);
        }

        private static JToken Required(JObject args, string name)
        {
            var token = args[name];
            if (token == null || token.Type == JTokenType.Null)
                throw new McpToolException($"Missing argument '{name}'");
            return token;
        }

        private static double RequiredNumber(JObject args, string name)
        {
            var token = Required(args, name);
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new McpToolException($"'{name}' must be a number");
            return (double)token;
        }
    }
}
