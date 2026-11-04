using System;
using System.Collections.Generic;
using System.Linq;
using LogicSim.Simulation;
using Newtonsoft.Json.Linq;

namespace LogicSim.Mcp
{
    /// <summary>The MCP tool set: build, edit, simulate and analyse circuits in a <see cref="ILogicSimWorkspace"/>.</summary>
    public sealed partial class LogicSimTools
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
    }
}
