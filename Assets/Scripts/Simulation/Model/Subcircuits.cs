using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LogicSim.Simulation
{
    /// <summary>
    /// A reusable circuit ("custom IC"). Its pins are the interface components of the inner circuit:
    /// Switch/Button/PinIn become inputs, Bulb/PinOut become outputs, ordered top to bottom.
    /// Definitions are treated as immutable once created.
    /// </summary>
    public sealed class SubcircuitDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public Circuit Circuit { get; }
        public IReadOnlyList<int> InputComponentIds { get; }
        public IReadOnlyList<int> OutputComponentIds { get; }
        public IReadOnlyList<string> InputNames { get; }
        public IReadOnlyList<string> OutputNames { get; }

        public SubcircuitDefinition(string id, string name, Circuit circuit)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Subcircuit id must not be empty", nameof(id));
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? id : name;
            Circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));

            var inputs = OrderInterface(circuit.Components.Where(c => c.Spec.IsInterfaceInput));
            var outputs = OrderInterface(circuit.Components.Where(c => c.Spec.IsInterfaceOutput));
            InputComponentIds = inputs.Select(c => c.Id).ToArray();
            OutputComponentIds = outputs.Select(c => c.Id).ToArray();
            InputNames = inputs.Select((c, i) => string.IsNullOrEmpty(c.Label) ? "in" + i : c.Label).ToArray();
            OutputNames = outputs.Select((c, i) => string.IsNullOrEmpty(c.Label) ? "out" + i : c.Label).ToArray();
        }

        private static List<CircuitComponent> OrderInterface(IEnumerable<CircuitComponent> components)
        {
            // Top to bottom, then left to right, so pin order matches the visual layout of the definition.
            return components.OrderByDescending(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Id).ToList();
        }

        public int IndexOfInputComponent(int componentId)
        {
            for (int i = 0; i < InputComponentIds.Count; i++)
                if (InputComponentIds[i] == componentId)
                    return i;
            return -1;
        }

        /// <summary>True when this definition uses <paramref name="otherId"/> directly or indirectly.</summary>
        public bool DependsOn(string otherId, SubcircuitLibrary library)
        {
            var visited = new HashSet<string>();
            var stack = new Stack<SubcircuitDefinition>();
            stack.Push(this);
            while (stack.Count > 0)
            {
                var def = stack.Pop();
                if (!visited.Add(def.Id))
                    continue;
                foreach (var c in def.Circuit.Components)
                {
                    if (c.Kind != ComponentKind.Subcircuit)
                        continue;
                    if (c.SubcircuitId == otherId)
                        return true;
                    if (library.TryGet(c.SubcircuitId, out var inner))
                        stack.Push(inner);
                }
            }
            return false;
        }
    }

    /// <summary>Set of subcircuit definitions shared by a document and all of its nested circuits.</summary>
    public sealed class SubcircuitLibrary
    {
        private readonly Dictionary<string, SubcircuitDefinition> _definitions = new Dictionary<string, SubcircuitDefinition>();
        private readonly List<string> _order = new List<string>();

        public event Action Changed;

        public IEnumerable<SubcircuitDefinition> Definitions => _order.Select(id => _definitions[id]);

        public int Count => _order.Count;

        public bool TryGet(string id, out SubcircuitDefinition definition)
        {
            if (id == null)
            {
                definition = null;
                return false;
            }
            return _definitions.TryGetValue(id, out definition);
        }

        public SubcircuitDefinition Get(string id)
        {
            if (TryGet(id, out var definition))
                return definition;
            throw new CircuitException($"Unknown subcircuit '{id}'");
        }

        public void Add(SubcircuitDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (_definitions.ContainsKey(definition.Id))
                throw new CircuitException($"Subcircuit '{definition.Id}' already exists");
            if (definition.DependsOn(definition.Id, this))
                throw new CircuitException($"Subcircuit '{definition.Id}' cannot contain itself");
            _definitions.Add(definition.Id, definition);
            _order.Add(definition.Id);
            Changed?.Invoke();
        }

        public bool Remove(string id)
        {
            if (!_definitions.Remove(id))
                return false;
            _order.Remove(id);
            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            if (_order.Count == 0)
                return;
            _definitions.Clear();
            _order.Clear();
            Changed?.Invoke();
        }

        /// <summary>Replaces all definitions at once (used when loading documents).</summary>
        public void ReplaceAll(IEnumerable<SubcircuitDefinition> definitions)
        {
            _definitions.Clear();
            _order.Clear();
            foreach (var definition in definitions)
            {
                _definitions[definition.Id] = definition;
                if (!_order.Contains(definition.Id))
                    _order.Add(definition.Id);
            }
            Changed?.Invoke();
        }

        public string CreateUniqueId(string name)
        {
            var slug = new StringBuilder();
            foreach (char ch in (name ?? string.Empty).Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                    slug.Append(ch);
                else if (slug.Length > 0 && slug[slug.Length - 1] != '-')
                    slug.Append('-');
            }
            var baseId = slug.ToString().Trim('-');
            if (baseId.Length == 0)
                baseId = "ic";
            var id = baseId;
            int suffix = 2;
            while (_definitions.ContainsKey(id))
                id = baseId + "-" + suffix++;
            return id;
        }
    }
}
