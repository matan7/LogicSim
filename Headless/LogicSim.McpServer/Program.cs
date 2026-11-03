using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LogicSim.Mcp;
using LogicSim.Simulation;

namespace LogicSim.McpServer
{
    internal static class Program
    {
        private const string Usage =
@"LogicSim MCP server (headless)

Usage: logicsim-mcp [options]

  --http <port>     Serve MCP over HTTP at http://127.0.0.1:<port>/mcp instead of stdio
  --dir <path>      Directory for saved circuits (default: ./circuits)
  --open <file>     Load a circuit file (*.lsim.json) at start-up
  --help            Show this help

Without --http the server speaks MCP over stdin/stdout, e.g. for Claude Desktop or Claude Code:
  claude mcp add logicsim -- dotnet run --project Headless/LogicSim.McpServer";

        private static int Main(string[] args)
        {
            int? httpPort = null;
            string directory = Path.Combine(Environment.CurrentDirectory, "circuits");
            string openFile = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--http" when i + 1 < args.Length:
                        httpPort = int.Parse(args[++i]);
                        break;
                    case "--dir" when i + 1 < args.Length:
                        directory = Path.GetFullPath(args[++i]);
                        break;
                    case "--open" when i + 1 < args.Length:
                        openFile = args[++i];
                        break;
                    case "--help":
                    case "-h":
                        Console.Error.WriteLine(Usage);
                        return 0;
                    default:
                        Console.Error.WriteLine($"Unknown argument '{args[i]}'\n\n{Usage}");
                        return 1;
                }
            }

            using var workspace = new HeadlessWorkspace(directory);
            if (openFile != null)
            {
                var document = CircuitSerializer.Deserialize(File.ReadAllText(openFile), workspace.Circuit.Library);
                document.Snapshot.Restore(workspace.Circuit);
                workspace.DocumentName = string.IsNullOrEmpty(document.Name) ? Path.GetFileNameWithoutExtension(openFile) : document.Name;
                Console.Error.WriteLine($"Loaded {openFile}: {workspace.Circuit.ComponentCount} components");
            }

            var server = LogicSimTools.CreateServer(workspace);
            var gate = new object();
            string Handle(string message)
            {
                lock (gate)
                    return server.HandleMessage(message);
            }

            if (httpPort.HasValue)
            {
                using var transport = new StreamableHttpTransport(httpPort.Value, body => Task.FromResult(Handle(body)));
                transport.Log += message => Console.Error.WriteLine(message);
                transport.Start();
                Console.Error.WriteLine("Press Ctrl+C to stop.");
                var stop = new ManualResetEventSlim();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
                stop.Wait();
                return 0;
            }

            Console.Error.WriteLine($"LogicSim MCP server on stdio, circuits in {directory}");
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };
            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            new StdioTransport(stdin, stdout, Handle).Run();
            return 0;
        }
    }
}
