using System;
using System.Collections.Generic;
using System.Linq;
using LogicSim.Simulation;
using Newtonsoft.Json.Linq;

namespace LogicSim.Mcp
{
    // Editing tools: build_circuit, add/remove/move components, properties, wires, new_circuit.
    public sealed partial class LogicSimTools
    {
        private McpToolResult BuildCircuit(JObject args)
        {
            bool clear = Bool(args, "clear", false);
            var componentSpecs = args["components"] as JArray ?? new JArray();
            var connectionSpecs = args["connections"] as JArray ?? new JArray();

            var created = new List<CircuitComponent>();
            var byKey = new Dictionary<string, CircuitComponent>(StringComparer.OrdinalIgnoreCase);
            var positioned = new HashSet<CircuitComponent>();
            int nextId = 1;
            foreach (var token in componentSpecs)
            {
                if (!(token is JObject spec))
                    throw new McpToolException("Each component must be an object");
                var component = CreateComponent(spec, out bool hasPosition);
                if (clear)
                    ComponentFactory.SetDetachedId(component, nextId++);
                else
                    ComponentFactory.ReserveId(Circuit, component);
                created.Add(component);
                if (hasPosition)
                    positioned.Add(component);

                var key = (string)spec["key"] ?? (string.IsNullOrEmpty(component.Label) ? null : component.Label);
                if (key != null)
                {
                    if (byKey.ContainsKey(key))
                        throw new McpToolException($"Duplicate component key '{key}'");
                    byKey[key] = component;
                }
            }

            CircuitComponent Lookup(string reference)
            {
                if (byKey.TryGetValue(reference, out var component))
                    return component;
                if (!clear)
                    return ResolveComponent(reference);
                throw new McpToolException($"Unknown component '{reference}'");
            }

            var wires = new List<Wire>();
            var drivenInputs = new HashSet<(int, int)>();
            int nextWireId = 1;
            foreach (var token in connectionSpecs)
            {
                string from, to;
                if (token is JArray pair && pair.Count == 2)
                {
                    from = (string)pair[0];
                    to = (string)pair[1];
                }
                else if (token is JObject obj)
                {
                    from = (string)obj["from"];
                    to = (string)obj["to"];
                }
                else
                {
                    throw new McpToolException("Connections must be {\"from\", \"to\"} objects or [from, to] pairs");
                }

                var source = ParsePin(from, output: true, Lookup, null, checkCircuit: !clear);
                var target = ParsePin(to, output: false, Lookup, (component, pin) => drivenInputs.Contains((component.Id, pin)), checkCircuit: !clear);
                if (!drivenInputs.Add((target.ComponentId, target.Pin)) || (!clear && Circuit.GetWireInto(target.ComponentId, target.Pin) != null))
                    throw new McpToolException($"Input '{to}' is connected more than once");
                var wire = new Wire(clear ? nextWireId++ : Circuit.ReserveWireId(), source, target);
                wires.Add(wire);
            }

            var unplaced = created.Where(c => !positioned.Contains(c)).ToList();
            if (unplaced.Count > 0)
            {
                float originX = clear ? 0 : NextFreeX();
                AutoLayout.Arrange(unplaced, wires, originX, 0);
            }

            ICircuitEdit edit;
            if (clear)
            {
                var snapshot = new CircuitSnapshot(created, wires);
                edit = new ReplaceCircuitEdit(Circuit, snapshot, "Build circuit");
                var name = (string)args["name"];
                if (!string.IsNullOrWhiteSpace(name))
                    _workspace.DocumentName = name;
            }
            else
            {
                edit = new AddElementsEdit(Circuit, created, wires, "Build circuit");
            }
            _workspace.Editor.Apply(edit);
            _workspace.Focus(created.Select(c => c.Id));

            var keys = new JObject();
            foreach (var pair in byKey)
                keys[pair.Key] = pair.Value.Id;
            return McpToolResult.Ok(new JObject
            {
                ["created_components"] = created.Count,
                ["created_wires"] = wires.Count,
                ["ids"] = keys,
                ["components"] = new JArray(created.Select(c => Describe(Circuit.GetComponent(c.Id), null)))
            });
        }

        private McpToolResult AddComponent(JObject args)
        {
            var component = CreateComponent(args, out bool hasPosition);
            if (!hasPosition)
                ComponentFactory.SetDetachedPosition(component, NextFreeX(), 0);
            _workspace.Editor.Apply(AddElementsEdit.Single(Circuit, component));
            _workspace.Focus(new[] { component.Id });
            return McpToolResult.Ok(Describe(component, null));
        }

        private McpToolResult RemoveComponents(JObject args)
        {
            var components = (args["components"] as JArray ?? new JArray()).Select(t => ResolveComponent(t).Id).ToList();
            var wires = (args["wires"] as JArray ?? new JArray()).Select(t => (int)t).ToList();
            foreach (var id in wires)
                if (!Circuit.TryGetWire(id, out _))
                    throw new McpToolException($"Wire {id} does not exist");
            var edit = new RemoveElementsEdit(Circuit, components, wires);
            if (edit.IsEmpty)
                throw new McpToolException("Nothing to remove");
            _workspace.Editor.Apply(edit);
            return McpToolResult.Ok(new { removed_components = components.Count, removed_wires = wires.Count, remaining_components = Circuit.ComponentCount });
        }

        private McpToolResult MoveComponent(JObject args)
        {
            var component = ResolveComponent(Required(args, "component"));
            float x = (float)RequiredNumber(args, "x");
            float y = (float)RequiredNumber(args, "y");
            _workspace.Editor.Apply(MoveComponentsEdit.To(Circuit, new[] { component.Id }, new[] { (x, y) }));
            return McpToolResult.Ok(Describe(component, null));
        }

        private McpToolResult SetProperties(JObject args)
        {
            var component = ResolveComponent(Required(args, "component"));
            var properties = component.GetProperties();
            if (args["label"] != null)
                properties.Label = (string)args["label"];
            if (args["rotation"] != null)
                properties.Rotation = (int)args["rotation"];
            if (args["inputs"] != null)
                properties.InputCount = (int)args["inputs"];
            if (args["half_period"] != null)
                properties.ClockHalfPeriod = (int)args["half_period"];
            if (args["value"] != null)
                properties.Value = ParseBool(args["value"]);
            try
            {
                ApplyNeuralArguments(component, args, ref properties);
                _workspace.Editor.Apply(new SetPropertiesEdit(Circuit, component.Id, properties));
            }
            catch (CircuitException exception)
            {
                throw new McpToolException(exception.Message);
            }
            return McpToolResult.Ok(Describe(component, null));
        }

        private McpToolResult Connect(JObject args)
        {
            var from = ParsePin((string)Required(args, "from"), output: true, ResolveComponent, null);
            var to = ParsePin((string)Required(args, "to"), output: false, ResolveComponent, null);
            bool replace = Bool(args, "replace", false);
            if (!replace && Circuit.GetWireInto(to.ComponentId, to.Pin) != null)
                throw new McpToolException($"Input {(string)args["to"]} is already connected; pass replace=true to rewire it");
            if (!Circuit.CanConnect(from, to, out var error) && !(replace && Circuit.GetWireInto(to.ComponentId, to.Pin) != null))
                throw new McpToolException(error);
            var edit = new ConnectEdit(Circuit, from, to);
            _workspace.Editor.Apply(edit);
            return McpToolResult.Ok(DescribeWire(edit.Wire, null));
        }

        private McpToolResult Disconnect(JObject args)
        {
            Wire wire;
            if (args["wire"] != null)
            {
                if (!Circuit.TryGetWire((int)args["wire"], out wire))
                    throw new McpToolException($"Wire {(int)args["wire"]} does not exist");
            }
            else if (args["to"] != null)
            {
                var to = ParsePin((string)args["to"], output: false, ResolveComponent, null);
                wire = Circuit.GetWireInto(to.ComponentId, to.Pin) ?? throw new McpToolException($"{(string)args["to"]} is not connected");
            }
            else
            {
                throw new McpToolException("Pass 'wire' or 'to'");
            }
            _workspace.Editor.Apply(new RemoveElementsEdit(Circuit, Array.Empty<int>(), new[] { wire.Id }));
            return McpToolResult.Ok(new { removed_wire = wire.Id });
        }

        private McpToolResult NewCircuit(JObject args)
        {
            Hierarchy.CloseTo(0, apply: true);
            _workspace.Editor.Apply(new ReplaceCircuitEdit(Circuit, CircuitSnapshot.Empty(), "New circuit"));
            _workspace.DocumentName = (string)args["name"] ?? "Untitled";
            return McpToolResult.Ok(new { name = _workspace.DocumentName });
        }
    }
}
