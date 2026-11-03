using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LogicSim.Mcp
{
    /// <summary>Result of a tool call. <see cref="Data"/> is returned to the client as JSON text.</summary>
    public sealed class McpToolResult
    {
        public JToken Data { get; }
        public bool IsError { get; }

        private McpToolResult(JToken data, bool isError)
        {
            Data = data;
            IsError = isError;
        }

        public static McpToolResult Ok(object data) => new McpToolResult(data == null ? JValue.CreateNull() : JToken.FromObject(data), false);
        public static McpToolResult Error(string message) => new McpToolResult(new JObject { ["error"] = message }, true);
    }

    /// <summary>Thrown by tool handlers for invalid arguments; reported as a tool error, not a protocol error.</summary>
    public sealed class McpToolException : Exception
    {
        public McpToolException(string message) : base(message) { }
    }

    public sealed class McpTool
    {
        public string Name { get; }
        public string Title { get; }
        public string Description { get; }
        public JObject InputSchema { get; }
        public bool ReadOnly { get; }
        public Func<JObject, McpToolResult> Handler { get; }

        public McpTool(string name, string title, string description, JObject inputSchema, Func<JObject, McpToolResult> handler, bool readOnly = false)
        {
            Name = name;
            Title = title;
            Description = description;
            InputSchema = inputSchema ?? new JObject { ["type"] = "object", ["properties"] = new JObject() };
            Handler = handler;
            ReadOnly = readOnly;
        }
    }

    /// <summary>
    /// Transport-independent Model Context Protocol server (JSON-RPC 2.0). Supports tools only.
    /// Not thread-safe: the host must serialize calls (the Unity host runs them on the main thread).
    /// </summary>
    public sealed class McpServer
    {
        public static readonly string[] SupportedProtocolVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };

        private readonly Dictionary<string, McpTool> _tools = new Dictionary<string, McpTool>();
        private readonly List<string> _toolOrder = new List<string>();

        public string Name { get; }
        public string Version { get; }
        public string Instructions { get; set; }
        public string NegotiatedProtocolVersion { get; private set; }

        /// <summary>Raised after each tool call (for logging/UI).</summary>
        public event Action<string, bool> ToolCalled;

        public McpServer(string name, string version)
        {
            Name = name;
            Version = version;
        }

        public IEnumerable<McpTool> Tools => _toolOrder.Select(n => _tools[n]);

        public void AddTool(McpTool tool)
        {
            if (_tools.ContainsKey(tool.Name))
                throw new ArgumentException($"Tool '{tool.Name}' already registered");
            _tools.Add(tool.Name, tool);
            _toolOrder.Add(tool.Name);
        }

        /// <summary>Handles one JSON-RPC message or batch. Returns null when no response is due (notifications).</summary>
        public string HandleMessage(string json)
        {
            JToken token;
            try
            {
                token = JToken.Parse(json);
            }
            catch (JsonException exception)
            {
                return Serialize(ErrorResponse(JValue.CreateNull(), -32700, "Parse error: " + exception.Message));
            }

            if (token is JArray batch)
            {
                if (batch.Count == 0)
                    return Serialize(ErrorResponse(JValue.CreateNull(), -32600, "Empty batch"));
                var responses = new JArray();
                foreach (var item in batch)
                {
                    var response = HandleSingle(item);
                    if (response != null)
                        responses.Add(response);
                }
                return responses.Count == 0 ? null : Serialize(responses);
            }

            var single = HandleSingle(token);
            return single == null ? null : Serialize(single);
        }

        /// <summary>True when the message contains at least one request (as opposed to only notifications/responses).</summary>
        public static bool ContainsRequest(string json)
        {
            try
            {
                var token = JToken.Parse(json);
                var items = token is JArray array ? array.Children().ToArray() : new[] { token };
                return items.OfType<JObject>().Any(o => o["method"] != null && o["id"] != null);
            }
            catch (JsonException)
            {
                return true;
            }
        }

        public static bool IsInitializeRequest(string json)
        {
            try
            {
                var token = JToken.Parse(json);
                var items = token is JArray array ? array.Children().ToArray() : new[] { token };
                return items.OfType<JObject>().Any(o => (string)o["method"] == "initialize");
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private JObject HandleSingle(JToken token)
        {
            if (!(token is JObject message))
                return ErrorResponse(JValue.CreateNull(), -32600, "Invalid request");

            var id = message["id"];
            var method = (string)message["method"];
            bool isNotification = id == null || id.Type == JTokenType.Undefined;

            if (method == null)
            {
                // A response to a server request; this server never sends requests.
                return null;
            }

            try
            {
                var parameters = message["params"] as JObject ?? new JObject();
                JToken result;
                switch (method)
                {
                    case "initialize":
                        result = Initialize(parameters);
                        break;
                    case "ping":
                        result = new JObject();
                        break;
                    case "tools/list":
                        result = ListTools();
                        break;
                    case "tools/call":
                        result = CallTool(parameters);
                        break;
                    case "resources/list":
                        result = new JObject { ["resources"] = new JArray() };
                        break;
                    case "resources/templates/list":
                        result = new JObject { ["resourceTemplates"] = new JArray() };
                        break;
                    case "prompts/list":
                        result = new JObject { ["prompts"] = new JArray() };
                        break;
                    case "logging/setLevel":
                        result = new JObject();
                        break;
                    default:
                        if (method.StartsWith("notifications/", StringComparison.Ordinal))
                            return null;
                        return isNotification ? null : ErrorResponse(id, -32601, $"Method not found: {method}");
                }

                if (isNotification)
                    return null;
                return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
            }
            catch (McpProtocolException exception)
            {
                return isNotification ? null : ErrorResponse(id, exception.Code, exception.Message);
            }
            catch (Exception exception)
            {
                return isNotification ? null : ErrorResponse(id, -32603, "Internal error: " + exception.Message);
            }
        }

        private JObject Initialize(JObject parameters)
        {
            var requested = (string)parameters["protocolVersion"];
            NegotiatedProtocolVersion = SupportedProtocolVersions.Contains(requested) ? requested : SupportedProtocolVersions[0];
            var result = new JObject
            {
                ["protocolVersion"] = NegotiatedProtocolVersion,
                ["capabilities"] = new JObject
                {
                    ["tools"] = new JObject { ["listChanged"] = false }
                },
                ["serverInfo"] = new JObject
                {
                    ["name"] = Name,
                    ["title"] = "LogicSim",
                    ["version"] = Version
                }
            };
            if (!string.IsNullOrEmpty(Instructions))
                result["instructions"] = Instructions;
            return result;
        }

        private JObject ListTools()
        {
            var tools = new JArray();
            foreach (var tool in Tools)
            {
                var entry = new JObject
                {
                    ["name"] = tool.Name,
                    ["title"] = tool.Title,
                    ["description"] = tool.Description,
                    ["inputSchema"] = tool.InputSchema
                };
                entry["annotations"] = new JObject
                {
                    ["title"] = tool.Title,
                    ["readOnlyHint"] = tool.ReadOnly
                };
                tools.Add(entry);
            }
            return new JObject { ["tools"] = tools };
        }

        private JObject CallTool(JObject parameters)
        {
            var name = (string)parameters["name"];
            if (string.IsNullOrEmpty(name) || !_tools.TryGetValue(name, out var tool))
                throw new McpProtocolException(-32602, $"Unknown tool: {name}");
            var arguments = parameters["arguments"] as JObject ?? new JObject();

            McpToolResult result;
            try
            {
                result = tool.Handler(arguments) ?? McpToolResult.Ok(new JObject());
            }
            catch (McpToolException exception)
            {
                result = McpToolResult.Error(exception.Message);
            }
            catch (Simulation.CircuitException exception)
            {
                result = McpToolResult.Error(exception.Message);
            }
            catch (Exception exception)
            {
                result = McpToolResult.Error($"{exception.GetType().Name}: {exception.Message}");
            }

            ToolCalled?.Invoke(name, !result.IsError);
            var text = result.Data.Type == JTokenType.String ? (string)result.Data : result.Data.ToString(Formatting.Indented);
            return new JObject
            {
                ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                ["isError"] = result.IsError
            };
        }

        private static JObject ErrorResponse(JToken id, int code, string message)
        {
            return new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id ?? JValue.CreateNull(),
                ["error"] = new JObject { ["code"] = code, ["message"] = message }
            };
        }

        private static string Serialize(JToken token) => token.ToString(Formatting.None);
    }

    internal sealed class McpProtocolException : Exception
    {
        public int Code { get; }

        public McpProtocolException(int code, string message) : base(message)
        {
            Code = code;
        }
    }
}
