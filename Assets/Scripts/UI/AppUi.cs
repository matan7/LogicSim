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
    /// Extends the scene's tool panel at runtime: toolbar buttons, palette entries for every component kind,
    /// custom circuits, a status bar and dialogs. Built from templates already in the scene so it matches their style.
    /// </summary>
    public class AppUi : MonoBehaviour
    {
        public static AppUi Instance { get; private set; }

        private const float SectionTitleHeight = 40f;
        private const float SectionPadding = 20f;

        private Canvas _canvas;
        private readonly Stack<ModalPanel> _panels = new Stack<ModalPanel>();
        private TextMeshProUGUI _status;
        private string _toast;
        private float _toastUntil;
        private float _nextStatusRefresh;
        private Toggle _simulationToggle;
        private bool _syncingToggle;

        private GameObject _buttonTemplate;
        private GameObject _paletteTemplate;
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

            RegisterPalettePrefabs();
            BuildToolbar();
            BuildPalette();
            MakeToolPanelScrollable();
            BindSimulationToggle();
            BuildStatusBar();
            HierarchyBar.Create(_canvas);

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
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace)
                    return canvas;
            return null;
        }

        private Transform FindDeep(string name, Transform root = null)
        {
            root = root ?? _canvas.transform;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        private RectTransform IconContainerOf(string sectionName)
        {
            var section = FindDeep(sectionName);
            return section != null ? section.Find("IconContainer") as RectTransform : null;
        }

        #endregion

        #region Palette and toolbar

        /// <summary>Palette entries reference the gate prefabs; reuse their artwork for loaded circuits.</summary>
        private void RegisterPalettePrefabs()
        {
            foreach (var spawner in FindObjectsByType<ObjectSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Views.ComponentPrefabRegistry.Register(spawner.Prefab);
        }

        private void BuildToolbar()
        {
            var undo = FindDeep("UndoButton");
            if (undo == null)
            {
                Debug.LogWarning("[LogicSim] UndoButton template not found; toolbar buttons are not created.");
                return;
            }
            // Pristine, inactive copy: the scene's undo/redo buttons get restyled below.
            _buttonTemplate = Instantiate(undo.gameObject, transform);
            _buttonTemplate.SetActive(false);
            foreach (var behaviour in _buttonTemplate.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is UndoButton || behaviour is RedoButton)
                    DestroyImmediate(behaviour);
            var icons = UiIconSet.Instance;

            var edit = IconContainerOf("Edit");
            if (edit != null)
            {
                StyleTextButton(undo.gameObject, "UNDO");
                var redo = FindDeep("RedoButton");
                if (redo != null)
                    StyleTextButton(redo.gameObject, "REDO");
                AddToolbarButton(edit, "Cut", icons != null ? icons.Cut : null, null, () => Actions.EditCommands.Cut());
                AddToolbarButton(edit, "Copy", icons != null ? icons.Copy : null, null, () => Actions.EditCommands.Copy());
                AddToolbarButton(edit, "Paste", icons != null ? icons.Paste : null, null, () => Actions.EditCommands.Paste());
                AddToolbarButton(edit, "Delete", icons != null ? icons.Delete : null, null, () => Actions.EditCommands.Delete());
                AddToolbarButton(edit, "Rotate", null, "R", () => Actions.EditCommands.Rotate());
                AddToolbarButton(edit, "Select all", null, "ALL", Actions.EditCommands.SelectAll);
                FitSection(edit, 3, 80, 70, 10, 25);
            }

            var menu = IconContainerOf("Menu");
            if (menu != null)
            {
                AddToolbarButton(menu, "New", icons != null ? icons.New : null, null, AppCommands.New);
                AddToolbarButton(menu, "Open", icons != null ? icons.Open : null, null, AppCommands.ShowOpen);
                AddToolbarButton(menu, "Save", icons != null ? icons.Save : null, null, () => AppCommands.Save(saveAs: false));
                AddToolbarButton(menu, "Save as", null, "AS", () => AppCommands.Save(saveAs: true));
                AddToolbarButton(menu, "Truth table", null, "TT", AppCommands.ShowTruthTable);
                AddToolbarButton(menu, "Help", null, "?", AppCommands.ShowHelp);
                FitSection(menu, 3, 80, 70, 10, 25);
            }
        }

        private void AddToolbarButton(RectTransform container, string label, Sprite icon, string iconText, Action onClick)
        {
            var go = Instantiate(_buttonTemplate, container);
            go.name = label + "Button";
            go.SetActive(true);

            var button = go.GetComponentInChildren<Button>(true);
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onClick());
                button.interactable = true;
            }
            var name = go.transform.Find("NameTxt");
            if (name != null && name.TryGetComponent(out TextMeshProUGUI nameText))
                nameText.text = label;

            if (icon != null && button != null && button.targetGraphic is Image image)
            {
                image.sprite = icon;
                image.preserveAspect = true;
                image.color = Color.white;
            }
            else
            {
                StyleTextButton(go, iconText ?? label);
            }
        }

        /// <summary>Icon-less buttons: dark tile with a short caption instead of a blank white box.</summary>
        private static void StyleTextButton(GameObject buttonRoot, string caption)
        {
            var button = buttonRoot.GetComponentInChildren<Button>(true);
            if (button == null || !(button.targetGraphic is Image image) || image.sprite != null)
                return;
            image.color = UiFactory.ButtonColor;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            button.colors = colors;
            if (image.transform.Find("Caption") != null)
                return;
            var text = UiFactory.CreateText(image.transform, caption, 18, UiFactory.TextColor, TextAlignmentOptions.Center);
            text.name = "Caption";
            text.fontStyle = FontStyles.Bold;
            UiFactory.Stretch(text.rectTransform);
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private void BuildPalette()
        {
            _paletteTemplate = FindObjectsByType<ObjectSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(s => FindPaletteRoot(s.transform))
                .FirstOrDefault(t => t != null)?.gameObject;
            if (_paletteTemplate == null)
                return;

            var gates = IconContainerOf("LogicGates");
            if (gates != null)
            {
                AddPaletteEntry(gates, ComponentKind.DFlipFlop, "D flip-flop", "D");
                AddPaletteEntry(gates, ComponentKind.TFlipFlop, "T flip-flop", "T");
                AddPaletteEntry(gates, ComponentKind.JKFlipFlop, "JK flip-flop", "JK");
                FitSection(gates, 2, 100, 75, 50, 35);
            }

            var io = IconContainerOf("ToolsIO");
            if (io != null)
            {
                AddPaletteEntry(io, ComponentKind.Button, "Push button", "BTN");
                AddPaletteEntry(io, ComponentKind.Clock, "Clock", "CLK");
                AddPaletteEntry(io, ComponentKind.Constant, "Constant", "0/1");
                AddPaletteEntry(io, ComponentKind.HexDisplay, "Hex display", "HEX");
                FitSection(io, 2, 100, 75, 50, 35);
            }

            _customContainer = IconContainerOf("CustomCircuits");
            if (_customContainer != null)
            {
                var icons = UiIconSet.Instance;
                if (_buttonTemplate != null)
                {
                    AddToolbarButton(_customContainer, "Create IC", icons != null ? icons.Subcircuit : null, "IC", AppCommands.CreateSubcircuit);
                    AddToolbarButton(_customContainer, "Collapse", null, ">IC", AppCommands.CollapseSelection);
                    AddToolbarButton(_customContainer, "Open IC", null, "OPEN", AppCommands.OpenSelectedSubcircuit);
                    AddToolbarButton(_customContainer, "Explode", null, "IC>", AppCommands.ExplodeSelection);
                    AddToolbarButton(_customContainer, "Make unique", null, "UNIQ", AppCommands.MakeSelectedUnique);
                }
                AddPaletteEntry(_customContainer, ComponentKind.PinIn, "Input pin", "IN");
                AddPaletteEntry(_customContainer, ComponentKind.PinOut, "Output pin", "OUT");
                _customFixedEntries = _customContainer.childCount;
                FitSection(_customContainer, 2, 100, 75, 50, 35);
            }
        }

        private static Transform FindPaletteRoot(Transform spawner)
        {
            // Palette prefab: Root -> Image (ObjectSpawner, UiDraggable) + NameTxt.
            var root = spawner.parent;
            return root != null && root.Find("NameTxt") != null ? root : null;
        }

        private GameObject AddPaletteEntry(RectTransform container, ComponentKind kind, string label, string caption, string subcircuitId = null, Sprite icon = null)
        {
            var go = Instantiate(_paletteTemplate, container);
            go.name = label;
            var spawner = go.GetComponentInChildren<ObjectSpawner>(true);
            spawner.Configure(kind, subcircuitId);
            var name = go.transform.Find("NameTxt");
            if (name != null && name.TryGetComponent(out TextMeshProUGUI nameText))
                nameText.text = label;

            var image = spawner.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = icon;
                image.preserveAspect = true;
                image.color = icon != null ? Color.white : UiFactory.ButtonColor;
                if (icon == null)
                {
                    var text = UiFactory.CreateText(image.transform, caption, 20, UiFactory.TextColor, TextAlignmentOptions.Center);
                    text.fontStyle = FontStyles.Bold;
                    UiFactory.Stretch(text.rectTransform);
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                }
            }
            return go;
        }

        private void RebuildSubcircuitEntries()
        {
            if (_customContainer == null || _paletteTemplate == null)
                return;
            foreach (var entry in _subcircuitEntries)
                if (entry != null)
                    Destroy(entry);
            _subcircuitEntries.Clear();

            var icons = UiIconSet.Instance;
            var hierarchy = SystemCore.CircuitService.Hierarchy;
            foreach (var definition in hierarchy.Library.Definitions)
            {
                var entry = AddPaletteEntry(_customContainer, ComponentKind.Subcircuit, definition.Name, "IC", definition.Id, icons != null ? icons.Subcircuit : null);
                // ICs that would end up inside themselves are shown dimmed (dropping them is refused).
                if (!hierarchy.CanPlace(definition.Id))
                {
                    var image = entry.GetComponentInChildren<ObjectSpawner>(true)?.GetComponent<Image>();
                    if (image != null)
                        image.color = new Color(1, 1, 1, 0.3f);
                }
                _subcircuitEntries.Add(entry);
            }
            // Destroyed entries only leave the hierarchy at the end of the frame, so count explicitly.
            FitSection(_customContainer, 2, 100, 75, 50, 35, _customFixedEntries + _subcircuitEntries.Count);
        }

        /// <summary>Resizes a tool panel section so its grid of items fits (sections have fixed heights in the scene).</summary>
        private static void FitSection(RectTransform container, int columns, float cellWidth, float cellHeight, float spacingX, float spacingY, int? itemCount = null)
        {
            var grid = container.GetComponent<GridLayoutGroup>();
            if (grid == null)
                grid = container.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(cellWidth, cellHeight);
            grid.spacing = new Vector2(spacingX, spacingY);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.padding = new RectOffset(0, 0, 10, 0);

            int items = itemCount ?? container.childCount;
            int rows = Mathf.Max(1, Mathf.CeilToInt(items / (float)columns));
            float gridHeight = 10 + rows * cellHeight + (rows - 1) * spacingY;

            container.anchorMin = container.anchorMax = new Vector2(0, 1);
            container.pivot = new Vector2(0.5f, 1);
            container.anchoredPosition = new Vector2(140, -SectionTitleHeight);
            container.sizeDelta = new Vector2(280, gridHeight);

            if (container.parent is RectTransform section)
            {
                section.sizeDelta = new Vector2(section.sizeDelta.x, SectionTitleHeight + gridHeight + SectionPadding);
                if (section.parent is RectTransform list)
                    LayoutRebuilder.MarkLayoutForRebuild(list);
            }
        }

        /// <summary>The tool panel content is taller than the screen: scroll it with the mouse wheel.</summary>
        private void MakeToolPanelScrollable()
        {
            var main = FindDeep("MainContainer") as RectTransform;
            var content = FindDeep("ExpandableContainer") as RectTransform;
            if (main == null || content == null)
                return;
            if (main.GetComponent<RectMask2D>() == null)
                main.gameObject.AddComponent<RectMask2D>();
            if (main.GetComponent<Image>() == null)
                UiFactory.AddImage(main, new Color(0, 0, 0, 0));
            var scroller = main.gameObject.AddComponent<WheelScroller>();
            scroller.Content = content;
            scroller.Viewport = main;
        }

        #endregion

        #region Simulation toggle and status

        private void BindSimulationToggle()
        {
            var toggleTransform = FindDeep("StartSimButton");
            if (toggleTransform == null || !toggleTransform.TryGetComponent(out _simulationToggle))
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

        private void BuildStatusBar()
        {
            var rect = UiFactory.CreateRect("Status Bar", _canvas.transform);
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.offsetMin = new Vector2(310, 6);
            rect.offsetMax = new Vector2(-12, 34);
            _status = rect.gameObject.AddComponent<TextMeshProUGUI>();
            _status.fontSize = 16;
            _status.alignment = TextAlignmentOptions.BottomRight;
            _status.color = UiFactory.MutedText;
            _status.raycastTarget = false;
            _status.richText = true;
            _status.textWrappingMode = TextWrappingModes.NoWrap;
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
                parts.Add($"{state} {FormatSpeed(simulation.TicksPerSecond)}  tick {engine.Tick:N0}");
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

        public bool HasOpenPanel => _panels.Any(p => p.IsOpen);

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
