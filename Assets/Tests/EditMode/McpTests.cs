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
    }
}
