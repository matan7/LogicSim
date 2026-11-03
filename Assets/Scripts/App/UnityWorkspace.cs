using System.Collections.Generic;
using System.Linq;
using Behaviours;
using Core;
using Core.AppState;
using LogicSim.Mcp;
using LogicSim.Simulation;
using LogicSim.Views;

namespace LogicSim.App
{
    /// <summary>Exposes the running app to the MCP tools: edits go through the undo stack and show up live.</summary>
    public sealed class UnityWorkspace : ILogicSimWorkspace
    {
        public string HostName => "Unity app";
        public Circuit Circuit => SystemCore.CircuitService.Circuit;
        public ICircuitEditor Editor => SystemCore.CircuitService.Editor;
        public SimulationSession Session => SystemCore.SimulationService.Session;
        public CircuitDocumentStore Documents => SystemCore.CircuitService.Documents;

        public string DocumentName
        {
            get => SystemCore.CircuitService.DocumentName;
            set => SystemCore.CircuitService.SetDocumentName(value);
        }

        public bool IsRunning
        {
            get => SystemCore.AppStateService.IsSimulating;
            set => SystemCore.AppStateService.SetState(value ? ApplicationState.Simulator : ApplicationState.Editor);
        }

        public double TicksPerSecond
        {
            get => SystemCore.SimulationService.TicksPerSecond;
            set => SystemCore.SimulationService.TicksPerSecond = value;
        }

        public bool Undo()
        {
            int before = SystemCore.CommandService.UndoCount;
            SystemCore.CommandService.UndoCommand();
            return SystemCore.CommandService.UndoCount < before;
        }

        public bool Redo()
        {
            int before = SystemCore.CommandService.RedoCount;
            SystemCore.CommandService.RedoCommand();
            return SystemCore.CommandService.RedoCount < before;
        }

        public IReadOnlyList<int> SelectedComponentIds
        {
            get
            {
                return SystemCore.SelectingService.SelectionList
                    .Select(s => s.Transform != null ? s.Transform.GetComponent<ComponentView>() : null)
                    .Where(v => v != null)
                    .Select(v => v.ComponentId)
                    .ToList();
            }
        }

        public void Focus(IEnumerable<int> componentIds)
        {
            var view = CircuitView.Instance;
            var camera = CameraControl.Instance;
            if (view != null && camera != null && view.TryGetBounds(componentIds, out var bounds))
                camera.FrameBounds(bounds, onlyIfOutside: true);
        }
    }
}
