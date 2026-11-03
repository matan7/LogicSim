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

        /// <summary>Turns the selection (or the whole circuit) into a reusable subcircuit.</summary>
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
                    "of the IC, its bulbs and output pins the outputs (ordered top to bottom).");
                return;
            }

            var fragment = CircuitFragment.Capture(Circuit, ids);
            var preview = new SubcircuitDefinition("preview", "preview", fragment.ToCircuit(Circuit.Library));
            if (preview.InputComponentIds.Count == 0 && preview.OutputComponentIds.Count == 0)
            {
                Ui.ShowMessage("Create IC", "The selection has no switch, button or input pin and no bulb or output pin, so the IC would have no pins.");
                return;
            }

            Ui.PromptText("Create IC", $"{ids.Count} components, {preview.InputComponentIds.Count} inputs, {preview.OutputComponentIds.Count} outputs. Name:",
                "IC " + (Circuit.Library.Count + 1), name =>
                {
                    var library = Circuit.Library;
                    var definition = new SubcircuitDefinition(library.CreateUniqueId(name), name, fragment.ToCircuit(library));
                    library.Add(definition);
                    Ui.ShowToast($"Created '{name}' - drag it from Custom Circuit Elements");
                });
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
