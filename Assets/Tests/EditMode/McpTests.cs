using System.IO;
using System.Linq;
using LogicSim.Mcp;
using LogicSim.Simulation;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LogicSim.Tests
{
    public class McpTests
    {
        private HeadlessWorkspace _workspace;
        private McpServer _server;
        private int _nextId;
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "logicsim-tests-" + System.Guid.NewGuid().ToString("N"));
            _workspace = new HeadlessWorkspace(_directory);
            _server = LogicSimTools.CreateServer(_workspace);
            _nextId = 1;
        }

        [TearDown]
        public void TearDown()
        {
            _workspace.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        private JObject Request(string method, JObject parameters = null)
        {
            var message = new JObject { ["jsonrpc"] = "2.0", ["id"] = _nextId++, ["method"] = method };
            if (parameters != null)
                message["params"] = parameters;
            var response = _server.HandleMessage(message.ToString());
            return JObject.Parse(response);
        }

        private JToken Call(string tool, object arguments = null, bool expectError = false)
        {
            var response = Request("tools/call", new JObject
            {
                ["name"] = tool,
                ["arguments"] = arguments == null ? new JObject() : JObject.FromObject(arguments)
            });
            Assert.That(response["error"], Is.Null, response.ToString());
            var result = response["result"];
            Assert.That((bool)result["isError"], Is.EqualTo(expectError), result.ToString());
            var text = (string)result["content"][0]["text"];
            return text.TrimStart().StartsWith("{") ? JToken.Parse(text) : new JValue(text);
        }

        [Test]
        public void Initialize_NegotiatesProtocolAndAdvertisesTools()
        {
            var response = Request("initialize", new JObject
            {
                ["protocolVersion"] = "2025-03-26",
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = "test", ["version"] = "1" }
            });
            Assert.That((string)response["result"]["protocolVersion"], Is.EqualTo("2025-03-26"));
            Assert.That(response["result"]["capabilities"]["tools"], Is.Not.Null);
            Assert.That((string)response["result"]["serverInfo"]["name"], Is.EqualTo("logicsim"));

            var unknownVersion = Request("initialize", new JObject { ["protocolVersion"] = "1999-01-01" });
            Assert.That((string)unknownVersion["result"]["protocolVersion"], Is.EqualTo(McpServer.SupportedProtocolVersions[0]));

            var tools = Request("tools/list")["result"]["tools"];
            var names = tools.Select(t => (string)t["name"]).ToList();
            Assert.That(names, Does.Contain("build_circuit").And.Contain("truth_table").And.Contain("run_simulation"));
            foreach (var tool in tools)
                Assert.That((string)tool["inputSchema"]["type"], Is.EqualTo("object"));
        }

        [Test]
        public void Notifications_GetNoResponse_UnknownMethodsGetAnError()
        {
            Assert.That(_server.HandleMessage("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"), Is.Null);
            var error = JObject.Parse(_server.HandleMessage("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"does/not/exist\"}"));
            Assert.That((int)error["error"]["code"], Is.EqualTo(-32601));
            var parse = JObject.Parse(_server.HandleMessage("{broken"));
            Assert.That((int)parse["error"]["code"], Is.EqualTo(-32700));
        }

        [Test]
        public void Batch_ReturnsOneResponsePerRequest()
        {
            var batch = "[{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"},{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"},{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}]";
            var responses = JArray.Parse(_server.HandleMessage(batch));
            Assert.That(responses.Count, Is.EqualTo(2));
        }

        [Test]
        public void BuildCircuit_ThenTruthTable_DescribesAHalfAdder()
        {
            var built = Call("build_circuit", new
            {
                clear = true,
                components = new object[]
                {
                    new { type = "Switch", label = "A" },
                    new { type = "Switch", label = "B" },
                    new { key = "x", type = "Xor" },
                    new { key = "carry", type = "And" },
                    new { type = "Bulb", label = "S" },
                    new { type = "Bulb", label = "C" }
                },
                connections = new object[]
                {
                    new[] { "A", "x" }, new[] { "B", "x" }, new[] { "A", "carry" }, new[] { "B", "carry" },
                    new { from = "x.out", to = "S" }, new { from = "carry", to = "C.in" }
                }
            });
            Assert.That((int)built["created_wires"], Is.EqualTo(6));

            var table = Call("truth_table");
            var rows = (JArray)table["rows"];
            Assert.That(rows.Count, Is.EqualTo(4));
            Assert.That((int)rows[3]["S"], Is.EqualTo(0));
            Assert.That((int)rows[3]["C"], Is.EqualTo(1));
            Assert.That((int)rows[1]["S"], Is.EqualTo(1));
        }

        [Test]
        public void SetInputsAndRun_ReportsOutputs()
        {
            Call("build_circuit", new
            {
                components = new object[] { new { type = "Switch", label = "In" }, new { key = "n", type = "Not" }, new { type = "Bulb", label = "Out" } },
                connections = new object[] { new[] { "In", "n" }, new[] { "n", "Out" } }
            });
            var first = Call("run_simulation");
            Assert.That((int)first["outputs"]["Out"], Is.EqualTo(1));
            Assert.That((bool)first["stable"], Is.True);

            Call("set_inputs", new { values = new JObject { ["In"] = 1 } });
            var second = Call("run_simulation");
            Assert.That((int)second["outputs"]["Out"], Is.EqualTo(0));

            var probe = Call("read_signals", new { pins = new[] { "n.out", "Out.in" } });
            Assert.That((int)probe["pins"]["n.out"], Is.EqualTo(0));
        }

        [Test]
        public void Errors_AreReportedAsToolErrors()
        {
            Call("add_component", new { type = "Teleporter" }, expectError: true);
            Call("connect", new { from = "1.out", to = "2.in" }, expectError: true);
            Call("add_component", new { type = "And", label = "G", inputs = 3 });
            Call("add_component", new { type = "Switch", label = "S" });
            Call("connect", new { from = "S", to = "G.in2" });
            Call("connect", new { from = "S", to = "G.in2" }, expectError: true);
            Call("connect", new { from = "S", to = "G.in9" }, expectError: true);
        }

        [Test]
        public void EditsAreUndoable()
        {
            Call("add_component", new { type = "Or" });
            Assert.That(_workspace.Circuit.ComponentCount, Is.EqualTo(1));
            Call("undo");
            Assert.That(_workspace.Circuit.ComponentCount, Is.EqualTo(0));
            Call("redo");
            Assert.That(_workspace.Circuit.ComponentCount, Is.EqualTo(1));
        }

        [Test]
        public void SubcircuitSaveAndLoad_RoundTrip()
        {
            Call("build_circuit", new
            {
                clear = true,
                components = new object[] { new { type = "PinIn", label = "I" }, new { key = "n", type = "Not" }, new { type = "PinOut", label = "O" } },
                connections = new object[] { new[] { "I", "n" }, new[] { "n", "O" } }
            });
            var definition = Call("create_subcircuit", new { name = "Inverter" });
            Assert.That((string)definition["id"], Is.EqualTo("inverter"));

            Call("build_circuit", new
            {
                clear = true,
                components = new object[]
                {
                    new { type = "Switch", label = "A" },
                    new { key = "inv", type = "Subcircuit", subcircuit = "inverter" },
                    new { type = "Bulb", label = "Q" }
                },
                connections = new object[] { new[] { "A", "inv.I" }, new[] { "inv.O", "Q" } }
            });
            Call("save_circuit", new { name = "uses-inverter" });
            Call("new_circuit");
            Assert.That(_workspace.Circuit.Library.Count, Is.EqualTo(0));

            var loaded = Call("load_circuit", new { name = "uses-inverter" });
            Assert.That((int)loaded["components"], Is.EqualTo(3));
            var table = Call("truth_table");
            Assert.That((int)table["rows"][0]["Q"], Is.EqualTo(1));
            Assert.That((int)table["rows"][1]["Q"], Is.EqualTo(0));
        }

        [Test]
        public void ExportImport_RoundTrip()
        {
            Call("add_component", new { type = "Clock", label = "clk", half_period = 7 });
            var json = Call("export_circuit_json").ToString();
            Call("new_circuit");
            Call("import_circuit_json", new { json });
            Assert.That(_workspace.Circuit.FindByLabel("clk").ClockHalfPeriod, Is.EqualTo(7));
        }

        private void BuildFlatFullAdder()
        {
            Call("build_circuit", new
            {
                clear = true,
                components = new object[]
                {
                    new { key = "A", type = "Switch" }, new { key = "B", type = "Switch" }, new { key = "Cin", type = "Switch" },
                    new { key = "x1", type = "Xor" }, new { key = "x2", type = "Xor" },
                    new { key = "a1", type = "And" }, new { key = "a2", type = "And" }, new { key = "o", type = "Or" },
                    new { key = "S", type = "Bulb" }, new { key = "Cout", type = "Bulb" }
                },
                connections = new object[]
                {
                    new[] { "A", "x1" }, new[] { "B", "x1" }, new[] { "x1", "x2" }, new[] { "Cin", "x2" },
                    new[] { "A", "a1" }, new[] { "B", "a1" }, new[] { "x1", "a2" }, new[] { "Cin", "a2" },
                    new[] { "a1", "o" }, new[] { "a2", "o" }, new[] { "x2", "S" }, new[] { "o", "Cout" }
                }
            });
        }

        [Test]
        public void Collapse_OpenEditClose_Explode()
        {
            BuildFlatFullAdder();
            var before = (string)Call("truth_table")["text"];

            var collapsed = Call("collapse_to_subcircuit", new { name = "Adder", components = new[] { "x1", "x2", "a1", "a2", "o" } });
            Assert.That(collapsed["inputs"].ToObject<string[]>(), Is.EqualTo(new[] { "A", "B", "Cin" }));
            int instance = (int)collapsed["instance"]["id"];
            Assert.That((string)Call("truth_table")["text"], Is.EqualTo(before));

            // Look inside the instance while simulating.
            Call("set_inputs", new { values = new JObject { ["A"] = 1, ["B"] = 1 } });
            Call("run_simulation");
            var opened = Call("open_subcircuit", new { component = instance });
            Assert.That(opened["level"]["path"].ToObject<string[]>(), Is.EqualTo(new[] { "Main", "Adder" }));
            var inside = Call("read_signals");
            Assert.That((int)inside["inputs"]["A"], Is.EqualTo(1));
            Assert.That((int)inside["outputs"]["Cout"], Is.EqualTo(1));
            Assert.That((int)inside["outputs"]["S"], Is.EqualTo(0));

            // Rename an output pin inside the IC; the wire to the bulb outside must survive.
            Call("set_component_properties", new { component = "Cout", label = "Carry" });
            Assert.That((bool)Call("get_circuit")["level"]["modified"], Is.True);
            Call("read_signals", expectError: true);
            var closed = Call("close_subcircuit");
            Assert.That((int)closed["level"]["depth"], Is.EqualTo(0));
            var definition = Call("list_subcircuits")["subcircuits"][0];
            Assert.That(definition["outputs"].ToObject<string[]>(), Is.EqualTo(new[] { "S", "Carry" }));
            Assert.That((string)Call("truth_table")["text"], Is.EqualTo(before));

            var exploded = Call("explode_subcircuit", new { component = instance });
            Assert.That(exploded["components"].Count(), Is.EqualTo(5));
            Assert.That((string)Call("truth_table")["text"], Is.EqualTo(before));

            // Explode, edit IC and collapse are one undo step each.
            Call("undo");
            Call("undo");
            Assert.That((string)Call("list_subcircuits")["subcircuits"][0]["outputs"][1], Is.EqualTo("Cout"));
            Call("undo");
            Assert.That(Call("list_subcircuits")["subcircuits"].Count(), Is.EqualTo(0));
            Assert.That((string)Call("truth_table")["text"], Is.EqualTo(before));
        }

        [Test]
        public void OpenSubcircuit_RefusesRecursionAndDiscardsOnCancel()
        {
            BuildFlatFullAdder();
            var collapsed = Call("collapse_to_subcircuit", new { name = "Adder", components = new[] { "x1", "x2", "a1", "a2", "o" } });
            var id = (string)collapsed["id"];
            Call("open_subcircuit", new { subcircuit = "Adder" });
            Call("add_component", new { type = "Subcircuit", subcircuit = id }, expectError: true);
            Call("add_component", new { type = "Not" });
            Call("close_subcircuit", new { apply = false });
            Assert.That((int)Call("get_subcircuit", new { subcircuit = id })["components"], Is.EqualTo(collapsed["components"].Value<int>()));
            Call("delete_subcircuit", new { subcircuit = id }, expectError: true);
            Call("make_subcircuit_unique", new { component = (int)collapsed["instance"]["id"] });
            Call("delete_subcircuit", new { subcircuit = id });
        }
    }
}
