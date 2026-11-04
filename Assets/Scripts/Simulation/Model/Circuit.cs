using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicSim.Simulation
{
    public sealed class CircuitException : Exception
    {
        public CircuitException(string message) : base(message) { }
    }

    /// <summary>
    /// Editable circuit model: components and wires. This is the single source of truth;
    /// views and the simulator observe it through events.
    /// </summary>
    public sealed class Circuit
    {
        private readonly SortedDictionary<int, CircuitComponent> _components = new SortedDictionary<int, CircuitComponent>();
        private readonly SortedDictionary<int, Wire> _wires = new SortedDictionary<int, Wire>();
        private readonly Dictionary<long, Wire> _wireByInput = new Dictionary<long, Wire>();
        private readonly Dictionary<int, List<Wire>> _wiresByComponent = new Dictionary<int, List<Wire>>();
        private int _nextComponentId = 1;
        private int _nextWireId = 1;

        public SubcircuitLibrary Library { get; }

        /// <summary>Incremented on every change that requires recompiling the netlist.</summary>
        public int StructureVersion { get; private set; }

        public IEnumerable<CircuitComponent> Components => _components.Values;
        public IEnumerable<Wire> Wires => _wires.Values;
        public int ComponentCount => _components.Count;
        public int WireCount => _wires.Count;

        public event Action<CircuitComponent> ComponentAdded;
        public event Action<CircuitComponent> ComponentRemoved;
        public event Action<CircuitComponent> ComponentMoved;
        public event Action<CircuitComponent> ComponentPropertiesChanged;
        public event Action<CircuitComponent> InputValueChanged;
        public event Action<Wire> WireAdded;
        public event Action<Wire> WireRemoved;

        /// <summary>All contents were replaced (load, clear). Observers should rebuild.</summary>
        public event Action Reset;

        /// <summary>Raised after any change that affects the netlist.</summary>
        public event Action StructureChanged;

        public Circuit(SubcircuitLibrary library = null)
        {
            Library = library ?? new SubcircuitLibrary();
        }

        #region Queries

        public bool TryGetComponent(int id, out CircuitComponent component) => _components.TryGetValue(id, out component);

        public CircuitComponent GetComponent(int id)
        {
            if (_components.TryGetValue(id, out var component))
                return component;
            throw new CircuitException($"Component {id} does not exist");
        }

        public bool TryGetWire(int id, out Wire wire) => _wires.TryGetValue(id, out wire);

        public int GetInputCount(CircuitComponent component)
        {
            if (component.Kind == ComponentKind.Subcircuit)
                return Library.TryGet(component.SubcircuitId, out var def) ? def.InputComponentIds.Count : 0;
            if (component.Kind == ComponentKind.NeuralBlock)
                return component.Network?.InputCount ?? 0;
            var spec = component.Spec;
            return spec.HasVariableInputs ? component.InputCount : spec.DefaultInputs;
        }

        public int GetOutputCount(CircuitComponent component)
        {
            if (component.Kind == ComponentKind.Subcircuit)
                return Library.TryGet(component.SubcircuitId, out var def) ? def.OutputComponentIds.Count : 0;
            if (component.Kind == ComponentKind.NeuralBlock)
                return component.Network?.OutputCount ?? 0;
            return component.Spec.OutputCount;
        }

        public string GetInputName(CircuitComponent component, int pin)
        {
            if (component.Kind == ComponentKind.Subcircuit && Library.TryGet(component.SubcircuitId, out var def) && pin < def.InputNames.Count)
                return def.InputNames[pin];
            return component.Spec.GetInputName(pin);
        }

        public string GetOutputName(CircuitComponent component, int pin)
        {
            if (component.Kind == ComponentKind.Subcircuit && Library.TryGet(component.SubcircuitId, out var def) && pin < def.OutputNames.Count)
                return def.OutputNames[pin];
            return component.Spec.GetOutputName(pin);
        }

        /// <summary>Resolves a pin by name ("in0", "Q", "CLK") or by index ("1"). Returns -1 if not found.</summary>
        public int FindPin(CircuitComponent component, string name, bool output)
        {
            if (string.IsNullOrEmpty(name))
                return output && GetOutputCount(component) > 0 ? 0 : (!output && GetInputCount(component) > 0 ? 0 : -1);
            int count = output ? GetOutputCount(component) : GetInputCount(component);
            for (int i = 0; i < count; i++)
            {
                var pinName = output ? GetOutputName(component, i) : GetInputName(component, i);
                if (string.Equals(pinName, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            if (int.TryParse(name, out int index) && index >= 0 && index < count)
                return index;
            if (count == 1 && (string.Equals(name, output ? "out" : "in", StringComparison.OrdinalIgnoreCase)))
                return 0;
            return -1;
        }

        public Wire GetWireInto(int componentId, int pin)
        {
            _wireByInput.TryGetValue(new PinRef(componentId, pin).Key, out var wire);
            return wire;
        }

        public IReadOnlyList<Wire> GetWiresOf(int componentId)
        {
            if (_wiresByComponent.TryGetValue(componentId, out var list))
                return list;
            return Array.Empty<Wire>();
        }

        public IEnumerable<Wire> GetWiresFrom(int componentId, int pin)
        {
            return GetWiresOf(componentId).Where(w => w.From.ComponentId == componentId && w.From.Pin == pin);
        }

        public IEnumerable<CircuitComponent> InstancesOf(string subcircuitId)
        {
            return _components.Values.Where(c => c.Kind == ComponentKind.Subcircuit && c.SubcircuitId == subcircuitId);
        }

        public CircuitComponent FindByLabel(string label)
        {
            if (string.IsNullOrEmpty(label))
                return null;
            return _components.Values.FirstOrDefault(c => string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Ids

        public int ReserveComponentId() => _nextComponentId++;
        public int ReserveWireId() => _nextWireId++;

        #endregion

        #region Components

        public void AddComponent(CircuitComponent component)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component));
            if (component.Id == 0)
                component.Id = ReserveComponentId();
            else if (_components.ContainsKey(component.Id))
                throw new CircuitException($"Component id {component.Id} is already used");
            if (component.Kind == ComponentKind.Subcircuit && string.IsNullOrEmpty(component.SubcircuitId))
                throw new CircuitException("Subcircuit component needs a subcircuit id");

            NormalizeInputCount(component);
            _nextComponentId = Math.Max(_nextComponentId, component.Id + 1);
            _components.Add(component.Id, component);
            ComponentAdded?.Invoke(component);
            MarkStructureChanged();
        }

        /// <summary>Removes a component and every wire attached to it. Returns the removed wires.</summary>
        public IReadOnlyList<Wire> RemoveComponent(int id)
        {
            if (!_components.TryGetValue(id, out var component))
                throw new CircuitException($"Component {id} does not exist");

            var removedWires = GetWiresOf(id).ToList();
            foreach (var wire in removedWires)
                RemoveWireInternal(wire);

            _components.Remove(id);
            _wiresByComponent.Remove(id);
            ComponentRemoved?.Invoke(component);
            MarkStructureChanged();
            return removedWires;
        }

        public void MoveComponent(int id, float x, float y)
        {
            var component = GetComponent(id);
            if (component.X == x && component.Y == y)
                return;
            component.X = x;
            component.Y = y;
            ComponentMoved?.Invoke(component);
        }

        public void SetProperties(int id, ComponentProperties properties)
        {
            var component = GetComponent(id);
            var spec = component.Spec;
            if (!spec.HasVariableInputs)
                properties.InputCount = component.InputCount;
            else if (properties.InputCount < spec.MinInputs || properties.InputCount > spec.MaxInputs)
                throw new CircuitException($"{spec.DisplayName} supports {spec.MinInputs}..{spec.MaxInputs} inputs");

            if (spec.HasVariableInputs)
            {
                for (int pin = properties.InputCount; pin < component.InputCount; pin++)
                {
                    if (GetWireInto(id, pin) != null)
                        throw new CircuitException($"Disconnect input {pin} of {component} before removing it");
                }
            }
            if (component.Kind == ComponentKind.NeuralBlock && properties.Network != null)
            {
                for (int pin = properties.Network.InputCount; pin < GetInputCount(component); pin++)
                    if (GetWireInto(id, pin) != null)
                        throw new CircuitException($"Disconnect input {pin} of {component} before removing it");
                for (int pin = properties.Network.OutputCount; pin < GetOutputCount(component); pin++)
                    if (GetWiresFrom(id, pin).Any())
                        throw new CircuitException($"Disconnect output {pin} of {component} before removing it");
            }

            var before = component.GetProperties();
            component.ApplyProperties(properties);
            var after = component.GetProperties();
            if (before.Equals(after))
                return;

            bool structural = before.InputCount != after.InputCount
                || before.ClockHalfPeriod != after.ClockHalfPeriod
                || (component.Kind == ComponentKind.Constant && before.Value != after.Value)
                || !Equals(before.Neuron, after.Neuron)
                || !Equals(before.Network, after.Network);

            ComponentPropertiesChanged?.Invoke(component);
            if (before.Value != after.Value && spec.IsInterfaceInput)
                InputValueChanged?.Invoke(component);
            if (structural)
                MarkStructureChanged();
        }

        /// <summary>Changes the state of a Switch, Button or PinIn without touching other properties.</summary>
        public void SetInputValue(int id, bool value)
        {
            var component = GetComponent(id);
            if (!component.Spec.IsInterfaceInput)
                throw new CircuitException($"{component} is not an input");
            if (component.Value == value)
                return;
            component.Value = value;
            InputValueChanged?.Invoke(component);
        }

        /// <summary>Points a subcircuit instance at another definition. Wires are kept; the caller makes sure the pins match.</summary>
        public void SetSubcircuitId(int id, string subcircuitId)
        {
            var component = GetComponent(id);
            if (component.Kind != ComponentKind.Subcircuit)
                throw new CircuitException($"{component} is not a subcircuit");
            if (string.IsNullOrEmpty(subcircuitId))
                throw new CircuitException("Subcircuit id must not be empty");
            if (component.SubcircuitId == subcircuitId)
                return;
            component.SubcircuitId = subcircuitId;
            ComponentPropertiesChanged?.Invoke(component);
            MarkStructureChanged();
        }

        private void NormalizeInputCount(CircuitComponent component)
        {
            var spec = component.Spec;
            if (!spec.HasVariableInputs)
                component.InputCount = spec.DefaultInputs;
            else
                component.InputCount = Math.Max(spec.MinInputs, Math.Min(spec.MaxInputs, component.InputCount));
            component.NormalizeNeuralState();
        }

        #endregion

        #region Wires

        public bool CanConnect(PinRef from, PinRef to, out string error)
        {
            if (!_components.TryGetValue(from.ComponentId, out var source))
            {
                error = $"Component {from.ComponentId} does not exist";
                return false;
            }
            if (!_components.TryGetValue(to.ComponentId, out var target))
            {
                error = $"Component {to.ComponentId} does not exist";
                return false;
            }
            // Pins of a subcircuit whose definition is not loaded yet cannot be validated (documents load in any order).
            if (from.Pin < 0 || (from.Pin >= GetOutputCount(source) && !IsUnresolvedSubcircuit(source)))
            {
                error = $"{source} has no output pin {from.Pin}";
                return false;
            }
            if (to.Pin < 0 || (to.Pin >= GetInputCount(target) && !IsUnresolvedSubcircuit(target)))
            {
                error = $"{target} has no input pin {to.Pin}";
                return false;
            }
            if (GetWireInto(to.ComponentId, to.Pin) != null)
            {
                error = $"Input {GetInputName(target, to.Pin)} of {target} is already connected";
                return false;
            }
            error = null;
            return true;
        }

        private bool IsUnresolvedSubcircuit(CircuitComponent component)
        {
            return component.Kind == ComponentKind.Subcircuit && !Library.TryGet(component.SubcircuitId, out _);
        }

        public Wire Connect(PinRef from, PinRef to)
        {
            var wire = new Wire(from, to);
            AddWire(wire);
            return wire;
        }

        public void AddWire(Wire wire)
        {
            if (wire == null)
                throw new ArgumentNullException(nameof(wire));
            if (!CanConnect(wire.From, wire.To, out var error))
                throw new CircuitException(error);
            if (wire.Id == 0)
                wire.Id = ReserveWireId();
            else if (_wires.ContainsKey(wire.Id))
                throw new CircuitException($"Wire id {wire.Id} is already used");

            _nextWireId = Math.Max(_nextWireId, wire.Id + 1);
            _wires.Add(wire.Id, wire);
            _wireByInput.Add(wire.To.Key, wire);
            AddToComponentIndex(wire.From.ComponentId, wire);
            if (wire.To.ComponentId != wire.From.ComponentId)
                AddToComponentIndex(wire.To.ComponentId, wire);
            WireAdded?.Invoke(wire);
            MarkStructureChanged();
        }

        public bool RemoveWire(int id)
        {
            if (!_wires.TryGetValue(id, out var wire))
                return false;
            RemoveWireInternal(wire);
            MarkStructureChanged();
            return true;
        }

        private void RemoveWireInternal(Wire wire)
        {
            _wires.Remove(wire.Id);
            _wireByInput.Remove(wire.To.Key);
            if (_wiresByComponent.TryGetValue(wire.From.ComponentId, out var fromList))
                fromList.Remove(wire);
            if (_wiresByComponent.TryGetValue(wire.To.ComponentId, out var toList))
                toList.Remove(wire);
            WireRemoved?.Invoke(wire);
        }

        private void AddToComponentIndex(int componentId, Wire wire)
        {
            if (!_wiresByComponent.TryGetValue(componentId, out var list))
            {
                list = new List<Wire>();
                _wiresByComponent.Add(componentId, list);
            }
            list.Add(wire);
        }

        #endregion

        #region Bulk

        public void Clear() => ReplaceContents(Array.Empty<CircuitComponent>(), Array.Empty<Wire>());

        /// <summary>
        /// Replaces all components and wires. Invalid wires are skipped and reported in the return value.
        /// Raises <see cref="Reset"/> instead of individual events.
        /// </summary>
        public IReadOnlyList<string> ReplaceContents(IEnumerable<CircuitComponent> components, IEnumerable<Wire> wires)
        {
            return ReplaceContents(components, wires, validatePins: true);
        }

        /// <summary>Like <see cref="ReplaceContents(IEnumerable{CircuitComponent}, IEnumerable{Wire})"/> but trusts the pin
        /// numbers of the wires (used while a subcircuit definition is being swapped for a version with other pins).</summary>
        internal IReadOnlyList<string> ReplaceContentsUnchecked(IEnumerable<CircuitComponent> components, IEnumerable<Wire> wires)
        {
            return ReplaceContents(components, wires, validatePins: false);
        }

        private IReadOnlyList<string> ReplaceContents(IEnumerable<CircuitComponent> components, IEnumerable<Wire> wires, bool validatePins)
        {
            var problems = new List<string>();
            _components.Clear();
            _wires.Clear();
            _wireByInput.Clear();
            _wiresByComponent.Clear();
            _nextComponentId = 1;
            _nextWireId = 1;

            foreach (var component in components)
            {
                if (component.Id == 0)
                    component.Id = ReserveComponentId();
                if (_components.ContainsKey(component.Id))
                {
                    problems.Add($"Duplicate component id {component.Id} skipped");
                    continue;
                }
                NormalizeInputCount(component);
                _components.Add(component.Id, component);
                _nextComponentId = Math.Max(_nextComponentId, component.Id + 1);
            }

            foreach (var wire in wires)
            {
                string error = null;
                bool valid = validatePins
                    ? CanConnect(wire.From, wire.To, out error)
                    : _components.ContainsKey(wire.From.ComponentId) && _components.ContainsKey(wire.To.ComponentId) && !_wireByInput.ContainsKey(wire.To.Key);
                if (!valid)
                {
                    error = error ?? "invalid endpoints";
                    problems.Add($"Wire {wire.Id} skipped: {error}");
                    continue;
                }
                if (wire.Id == 0 || _wires.ContainsKey(wire.Id))
                    wire.Id = ReserveWireId();
                _wires.Add(wire.Id, wire);
                _wireByInput.Add(wire.To.Key, wire);
                AddToComponentIndex(wire.From.ComponentId, wire);
                if (wire.To.ComponentId != wire.From.ComponentId)
                    AddToComponentIndex(wire.To.ComponentId, wire);
                _nextWireId = Math.Max(_nextWireId, wire.Id + 1);
            }

            Reset?.Invoke();
            MarkStructureChanged();
            return problems;
        }

        /// <summary>Deep copy of components and wires (ids preserved). The copy shares the library.</summary>
        public Circuit Clone()
        {
            var copy = new Circuit(Library);
            copy.ReplaceContents(_components.Values.Select(c => c.Clone()).ToList(), _wires.Values.Select(w => w.Clone()).ToList());
            return copy;
        }

        #endregion

        private void MarkStructureChanged()
        {
            StructureVersion++;
            StructureChanged?.Invoke();
        }
    }
}
