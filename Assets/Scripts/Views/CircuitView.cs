using System.Collections.Generic;
using System.Linq;
using Core;
using Core.AppState;
using Core.Selecting;
using LogicSim.Simulation;
using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>Prefabs for component kinds that have hand-drawn art. Filled from the palette at start-up.</summary>
    public static class ComponentPrefabRegistry
    {
        private static readonly Dictionary<ComponentKind, GameObject> Prefabs = new Dictionary<ComponentKind, GameObject>();

        public static void Register(GameObject prefab)
        {
            if (prefab == null)
                return;
            var view = prefab.GetComponent<ComponentView>();
            if (view != null && !Prefabs.ContainsKey(view.Kind))
                Prefabs.Add(view.Kind, prefab);
        }

        public static GameObject Get(CircuitComponent component)
        {
            if (!Prefabs.TryGetValue(component.Kind, out var prefab))
                return null;
            // Prefabs draw exactly two inputs (one for NOT/Buffer); other counts use a generated view.
            if (component.Spec.HasVariableInputs && component.InputCount != 2)
                return null;
            return prefab;
        }

        public static bool Has(ComponentKind kind) => Prefabs.ContainsKey(kind);
    }

    /// <summary>
    /// Keeps the scene in sync with the circuit model: creates, moves and destroys component and wire views
    /// and paints simulation values.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class CircuitView : MonoBehaviour
    {
        public static CircuitView Instance { get; private set; }

        private readonly Dictionary<int, ComponentView> _components = new Dictionary<int, ComponentView>();
        private readonly Dictionary<int, WireView> _wires = new Dictionary<int, WireView>();
        private readonly HashSet<int> _dirtyWires = new HashSet<int>();
        private Circuit _circuit;
        private Transform _componentRoot;
        private Transform _wireRoot;
        private bool _valuesDirty;
        private SymbolRenderer _symbols;
        private WireRenderer _wireRenderer;
        private NetValues _netValues;
        // Nets of the wires and symbols are resolved for one netlist and one level reader.
        private CompiledCircuit _netsCompiled;
        private ISignalReader _netsReader;
        private bool _netsDirty = true;
        private readonly List<Rect> _obstacles = new List<Rect>(2);

        // Picking: hit boxes of the bodies, pins and wire segments (see SpatialGrid).
        private readonly SpatialGrid<ComponentView> _componentGrid = new SpatialGrid<ComponentView>();
        private readonly SpatialGrid<PinView> _pinGrid = new SpatialGrid<PinView>();
        private readonly SpatialGrid<WireView> _wireGrid = new SpatialGrid<WireView>();
        private readonly Rect[] _wireRects = new Rect[WireRenderer.SegmentsPerWire];
        private readonly List<ComponentView> _componentHits = new List<ComponentView>();
        private readonly List<PinView> _pinHits = new List<PinView>();
        private readonly List<WireView> _wireHits = new List<WireView>();

        private void Awake()
        {
            Instance = this;
            _componentRoot = new GameObject("Components").transform;
            _componentRoot.SetParent(transform, false);
            _wireRoot = new GameObject("Wires").transform;
            _wireRoot.SetParent(transform, false);
            _symbols = SymbolRenderer.TryCreate();
            if (_symbols == null)
                Debug.LogWarning("[LogicSim] Symbol rendering is not available (no SymbolSet in Resources or no GPU support); drawing component sprites.");
            _wireRenderer = WireRenderer.TryCreate();
            if (_wireRenderer == null)
                Debug.LogWarning("[LogicSim] Wire rendering is not available (no wire shader or no GPU support); drawing line renderers.");
            if (_wireRenderer != null || _symbols != null)
                _netValues = new NetValues();

            SystemCore.SimulationService.ValuesUpdated += OnValuesUpdated;
            SystemCore.AppStateService.StateChanged += OnStateChanged;
            SystemCore.SelectingService.ItemChanged += OnSelectionItemChanged;
            SystemCore.CircuitService.LevelChanged += OnLevelChanged;
            BindCircuit(SystemCore.CircuitService.Circuit);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            UnbindCircuit();
            _symbols?.Dispose();
            _symbols = null;
            _wireRenderer?.Dispose();
            _wireRenderer = null;
            _netValues?.Dispose();
            _netValues = null;
            if (SystemCore.SimulationService != null)
                SystemCore.SimulationService.ValuesUpdated -= OnValuesUpdated;
            if (SystemCore.AppStateService != null)
                SystemCore.AppStateService.StateChanged -= OnStateChanged;
            if (SystemCore.SelectingService != null)
                SystemCore.SelectingService.ItemChanged -= OnSelectionItemChanged;
            if (SystemCore.CircuitService != null)
                SystemCore.CircuitService.LevelChanged -= OnLevelChanged;
        }

        /// <summary>The circuit shown: the document or the IC opened for editing.</summary>
        public Circuit Circuit => _circuit;

        /// <summary>Draws the bodies of the components that have a symbol; null when sprites are used instead.</summary>
        public SymbolRenderer Symbols => _symbols;

        /// <summary>Raised after the view switched to another circuit level and rebuilt itself.</summary>
        public event System.Action Rebound;

        private void OnLevelChanged()
        {
            UnbindCircuit();
            BindCircuit(SystemCore.CircuitService.Circuit);
            Rebound?.Invoke();
        }

        private void BindCircuit(Circuit circuit)
        {
            _circuit = circuit;
            _circuit.ComponentAdded += OnComponentAdded;
            _circuit.ComponentRemoved += OnComponentRemoved;
            _circuit.ComponentMoved += OnComponentMoved;
            _circuit.ComponentPropertiesChanged += OnComponentPropertiesChanged;
            _circuit.InputValueChanged += OnInputValueChanged;
            _circuit.WireAdded += OnWireAdded;
            _circuit.WireRemoved += OnWireRemoved;
            _circuit.Reset += Rebuild;
            _circuit.StructureChanged += OnStructureChanged;
            _circuit.Library.Changed += OnLibraryChanged;
            Rebuild();
        }

        private void UnbindCircuit()
        {
            if (_circuit == null)
                return;
            _circuit.ComponentAdded -= OnComponentAdded;
            _circuit.ComponentRemoved -= OnComponentRemoved;
            _circuit.ComponentMoved -= OnComponentMoved;
            _circuit.ComponentPropertiesChanged -= OnComponentPropertiesChanged;
            _circuit.InputValueChanged -= OnInputValueChanged;
            _circuit.WireAdded -= OnWireAdded;
            _circuit.WireRemoved -= OnWireRemoved;
            _circuit.Reset -= Rebuild;
            _circuit.StructureChanged -= OnStructureChanged;
            _circuit.Library.Changed -= OnLibraryChanged;
            _circuit = null;
        }

        // Editing an open IC changes whether its values can be shown.
        private void OnStructureChanged() => _valuesDirty = true;

        #region Queries

        public bool TryGetComponentView(int id, out ComponentView view) => _components.TryGetValue(id, out view);

        public Vector3 GetPinPosition(PinRef pin, bool output)
        {
            if (!_components.TryGetValue(pin.ComponentId, out var view))
                return Vector3.zero;
            var pins = output ? view.Outputs : view.Inputs;
            if (pin.Pin < 0 || pin.Pin >= pins.Count)
                return view.transform.position;
            return pins[pin.Pin].transform.position;
        }

        public bool TryGetBounds(IEnumerable<int> componentIds, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var id in componentIds)
            {
                if (!_components.TryGetValue(id, out var view))
                    continue;
                var b = view.GetBounds();
                if (!any)
                    bounds = b;
                else
                    bounds.Encapsulate(b);
                any = true;
            }
            return any;
        }

        #endregion

        #region Picking

        /// <summary>The pin whose hit box contains <paramref name="world"/>, the nearest one if several do.</summary>
        public PinView PinAt(Vector2 world)
        {
            _pinHits.Clear();
            _pinGrid.Query(world, _pinHits);
            PinView best = null;
            float bestDistance = float.MaxValue;
            foreach (var pin in _pinHits)
            {
                if (pin == null || pin.Owner == null)
                    continue;
                float distance = (pin.Position - world).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = pin;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>The component whose body contains <paramref name="world"/>; of overlapping ones the newest.</summary>
        public ComponentView ComponentAt(Vector2 world)
        {
            _componentHits.Clear();
            _componentGrid.Query(world, _componentHits);
            ComponentView best = null;
            foreach (var view in _componentHits)
                if (view != null && (best == null || view.ComponentId > best.ComponentId))
                    best = view;
            return best;
        }

        public WireView WireAt(Vector2 world)
        {
            _wireHits.Clear();
            _wireGrid.Query(world, _wireHits);
            WireView best = null;
            foreach (var view in _wireHits)
                if (view != null && (best == null || view.WireId > best.WireId))
                    best = view;
            return best;
        }

        /// <summary>The components and wires that overlap <paramref name="area"/>.</summary>
        public void ElementsIn(Rect area, List<SelectionItem> results)
        {
            _componentHits.Clear();
            _componentGrid.Query(area, _componentHits);
            foreach (var view in _componentHits)
                results.Add(SelectionItem.Component(view.ComponentId));
            _wireHits.Clear();
            _wireGrid.Query(area, _wireHits);
            foreach (var view in _wireHits)
                results.Add(SelectionItem.Wire(view.WireId));
        }

        private void IndexComponent(ComponentView view)
        {
            _componentGrid.Set(view, view.GetFootprint());
            foreach (var pin in view.Inputs)
                _pinGrid.Set(pin, pin.HitRect);
            foreach (var pin in view.Outputs)
                _pinGrid.Set(pin, pin.HitRect);
        }

        private void UnindexComponent(ComponentView view)
        {
            _componentGrid.Remove(view);
            foreach (var pin in view.Inputs)
                _pinGrid.Remove(pin);
            foreach (var pin in view.Outputs)
                _pinGrid.Remove(pin);
        }

        #endregion

        #region Selection

        private void OnSelectionItemChanged(SelectionItem item, bool selected)
        {
            Component view = null;
            if (item.IsWire)
            {
                if (_wires.TryGetValue(item.Id, out var wire))
                    view = wire;
            }
            else if (_components.TryGetValue(item.Id, out var component))
            {
                view = component;
            }
            if (view != null && view.TryGetComponent(out ISelectionView shown))
                shown.SetSelected(selected);
        }

        // A view created for a selected element (a recreated component, an undone removal) shows it.
        private static void ShowSelection(Component view, SelectionItem item)
        {
            if (SystemCore.SelectingService.Contains(item) && view.TryGetComponent(out ISelectionView shown))
                shown.SetSelected(true);
        }

        #endregion

        #region Model events

        private void Rebuild()
        {
            SystemCore.SelectingService.DeselectAll();
            foreach (var view in _components.Values)
                DestroyView(view);
            foreach (var view in _wires.Values)
                DestroyView(view);
            _components.Clear();
            _wires.Clear();
            _dirtyWires.Clear();
            _componentGrid.Clear();
            _pinGrid.Clear();
            _wireGrid.Clear();

            foreach (var component in _circuit.Components)
                CreateComponentView(component);
            foreach (var wire in _circuit.Wires)
                CreateWireView(wire);
            _valuesDirty = true;
        }

        private void OnComponentAdded(CircuitComponent component)
        {
            CreateComponentView(component);
            _valuesDirty = true;
        }

        private void OnComponentRemoved(CircuitComponent component)
        {
            if (!_components.TryGetValue(component.Id, out var view))
                return;
            _components.Remove(component.Id);
            UnindexComponent(view);
            DestroyView(view);
            SystemCore.SelectingService.Deselect(SelectionItem.Component(component.Id));
        }

        private void OnComponentMoved(CircuitComponent component)
        {
            if (!_components.TryGetValue(component.Id, out var view))
                return;
            view.ApplyTransform();
            NotifyViewMoved(component.Id);
        }

        /// <summary>
        /// Re-routes the wires of a component whose view moved: by a model change or while it is dragged. Views move only
        /// through these paths, so no per-frame scan of all transforms is needed.
        /// </summary>
        public void NotifyViewMoved(int componentId)
        {
            if (!_components.TryGetValue(componentId, out var view))
                return;
            if (view is SymbolComponentView symbol)
                symbol.SyncTransform();
            IndexComponent(view);
            foreach (var wire in _circuit.GetWiresOf(componentId))
                _dirtyWires.Add(wire.Id);
        }

        private void OnComponentPropertiesChanged(CircuitComponent component)
        {
            if (!_components.TryGetValue(component.Id, out var view))
                return;
            if (!view.CanDisplay(component, _circuit) || view.GetType() != ViewTypeFor(component))
            {
                RecreateComponentView(component);
                return;
            }
            view.OnPropertiesChanged();
            view.ApplyTransform();
            NotifyViewMoved(component.Id); // rotation moves the pins
            _valuesDirty = true;
        }

        private void OnInputValueChanged(CircuitComponent component)
        {
            if (_components.TryGetValue(component.Id, out var view))
                view.OnPropertiesChanged();
        }

        private void OnWireAdded(Wire wire)
        {
            CreateWireView(wire);
            _valuesDirty = true;
        }

        private void OnWireRemoved(Wire wire)
        {
            if (!_wires.TryGetValue(wire.Id, out var view))
                return;
            _wires.Remove(wire.Id);
            _wireGrid.Remove(view);
            DestroyView(view);
            SystemCore.SelectingService.Deselect(SelectionItem.Wire(wire.Id));
        }

        private void OnLibraryChanged()
        {
            // Subcircuit instances may have gained or lost their definition.
            foreach (var component in _circuit.Components.Where(c => c.Kind == ComponentKind.Subcircuit).ToList())
                RecreateComponentView(component);
        }

        #endregion

        #region View creation

        /// <summary>The view class that shows the component: a symbol, a prefab with sprites, or a generated box.</summary>
        private System.Type ViewTypeFor(CircuitComponent component)
        {
            if (SymbolComponentView.CanShow(component, _circuit, _symbols))
                return typeof(SymbolComponentView);
            var prefab = ComponentPrefabRegistry.Get(component);
            var prefabView = prefab != null ? prefab.GetComponent<ComponentView>() : null;
            return prefabView != null ? prefabView.GetType() : typeof(GenericComponentView);
        }

        private void CreateComponentView(CircuitComponent component)
        {
            ComponentView view;
            var prefab = ComponentPrefabRegistry.Get(component);
            if (SymbolComponentView.CanShow(component, _circuit, _symbols))
            {
                var go = new GameObject();
                go.transform.SetParent(_componentRoot, false);
                var symbol = go.AddComponent<SymbolComponentView>();
                symbol.Renderer = _symbols;
                view = symbol;
                _netsDirty = true;
            }
            else if (prefab != null)
            {
                var instance = Instantiate(prefab, _componentRoot);
                view = instance.GetComponent<ComponentView>();
            }
            else
            {
                var go = new GameObject();
                go.transform.SetParent(_componentRoot, false);
                view = go.AddComponent<GenericComponentView>();
            }
            view.Bind(component, _circuit);
            _components[component.Id] = view;
            NotifyViewMoved(component.Id);
            ShowSelection(view, SelectionItem.Component(component.Id));
        }

        /// <summary>Replaces the view of a component; the selection keeps its id, so the new view shows it.</summary>
        private void RecreateComponentView(CircuitComponent component)
        {
            if (_components.TryGetValue(component.Id, out var old))
            {
                _components.Remove(component.Id);
                UnindexComponent(old);
                DestroyView(old);
            }
            CreateComponentView(component);
            foreach (var wire in _circuit.GetWiresOf(component.Id))
                _dirtyWires.Add(wire.Id);
            _valuesDirty = true;
        }

        /// <summary>
        /// Destroys a view at the end of the frame, and disables it now: the symbol and wire renderers stop drawing it in
        /// this frame. Callers take it out of the lookups and grids first.
        /// </summary>
        private static void DestroyView(Component view)
        {
            view.gameObject.SetActive(false);
            Destroy(view.gameObject);
        }

        private void CreateWireView(Wire wire)
        {
            var view = WireView.Create(_wireRoot, wire.Id, _wireRenderer);
            _wires[wire.Id] = view;
            _netsDirty = true;
            RouteWire(wire, view);
            ShowSelection(view, SelectionItem.Wire(wire.Id));
        }

        private void RouteWire(Wire wire, WireView view)
        {
            // A wire that has to turn back crosses outside the bodies of its two components.
            _obstacles.Clear();
            if (_components.TryGetValue(wire.From.ComponentId, out var source))
                _obstacles.Add(source.GetFootprint());
            if (wire.To.ComponentId != wire.From.ComponentId && _components.TryGetValue(wire.To.ComponentId, out var target))
                _obstacles.Add(target.GetFootprint());
            view.SetEndpoints(GetPinPosition(wire.From, true), GetPinPosition(wire.To, false), _obstacles);
            _wireGrid.Set(view, _wireRects, view.GetHitRects(_wireRects));
        }

        #endregion

        #region Frame update

        // Values are read in Update: the simulation job runs from SimulationDriver.LateUpdate until the
        // start of the next frame, and reading nets while it runs would block the main thread.
        private void Update()
        {
            if (_valuesDirty)
                ApplyValues();
        }

        private void LateUpdate()
        {
            if (_dirtyWires.Count > 0)
            {
                foreach (var id in _dirtyWires)
                    if (_wires.TryGetValue(id, out var view) && _circuit.TryGetWire(id, out var wire))
                        RouteWire(wire, view);
                _dirtyWires.Clear();
            }
            if (_netValues != null)
            {
                _wireRenderer?.Render(_netValues);
                _symbols?.Render(_netValues);
            }
        }

        private void OnValuesUpdated() => _valuesDirty = true;

        private void OnStateChanged(ApplicationState state) => _valuesDirty = true;

        private void ApplyValues()
        {
            _valuesDirty = false;
            var simulation = SystemCore.SimulationService;
            bool live = SystemCore.AppStateService.IsSimulating;
            ISignalReader session = null;
            if (live && !simulation.Session.IsDirty)
            {
                // Inside an IC the values come from the instance it was opened through (if any).
                session = SystemCore.CircuitService.GetLevelSignals(simulation.Session);
            }

            // Symbols and drawn wires read their values on the GPU; the other views are updated here.
            foreach (var view in _components.Values)
                if (!(view is SymbolComponentView))
                    view.ShowValues(session);
            if (_netValues != null)
                ShowNetValues(session);
            if (_wireRenderer != null)
                return;
            foreach (var pair in _wires)
            {
                if (session == null || !_circuit.TryGetWire(pair.Key, out var wire))
                    pair.Value.SetValue(null);
                else
                    pair.Value.SetValue(session.GetWireValue(wire));
            }
        }

        /// <summary>
        /// Wires and symbols are colored from the engine's net values on the GPU: their nets are looked up once per
        /// netlist and level, after that each frame only copies the net values.
        /// </summary>
        private void ShowNetValues(ISignalReader session)
        {
            var simulation = SystemCore.SimulationService.Session;
            var compiled = simulation.Compiled;
            if (session == null || compiled == null)
            {
                _netValues.Hide();
                return;
            }
            if (_netsDirty || !ReferenceEquals(compiled, _netsCompiled) || !ReferenceEquals(session, _netsReader))
            {
                _wireRenderer?.ResolveNets(session, _circuit);
                foreach (var view in _components.Values)
                    if (view is SymbolComponentView symbol)
                        symbol.ResolveNets(session);
                _netsCompiled = compiled;
                _netsReader = session;
                _netsDirty = false;
            }
            _netValues.Show(simulation.Engine, compiled.NetCount);
        }

        #endregion
    }
}
