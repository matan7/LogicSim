using System;
using System.Linq;
using System.Text;
using Behaviours;
using Core;
using Core.AppState;
using Core.Settings;
using LogicSim.App;
using LogicSim.Simulation;
using LogicSim.Views;
using UnityEngine;

namespace LogicSim.UI
{
    /// <summary>App-level commands used by toolbar buttons, shortcuts and dialogs.</summary>
    public static class AppCommands
    {
        private const string ExamplesFolder = "Examples";

        private static AppUi Ui => AppUi.Instance;
        private static Circuit Circuit => SystemCore.CircuitService.Circuit;

        private static void EnsureEditing()
        {
            SystemCore.AppStateService.SetState(ApplicationState.Editor);
        }

        #region Files

        public static void New()
        {
            EnsureEditing();
            SystemCore.CircuitService.New();
            Ui?.ShowToast("New circuit (Ctrl+Z restores the previous one)");
        }

        public static void Save(bool saveAs)
        {
            var service = SystemCore.CircuitService;
            if (!saveAs && service.HasName)
            {
                SaveAs(service.DocumentName);
                return;
            }
            if (Ui == null)
                return;
            Ui.PromptText(saveAs ? "Save as" : "Save circuit", "Name of the circuit file:",
                service.HasName ? service.DocumentName : "My circuit", SaveAs);
        }

        private static void SaveAs(string name)
        {
            try
            {
                var path = SystemCore.CircuitService.Save(name);
                Ui?.ShowToast("Saved " + path);
            }
            catch (Exception exception)
            {
                Ui?.ShowMessage("Save failed", exception.Message);
            }
        }

        public static void ShowOpen()
        {
            if (Ui == null)
                return;
            var panel = Ui.OpenPanel("Open circuit");

            var examples = Resources.LoadAll<TextAsset>(ExamplesFolder).OrderBy(a => a.name).ToList();
            if (examples.Count > 0)
            {
                panel.AddHeading("Examples");
                foreach (var example in examples)
                {
                    var asset = example;
                    panel.AddListButton(PrettyName(asset.name), () =>
                    {
                        panel.Close();
                        OpenJson(asset.text, PrettyName(asset.name));
                    });
                }
            }

            panel.AddHeading("Saved circuits");
            var saved = SystemCore.CircuitService.Documents.List();
            if (saved.Count == 0)
                panel.AddText("Nothing saved yet. Use Save (Ctrl+S).", 18);
            foreach (var name in saved)
            {
                var file = name;
                panel.AddListButton(file, () =>
                {
                    panel.Close();
                    try
                    {
                        OpenJson(SystemCore.CircuitService.Documents.Read(file), file);
                    }
                    catch (Exception exception)
                    {
                        Ui.ShowMessage("Open failed", exception.Message);
                    }
                });
            }
            panel.AddText("Files are stored in " + SystemCore.CircuitService.Documents.Directory, 14);
            panel.AddButton("Close", panel.Close);
        }

        private static void OpenJson(string json, string name)
        {
            EnsureEditing();
            try
            {
                var document = SystemCore.CircuitService.OpenJson(json, name);
                FrameAll();
                Ui?.ShowToast(document.Warnings.Count == 0 ? "Opened " + name : $"Opened {name} with {document.Warnings.Count} warnings (see console)");
            }
            catch (CircuitException exception)
            {
                Ui?.ShowMessage("Open failed", exception.Message);
            }
        }

        private static string PrettyName(string assetName)
        {
            var words = assetName.Replace('-', ' ').Replace('_', ' ');
            return words.Length == 0 ? assetName : char.ToUpperInvariant(words[0]) + words.Substring(1);
        }

        #endregion

        #region Simulation

        public static void ToggleSimulation() => SystemCore.AppStateService.Toggle();

        public static void TogglePause()
        {
            var simulation = SystemCore.SimulationService;
            if (!SystemCore.AppStateService.IsSimulating)
                return;
            simulation.Paused = !simulation.Paused;
            Ui?.ShowToast(simulation.Paused ? "Paused - press . to step one tick" : "Running");
        }

        public static void Step()
        {
            var simulation = SystemCore.SimulationService;
            if (!SystemCore.AppStateService.IsSimulating)
                return;
            simulation.Paused = true;
            simulation.Step(1);
        }

        public static void ChangeSpeed(double factor)
        {
            var simulation = SystemCore.SimulationService;
            simulation.TicksPerSecond *= factor;
            var settings = SettingsService.Settings;
            if (settings != null)
            {
                settings.TicksPerSecond = simulation.TicksPerSecond;
                SystemCore.SettingsService.SaveSettings();
            }
            Ui?.ShowToast($"Speed {simulation.TicksPerSecond:N0} ticks/s");
        }

        #endregion

        #region Analysis and subcircuits

        public static void ShowTruthTable()
        {
            if (Ui == null)
                return;
            if (!TruthTableGenerator.DefaultInputs(Circuit).Any() || !TruthTableGenerator.DefaultOutputs(Circuit).Any())
            {
                Ui.ShowMessage("Truth table", "The truth table uses the circuit's switches, buttons and input pins as inputs " +
                    "and its bulbs and output pins as outputs. Add at least one of each.");
                return;
            }
            try
            {
                var table = TruthTableGenerator.Generate(Circuit);
                var text = table.ToText();
                if (!table.AllStable)
                    text += "\nSome rows did not settle (oscillation or very deep logic).";
                Ui.ShowMessage("Truth table", text, monospace: true);
            }
            catch (CircuitException exception)
            {
                Ui.ShowMessage("Truth table", exception.Message);
            }
        }

        /// <summary>Turns the selection (or the whole circuit) into a reusable subcircuit; the selection stays as it is.</summary>
        public static void CreateSubcircuit()
        {
            if (Ui == null)
                return;
            var ids = Actions.EditCommands.SelectedComponentIds();
            if (ids.Count == 0)
                ids = Circuit.Components.Select(c => c.Id).ToList();
            if (ids.Count == 0)
            {
                Ui.ShowMessage("Create IC", "Build a circuit first. Its switches, buttons and input pins become the inputs " +
                    "of the IC, its bulbs and output pins the outputs (ordered top to bottom).\n\n" +
                    "To replace part of a circuit with an IC, select it and use Collapse instead.");
                return;
            }

            var fragment = CircuitFragment.Capture(Circuit, ids);
            var preview = new SubcircuitDefinition("preview", "preview", fragment.ToCircuit(Circuit.Library));
            if (preview.InputComponentIds.Count == 0 && preview.OutputComponentIds.Count == 0)
            {
                Ui.ShowMessage("Create IC", "The selection has no switch, button or input pin and no bulb or output pin, so the IC would have no pins.\n\n" +
                    "Collapse creates the pins from the wires that cross the selection instead.");
                return;
            }

            Ui.PromptText("Create IC", $"{ids.Count} components, {preview.InputComponentIds.Count} inputs, {preview.OutputComponentIds.Count} outputs. Name:",
                "IC " + (Circuit.Library.Count + 1), name =>
                {
                    var library = Circuit.Library;
                    var definition = new SubcircuitDefinition(library.CreateUniqueId(name), name, fragment.ToCircuit(library));
                    SystemCore.CircuitService.Editor.Apply(new AddSubcircuitDefinitionEdit(library, definition));
                    Ui.ShowToast($"Created '{name}' - drag it from Custom Circuit Elements");
                });
        }

        /// <summary>Replaces the selected components with an instance of a new IC made from them.</summary>
        public static void CollapseSelection()
        {
            if (Ui == null || !RequireEditing("Collapse"))
                return;
            var ids = Actions.EditCommands.SelectedComponentIds();
            if (ids.Count == 0)
            {
                Ui.ShowMessage("Collapse to IC", "Select the gates to move into a new IC. Wires that cross the selection become its pins; " +
                    "switches, buttons, bulbs and pins stay outside so you can still drive and watch the circuit.");
                return;
            }
            Ui.PromptText("Collapse to IC", "Name of the new IC:", "IC " + (Circuit.Library.Count + 1), name =>
            {
                try
                {
                    var result = SubcircuitRefactoring.Collapse(Circuit, ids, name);
                    SystemCore.SelectingService.DeselectAll();
                    SystemCore.CircuitService.Editor.Apply(result.Edit);
                    Actions.EditCommands.Select(new[] { result.Instance.Id });
                    Ui.ShowToast($"'{result.Definition.Name}': {result.Definition.InputComponentIds.Count} inputs, " +
                        $"{result.Definition.OutputComponentIds.Count} outputs. Double-click it to edit.");
                }
                catch (CircuitException exception)
                {
                    Ui.ShowMessage("Collapse to IC", exception.Message);
                }
            });
        }

        public static void ExplodeSelection()
        {
            if (Ui == null || !RequireEditing("Explode"))
                return;
            var instances = SelectedInstances();
            if (instances.Count == 0)
            {
                Ui.ShowToast("Select an IC to explode");
                return;
            }
            var edits = new System.Collections.Generic.List<ICircuitEdit>();
            var created = new System.Collections.Generic.List<int>();
            // Each explode reserves fresh ids, so the edits can be built one after another before applying.
            foreach (var instance in instances)
            {
                var result = SubcircuitRefactoring.Explode(Circuit, instance.Id);
                edits.Add(result.Edit);
                created.AddRange(result.Components.Select(c => c.Id));
            }
            SystemCore.SelectingService.DeselectAll();
            SystemCore.CircuitService.Editor.Apply(edits.Count == 1 ? edits[0] : new CompositeEdit("Explode ICs", edits));
            Actions.EditCommands.Select(created);
        }

        public static void MakeSelectedUnique()
        {
            if (Ui == null || !RequireEditing("Make unique"))
                return;
            var instances = SelectedInstances();
            if (instances.Count != 1)
            {
                Ui.ShowToast("Select one IC to give it its own copy");
                return;
            }
            var instance = instances[0];
            var definition = Circuit.Library.Get(instance.SubcircuitId);
            Ui.PromptText("Make unique", $"Copy '{definition.Name}' for this instance only. Name of the copy:",
                SubcircuitRefactoring.UniqueName(Circuit.Library, definition.Name), name =>
                {
                    SystemCore.CircuitService.Editor.Apply(SubcircuitRefactoring.MakeUnique(Circuit, instance.Id, name, out var copy));
                    Ui.ShowToast($"This instance now uses '{copy.Name}'");
                });
        }

        /// <summary>Opens the selected IC (or the given instance) for in-place editing.</summary>
        public static void OpenSelectedSubcircuit()
        {
            var instances = SelectedInstances();
            if (instances.Count != 1)
            {
                Ui?.ShowToast("Select one IC (or double-click it) to open it");
                return;
            }
            OpenSubcircuit(instances[0].Id);
        }

        public static void OpenSubcircuit(int instanceId)
        {
            if (!Circuit.TryGetComponent(instanceId, out var instance) || instance.Kind != ComponentKind.Subcircuit)
                return;
            if (!Circuit.Library.Contains(instance.SubcircuitId))
            {
                Ui?.ShowToast("This IC's definition is missing");
                return;
            }
            var camera = CameraControl.Instance;
            if (camera != null)
                _savedViews.Push(camera.GetView());
            try
            {
                SystemCore.SelectingService.DeselectAll();
                SystemCore.CircuitService.OpenInstance(instanceId);
            }
            catch (CircuitException exception)
            {
                if (camera != null)
                    _savedViews.Pop();
                Ui?.ShowMessage("Open IC", exception.Message);
                return;
            }
            FrameAll();
            var hierarchy = SystemCore.CircuitService.Hierarchy;
            Ui?.ShowToast(SystemCore.AppStateService.IsSimulating
                ? "Showing the values of this instance. Esc goes back."
                : $"Editing '{hierarchy.GetName(hierarchy.Current)}': changes apply to every instance when you leave (Esc or Done).");
        }

        /// <summary>Leaves the open IC. <paramref name="apply"/>: keep the changes (one undo step) or discard them.</summary>
        public static void CloseSubcircuit(bool apply)
        {
            var service = SystemCore.CircuitService;
            if (service.Hierarchy.IsAtRoot)
                return;
            CloseTo(service.Hierarchy.Depth - 1, apply);
        }

        /// <summary>Goes back to the given level of the breadcrumb (0 = the document).</summary>
        public static void CloseTo(int depth, bool apply)
        {
            var service = SystemCore.CircuitService;
            var hierarchy = service.Hierarchy;
            if (depth >= hierarchy.Depth)
                return;
            if (!apply && SystemCore.AppStateService.IsSimulating)
                apply = true;
            try
            {
                SystemCore.SelectingService.DeselectAll();
                while (hierarchy.Depth > depth)
                {
                    int instanceId = hierarchy.Current.InstanceId;
                    var name = hierarchy.GetName(hierarchy.Current);
                    bool modified = hierarchy.IsModified(hierarchy.Current);
                    service.CloseLevel(apply);
                    var camera = CameraControl.Instance;
                    if (_savedViews.Count > 0)
                    {
                        var (position, size) = _savedViews.Pop();
                        if (camera != null)
                            camera.SetView(position, size);
                    }
                    if (hierarchy.Depth == depth)
                    {
                        if (instanceId != 0)
                            Actions.EditCommands.Select(new[] { instanceId });
                        if (modified)
                            Ui?.ShowToast(apply ? $"Updated every instance of '{name}'" : $"Discarded the changes to '{name}'");
                    }
                }
            }
            catch (CircuitException exception)
            {
                Ui?.ShowMessage("Close IC", exception.Message);
            }
        }

        private static readonly System.Collections.Generic.Stack<(Vector3 position, float size)> _savedViews =
            new System.Collections.Generic.Stack<(Vector3 position, float size)>();

        private static System.Collections.Generic.List<CircuitComponent> SelectedInstances()
        {
            return Actions.EditCommands.SelectedComponentIds()
                .Select(id => Circuit.GetComponent(id))
                .Where(c => c.Kind == ComponentKind.Subcircuit && Circuit.Library.Contains(c.SubcircuitId))
                .ToList();
        }

        private static bool RequireEditing(string what)
        {
            if (SystemCore.AppStateService.IsEditing)
                return true;
            Ui?.ShowToast(what + " is available in edit mode (F5 stops the simulation)");
            return false;
        }

        #endregion

        #region View

        public static void FrameAll()
        {
            var view = CircuitView.Instance;
            var camera = CameraControl.Instance;
            if (view == null || camera == null)
                return;
            if (view.TryGetBounds(Circuit.Components.Select(c => c.Id), out var bounds))
                camera.FrameBounds(bounds, onlyIfOutside: false);
        }

        public static void ShowHelp()
        {
            if (Ui == null)
                return;
            var mcp = McpServerHost.Instance;
            var endpoint = mcp != null && mcp.IsListening ? mcp.Endpoint : $"http://127.0.0.1:{SettingsService.Settings?.McpPort ?? 8765}/mcp";
            var text = new StringBuilder();
            text.AppendLine("<b>Editing</b>");
            text.AppendLine("Drag components from the palette. Drag from a pin to another pin to connect them;");
            text.AppendLine("drag a connected input onto empty space to disconnect it.");
            text.AppendLine("Click / Shift+click / drag on empty space: select.  Middle mouse or Alt+drag: pan.  Wheel: zoom.");
            text.AppendLine("Right click: set the paste position.");
            text.AppendLine();
            text.AppendLine("<b>Shortcuts</b>");
            text.AppendLine("Ctrl+Z / Ctrl+Y undo / redo     Ctrl+X / C / V cut, copy, paste     Del delete");
            text.AppendLine("Ctrl+A select all     R rotate     F frame circuit     Esc cancel / deselect");
            text.AppendLine("Ctrl+N new     Ctrl+O open     Ctrl+S save     F1 help");
            text.AppendLine();
            text.AppendLine("<b>ICs (custom components)</b>");
            text.AppendLine("Create IC: the selection becomes a palette entry (its switches/bulbs/pins are the IC pins).");
            text.AppendLine("Collapse: the selection is replaced by a new IC; wires crossing it become the pins.");
            text.AppendLine("Double-click an IC (or Open IC) to edit it in place: Done updates every instance, Discard drops the changes,");
            text.AppendLine("Esc goes back. While simulating, double-click shows the live values inside that instance.");
            text.AppendLine("Explode replaces an IC with its contents; Make unique gives one instance its own copy.");
            text.AppendLine();
            text.AppendLine("<b>Neurons</b>");
            text.AppendLine("Neuron: fires when the weighted sum of its high inputs reaches the threshold. Spiking neuron: inputs charge");
            text.AppendLine("a leaking potential, at the threshold it spikes for one tick. Neural block: a small trained network.");
            text.AppendLine("Select one to edit its weights, layers or to train it from a truth table or an IC in the inspector.");
            text.AppendLine();
            text.AppendLine("<b>Simulation</b>");
            text.AppendLine("F5 or the play button toggles the simulation. Click switches, hold buttons.");
            text.AppendLine("P pause, . step one tick, [ and ] change the speed.");
            text.AppendLine("Each gate delays its output by one tick, so glitches, latches and oscillators behave like real logic.");
            text.AppendLine();
            text.AppendLine("<b>AI agents (MCP)</b>");
            text.AppendLine("This app runs a Model Context Protocol server at " + endpoint);
            text.AppendLine("Claude Code:  claude mcp add --transport http logicsim " + endpoint);
            text.AppendLine("Agents can build circuits, run the simulation and compute truth tables while you watch.");
            Ui.ShowMessage("Help", text.ToString());
        }

        #endregion
    }
}
