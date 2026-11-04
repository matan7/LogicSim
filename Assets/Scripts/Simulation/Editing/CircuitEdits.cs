using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicSim.Simulation
{
    /// <summary>A reversible change of a <see cref="Circuit"/>. Apply may be called again after Revert (redo).</summary>
    public interface ICircuitEdit
    {
        string Description { get; }
        void Apply();
        void Revert();
    }

    /// <summary>Applies edits. The Unity app routes them through its undo stack, the headless host applies them directly.</summary>
    public interface ICircuitEditor
    {
        Circuit Circuit { get; }
        void Apply(ICircuitEdit edit);
    }

    /// <summary>Applies edits immediately and keeps an undo/redo history (with one scope per opened IC).</summary>
    public sealed class DirectCircuitEditor : ICircuitEditor, IEditHistory
    {
        private sealed class Scope
        {
            public readonly List<ICircuitEdit> Undo = new List<ICircuitEdit>();
            public readonly List<ICircuitEdit> Redo = new List<ICircuitEdit>();
        }

        private readonly List<Scope> _scopes = new List<Scope> { new Scope() };

        private Scope Top => _scopes[_scopes.Count - 1];

        public Circuit Circuit { get; }
        public int UndoCount => Top.Undo.Count;
        public int RedoCount => Top.Redo.Count;
        public int ScopeDepth => _scopes.Count - 1;

        public DirectCircuitEditor(Circuit circuit)
        {
            Circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
        }

        public void Apply(ICircuitEdit edit) => Execute(edit);

        public void Execute(ICircuitEdit edit)
        {
            edit.Apply();
            Record(edit);
        }

        public void Record(ICircuitEdit appliedEdit)
        {
            Top.Undo.Add(appliedEdit);
            Top.Redo.Clear();
        }

        public bool Undo()
        {
            var scope = Top;
            if (scope.Undo.Count == 0)
                return false;
            var edit = scope.Undo[scope.Undo.Count - 1];
            scope.Undo.RemoveAt(scope.Undo.Count - 1);
            edit.Revert();
            scope.Redo.Add(edit);
            return true;
        }

        public bool Redo()
        {
            var scope = Top;
            if (scope.Redo.Count == 0)
                return false;
            var edit = scope.Redo[scope.Redo.Count - 1];
            scope.Redo.RemoveAt(scope.Redo.Count - 1);
            edit.Apply();
            scope.Undo.Add(edit);
            return true;
        }

        public void PushScope() => _scopes.Add(new Scope());

        public IReadOnlyList<ICircuitEdit> PopScope()
        {
            if (_scopes.Count == 1)
                throw new InvalidOperationException("The document scope cannot be removed");
            var scope = Top;
            _scopes.RemoveAt(_scopes.Count - 1);
            return scope.Undo.ToList();
        }
    }

    /// <summary>Adds components and wires. Ids are reserved up front so redo restores the same ids.</summary>
    public sealed class AddElementsEdit : ICircuitEdit
    {
        private readonly Circuit _circuit;
        private readonly List<CircuitComponent> _components;
        private readonly List<Wire> _wires;

        public string Description { get; }
        public IReadOnlyList<CircuitComponent> Components => _components;
        public IReadOnlyList<Wire> Wires => _wires;

        /// <param name="wires">Wires may reference the new components; their ids must be assigned already.</param>
        public AddElementsEdit(Circuit circuit, IEnumerable<CircuitComponent> components, IEnumerable<Wire> wires = null, string description = null)
        {
            _circuit = circuit;
            _components = components.ToList();
            foreach (var component in _components)
                if (component.Id == 0)
                    component.Id = circuit.ReserveComponentId();
            _wires = (wires ?? Enumerable.Empty<Wire>()).ToList();
            foreach (var wire in _wires)
                if (wire.Id == 0)
                    wire.Id = circuit.ReserveWireId();
            Description = description ?? (_components.Count == 1 && _wires.Count == 0 ? $"Add {_components[0].Kind}" : "Add elements");
        }

        public static AddElementsEdit Single(Circuit circuit, CircuitComponent component)
        {
            return new AddElementsEdit(circuit, new[] { component });
        }

        public void Apply()
        {
            foreach (var component in _components)
                _circuit.AddComponent(component);
            foreach (var wire in _wires)
                _circuit.AddWire(wire);
        }

        public void Revert()
        {
            foreach (var wire in _wires)
                _circuit.RemoveWire(wire.Id);
            for (int i = _components.Count - 1; i >= 0; i--)
                _circuit.RemoveComponent(_components[i].Id);
        }
    }

    /// <summary>Removes components (with every attached wire) and individual wires.</summary>
    public sealed class RemoveElementsEdit : ICircuitEdit
    {
        private readonly Circuit _circuit;
        private readonly List<int> _componentIds;
        private readonly List<int> _wireIds;
        private readonly List<CircuitComponent> _removedComponents = new List<CircuitComponent>();
        private readonly List<Wire> _removedWires = new List<Wire>();

        public string Description => "Delete";

        public RemoveElementsEdit(Circuit circuit, IEnumerable<int> componentIds, IEnumerable<int> wireIds = null)
        {
            _circuit = circuit;
            _componentIds = componentIds.Distinct().ToList();
            _wireIds = (wireIds ?? Enumerable.Empty<int>()).Distinct().ToList();
        }

        public bool IsEmpty => _componentIds.Count == 0 && _wireIds.Count == 0;

        public void Apply()
        {
            _removedComponents.Clear();
            _removedWires.Clear();
            foreach (var wireId in _wireIds)
            {
                if (_circuit.TryGetWire(wireId, out var wire))
                {
                    _circuit.RemoveWire(wireId);
                    _removedWires.Add(wire);
                }
            }
            foreach (var id in _componentIds)
            {
                if (!_circuit.TryGetComponent(id, out var component))
                    continue;
                _removedWires.AddRange(_circuit.RemoveComponent(id));
                _removedComponents.Add(component);
            }
        }

        public void Revert()
        {
            foreach (var component in _removedComponents)
                _circuit.AddComponent(component);
            foreach (var wire in _removedWires.OrderBy(w => w.Id))
                if (_circuit.CanConnect(wire.From, wire.To, out _))
                    _circuit.AddWire(wire);
        }
    }

    public sealed class MoveComponentsEdit : ICircuitEdit
    {
        private readonly Circuit _circuit;
        private readonly int[] _ids;
        private readonly (float x, float y)[] _from;
        private readonly (float x, float y)[] _to;

        public string Description => "Move";

        public MoveComponentsEdit(Circuit circuit, IList<int> ids, IList<(float x, float y)> from, IList<(float x, float y)> to)
        {
            if (ids.Count != from.Count || ids.Count != to.Count)
                throw new ArgumentException("Ids and positions must have the same length");
            _circuit = circuit;
            _ids = ids.ToArray();
            _from = from.ToArray();
            _to = to.ToArray();
        }

        public static MoveComponentsEdit To(Circuit circuit, IList<int> ids, IList<(float x, float y)> to)
        {
            var from = ids.Select(id => { var c = circuit.GetComponent(id); return (c.X, c.Y); }).ToList();
            return new MoveComponentsEdit(circuit, ids, from, to);
        }

        public void Apply()
        {
            for (int i = 0; i < _ids.Length; i++)
                if (_circuit.TryGetComponent(_ids[i], out _))
                    _circuit.MoveComponent(_ids[i], _to[i].x, _to[i].y);
        }

        public void Revert()
        {
            for (int i = 0; i < _ids.Length; i++)
                if (_circuit.TryGetComponent(_ids[i], out _))
                    _circuit.MoveComponent(_ids[i], _from[i].x, _from[i].y);
        }
    }

    /// <summary>Connects an output to an input, replacing the wire that previously drove the input.</summary>
    public sealed class ConnectEdit : ICircuitEdit
    {
        private readonly Circuit _circuit;
        private readonly Wire _wire;
        private Wire _replaced;

        public string Description => "Connect";
        public Wire Wire => _wire;

        public ConnectEdit(Circuit circuit, PinRef from, PinRef to)
        {
            _circuit = circuit;
            var existing = circuit.GetWireInto(to.ComponentId, to.Pin);
            if (existing != null && existing.From.Equals(from))
                throw new CircuitException("These pins are already connected");
            _wire = new Wire(circuit.ReserveWireId(), from, to);
        }

        public void Apply()
        {
            _replaced = _circuit.GetWireInto(_wire.To.ComponentId, _wire.To.Pin);
            if (_replaced != null)
                _circuit.RemoveWire(_replaced.Id);
            try
            {
                _circuit.AddWire(_wire);
            }
            catch
            {
                if (_replaced != null)
                    _circuit.AddWire(_replaced);
                throw;
            }
        }

        public void Revert()
        {
            _circuit.RemoveWire(_wire.Id);
            if (_replaced != null)
                _circuit.AddWire(_replaced);
        }
    }

    /// <summary>Changes label, rotation, input count, clock period or constant value.</summary>
    public sealed class SetPropertiesEdit : ICircuitEdit
    {
        private readonly Circuit _circuit;
        private readonly int _id;
        private readonly ComponentProperties _before;
        private readonly ComponentProperties _after;
        private readonly List<Wire> _removedWires = new List<Wire>();

        public string Description => "Change properties";

        public SetPropertiesEdit(Circuit circuit, int id, ComponentProperties after)
        {
            _circuit = circuit;
            _id = id;
            _before = circuit.GetComponent(id).GetProperties();
            _after = after;
        }

        public void Apply()
        {
            _removedWires.Clear();
            var component = _circuit.GetComponent(_id);
            // Pins that disappear must be disconnected first.
            int inputsAfter = component.Kind == ComponentKind.NeuralBlock ? (_after.Network ?? component.Network).InputCount : _after.InputCount;
            int inputsBefore = _circuit.GetInputCount(component);
            if (component.Spec.HasVariableInputs || component.Kind == ComponentKind.NeuralBlock)
            {
                for (int pin = inputsAfter; pin < inputsBefore; pin++)
                {
                    var wire = _circuit.GetWireInto(_id, pin);
                    if (wire != null)
                    {
                        _circuit.RemoveWire(wire.Id);
                        _removedWires.Add(wire);
                    }
                }
            }
            if (component.Kind == ComponentKind.NeuralBlock)
            {
                int outputsAfter = (_after.Network ?? component.Network).OutputCount;
                for (int pin = outputsAfter; pin < _circuit.GetOutputCount(component); pin++)
                {
                    foreach (var wire in _circuit.GetWiresFrom(_id, pin).ToList())
                    {
                        _circuit.RemoveWire(wire.Id);
                        _removedWires.Add(wire);
                    }
                }
            }
            _circuit.SetProperties(_id, _after);
        }

        public void Revert()
        {
            _circuit.SetProperties(_id, _before);
            foreach (var wire in _removedWires)
                _circuit.AddWire(wire);
        }
    }

    /// <summary>Replaces the whole document (new, load, MCP rebuilds). Keeps both snapshots for undo.</summary>
    public sealed class ReplaceCircuitEdit : ICircuitEdit
    {
        private readonly Circuit _circuit;
        private readonly CircuitSnapshot _before;
        private readonly CircuitSnapshot _after;

        public string Description { get; }

        public ReplaceCircuitEdit(Circuit circuit, CircuitSnapshot after, string description = "Replace circuit")
        {
            _circuit = circuit;
            _before = CircuitSnapshot.Capture(circuit, includeLibrary: true);
            _after = after;
            Description = description;
        }

        public void Apply() => _after.Restore(_circuit);
        public void Revert() => _before.Restore(_circuit);
    }

    public sealed class CompositeEdit : ICircuitEdit
    {
        private readonly List<ICircuitEdit> _edits;

        public string Description { get; }
        public IReadOnlyList<ICircuitEdit> Edits => _edits;

        public CompositeEdit(string description, IEnumerable<ICircuitEdit> edits)
        {
            Description = description;
            _edits = edits.ToList();
        }

        public void Apply()
        {
            int applied = 0;
            try
            {
                foreach (var edit in _edits)
                {
                    edit.Apply();
                    applied++;
                }
            }
            catch
            {
                for (int i = applied - 1; i >= 0; i--)
                    _edits[i].Revert();
                throw;
            }
        }

        public void Revert()
        {
            for (int i = _edits.Count - 1; i >= 0; i--)
                _edits[i].Revert();
        }
    }
}
