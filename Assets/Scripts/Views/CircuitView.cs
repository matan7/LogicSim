using System.Collections.Generic;
using System.Linq;
using Core;
using Core.AppState;
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

        public IEnumerable<ComponentView> ComponentViews => _components.Values;
        public IEnumerable<WireView> WireViews => _wires.Values;

        private void Awake()
        {
            Instance = this;
            _componentRoot = new GameObject("Components").transform;
            _componentRoot.SetParent(transform, false);
            _wireRoot = new GameObject("Wires").transform;
            _wireRoot.SetParent(transform, false);

            _circuit = SystemCore.CircuitService.Circuit;
            _circuit.ComponentAdded += OnComponentAdded;
            _circuit.ComponentRemoved += OnComponentRemoved;
            _circuit.ComponentMoved += OnComponentMoved;
            _circuit.ComponentPropertiesChanged += OnComponentPropertiesChanged;
            _circuit.InputValueChanged += OnInputValueChanged;
            _circuit.WireAdded += OnWireAdded;
            _circuit.WireRemoved += OnWireRemoved;
            _circuit.Reset += Rebuild;
            _circuit.Library.Changed += OnLibraryChanged;
            SystemCore.SimulationService.ValuesUpdated += OnValuesUpdated;
            SystemCore.AppStateService.StateChanged += OnStateChanged;

            Rebuild();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
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
            _circuit.Library.Changed -= OnLibraryChanged;
            if (SystemCore.SimulationService != null)
                SystemCore.SimulationService.ValuesUpdated -= OnValuesUpdated;
            if (SystemCore.AppStateService != null)
                SystemCore.AppStateService.StateChanged -= OnStateChanged;
        }

        #region Queries

        public bool TryGetComponentView(int id, out ComponentView view) => _components.TryGetValue(id, out view);

        public bool TryGetWireView(int id, out WireView view) => _wires.TryGetValue(id, out view);

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

        #region Model events

        private void Rebuild()
        {
            SystemCore.SelectingService.DeselectAll();
            foreach (var view in _components.Values)
                Destroy(view.gameObject);
            foreach (var view in _wires.Values)
                Destroy(view.gameObject);
            _components.Clear();
            _wires.Clear();
            _dirtyWires.Clear();

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
            DeselectView(view);
            _components.Remove(component.Id);
            Destroy(view.gameObject);
        }

        private void OnComponentMoved(CircuitComponent component)
        {
            if (_components.TryGetValue(component.Id, out var view))
                view.ApplyTransform();
        }

        private void OnComponentPropertiesChanged(CircuitComponent component)
        {
            if (!_components.TryGetValue(component.Id, out var view))
                return;
            if (!view.CanDisplay(component, _circuit) || (ComponentPrefabRegistry.Get(component) != null) != !(view is GenericComponentView))
            {
                RecreateComponentView(component);
                return;
            }
            view.OnPropertiesChanged();
            view.ApplyTransform();
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
            DeselectView(view);
            _wires.Remove(wire.Id);
            Destroy(view.gameObject);
        }

        private void OnLibraryChanged()
        {
            // Subcircuit instances may have gained or lost their definition.
            foreach (var component in _circuit.Components.Where(c => c.Kind == ComponentKind.Subcircuit).ToList())
                RecreateComponentView(component);
        }

        #endregion

        #region View creation

        private void CreateComponentView(CircuitComponent component)
        {
            ComponentView view;
            var prefab = ComponentPrefabRegistry.Get(component);
            if (prefab != null)
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
            view.transform.hasChanged = true;
            _components[component.Id] = view;
        }

        private void RecreateComponentView(CircuitComponent component)
        {
            bool wasSelected = false;
            if (_components.TryGetValue(component.Id, out var old))
            {
                wasSelected = IsSelected(old);
                DeselectView(old);
                _components.Remove(component.Id);
                // Destroy at the end of the frame; detach now so lookups find the new view only.
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
            CreateComponentView(component);
            if (wasSelected && _components.TryGetValue(component.Id, out var view))
            {
                var selectable = view.GetComponent<Core.Selecting.ISelectable>();
                if (selectable != null)
                    SystemCore.SelectingService.Select(selectable);
            }
            foreach (var wire in _circuit.GetWiresOf(component.Id))
                _dirtyWires.Add(wire.Id);
            _valuesDirty = true;
        }

        private void CreateWireView(Wire wire)
        {
            var view = WireView.Create(_wireRoot, wire.Id);
            _wires[wire.Id] = view;
            RouteWire(wire, view);
        }

        private void RouteWire(Wire wire, WireView view)
        {
            view.SetEndpoints(GetPinPosition(wire.From, true), GetPinPosition(wire.To, false));
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
            foreach (var pair in _components)
            {
                var t = pair.Value.transform;
                if (!t.hasChanged)
                    continue;
                t.hasChanged = false;
                foreach (var wire in _circuit.GetWiresOf(pair.Key))
                    _dirtyWires.Add(wire.Id);
            }

            if (_dirtyWires.Count > 0)
            {
                foreach (var id in _dirtyWires)
                    if (_wires.TryGetValue(id, out var view) && _circuit.TryGetWire(id, out var wire))
                        RouteWire(wire, view);
                _dirtyWires.Clear();
            }
        }

        private void OnValuesUpdated() => _valuesDirty = true;

        private void OnStateChanged(ApplicationState state) => _valuesDirty = true;

        private void ApplyValues()
        {
            _valuesDirty = false;
            var simulation = SystemCore.SimulationService;
            bool live = SystemCore.AppStateService.IsSimulating;
            var session = live ? simulation.Session : null;
            if (session != null && session.IsDirty)
            {
                // Structure changed while running; values appear after the next simulated frame.
                session = null;
            }

            foreach (var view in _components.Values)
                view.ShowValues(session);
            foreach (var pair in _wires)
            {
                if (session == null || !_circuit.TryGetWire(pair.Key, out var wire))
                    pair.Value.SetValue(null);
                else
                    pair.Value.SetValue(session.GetWireValue(wire));
            }
        }

        #endregion

        #region Selection helpers

        private static bool IsSelected(Component view)
        {
            var selectable = view.GetComponent<Core.Selecting.ISelectable>();
            return selectable != null && SystemCore.SelectingService.SelectionList.Contains(selectable);
        }

        private static void DeselectView(Component view)
        {
            var selectable = view.GetComponent<Core.Selecting.ISelectable>();
            if (selectable != null && SystemCore.SelectingService.SelectionList.Contains(selectable))
                SystemCore.SelectingService.Deselect(selectable);
        }

        #endregion
    }
}
