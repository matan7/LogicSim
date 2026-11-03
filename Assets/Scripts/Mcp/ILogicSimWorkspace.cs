using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogicSim.Simulation;

namespace LogicSim.Mcp
{
    /// <summary>What the MCP tools operate on. Implemented by the Unity app and by the headless server.</summary>
    public interface ILogicSimWorkspace
    {
        /// <summary>Short description of the host, e.g. "Unity app" or "headless".</summary>
        string HostName { get; }

        Circuit Circuit { get; }

        /// <summary>Applies edits; in the Unity app they land on the undo stack.</summary>
        ICircuitEditor Editor { get; }

        SimulationSession Session { get; }

        string DocumentName { get; set; }

        /// <summary>Free-running simulation (the Unity app's simulation mode).</summary>
        bool IsRunning { get; set; }

        double TicksPerSecond { get; set; }

        CircuitDocumentStore Documents { get; }

        bool Undo();
        bool Redo();

        /// <summary>Components currently selected by the user (empty when there is no UI).</summary>
        IReadOnlyList<int> SelectedComponentIds { get; }

        /// <summary>Asks the UI to show the given components (no-op without UI).</summary>
        void Focus(IEnumerable<int> componentIds);
    }

    /// <summary>Saves and loads circuit documents in a directory.</summary>
    public sealed class CircuitDocumentStore
    {
        public string Directory { get; }

        public CircuitDocumentStore(string directory)
        {
            Directory = directory;
        }

        public string PathFor(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new McpToolException("Name must not be empty");
            var file = SanitizeFileName(name);
            if (!file.EndsWith(CircuitSerializer.FileExtension, StringComparison.OrdinalIgnoreCase))
                file += CircuitSerializer.FileExtension;
            return Path.Combine(Directory, file);
        }

        public IReadOnlyList<string> List()
        {
            if (!System.IO.Directory.Exists(Directory))
                return Array.Empty<string>();
            return System.IO.Directory.GetFiles(Directory, "*" + CircuitSerializer.FileExtension)
                .Select(p => Path.GetFileName(p).Substring(0, Path.GetFileName(p).Length - CircuitSerializer.FileExtension.Length))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string Save(Circuit circuit, string name)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = PathFor(name);
            File.WriteAllText(path, CircuitSerializer.Serialize(circuit, name));
            return path;
        }

        public string Read(string name)
        {
            var path = PathFor(name);
            if (!File.Exists(path))
                throw new McpToolException($"No saved circuit named '{name}'");
            return File.ReadAllText(path);
        }

        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray();
            var result = new string(chars).Trim('.', ' ');
            return result.Length == 0 ? "circuit" : result;
        }
    }

    /// <summary>Workspace without UI: plain model, managed simulation engine, files on disk.</summary>
    public sealed class HeadlessWorkspace : ILogicSimWorkspace, IDisposable
    {
        private readonly DirectCircuitEditor _editor;

        public string HostName => "headless";
        public Circuit Circuit { get; }
        public ICircuitEditor Editor => _editor;
        public SimulationSession Session { get; }
        public string DocumentName { get; set; } = "Untitled";
        public bool IsRunning { get; set; }
        public double TicksPerSecond { get; set; } = 600;
        public CircuitDocumentStore Documents { get; }
        public IReadOnlyList<int> SelectedComponentIds => Array.Empty<int>();

        public HeadlessWorkspace(string documentsDirectory, ISimulationEngine engine = null)
        {
            Circuit = new Circuit();
            _editor = new DirectCircuitEditor(Circuit);
            Session = new SimulationSession(Circuit, engine ?? new ManagedSimulationEngine());
            Documents = new CircuitDocumentStore(documentsDirectory);
        }

        public bool Undo() => _editor.Undo();
        public bool Redo() => _editor.Redo();

        public void Focus(IEnumerable<int> componentIds)
        {
        }

        public void Dispose() => Session.Dispose();
    }
}
