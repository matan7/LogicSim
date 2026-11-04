using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LogicSim.Simulation;
using Newtonsoft.Json.Linq;

namespace LogicSim.Mcp
{
    /// <summary>The MCP tool set: build, edit, simulate and analyse circuits in a <see cref="ILogicSimWorkspace"/>.</summary>
    public sealed class LogicSimTools
    {
        public const string ServerName = "logicsim";
        public const string ServerVersion = "1.0.0";

        public const string Instructions =
            "LogicSim is a digital logic simulator. Build circuits from gates, inputs (Switch, Button, Clock, Constant) and outputs " +
            "(Bulb, HexDisplay), simulate them and inspect the results. Prefer build_circuit to create several components and wires " +
            "in one step; positions are optional and are laid out automatically. Pins are referenced as '<component>.<pin>' where " +
            "<component> is a numeric id, a label or a build_circuit key and <pin> is a pin name (in0, in1, out, D, CLK, Q, nQ, ...) or an index; " +
            "the pin may be omitted for the first output / first free input. The simulation is unit-delay: each gate needs one tick, " +
            "run_simulation with settle=true runs until the circuit is stable. Switch, Button, PinIn and Bulb, PinOut components become " +
            "the pins of a subcircuit created with create_subcircuit; collapse_to_subcircuit turns a selection into an IC in place. " +
            "open_subcircuit edits an IC in place: the editing tools then work on the IC's contents until close_subcircuit writes the " +
            "changes back to every instance (pins keep their wires when moved or renamed). The simulation always runs the whole " +
            "document; inside an IC opened through an instance, read_signals and get_circuit show the values of that instance. " +
            "Neurons: Neuron (perceptron: weights + threshold), SpikingNeuron (leaky integrate-and-fire: weights, threshold, leak, reset, " +
            "refractory; emits one-tick spikes) and NeuralBlock (small neural network given by 'layers', compiled to lookup tables). " +
            "train_component learns their weights from a truth table, explicit rows or another IC. Edits are undoable with undo.";

        private readonly ILogicSimWorkspace _workspace;

        private Circuit Circuit => _workspace.Circuit;
        private CircuitHierarchy Hierarchy => _workspace.Hierarchy;
        private Circuit Root => _workspace.Hierarchy.Root;

        public LogicSimTools(ILogicSimWorkspace workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        }

        public static McpServer CreateServer(ILogicSimWorkspace workspace)
        {
            var server = new McpServer(ServerName, ServerVersion) { Instructions = Instructions };
            new LogicSimTools(workspace).Register(server);
            return server;
        }

        public void Register(McpServer server)
        {
            // Inspection
            server.AddTool(new McpTool("get_circuit", "Get circuit",
                "Returns all components (with pins and properties), wires and subcircuit definitions of the open circuit.",
                Schema(Prop("include_values", "boolean", "Include current signal values of pins and wires.")),
                GetCircuit, readOnly: true));
            server.AddTool(new McpTool("list_component_types", "List component types",
                "Lists every component type with its pins and configurable properties.",
                Schema(), ListComponentTypes, readOnly: true));
            server.AddTool(new McpTool("get_selection", "Get selection",
                "Returns the components the user currently has selected in the editor (empty in headless mode).",
                Schema(), GetSelection, readOnly: true));

            // Editing
            server.AddTool(new McpTool("build_circuit", "Build circuit",
                "Creates several components and connections in one undoable step. Components get a 'key' used to reference them in " +
                "connections (defaults to the label). Connections are {\"from\": \"key.pin\", \"to\": \"key.pin\"} or [\"from\", \"to\"]; " +
                "'to' without a pin uses the first free input. Components without x/y are laid out automatically. " +
                "Set clear=true to replace the current circuit.",
                Schema(
                    Prop("clear", "boolean", "Replace the current circuit instead of adding to it."),
                    Prop("name", "string", "Circuit name (used with clear)."),
                    Prop("components", ArrayOf(ComponentSchema(includeKey: true)), "Components to create."),
                    Prop("connections", ArrayOf(new JObject()), "Connections between pins.")),
                BuildCircuit));
            server.AddTool(new McpTool("add_component", "Add component",
                "Adds one component. Without x/y it is placed to the right of the existing circuit.",
                ComponentSchema(includeKey: false, required: new[] { "type" }), AddComponent));
            server.AddTool(new McpTool("remove_components", "Remove components",
                "Removes components (and their wires) and/or individual wires.",
                Schema(
                    Prop("components", ArrayOf(new JObject { ["type"] = new JArray("integer", "string") }), "Component ids or labels."),
                    Prop("wires", ArrayOf(new JObject { ["type"] = "integer" }), "Wire ids.")),
                RemoveComponents));
            server.AddTool(new McpTool("move_component", "Move component", "Moves a component to grid coordinates.",
                Schema(new[] { "component", "x", "y" },
                    Prop("component", AnyId(), "Component id or label."),
                    Prop("x", "number", "X coordinate (grid units, right is positive)."),
                    Prop("y", "number", "Y coordinate (grid units, up is positive).")),
                MoveComponent));
            server.AddTool(new McpTool("set_component_properties", "Set component properties",
                "Changes label, rotation (quarter turns), number of inputs (gates), clock half period (ticks) or constant value.",
                Schema(new[] { "component" },
                    Prop("component", AnyId(), "Component id or label."),
                    Prop("label", "string", "New label."),
                    Prop("rotation", "integer", "Quarter turns clockwise (0-3)."),
                    Prop("inputs", "integer", "Number of inputs for AND/OR/NAND/NOR/XOR/XNOR (1-8)."),
                    Prop("half_period", "integer", "Clock half period in ticks."),
                    Prop("value", "boolean", "Value of a Constant (or initial state of a Switch)."),
                    Prop("weights", ArrayOf(new JObject { ["type"] = "number" }), "Neuron weights, one per input (sets the input count)."),
                    Prop("threshold", "number", "Neuron threshold."),
                    Prop("leak", "number", "Spiking neuron: fraction of the potential kept per tick (0-1)."),
                    Prop("reset", "number", "Spiking neuron: potential after a spike."),
                    Prop("refractory", "integer", "Spiking neuron: ticks after a spike in which inputs are ignored."),
                    Prop("layers", ArrayOf(new JObject { ["type"] = "integer" }), "Neural block: neurons per layer, e.g. [3, 8, 2] (inputs, hidden..., outputs). Re-initializes the weights."),
                    Prop("activation", "string", "Neural block hidden activation: sigmoid, tanh or relu."),
                    Prop("seed", "integer", "Neural block: random seed for new weights.")),
                SetProperties));
            server.AddTool(new McpTool("connect", "Connect pins",
                "Connects an output pin to an input pin. Each input accepts one driver; set replace=true to rewire a connected input.",
                Schema(new[] { "from", "to" },
                    Prop("from", "string", "Output pin, e.g. '3.out', 'A' or 'FF1.Q'."),
                    Prop("to", "string", "Input pin, e.g. '5.in1', 'Q' or 'FF1.CLK'."),
                    Prop("replace", "boolean", "Replace an existing connection into the input.")),
                Connect));
            server.AddTool(new McpTool("disconnect", "Disconnect",
                "Removes a wire by id, or the wire driving an input pin.",
                Schema(
                    Prop("wire", "integer", "Wire id."),
                    Prop("to", "string", "Input pin whose wire should be removed.")),
                Disconnect));
            server.AddTool(new McpTool("new_circuit", "New circuit", "Clears the circuit and the subcircuit library (undoable).",
                Schema(Prop("name", "string", "Name of the new circuit.")), NewCircuit));
            server.AddTool(new McpTool("undo", "Undo", "Undoes the last edit.", Schema(), _ => McpToolResult.Ok(new { undone = _workspace.Undo() })));
            server.AddTool(new McpTool("redo", "Redo", "Redoes the last undone edit.", Schema(), _ => McpToolResult.Ok(new { redone = _workspace.Redo() })));

            // Simulation
            server.AddTool(new McpTool("set_inputs", "Set inputs",
                "Sets Switch, Button or PinIn components. 'values' maps component ids or labels to 0/1 or true/false.",
                Schema(new[] { "values" }, Prop("values", new JObject { ["type"] = "object", ["additionalProperties"] = new JObject { ["type"] = new JArray("boolean", "integer") } }, "Input values.")),
                SetInputs));
            server.AddTool(new McpTool("run_simulation", "Run simulation",
                "Advances the simulation. With settle=true (default when 'ticks' is omitted) it runs until no signal changes. Returns all outputs.",
                Schema(
                    Prop("ticks", "integer", "Exact number of ticks to run."),
                    Prop("settle", "boolean", "Run until stable (up to max_ticks)."),
                    Prop("max_ticks", "integer", "Limit for settle (default 100000).")),
                RunSimulation));
            server.AddTool(new McpTool("read_signals", "Read signals",
                "Returns the values of inputs and outputs, or of specific pins (e.g. ['5.out', 'FF.Q']).",
                Schema(Prop("pins", ArrayOf(new JObject { ["type"] = "string" }), "Pins to probe; omitted returns all inputs and outputs.")),
                ReadSignals, readOnly: true));
            server.AddTool(new McpTool("reset_simulation", "Reset simulation", "Clears all signals and flip-flop states.", Schema(), ResetSimulation));
            server.AddTool(new McpTool("get_simulation_status", "Get simulation status",
                "Returns backend, mode, tick, stability, speed and compile diagnostics.", Schema(), GetStatus, readOnly: true));
            server.AddTool(new McpTool("set_simulation_running", "Start or stop simulation",
                "Starts/stops the free-running simulation (in the Unity app this switches simulation mode) and sets its speed.",
                Schema(
                    Prop("running", "boolean", "Run continuously."),
                    Prop("ticks_per_second", "number", "Simulation speed.")),
                SetRunning));
            server.AddTool(new McpTool("truth_table", "Truth table",
                "Computes the truth table from a reset state. Defaults: all Switch/Button/PinIn as inputs, all Bulb/PinOut as outputs (max 12 inputs).",
                Schema(
                    Prop("inputs", ArrayOf(AnyId()), "Input components (ids or labels), first is the most significant bit."),
                    Prop("outputs", ArrayOf(AnyId()), "Output components (ids or labels)."),
                    Prop("max_ticks_per_row", "integer", "Settle limit per row.")),
                TruthTableTool, readOnly: true));
            server.AddTool(new McpTool("train_component", "Train neuron or neural block",
                "Learns the weights of a Neuron (perceptron rule) or NeuralBlock (backpropagation) so it reproduces a truth table. " +
                "Targets: 'subcircuit' (an IC with the same number of pins; a block is resized to match), 'targets' (one entry per input " +
                "pattern p, where bit i of p is input i; each entry is a 0/1 string or array with one value per output) or 'rows' " +
                "([{inputs:[..], outputs:[..]}]). Undoable. Spiking neurons cannot be trained this way.",
                Schema(new[] { "component" },
                    Prop("component", AnyId(), "Neuron or NeuralBlock (id or label)."),
                    Prop("subcircuit", "string", "Learn the function of this IC (definition id or name)."),
                    Prop("targets", ArrayOf(new JObject()), "Expected outputs for input patterns 0, 1, 2, ..."),
                    Prop("rows", ArrayOf(new JObject()), "Explicit samples."),
                    Prop("hidden", ArrayOf(new JObject { ["type"] = "integer" }), "Neural block: hidden layer sizes to start from (default keeps the current shape)."),
                    Prop("activation", "string", "Neural block: hidden activation for a new shape (sigmoid, tanh, relu)."),
                    Prop("epochs", "integer", "Training epochs (default 3000 for networks, 1000 for neurons)."),
                    Prop("learning_rate", "number", "Step size (default 0.5 for networks, 0.1 for neurons)."),
                    Prop("seed", "integer", "Random seed.")),
                TrainComponent));

            // Subcircuits and files
            server.AddTool(new McpTool("create_subcircuit", "Create subcircuit",
                "Turns components into a reusable subcircuit. Switch/Button/PinIn become its inputs, Bulb/PinOut its outputs (ordered top to bottom). " +
                "Use add_component with type 'Subcircuit' and 'subcircuit' = the returned id to place instances.",
                Schema(new[] { "name" },
                    Prop("name", "string", "Subcircuit name."),
                    Prop("components", ArrayOf(AnyId()), "Components to include (default: the whole circuit).")),
                CreateSubcircuit));
            server.AddTool(new McpTool("list_subcircuits", "List subcircuits", "Lists subcircuit definitions with their pins.", Schema(), ListSubcircuits, readOnly: true));
            server.AddTool(new McpTool("get_subcircuit", "Get subcircuit", "Returns the components and wires inside a subcircuit definition without opening it.",
                Schema(new[] { "subcircuit" }, Prop("subcircuit", "string", "Definition id or name.")), GetSubcircuit, readOnly: true));
            server.AddTool(new McpTool("collapse_to_subcircuit", "Collapse to subcircuit",
                "Moves components into a new IC and puts an instance in their place, wired so the circuit behaves the same. " +
                "Wires crossing the boundary become pins (one per driving output). Switches, buttons, bulbs, displays and pins stay outside.",
                Schema(new[] { "name", "components" },
                    Prop("name", "string", "Name of the new IC."),
                    Prop("components", ArrayOf(AnyId()), "Components to move into the IC.")),
                CollapseToSubcircuit));
            server.AddTool(new McpTool("explode_subcircuit", "Explode subcircuit",
                "Replaces an IC instance with a copy of its contents, wired like the instance was.",
                Schema(new[] { "component" }, Prop("component", AnyId(), "Subcircuit instance (id or label).")), ExplodeSubcircuit));
            server.AddTool(new McpTool("make_subcircuit_unique", "Make subcircuit unique",
                "Gives one instance its own copy of the definition, so editing it does not change the other instances.",
                Schema(new[] { "component" },
                    Prop("component", AnyId(), "Subcircuit instance (id or label)."),
                    Prop("name", "string", "Name of the copy (default: '<name> 2').")),
                MakeSubcircuitUnique));
            server.AddTool(new McpTool("rename_subcircuit", "Rename subcircuit", "Renames a subcircuit definition (its id stays the same).",
                Schema(new[] { "subcircuit", "name" },
                    Prop("subcircuit", "string", "Definition id or name."),
                    Prop("name", "string", "New name.")),
                RenameSubcircuit));
            server.AddTool(new McpTool("delete_subcircuit", "Delete subcircuit", "Deletes a subcircuit definition that is not used anywhere.",
                Schema(new[] { "subcircuit" }, Prop("subcircuit", "string", "Definition id or name.")), DeleteSubcircuit));
            server.AddTool(new McpTool("open_subcircuit", "Open subcircuit",
                "Opens an IC for in-place editing. Pass an instance ('component') to also see its live values, or a definition id ('subcircuit'). " +
                "Editing tools then act on the IC's contents (component ids are those inside the IC).",
                Schema(
                    Prop("component", AnyId(), "Subcircuit instance in the current circuit (id or label)."),
                    Prop("subcircuit", "string", "Definition id or name (when there is no instance).")),
                OpenSubcircuit));
            server.AddTool(new McpTool("close_subcircuit", "Close subcircuit",
                "Closes the opened IC. With apply=true (default) the changes update every instance as one undoable step; apply=false discards them.",
                Schema(
                    Prop("apply", "boolean", "Keep the changes (default true)."),
                    Prop("all", "boolean", "Close every open IC and return to the document.")),
                CloseSubcircuit));
            server.AddTool(new McpTool("save_circuit", "Save circuit", "Saves the circuit (including subcircuits) under a name.",
                Schema(Prop("name", "string", "File name (default: current name).")), SaveCircuit));
            server.AddTool(new McpTool("load_circuit", "Load circuit", "Opens a saved circuit (undoable).",
                Schema(new[] { "name" }, Prop("name", "string", "Name of a saved circuit.")), LoadCircuit));
            server.AddTool(new McpTool("list_saved_circuits", "List saved circuits", "Lists saved circuit names.", Schema(), ListSaved, readOnly: true));
            server.AddTool(new McpTool("export_circuit_json", "Export circuit JSON", "Returns the circuit in LogicSim's JSON file format.",
                Schema(), _ => McpToolResult.Ok(CircuitSerializer.Serialize(Root, _workspace.DocumentName)), readOnly: true));
            server.AddTool(new McpTool("import_circuit_json", "Import circuit JSON", "Replaces the circuit with a document in LogicSim's JSON format (undoable).",
                Schema(new[] { "json" }, Prop("json", "string", "Document JSON.")), ImportJson));
        }

        #region Inspection

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

        #endregion

        #region Editing

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

        #endregion

        #region Simulation

        private McpToolResult SetInputs(JObject args)
        {
            if (!(args["values"] is JObject values) || !values.HasValues)
                throw new McpToolException("'values' must map inputs to 0/1");
            var applied = new JObject();
            foreach (var property in values.Properties())
            {
                // Inputs belong to the document, also while an IC is open.
                var component = ResolveComponent(Root, property.Name);
                if (!component.Spec.IsInterfaceInput)
                    throw new McpToolException($"{component} is not a Switch, Button or PinIn");
                bool value = ParseBool(property.Value);
                Root.SetInputValue(component.Id, value);
                applied[NameOf(component)] = value ? 1 : 0;
            }
            return McpToolResult.Ok(new JObject { ["inputs"] = applied, ["hint"] = "Call run_simulation to propagate the change." });
        }

        private McpToolResult RunSimulation(JObject args)
        {
            var session = _workspace.Session;
            // The simulation runs the document; pending IC edits are written back first.
            int applied = Hierarchy.ApplyAll();
            session.EnsureCompiled();
            long startTick = session.Tick;
            bool stable;
            if (args["ticks"] != null && !Bool(args, "settle", false))
            {
                long ticks = Math.Max(0, (long)args["ticks"]);
                session.Run(ticks);
                stable = session.Engine.IsStable;
            }
            else
            {
                long maxTicks = args["max_ticks"] != null ? Math.Max(1, (long)args["max_ticks"]) : 100_000;
                stable = session.Settle(maxTicks);
            }

            var result = SignalSummary(Root, session);
            if (applied > 0)
                result["applied_ic_edits"] = applied;
            result["ticks_elapsed"] = session.Tick - startTick;
            result["tick"] = session.Tick;
            result["stable"] = stable;
            if (!stable)
                result["note"] = "Signals still change (oscillation, running clock or very deep logic).";
            AddDiagnostics(result);
            return McpToolResult.Ok(result);
        }

        private McpToolResult ReadSignals(JObject args)
        {
            var signals = CurrentSignals(out var reason);
            if (signals == null)
                throw new McpToolException(reason);
            if (args["pins"] is JArray pins && pins.Count > 0)
            {
                var values = new JObject();
                foreach (var pinToken in pins)
                {
                    var text = (string)pinToken;
                    var (component, pin, output) = ParseAnyPin(text);
                    bool value = output ? signals.GetOutputValue(component.Id, pin) : signals.GetInputPinValue(component.Id, pin);
                    values[text] = value ? 1 : 0;
                }
                return McpToolResult.Ok(new JObject { ["pins"] = values, ["tick"] = _workspace.Session.Tick });
            }
            var summary = SignalSummary(Circuit, signals);
            summary["tick"] = _workspace.Session.Tick;
            if (!Hierarchy.IsAtRoot)
                summary["level"] = DescribeLevel();
            return McpToolResult.Ok(summary);
        }

        private McpToolResult ResetSimulation(JObject args)
        {
            _workspace.Session.Reset();
            return McpToolResult.Ok(new { tick = _workspace.Session.Tick });
        }

        private McpToolResult GetStatus(JObject args)
        {
            var session = _workspace.Session;
            session.EnsureCompiled();
            var compiled = session.Compiled;
            var result = new JObject
            {
                ["host"] = _workspace.HostName,
                ["backend"] = session.Engine.Name,
                ["mode"] = session.Engine.ActiveMode.ToString(),
                ["running"] = _workspace.IsRunning,
                ["ticks_per_second"] = _workspace.TicksPerSecond,
                ["tick"] = session.Tick,
                ["stable"] = session.Engine.IsStable,
                ["gates"] = compiled.GateCount,
                ["nets"] = compiled.NetCount,
                ["gate_evaluations"] = session.Engine.TotalEvaluations
            };
            AddDiagnostics(result);
            return McpToolResult.Ok(result);
        }

        private McpToolResult SetRunning(JObject args)
        {
            if (args["ticks_per_second"] != null)
                _workspace.TicksPerSecond = Math.Max(1, (double)args["ticks_per_second"]);
            if (args["running"] != null)
                _workspace.IsRunning = ParseBool(args["running"]);
            return McpToolResult.Ok(new { running = _workspace.IsRunning, ticks_per_second = _workspace.TicksPerSecond });
        }

        private McpToolResult TruthTableTool(JObject args)
        {
            var inputs = (args["inputs"] as JArray)?.Select(ResolveComponent).ToList();
            var outputs = (args["outputs"] as JArray)?.Select(ResolveComponent).ToList();
            long maxTicks = args["max_ticks_per_row"] != null ? (long)args["max_ticks_per_row"] : TruthTableGenerator.DefaultMaxTicksPerRow;
            var table = TruthTableGenerator.Generate(Circuit, inputs, outputs, maxTicks);
            var rows = new JArray();
            foreach (var row in table.Rows)
            {
                var entry = new JObject();
                for (int i = 0; i < row.Inputs.Length; i++)
                    entry[table.InputNames[i]] = row.Inputs[i] ? 1 : 0;
                for (int i = 0; i < row.Outputs.Length; i++)
                    entry[table.OutputNames[i]] = row.Outputs[i] ? 1 : 0;
                if (!row.Stable)
                    entry["stable"] = false;
                rows.Add(entry);
            }
            return McpToolResult.Ok(new JObject
            {
                ["inputs"] = new JArray(table.InputNames),
                ["outputs"] = new JArray(table.OutputNames),
                ["rows"] = rows,
                ["text"] = table.ToText()
            });
        }

        #endregion

        #region Subcircuits and files

        private McpToolResult CreateSubcircuit(JObject args)
        {
            var name = (string)Required(args, "name");
            var ids = (args["components"] as JArray)?.Select(t => ResolveComponent(t).Id).ToList()
                ?? Circuit.Components.Select(c => c.Id).ToList();
            if (ids.Count == 0)
                throw new McpToolException("No components to turn into a subcircuit");
            var fragment = CircuitFragment.Capture(Circuit, ids);
            var definition = new SubcircuitDefinition(Circuit.Library.CreateUniqueId(name), name, fragment.ToCircuit(Circuit.Library));
            if (definition.InputComponentIds.Count == 0 && definition.OutputComponentIds.Count == 0)
                throw new McpToolException("A subcircuit needs at least one Switch/Button/PinIn or Bulb/PinOut to act as a pin");
            _workspace.Editor.Apply(new AddSubcircuitDefinitionEdit(Circuit.Library, definition));
            return McpToolResult.Ok(DescribeDefinition(definition));
        }

        private McpToolResult ListSubcircuits(JObject args)
        {
            return McpToolResult.Ok(new JObject { ["subcircuits"] = new JArray(Circuit.Library.Definitions.Select(DescribeDefinition)) });
        }

        /// <summary>Reads neuron / network arguments (weights, threshold, layers, ...) into <paramref name="properties"/>.</summary>
        private static void ApplyNeuralArguments(CircuitComponent component, JObject args, ref ComponentProperties properties)
        {
            bool neuronArgs = args["weights"] != null || args["threshold"] != null || args["leak"] != null || args["reset"] != null || args["refractory"] != null;
            bool networkArgs = args["layers"] != null || args["activation"] != null;
            if (neuronArgs && !component.IsNeuron)
                throw new McpToolException("weights/threshold/leak/reset/refractory only apply to Neuron and SpikingNeuron");
            if (networkArgs && component.Kind != ComponentKind.NeuralBlock)
                throw new McpToolException("layers/activation only apply to NeuralBlock");

            if (component.IsNeuron)
            {
                var neuron = properties.Neuron ?? NeuronParameters.Default(component.Kind, properties.InputCount);
                if (args["inputs"] != null && args["weights"] == null)
                    neuron = neuron.WithInputCount((int)args["inputs"]);
                if (args["weights"] is JArray weights)
                {
                    neuron = neuron.WithWeights(weights.Select(w => (float)w));
                    properties.InputCount = neuron.Weights.Count;
                }
                if (args["threshold"] != null)
                    neuron = neuron.WithThreshold((float)args["threshold"]);
                if (args["leak"] != null)
                    neuron = neuron.WithLeak((float)args["leak"]);
                if (args["reset"] != null)
                    neuron = neuron.WithReset((float)args["reset"]);
                if (args["refractory"] != null)
                    neuron = neuron.WithRefractory((int)args["refractory"]);
                properties.Neuron = neuron;
            }
            else if (component.Kind == ComponentKind.NeuralBlock && networkArgs)
            {
                var current = properties.Network ?? NeuralNetwork.Default;
                var activation = args["activation"] != null ? ParseActivation((string)args["activation"]) : current.Activation;
                if (args["layers"] is JArray layers)
                {
                    var sizes = layers.Select(l => (int)l).ToList();
                    if (sizes.Count < 2)
                        throw new McpToolException("'layers' needs at least an input and an output size, e.g. [2, 4, 1]");
                    int seed = args["seed"] != null ? (int)args["seed"] : 1;
                    properties.Network = NeuralNetwork.Create(sizes[0], sizes.Skip(1).Take(sizes.Count - 2), sizes[sizes.Count - 1], activation, seed);
                }
                else
                {
                    properties.Network = current.WithActivation(activation);
                }
            }
        }

        private static NeuralActivation ParseActivation(string text)
        {
            if (Enum.TryParse(text?.Trim(), true, out NeuralActivation activation))
                return activation;
            throw new McpToolException($"Unknown activation '{text}' (use sigmoid, tanh or relu)");
        }

        private McpToolResult TrainComponent(JObject args)
        {
            var component = ResolveComponent(Required(args, "component"));
            if (component.Kind == ComponentKind.SpikingNeuron)
                throw new McpToolException("Spiking neurons react over time and are not trained from truth tables; adjust weights, threshold and leak instead");
            if (component.Kind != ComponentKind.Neuron && component.Kind != ComponentKind.NeuralBlock)
                throw new McpToolException($"{component} is not a Neuron or NeuralBlock");

            List<NeuralTraining.Sample> samples;
            try
            {
                if (args["subcircuit"] != null)
                {
                    var definition = ResolveDefinition(args["subcircuit"]);
                    samples = NeuralTraining.SamplesFromCircuit(definition.Circuit);
                }
                else if (args["targets"] is JArray targets)
                {
                    samples = SamplesFromTargets(targets);
                }
                else if (args["rows"] is JArray rows)
                {
                    samples = rows.Select(r => new NeuralTraining.Sample(
                        ((JArray)r["inputs"]).Select(ParseBool).ToArray(),
                        ((JArray)r["outputs"]).Select(ParseBool).ToArray())).ToList();
                }
                else
                {
                    throw new McpToolException("Pass 'subcircuit', 'targets' or 'rows'");
                }
            }
            catch (CircuitException exception)
            {
                throw new McpToolException(exception.Message);
            }

            var options = new NeuralTraining.Options
            {
                Epochs = args["epochs"] != null ? (int)args["epochs"] : (int?)null,
                LearningRate = args["learning_rate"] != null ? (float)args["learning_rate"] : (float?)null,
                Seed = args["seed"] != null ? (int)args["seed"] : 1,
                Hidden = args["hidden"] is JArray hidden ? hidden.Select(v => (int)v).ToList() : null,
                Activation = args["activation"] != null ? ParseActivation((string)args["activation"]) : (NeuralActivation?)null
            };
            NeuralTraining.Result result;
            try
            {
                result = NeuralTraining.TrainComponent(component, samples, options, out var properties);
                _workspace.Editor.Apply(new SetPropertiesEdit(Circuit, component.Id, properties));
            }
            catch (CircuitException exception)
            {
                throw new McpToolException(exception.Message);
            }

            var output = new JObject
            {
                ["component"] = component.Id,
                ["accuracy"] = Math.Round(result.Accuracy, 4),
                ["correct_rows"] = result.Correct,
                ["rows"] = result.Total,
                ["epochs"] = result.Epochs
            };
            if (component.Kind == ComponentKind.NeuralBlock)
            {
                output["loss"] = Math.Round(result.Loss, 5);
                output["network"] = component.Network.Describe();
            }
            else
            {
                output["weights"] = new JArray(component.Neuron.Weights);
                output["threshold"] = component.Neuron.Threshold;
            }
            if (!result.Perfect)
                output["note"] = component.Kind == ComponentKind.Neuron
                    ? "A single neuron can only learn linearly separable functions (not XOR); use a NeuralBlock."
                    : "Not every row was learned; try more epochs, a larger hidden layer or another seed.";
            return McpToolResult.Ok(output);
        }

        private static List<NeuralTraining.Sample> SamplesFromTargets(JArray targets)
        {
            var values = new List<bool[]>();
            foreach (var token in targets)
            {
                if (token is JArray array)
                    values.Add(array.Select(ParseBool).ToArray());
                else if (token.Type == JTokenType.String && ((string)token).Length > 1)
                    values.Add(((string)token).Where(c => c == '0' || c == '1').Select(c => c == '1').ToArray());
                else
                    values.Add(new[] { ParseBool(token) });
            }
            return NeuralTraining.SamplesFromTargets(values);
        }

        private SubcircuitDefinition ResolveDefinition(JToken token)
        {
            var text = ((string)token)?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new McpToolException("Missing subcircuit id");
            var library = Circuit.Library;
            if (library.TryGet(text, out var byId))
                return byId;
            var byName = library.Definitions.FirstOrDefault(d => string.Equals(d.Name, text, StringComparison.OrdinalIgnoreCase));
            return byName ?? throw new McpToolException($"Unknown subcircuit '{text}'. Use list_subcircuits.");
        }

        private CircuitComponent ResolveInstance(JToken token)
        {
            var component = ResolveComponent(token);
            if (component.Kind != ComponentKind.Subcircuit)
                throw new McpToolException($"{component} is not a subcircuit instance");
            return component;
        }

        private McpToolResult GetSubcircuit(JObject args)
        {
            var definition = ResolveDefinition(Required(args, "subcircuit"));
            var inner = definition.Circuit;
            var result = DescribeDefinition(definition);
            result["circuit"] = new JObject
            {
                ["components"] = new JArray(inner.Components.Select(c => DescribeIn(inner, c))),
                ["wires"] = new JArray(inner.Wires.Select(w => new JObject
                {
                    ["id"] = w.Id,
                    ["from"] = $"{w.From.ComponentId}.{inner.GetOutputName(inner.GetComponent(w.From.ComponentId), w.From.Pin)}",
                    ["to"] = $"{w.To.ComponentId}.{inner.GetInputName(inner.GetComponent(w.To.ComponentId), w.To.Pin)}"
                }))
            };
            result["used_by"] = new JArray(Circuit.Library.DirectUsersOf(definition.Id).Select(d => d.Id));
            result["instances_here"] = Circuit.InstancesOf(definition.Id).Count();
            return McpToolResult.Ok(result);
        }

        private static JObject DescribeIn(Circuit circuit, CircuitComponent component)
        {
            var entry = new JObject { ["id"] = component.Id, ["type"] = component.Kind.ToString(), ["x"] = component.X, ["y"] = component.Y };
            if (!string.IsNullOrEmpty(component.Label))
                entry["label"] = component.Label;
            if (component.Kind == ComponentKind.Subcircuit)
                entry["subcircuit"] = component.SubcircuitId;
            return entry;
        }

        private McpToolResult CollapseToSubcircuit(JObject args)
        {
            var name = (string)Required(args, "name");
            var ids = (Required(args, "components") as JArray ?? throw new McpToolException("'components' must be an array"))
                .Select(t => ResolveComponent(t).Id).ToList();
            SubcircuitRefactoring.CollapseResult result;
            try
            {
                result = SubcircuitRefactoring.Collapse(Circuit, ids, name);
            }
            catch (CircuitException exception)
            {
                throw new McpToolException(exception.Message);
            }
            _workspace.Editor.Apply(result.Edit);
            _workspace.Focus(new[] { result.Instance.Id });
            var output = DescribeDefinition(result.Definition);
            output["instance"] = Describe(Circuit.GetComponent(result.Instance.Id), null);
            if (result.KeptOutside.Count > 0)
                output["kept_outside"] = new JArray(result.KeptOutside);
            return McpToolResult.Ok(output);
        }

        private McpToolResult ExplodeSubcircuit(JObject args)
        {
            var instance = ResolveInstance(Required(args, "component"));
            var result = SubcircuitRefactoring.Explode(Circuit, instance.Id);
            _workspace.Editor.Apply(result.Edit);
            _workspace.Focus(result.Components.Select(c => c.Id));
            return McpToolResult.Ok(new JObject
            {
                ["removed_instance"] = instance.Id,
                ["components"] = new JArray(result.Components.Select(c => Describe(Circuit.GetComponent(c.Id), null)))
            });
        }

        private McpToolResult MakeSubcircuitUnique(JObject args)
        {
            var instance = ResolveInstance(Required(args, "component"));
            var edit = SubcircuitRefactoring.MakeUnique(Circuit, instance.Id, (string)args["name"], out var copy);
            _workspace.Editor.Apply(edit);
            var output = DescribeDefinition(copy);
            output["instance"] = instance.Id;
            return McpToolResult.Ok(output);
        }

        private McpToolResult RenameSubcircuit(JObject args)
        {
            var definition = ResolveDefinition(Required(args, "subcircuit"));
            _workspace.Editor.Apply(new RenameSubcircuitEdit(Circuit.Library, definition.Id, (string)Required(args, "name")));
            return McpToolResult.Ok(DescribeDefinition(Circuit.Library.Get(definition.Id)));
        }

        private McpToolResult DeleteSubcircuit(JObject args)
        {
            var definition = ResolveDefinition(Required(args, "subcircuit"));
            var library = Circuit.Library;
            var users = library.DirectUsersOf(definition.Id).Select(d => d.Name).ToList();
            bool usedInOpenLevels = Hierarchy.Levels.Any(l => l.Circuit.InstancesOf(definition.Id).Any() || l.DefinitionId == definition.Id);
            if (users.Count > 0 || usedInOpenLevels)
                throw new McpToolException($"'{definition.Name}' is still used" + (users.Count > 0 ? " by " + string.Join(", ", users) : " in the open circuit"));
            _workspace.Editor.Apply(new RemoveSubcircuitDefinitionEdit(library, definition.Id));
            return McpToolResult.Ok(new { deleted = definition.Id });
        }

        private McpToolResult OpenSubcircuit(JObject args)
        {
            try
            {
                if (args["component"] != null)
                    Hierarchy.OpenInstance(ResolveInstance(args["component"]).Id);
                else if (args["subcircuit"] != null)
                    Hierarchy.OpenDefinition(ResolveDefinition(args["subcircuit"]).Id);
                else
                    throw new McpToolException("Pass 'component' (an instance) or 'subcircuit' (a definition id)");
            }
            catch (CircuitException exception)
            {
                throw new McpToolException(exception.Message);
            }
            var definition = Circuit.Library.Get(Hierarchy.Current.DefinitionId);
            var result = DescribeDefinition(definition);
            result["level"] = DescribeLevel();
            result["hint"] = "Edit with the usual tools, then close_subcircuit to update every instance.";
            return McpToolResult.Ok(result);
        }

        private McpToolResult CloseSubcircuit(JObject args)
        {
            if (Hierarchy.IsAtRoot)
                throw new McpToolException("No subcircuit is open");
            bool apply = Bool(args, "apply", true);
            bool all = Bool(args, "all", false);
            var closed = new JArray();
            try
            {
                do
                {
                    var level = Hierarchy.Current;
                    closed.Add(new JObject
                    {
                        ["subcircuit"] = level.DefinitionId,
                        ["modified"] = Hierarchy.IsModified(level)
                    });
                    Hierarchy.Close(apply);
                }
                while (all && !Hierarchy.IsAtRoot);
            }
            catch (CircuitException exception)
            {
                throw new McpToolException(exception.Message);
            }
            return McpToolResult.Ok(new JObject { ["closed"] = closed, ["applied"] = apply, ["level"] = DescribeLevel() });
        }

        private McpToolResult SaveCircuit(JObject args)
        {
            var name = (string)args["name"];
            if (string.IsNullOrWhiteSpace(name))
                name = _workspace.DocumentName;
            Hierarchy.ApplyAll();
            var path = _workspace.Documents.Save(Root, name);
            _workspace.DocumentName = name;
            return McpToolResult.Ok(new { name, path });
        }

        private McpToolResult LoadCircuit(JObject args)
        {
            var name = (string)Required(args, "name");
            var json = _workspace.Documents.Read(name);
            Hierarchy.CloseTo(0, apply: true);
            var document = CircuitSerializer.Deserialize(json, Circuit.Library);
            _workspace.Editor.Apply(new ReplaceCircuitEdit(Circuit, document.Snapshot, "Load circuit"));
            _workspace.DocumentName = string.IsNullOrEmpty(document.Name) ? name : document.Name;
            return McpToolResult.Ok(new { name = _workspace.DocumentName, components = Circuit.ComponentCount, wires = Circuit.WireCount, warnings = document.Warnings });
        }

        private McpToolResult ListSaved(JObject args)
        {
            return McpToolResult.Ok(new { directory = _workspace.Documents.Directory, circuits = _workspace.Documents.List() });
        }

        private McpToolResult ImportJson(JObject args)
        {
            var json = (string)Required(args, "json");
            Hierarchy.CloseTo(0, apply: true);
            var document = CircuitSerializer.Deserialize(json, Circuit.Library);
            _workspace.Editor.Apply(new ReplaceCircuitEdit(Circuit, document.Snapshot, "Import circuit"));
            if (!string.IsNullOrEmpty(document.Name))
                _workspace.DocumentName = document.Name;
            return McpToolResult.Ok(new { name = _workspace.DocumentName, components = Circuit.ComponentCount, wires = Circuit.WireCount, warnings = document.Warnings });
        }

        #endregion

        #region Helpers

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

        #endregion

        #region Schemas

        private static JObject Schema(params (string name, JObject schema)[] properties) => Schema(null, properties);

        private static JObject Schema(string[] required, params (string name, JObject schema)[] properties)
        {
            var props = new JObject();
            foreach (var (name, schema) in properties)
                props[name] = schema;
            var result = new JObject { ["type"] = "object", ["properties"] = props };
            if (required != null && required.Length > 0)
                result["required"] = new JArray(required);
            return result;
        }

        private static (string, JObject) Prop(string name, string type, string description)
        {
            return (name, new JObject { ["type"] = type, ["description"] = description });
        }

        private static (string, JObject) Prop(string name, JObject schema, string description)
        {
            var copy = (JObject)schema.DeepClone();
            copy["description"] = description;
            return (name, copy);
        }

        private static JObject ArrayOf(JObject items) => new JObject { ["type"] = "array", ["items"] = items };

        private static JObject AnyId() => new JObject { ["type"] = new JArray("integer", "string") };

        private static JObject ComponentSchema(bool includeKey, string[] required = null)
        {
            var props = new List<(string, JObject)>();
            if (includeKey)
                props.Add(Prop("key", "string", "Name used in connections (defaults to the label)."));
            props.Add(Prop("type", "string", "Component type, see list_component_types (e.g. And, Or, Not, Xor, Switch, Button, Clock, Constant, Bulb, HexDisplay, DFlipFlop, Subcircuit)."));
            props.Add(Prop("label", "string", "Label shown on the canvas; used as pin name inside subcircuits."));
            props.Add(Prop("x", "number", "X position in grid units."));
            props.Add(Prop("y", "number", "Y position in grid units (up is positive)."));
            props.Add(Prop("inputs", "integer", "Number of inputs for multi-input gates (1-8)."));
            props.Add(Prop("half_period", "integer", "Clock half period in ticks."));
            props.Add(Prop("value", "boolean", "Constant value or initial switch state."));
            props.Add(Prop("rotation", "integer", "Quarter turns clockwise."));
            props.Add(Prop("subcircuit", "string", "Definition id for type Subcircuit."));
            props.Add(Prop("weights", ArrayOf(new JObject { ["type"] = "number" }), "Neuron/SpikingNeuron weights (one per input)."));
            props.Add(Prop("threshold", "number", "Neuron/SpikingNeuron threshold."));
            props.Add(Prop("leak", "number", "SpikingNeuron: fraction of the potential kept per tick."));
            props.Add(Prop("refractory", "integer", "SpikingNeuron: ticks ignored after a spike."));
            props.Add(Prop("layers", ArrayOf(new JObject { ["type"] = "integer" }), "NeuralBlock layer sizes, e.g. [2, 4, 1]."));
            props.Add(Prop("activation", "string", "NeuralBlock hidden activation (sigmoid, tanh, relu)."));
            var schema = Schema(required, props.ToArray());
            if (required == null)
                schema["required"] = new JArray("type");
            return schema;
        }

        #endregion
    }
}
