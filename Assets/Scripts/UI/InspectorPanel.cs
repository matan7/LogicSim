using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core;
using LogicSim.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogicSim.UI
{
    /// <summary>
    /// Properties of the selected component (edit mode): label, inputs, clock period, constant value, neuron weights,
    /// network layers and training, IC actions. Every change is one undoable edit.
    /// </summary>
    public class InspectorPanel : MonoBehaviour
    {
        private const float Width = 340;
        private const float LabelWidth = 110;

        private RectTransform _root;
        private RectTransform _content;
        private int _shownId;
        private Circuit _shownCircuit;
        private bool _dirty = true;

        private static CultureInfo Invariant => CultureInfo.InvariantCulture;
        private static Circuit Circuit => SystemCore.CircuitService.Circuit;

        public static InspectorPanel Create(Canvas canvas)
        {
            // The behaviour lives on an always-active host so it keeps polling while the panel is hidden.
            var host = UiFactory.CreateRect("Inspector Host", canvas.transform);
            UiFactory.Stretch(host);
            var rect = UiFactory.CreateRect("Inspector", host);
            rect.anchorMin = new Vector2(1, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-20, -70);
            rect.sizeDelta = new Vector2(Width, 100);
            UiFactory.AddImage(rect, UiFactory.PanelColor);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 14);
            layout.spacing = 6;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var panel = host.gameObject.AddComponent<InspectorPanel>();
            panel._root = rect;
            panel._content = rect;
            rect.gameObject.SetActive(false);
            return panel;
        }

        private void Awake()
        {
            SystemCore.CircuitService.LevelChanged += MarkDirty;
            SystemCore.SelectingService.Changed += MarkDirty;
            SystemCore.AppStateService.StateChanged += OnStateChanged;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (SystemCore.CircuitService != null)
                SystemCore.CircuitService.LevelChanged -= MarkDirty;
            if (SystemCore.SelectingService != null)
                SystemCore.SelectingService.Changed -= MarkDirty;
            if (SystemCore.AppStateService != null)
                SystemCore.AppStateService.StateChanged -= OnStateChanged;
        }

        private void MarkDirty() => _dirty = true;

        private void OnStateChanged(Core.AppState.ApplicationState state) => _dirty = true;

        private void OnPropertiesChanged(CircuitComponent component)
        {
            if (component.Id == _shownId)
                _dirty = true;
        }

        private void Subscribe(Circuit circuit)
        {
            if (_shownCircuit == circuit)
                return;
            Unsubscribe();
            _shownCircuit = circuit;
            _shownCircuit.ComponentPropertiesChanged += OnPropertiesChanged;
            _shownCircuit.Library.Changed += MarkDirty;
        }

        private void Unsubscribe()
        {
            if (_shownCircuit == null)
                return;
            _shownCircuit.ComponentPropertiesChanged -= OnPropertiesChanged;
            _shownCircuit.Library.Changed -= MarkDirty;
            _shownCircuit = null;
        }

        // Rebuilt after selection, mode, level or property changes (see the subscriptions above).
        private void Update()
        {
            if (_dirty)
                Rebuild(SelectedId());
        }

        private static int SelectedId()
        {
            if (!SystemCore.AppStateService.IsEditing)
                return 0;
            var ids = Actions.EditCommands.SelectedComponentIds();
            return ids.Count == 1 ? ids[0] : 0;
        }

        private void Rebuild(int id)
        {
            // Do not rebuild under the user's caret.
            if (id == _shownId && IsEditingText())
                return;
            _shownId = id;
            _dirty = false;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            if (id == 0 || !Circuit.TryGetComponent(id, out var component))
            {
                _root.gameObject.SetActive(false);
                return;
            }
            Subscribe(Circuit);
            _root.gameObject.SetActive(true);
            Build(component);
            LayoutRebuilder.MarkLayoutForRebuild(_root);
        }

        private bool IsEditingText()
        {
            var selected = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.transform.IsChildOf(_root) && selected.GetComponent<TMP_InputField>() != null;
        }

        #region Layout helpers

        private void Heading(string text)
        {
            var heading = UiFactory.CreateText(_content, text, 20, UiFactory.TextColor);
            heading.fontStyle = FontStyles.Bold;
            heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
        }

        private void Note(string text)
        {
            var note = UiFactory.CreateText(_content, text, 14, UiFactory.MutedText);
            note.gameObject.AddComponent<LayoutElement>().preferredHeight = 20 * Mathf.Max(1, Mathf.CeilToInt(text.Length / 42f));
        }

        private RectTransform Row(string label)
        {
            var row = UiFactory.CreateHorizontalGroup(_content, 8);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            row.GetComponent<LayoutElement>().preferredHeight = 36;
            var text = UiFactory.CreateText(row, label, 16, UiFactory.MutedText, TextAlignmentOptions.MidlineLeft);
            text.rectTransform.sizeDelta = new Vector2(LabelWidth, 36);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return row;
        }

        private void Field(string label, string value, Action<string> apply, string placeholder = null)
        {
            var row = Row(label);
            var input = UiFactory.CreateInputField(row, value, Width - 28 - LabelWidth - 8, 34);
            if (input.placeholder is TMP_Text hint)
                hint.text = placeholder ?? string.Empty;
            input.onEndEdit.AddListener(text =>
            {
                if (text == value)
                    return;
                Run(() => apply(text));
            });
        }

        private void Buttons(params (string label, Action action)[] buttons)
        {
            var row = UiFactory.CreateHorizontalGroup(_content, 8);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            float width = (Width - 28 - 8 * (buttons.Length - 1)) / buttons.Length;
            foreach (var (label, action) in buttons)
                UiFactory.CreateButton(row, label, () => Run(action), width, 34);
        }

        /// <summary>Runs an edit; model errors become a toast and the panel shows the current state again.</summary>
        private void Run(Action action)
        {
            try
            {
                action();
            }
            catch (CircuitException exception)
            {
                AppUi.Instance?.ShowToast(exception.Message, 6);
            }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException)
            {
                AppUi.Instance?.ShowToast("Not a valid number", 4);
            }
            _dirty = true;
        }

        private static float ParseFloat(string text) => float.Parse(text.Trim().Replace(',', '.'), NumberStyles.Float, Invariant);
        private static int ParseInt(string text) => int.Parse(text.Trim(), NumberStyles.Integer, Invariant);
        private static string Format(float value) => value.ToString("0.####", Invariant);

        private static void Apply(CircuitComponent component, Func<ComponentProperties, ComponentProperties> change)
        {
            var after = change(component.GetProperties());
            if (after.Equals(component.GetProperties()))
                return;
            SystemCore.CircuitService.Editor.Apply(new SetPropertiesEdit(Circuit, component.Id, after));
        }

        #endregion

        private void Build(CircuitComponent component)
        {
            var spec = component.Spec;
            Heading($"{spec.DisplayName}  #{component.Id}");

            Field("Label", component.Label, text => Apply(component, p => { p.Label = text.Trim(); return p; }), "name");

            if (spec.HasVariableInputs)
                Field("Inputs", component.InputCount.ToString(Invariant), text => Apply(component, p => { p.InputCount = ParseInt(text); return p; }),
                    $"{spec.MinInputs}-{spec.MaxInputs}");

            switch (component.Kind)
            {
                case ComponentKind.Clock:
                    Field("Half period", component.ClockHalfPeriod.ToString(Invariant), text => Apply(component, p => { p.ClockHalfPeriod = ParseInt(text); return p; }), "ticks");
                    break;
                case ComponentKind.Constant:
                    Buttons(("Value: " + (component.Value ? "1" : "0"), () => Apply(component, p => { p.Value = !p.Value; return p; })));
                    break;
                case ComponentKind.Neuron:
                case ComponentKind.SpikingNeuron:
                    BuildNeuron(component);
                    break;
                case ComponentKind.NeuralBlock:
                    BuildNetwork(component);
                    break;
                case ComponentKind.Subcircuit:
                    BuildSubcircuit(component);
                    break;
            }
        }

        private void BuildNeuron(CircuitComponent component)
        {
            var neuron = component.Neuron;
            Field("Weights", string.Join(" ", neuron.Weights.Select(Format)), text =>
            {
                var weights = text.Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(ParseFloat).ToList();
                Apply(component, p => { p.Neuron = neuron.WithWeights(weights); p.InputCount = weights.Count; return p; });
            }, "one per input");
            Field("Threshold", Format(neuron.Threshold), text => Apply(component, p => { p.Neuron = neuron.WithThreshold(ParseFloat(text)); return p; }));

            if (component.Kind == ComponentKind.SpikingNeuron)
            {
                Field("Leak", Format(neuron.Leak), text => Apply(component, p => { p.Neuron = neuron.WithLeak(ParseFloat(text)); return p; }), "kept per tick, 0-1");
                Field("Reset", Format(neuron.Reset), text => Apply(component, p => { p.Neuron = neuron.WithReset(ParseFloat(text)); return p; }));
                Field("Refractory", neuron.Refractory.ToString(Invariant), text => Apply(component, p => { p.Neuron = neuron.WithRefractory(ParseInt(text)); return p; }), "ticks");
                Note("High inputs add their weight to the potential every tick; it leaks, and at the threshold the neuron spikes for one tick.");
                return;
            }

            Note("Fires when the sum of the weights of its high inputs reaches the threshold. Train it from a truth table:");
            BuildTargets(component);
        }

        private void BuildNetwork(CircuitComponent component)
        {
            var network = component.Network;
            Field("Layers", string.Join(" ", network.LayerSizes), text =>
            {
                var sizes = text.Split(new[] { ' ', ',', '-', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(ParseInt).ToList();
                if (sizes.Count < 2)
                    throw new CircuitException("Layers: inputs, hidden layers, outputs, e.g. 3 8 2");
                var created = NeuralNetwork.Create(sizes[0], sizes.Skip(1).Take(sizes.Count - 2), sizes[sizes.Count - 1], network.Activation, seed: 1);
                Apply(component, p => { p.Network = created; return p; });
            }, "inputs hidden... outputs");

            var activations = (NeuralActivation[])Enum.GetValues(typeof(NeuralActivation));
            var next = activations[((int)network.Activation + 1) % activations.Length];
            Buttons(("Activation: " + network.Activation.ToString().ToLowerInvariant(), () => Apply(component, p => { p.Network = network.WithActivation(next); return p; })),
                ("New weights", () =>
                {
                    var hidden = network.LayerSizes.Skip(1).Take(network.LayerSizes.Count - 2);
                    var created = NeuralNetwork.Create(network.InputCount, hidden, network.OutputCount, network.Activation, UnityEngine.Random.Range(1, 1_000_000));
                    Apply(component, p => { p.Network = created; return p; });
                }));

            Note($"{network.PatternCount} input patterns, compiled into {network.OutputCount} lookup table{(network.OutputCount == 1 ? "" : "s")}.");
            BuildTargets(component);

            var library = Circuit.Library;
            var candidates = library.Definitions.Where(d => d.InputComponentIds.Count >= 1 && d.InputComponentIds.Count <= NeuralNetwork.MaxInputs
                && d.OutputComponentIds.Count >= 1 && d.OutputComponentIds.Count <= NeuralNetwork.MaxOutputs).ToList();
            if (candidates.Count > 0)
                Buttons(("Learn an IC...", () => ShowIcChooser(component, candidates)));
        }

        private void BuildTargets(CircuitComponent component)
        {
            int inputs = Circuit.GetInputCount(component);
            int outputs = Circuit.GetOutputCount(component);
            string example = outputs == 1
                ? new string('0', (1 << inputs) - 1) + "1"
                : string.Join(" ", Enumerable.Repeat(new string('0', outputs), 1 << Math.Min(inputs, 3)));
            Field("Train", string.Empty, text =>
            {
                var samples = NeuralTraining.SamplesFromText(text);
                Train(component, samples, "the targets");
            }, inputs <= 4 ? example : "targets, pattern 0 first");
            if (inputs <= 4)
                Note("One digit per input pattern 0, 1, 2, ... (in0 is bit 0). Several outputs: one group per pattern.");
        }

        private void ShowIcChooser(CircuitComponent component, List<SubcircuitDefinition> candidates)
        {
            var ui = AppUi.Instance;
            if (ui == null)
                return;
            var panel = ui.OpenPanel("Learn an IC", 520, 520);
            panel.AddText("The block is resized to the IC's pins and trained on its truth table.", 18);
            foreach (var definition in candidates)
            {
                var target = definition;
                panel.AddListButton($"{target.Name}  ({target.InputComponentIds.Count} in, {target.OutputComponentIds.Count} out)", () =>
                {
                    panel.Close();
                    Run(() => Train(component, NeuralTraining.SamplesFromCircuit(target.Circuit), "'" + target.Name + "'"));
                });
            }
            panel.AddButton("Close", panel.Close);
        }

        private void Train(CircuitComponent component, IReadOnlyList<NeuralTraining.Sample> samples, string what)
        {
            var result = NeuralTraining.TrainComponent(component, samples, new NeuralTraining.Options(), out var properties);
            SystemCore.CircuitService.Editor.Apply(new SetPropertiesEdit(Circuit, component.Id, properties));
            var message = result.Perfect
                ? $"Learned {what}: {result.Total}/{result.Total} rows after {result.Epochs} epochs"
                : $"Learned {result.Correct}/{result.Total} rows of {what}" + (component.Kind == ComponentKind.Neuron
                    ? " - one neuron cannot learn it (not linearly separable); try a neural block"
                    : " - try more hidden neurons");
            AppUi.Instance?.ShowToast(message, 7);
        }

        private void BuildSubcircuit(CircuitComponent component)
        {
            if (!Circuit.Library.TryGet(component.SubcircuitId, out var definition))
            {
                Note("Definition '" + component.SubcircuitId + "' is missing.");
                return;
            }
            Field("IC name", definition.Name, text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;
                SystemCore.CircuitService.Editor.Apply(new RenameSubcircuitEdit(Circuit.Library, definition.Id, text));
            });
            Note($"{definition.InputComponentIds.Count} inputs, {definition.OutputComponentIds.Count} outputs, {definition.Circuit.ComponentCount} components inside.");
            Buttons(("Open", () => AppCommands.OpenSubcircuit(component.Id)),
                ("Explode", AppCommands.ExplodeSelection),
                ("Unique", AppCommands.MakeSelectedUnique));
        }
    }
}
