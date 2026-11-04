using System;
using System.Collections.Generic;
using LogicSim.Simulation;

namespace LogicSim.Mcp
{
    /// <summary>What the MCP tools operate on. Implemented by the Unity app and by the headless server.</summary>
    public interface ILogicSimWorkspace
    {
        /// <summary>Short description of the host, e.g. "Unity app" or "headless".</summary>
        string HostName { get; }

        /// <summary>The circuit being edited: the document or the IC opened with open_subcircuit.</summary>
        Circuit Circuit { get; }

        /// <summary>Applies edits to <see cref="Circuit"/>; in the Unity app they land on the undo stack.</summary>
        ICircuitEditor Editor { get; }

        /// <summary>The document and the ICs opened for editing.</summary>
        CircuitHierarchy Hierarchy { get; }

        /// <summary>Simulation of the whole document.</summary>
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

    /// <summary>Workspace without UI: plain model, managed simulation engine, files on disk.</summary>
    public sealed class HeadlessWorkspace : ILogicSimWorkspace, IDisposable
    {
        private readonly DirectCircuitEditor _editor;

        public string HostName => "headless";
        public Circuit Circuit => Hierarchy.Current.Circuit;
        public ICircuitEditor Editor => Hierarchy.Editor;
        public CircuitHierarchy Hierarchy { get; }
        public SimulationSession Session { get; }
        public string DocumentName { get; set; } = "Untitled";
        public bool IsRunning { get; set; }
        public double TicksPerSecond { get; set; } = 600;
        public CircuitDocumentStore Documents { get; }
        public IReadOnlyList<int> SelectedComponentIds => Array.Empty<int>();

        public HeadlessWorkspace(string documentsDirectory, ISimulationEngine engine = null)
        {
            var root = new Circuit();
            _editor = new DirectCircuitEditor(root);
            Hierarchy = new CircuitHierarchy(root, _editor);
            Session = new SimulationSession(root, engine ?? new ManagedSimulationEngine());
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
