using System;
using System.Collections.Generic;
using System.Linq;
using Behaviours;
using Core;
using Core.AppState;
using LogicSim.App;
using LogicSim.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogicSim.UI
{
    /// <summary>
    /// Runtime part of the scene's UI: binds the simulation toggle, keeps the status line current, adds the ICs of the
    /// library to the palette, and opens dialogs. The buttons, palette entries, inspector and IC bar are in the scene
    /// (prefabs under Assets/Prefabs/UI).
    /// </summary>
    public class AppUi : MonoBehaviour
    {
        public static AppUi Instance { get; private set; }

        private const float SectionTitleHeight = 40f;
        private const float SectionPadding = 20f;

        private Canvas _canvas;
        private AppUiReferences _refs;
        private readonly Stack<ModalPanel> _panels = new Stack<ModalPanel>();
        private TextMeshProUGUI _status;
        private string _toast;
        private float _toastUntil;
        private float _nextStatusRefresh;
        private Toggle _simulationToggle;
        private bool _syncingToggle;

        private RectTransform _customContainer;
        private int _customFixedEntries;
        private readonly List<GameObject> _subcircuitEntries = new List<GameObject>();

        public Canvas Canvas => _canvas;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            _canvas = FindRootCanvas();
            if (_canvas == null)
            {
                Debug.LogWarning("[LogicSim] No canvas found; toolbar and dialogs are disabled.");
                return;
            }
            _refs = _canvas.GetComponent<AppUiReferences>();
            if (_refs == null)
            {
                Debug.LogWarning("[LogicSim] The root canvas has no AppUiReferences; toolbar and dialogs are disabled.");
                return;
            }

            RegisterPalettePrefabs();
            if (Behaviours.CameraControl.Instance != null)
                Behaviours.CameraControl.Instance.LeftPanel = _refs.ToolsPanel;
            BindSimulationToggle();
            _status = _refs.StatusText;
            RefreshStatus();

            _customContainer = _refs.CustomCircuitIcons;
            _customFixedEntries = _customContainer != null ? _customContainer.childCount : 0;
            SystemCore.AppStateService.StateChanged += OnStateChanged;
            SystemCore.CircuitService.Root.Library.Changed += RebuildSubcircuitEntries;
            SystemCore.CircuitService.LevelChanged += RebuildSubcircuitEntries;
            RebuildSubcircuitEntries();
            ShowToast("Ctrl+O opens the examples, F1 shows help", 8f);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (SystemCore.AppStateService != null)
                SystemCore.AppStateService.StateChanged -= OnStateChanged;
            if (SystemCore.CircuitService != null)
            {
                SystemCore.CircuitService.Root.Library.Changed -= RebuildSubcircuitEntries;
                SystemCore.CircuitService.LevelChanged -= RebuildSubcircuitEntries;
            }
        }

        #region Scene lookups

        private static Canvas FindRootCanvas()
        {
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace)
                    return canvas;
            return null;
        }

        #endregion

        #region Palette

        /// <summary>Palette entries reference the gate prefabs; reuse their artwork for loaded circuits.</summary>
        private void RegisterPalettePrefabs()
        {
            foreach (var spawner in FindObjectsByType<ObjectSpawner>(FindObjectsInactive.Include))
                Views.ComponentPrefabRegistry.Register(spawner.Prefab);
        }

        /// <summary>One palette entry per IC of the library, after the fixed entries of the custom circuits section.</summary>
        private void RebuildSubcircuitEntries()
        {
            if (_customContainer == null || _refs.PaletteEntryPrefab == null)
                return;
            foreach (var entry in _subcircuitEntries)
                if (entry != null)
                    Destroy(entry);
            _subcircuitEntries.Clear();

            var icon = UiIconSet.Instance != null ? UiIconSet.Instance.Subcircuit : null;
            var hierarchy = SystemCore.CircuitService.Hierarchy;
            foreach (var definition in hierarchy.Library.Definitions)
            {
                var entry = Instantiate(_refs.PaletteEntryPrefab, _customContainer);
                entry.name = definition.Name;
                var spawner = entry.GetComponentInChildren<ObjectSpawner>(true);
                spawner.Configure(ComponentKind.Subcircuit, definition.Id);
                var label = entry.transform.Find("NameTxt");
                if (label != null && label.TryGetComponent(out TextMeshProUGUI labelText))
                    labelText.text = definition.Name;
                var image = spawner.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = icon;
                    image.preserveAspect = true;
                    // ICs that would end up inside themselves are shown dimmed (dropping them is refused).
                    image.color = new Color(1, 1, 1, hierarchy.CanPlace(definition.Id) ? 1f : 0.3f);
                }
                _subcircuitEntries.Add(entry);
            }
            // Destroyed entries only leave the hierarchy at the end of the frame, so count explicitly.
            FitSection(_customContainer, _customFixedEntries + _subcircuitEntries.Count);
        }

        /// <summary>
        /// Resizes a palette section (fixed heights in the scene) so that its grid of entries fits. The grid's own
        /// settings come from the scene.
        /// </summary>
        private static void FitSection(RectTransform container, int itemCount)
        {
            var grid = container.GetComponent<GridLayoutGroup>();
            if (grid == null)
                return;
            int columns = Mathf.Max(1, grid.constraintCount);
            int rows = Mathf.Max(1, Mathf.CeilToInt(itemCount / (float)columns));
            float gridHeight = grid.padding.top + rows * grid.cellSize.y + (rows - 1) * grid.spacing.y;
            container.sizeDelta = new Vector2(container.sizeDelta.x, gridHeight);
            if (container.parent is RectTransform section)
            {
                section.sizeDelta = new Vector2(section.sizeDelta.x, SectionTitleHeight + gridHeight + SectionPadding);
                if (section.parent is RectTransform list)
                    LayoutRebuilder.MarkLayoutForRebuild(list);
            }
        }

        #endregion

        #region Simulation toggle and status

        private void BindSimulationToggle()
        {
            _simulationToggle = _refs.SimulationToggle;
            if (_simulationToggle == null)
                return;
            _simulationToggle.onValueChanged.AddListener(isOn =>
            {
                if (_syncingToggle)
                    return;
                SystemCore.AppStateService.SetState(isOn ? ApplicationState.Simulator : ApplicationState.Editor);
            });
        }

        private void OnStateChanged(ApplicationState state)
        {
            if (_simulationToggle != null && _simulationToggle.isOn != (state == ApplicationState.Simulator))
            {
                // Setting isOn (not SetIsOnWithoutNotify) keeps the button animation in sync.
                _syncingToggle = true;
                _simulationToggle.isOn = state == ApplicationState.Simulator;
                _syncingToggle = false;
            }
            RefreshStatus();
        }

        public void ShowToast(string message, float seconds = 4f)
        {
            _toast = message;
            _toastUntil = Time.unscaledTime + seconds;
            RefreshStatus();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextStatusRefresh)
                RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (_status == null)
                return;
            _nextStatusRefresh = Time.unscaledTime + 0.25f;

            var circuitService = SystemCore.CircuitService;
            var simulation = SystemCore.SimulationService;
            var circuit = circuitService.Circuit;
            var parts = new List<string>();

            if (_toast != null && Time.unscaledTime < _toastUntil)
                parts.Add("<color=#FFD24A>" + _toast + "</color>");

            parts.Add(circuitService.DocumentName + (circuitService.IsDirty ? "*" : string.Empty));
            var hierarchy = circuitService.Hierarchy;
            if (!hierarchy.IsAtRoot)
                parts.Add("IC " + hierarchy.GetName(hierarchy.Current) + (hierarchy.IsModified(hierarchy.Current) ? "*" : string.Empty));
            parts.Add($"{circuit.ComponentCount} components, {circuit.WireCount} wires");

            if (SystemCore.AppStateService.IsSimulating)
            {
                var engine = simulation.Engine;
                string state = simulation.Paused ? "<color=#FFD24A>PAUSED</color>" : "<color=#5CFF73>SIMULATING</color>";
                // The circuit cannot keep up with the requested speed: show what is actually simulated.
                string behind = !simulation.Paused && simulation.ActualTicksPerSecond < simulation.TicksPerSecond * 0.8
                    ? $" <color=#FFD24A>(actual {FormatSpeed(simulation.ActualTicksPerSecond)})</color>"
                    : string.Empty;
                parts.Add($"{state} {FormatSpeed(simulation.TicksPerSecond)}{behind}  tick {engine.Tick:N0}");
                parts.Add($"{engine.Name}, {engine.ActiveMode}");
            }
            else
            {
                parts.Add("EDIT MODE");
            }

            var mcp = McpServerHost.Instance;
            if (mcp != null)
            {
                if (mcp.IsListening)
                    parts.Add("MCP " + mcp.Endpoint + (mcp.CallCount > 0 ? $" ({mcp.CallCount} calls, last: {mcp.LastTool})" : string.Empty));
                else if (!string.IsNullOrEmpty(mcp.LastError))
                    parts.Add("<color=#FF7A6B>MCP off: " + mcp.LastError + "</color>");
            }
            _status.text = string.Join("   |   ", parts);
        }

        private static string FormatSpeed(double ticksPerSecond)
        {
            if (ticksPerSecond >= 1_000_000)
                return $"{ticksPerSecond / 1_000_000:0.#}M ticks/s";
            if (ticksPerSecond >= 1000)
                return $"{ticksPerSecond / 1000:0.#}k ticks/s";
            return $"{ticksPerSecond:0} ticks/s";
        }

        #endregion

        #region Dialogs

        public ModalPanel OpenPanel(string title, float width = 620, float height = 640)
        {
            var panel = ModalPanel.Create(_canvas.transform, title, width, height);
            _panels.Push(panel);
            panel.Closed += () =>
            {
                var remaining = _panels.Where(p => p != panel && p.IsOpen).Reverse().ToList();
                _panels.Clear();
                foreach (var p in remaining)
                    _panels.Push(p);
            };
            return panel;
        }

        /// <returns>True when a dialog was closed.</returns>
        public bool CloseTopPanel()
        {
            while (_panels.Count > 0)
            {
                var panel = _panels.Pop();
                if (panel.IsOpen)
                {
                    panel.Close();
                    return true;
                }
            }
            return false;
        }

        public void ShowMessage(string title, string text, bool monospace = false)
        {
            var panel = OpenPanel(title);
            panel.AddText(text, monospace ? 18 : 20, monospace);
            if (monospace)
                panel.AddButton("Copy", () =>
                {
                    SystemCore.ClipboardService.SetStringClipboard(text);
                    ShowToast("Copied to clipboard");
                });
            panel.AddButton("Close", panel.Close);
        }

        public void PromptText(string title, string message, string initial, Action<string> onAccept)
        {
            var panel = OpenPanel(title, 520, 280);
            if (!string.IsNullOrEmpty(message))
                panel.AddText(message, 18);
            var input = UiFactory.CreateInputField(panel.Body, initial, 460, 44);
            void Accept()
            {
                var value = input.text;
                if (string.IsNullOrWhiteSpace(value))
                    return;
                panel.Close();
                onAccept(value.Trim());
            }
            input.onSubmit.AddListener(_ => Accept());
            panel.AddButton("Cancel", panel.Close);
            panel.AddButton("OK", Accept);
            input.Select();
            input.ActivateInputField();
        }

        #endregion
    }
}
