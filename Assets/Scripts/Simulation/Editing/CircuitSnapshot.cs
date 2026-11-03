using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicSim.Simulation
{
    /// <summary>Immutable copy of a circuit's contents (and optionally its subcircuit library).</summary>
    public sealed class CircuitSnapshot
    {
        private readonly List<CircuitComponent> _components;
        private readonly List<Wire> _wires;
        private readonly List<SubcircuitDefinition> _definitions;

        public IReadOnlyList<CircuitComponent> Components => _components;
        public IReadOnlyList<Wire> Wires => _wires;

        /// <summary>Null when the snapshot does not include the library.</summary>
        public IReadOnlyList<SubcircuitDefinition> Definitions => _definitions;

        public CircuitSnapshot(IEnumerable<CircuitComponent> components, IEnumerable<Wire> wires, IEnumerable<SubcircuitDefinition> definitions = null)
        {
            _components = components.Select(c => c.Clone()).ToList();
            _wires = wires.Select(w => w.Clone()).ToList();
            _definitions = definitions?.ToList();
        }

        public static CircuitSnapshot Capture(Circuit circuit, bool includeLibrary)
        {
            return new CircuitSnapshot(circuit.Components, circuit.Wires, includeLibrary ? circuit.Library.Definitions : null);
        }

        public static CircuitSnapshot Empty() => new CircuitSnapshot(Array.Empty<CircuitComponent>(), Array.Empty<Wire>(), Array.Empty<SubcircuitDefinition>());

        public IReadOnlyList<string> Restore(Circuit circuit)
        {
            if (_definitions != null)
                circuit.Library.ReplaceAll(_definitions);
            return circuit.ReplaceContents(_components.Select(c => c.Clone()).ToList(), _wires.Select(w => w.Clone()).ToList());
        }
    }

    /// <summary>
    /// A selection of components plus the wires between them, detached from any circuit.
    /// Used for copy/paste and for creating subcircuits.
    /// </summary>
    public sealed class CircuitFragment
    {
        public IReadOnlyList<CircuitComponent> Components { get; }
        public IReadOnlyList<Wire> Wires { get; }

        private CircuitFragment(List<CircuitComponent> components, List<Wire> wires)
        {
            Components = components;
            Wires = wires;
        }

        public bool IsEmpty => Components.Count == 0;

        public static CircuitFragment Capture(Circuit circuit, IEnumerable<int> componentIds)
        {
            var ids = new HashSet<int>(componentIds.Where(id => circuit.TryGetComponent(id, out _)));
            var components = ids.OrderBy(id => id).Select(id => circuit.GetComponent(id).Clone()).ToList();
            var wires = circuit.Wires.Where(w => ids.Contains(w.From.ComponentId) && ids.Contains(w.To.ComponentId))
                .Select(w => w.Clone()).ToList();
            return new CircuitFragment(components, wires);
        }

        public static CircuitFragment FromElements(IEnumerable<CircuitComponent> components, IEnumerable<Wire> wires)
        {
            return new CircuitFragment(components.Select(c => c.Clone()).ToList(), wires.Select(w => w.Clone()).ToList());
        }

        public (float x, float y) Center
        {
            get
            {
                if (Components.Count == 0)
                    return (0, 0);
                return (Components.Average(c => c.X), Components.Average(c => c.Y));
            }
        }

        /// <summary>Creates an edit that inserts a copy of the fragment with fresh ids, shifted by the offset.</summary>
        public AddElementsEdit CreatePasteEdit(Circuit circuit, float offsetX, float offsetY)
        {
            var idMap = new Dictionary<int, int>();
            var components = new List<CircuitComponent>();
            foreach (var source in Components)
            {
                if (source.Kind == ComponentKind.Subcircuit && !circuit.Library.TryGet(source.SubcircuitId, out _))
                    continue;
                var copy = source.Clone();
                copy.Id = circuit.ReserveComponentId();
                copy.X += offsetX;
                copy.Y += offsetY;
                idMap[source.Id] = copy.Id;
                components.Add(copy);
            }

            var wires = new List<Wire>();
            foreach (var wire in Wires)
            {
                if (!idMap.TryGetValue(wire.From.ComponentId, out int from) || !idMap.TryGetValue(wire.To.ComponentId, out int to))
                    continue;
                wires.Add(new Wire(circuit.ReserveWireId(), new PinRef(from, wire.From.Pin), new PinRef(to, wire.To.Pin)));
            }

            return new AddElementsEdit(circuit, components, wires, "Paste");
        }

        /// <summary>Builds a standalone circuit from the fragment (ids preserved).</summary>
        public Circuit ToCircuit(SubcircuitLibrary library)
        {
            var circuit = new Circuit(library);
            circuit.ReplaceContents(Components.Select(c => c.Clone()).ToList(), Wires.Select(w => w.Clone()).ToList());
            return circuit;
        }
    }
}
