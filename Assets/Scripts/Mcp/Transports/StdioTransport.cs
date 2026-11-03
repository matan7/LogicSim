using System;
using System.IO;

namespace LogicSim.Mcp
{
    /// <summary>MCP stdio transport: one JSON-RPC message per line on stdin/stdout.</summary>
    public sealed class StdioTransport
    {
        private readonly TextReader _input;
        private readonly TextWriter _output;
        private readonly Func<string, string> _handler;

        public StdioTransport(TextReader input, TextWriter output, Func<string, string> handler)
        {
            _input = input;
            _output = output;
            _handler = handler;
        }

        /// <summary>Processes messages until the input is closed.</summary>
        public void Run()
        {
            string line;
            while ((line = _input.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var response = _handler(line);
                if (response == null)
                    continue;
                _output.Write(response);
                _output.Write('\n');
                _output.Flush();
            }
        }
    }
}
