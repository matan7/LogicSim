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

        public int IndexOfOutputComponent(int componentId)
        {
            for (int i = 0; i < OutputComponentIds.Count; i++)
                if (OutputComponentIds[i] == componentId)
                    return i;
            return -1;
        }

        /// <summary>Same contents under a different name.</summary>
        public SubcircuitDefinition WithName(string name) => new SubcircuitDefinition(Id, name, Circuit);

        /// <summary>Copy of the contents under a new id.</summary>
        public SubcircuitDefinition WithId(string id, string name) => new SubcircuitDefinition(id, name, Circuit.Clone());

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

        /// <summary>Incremented on every change; compiled netlists remember the version they were built from.</summary>
        public int Version { get; private set; }

        public IEnumerable<SubcircuitDefinition> Definitions => _order.Select(id => _definitions[id]);

        public int Count => _order.Count;

        public bool Contains(string id) => id != null && _definitions.ContainsKey(id);

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
            NotifyChanged();
        }

        /// <summary>Swaps in a new version of an existing definition (same id, same position in the list).</summary>
        public void Replace(SubcircuitDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (!_definitions.TryGetValue(definition.Id, out var current))
                throw new CircuitException($"Unknown subcircuit '{definition.Id}'");
            if (ReferenceEquals(current, definition))
                return;
            if (definition.DependsOn(definition.Id, this))
                throw new CircuitException($"Subcircuit '{definition.Name}' cannot contain itself");
            _definitions[definition.Id] = definition;
            NotifyChanged();
        }

        public bool Remove(string id)
        {
            if (id == null || !_definitions.Remove(id))
                return false;
            _order.Remove(id);
            NotifyChanged();
            return true;
        }

        public void Clear()
        {
            if (_order.Count == 0)
                return;
            _definitions.Clear();
            _order.Clear();
            NotifyChanged();
        }

        /// <summary>Definitions whose circuit contains an instance of <paramref name="id"/> directly.</summary>
        public IEnumerable<SubcircuitDefinition> DirectUsersOf(string id)
        {
            return Definitions.Where(d => d.Id != id && d.Circuit.Components.Any(c => c.Kind == ComponentKind.Subcircuit && c.SubcircuitId == id));
        }

        /// <summary>True when an instance of <paramref name="id"/> would create a cycle if placed inside any of <paramref name="containerIds"/>.</summary>
        public bool WouldCreateCycle(string id, IEnumerable<string> containerIds)
        {
            if (!TryGet(id, out var definition))
                return false;
            foreach (var container in containerIds)
            {
                if (container == null)
                    continue;
                if (container == id || definition.DependsOn(container, this))
                    return true;
            }
            return false;
        }

        private int _batchDepth;
        private bool _changedInBatch;

        /// <summary>Groups several changes into one <see cref="Changed"/> notification (dispose to end the batch).</summary>
        public IDisposable BatchUpdate()
        {
            _batchDepth++;
            return new Batch(this);
        }

        private sealed class Batch : IDisposable
        {
            private SubcircuitLibrary _library;

            public Batch(SubcircuitLibrary library)
            {
                _library = library;
            }

            public void Dispose()
            {
                var library = _library;
                _library = null;
                if (library == null || --library._batchDepth > 0 || !library._changedInBatch)
                    return;
                library._changedInBatch = false;
                library.Changed?.Invoke();
            }
        }

        private void NotifyChanged()
        {
            Version++;
            if (_batchDepth > 0)
            {
                _changedInBatch = true;
                return;
            }
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
            NotifyChanged();
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

    /// <summary>
    /// How the pins of an old definition version map to a new one: by interface component (survives renames
    /// and reordering), then by pin name (k-th occurrence of a duplicate name maps to the k-th one). -1 = removed.
    /// </summary>
    public sealed class SubcircuitPinMap
    {
        public int[] Inputs { get; }
        public int[] Outputs { get; }

        public bool IsIdentity { get; }

        private SubcircuitPinMap(int[] inputs, int[] outputs, bool isIdentity)
        {
            Inputs = inputs;
            Outputs = outputs;
            IsIdentity = isIdentity;
        }

        public static SubcircuitPinMap Create(SubcircuitDefinition before, SubcircuitDefinition after)
        {
            var inputs = Map(before.InputComponentIds, before.InputNames, after.InputComponentIds, after.InputNames);
            var outputs = Map(before.OutputComponentIds, before.OutputNames, after.OutputComponentIds, after.OutputNames);
            bool identity = inputs.Length == after.InputComponentIds.Count && outputs.Length == after.OutputComponentIds.Count
                && inputs.Select((p, i) => p == i).All(x => x) && outputs.Select((p, i) => p == i).All(x => x);
            return new SubcircuitPinMap(inputs, outputs, identity);
        }

        public int MapInput(int pin) => pin >= 0 && pin < Inputs.Length ? Inputs[pin] : -1;
        public int MapOutput(int pin) => pin >= 0 && pin < Outputs.Length ? Outputs[pin] : -1;

        private static int[] Map(IReadOnlyList<int> oldIds, IReadOnlyList<string> oldNames, IReadOnlyList<int> newIds, IReadOnlyList<string> newNames)
        {
            var result = new int[oldIds.Count];
            var taken = new bool[newIds.Count];
            for (int i = 0; i < oldIds.Count; i++)
            {
                result[i] = -1;
                for (int j = 0; j < newIds.Count; j++)
                {
                    if (!taken[j] && newIds[j] == oldIds[i])
                    {
                        result[i] = j;
                        taken[j] = true;
                        break;
                    }
                }
            }

            var free = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < newIds.Count; j++)
            {
                if (taken[j])
                    continue;
                if (!free.TryGetValue(newNames[j], out var queue))
                    free.Add(newNames[j], queue = new Queue<int>());
                queue.Enqueue(j);
            }
            for (int i = 0; i < oldIds.Count; i++)
            {
                if (result[i] >= 0)
                    continue;
                if (free.TryGetValue(oldNames[i], out var queue) && queue.Count > 0)
                    result[i] = queue.Dequeue();
            }
            return result;
        }
    }
}
