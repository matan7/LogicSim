using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicSim.Simulation
{
    /// <summary>Marks edits that change the subcircuit library (and not only one circuit).</summary>
    public interface ILibraryEdit
    {
    }

    public sealed class AddSubcircuitDefinitionEdit : ICircuitEdit, ILibraryEdit
    {
        private readonly SubcircuitLibrary _library;

        public SubcircuitDefinition Definition { get; }
        public string Description => $"Create IC '{Definition.Name}'";

        public AddSubcircuitDefinitionEdit(SubcircuitLibrary library, SubcircuitDefinition definition)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public void Apply() => _library.Add(Definition);
        public void Revert() => _library.Remove(Definition.Id);
    }

    /// <summary>Deletes an unused definition.</summary>
    public sealed class RemoveSubcircuitDefinitionEdit : ICircuitEdit, ILibraryEdit
    {
        private readonly SubcircuitLibrary _library;
        private readonly string _id;
        private List<SubcircuitDefinition> _order;

        public string Description => "Delete IC";

        public RemoveSubcircuitDefinitionEdit(SubcircuitLibrary library, string id)
        {
            _library = library;
            _id = id;
        }

        public void Apply()
        {
            _library.Get(_id); // unknown ids throw
            _order = _library.Definitions.ToList();
            _library.Remove(_id);
        }

        public void Revert()
        {
            // Restore the original position in the palette.
            _library.ReplaceAll(_order);
        }
    }

    public sealed class RenameSubcircuitEdit : ICircuitEdit, ILibraryEdit
    {
        private readonly SubcircuitLibrary _library;
        private readonly string _id;
        private readonly string _name;
        private SubcircuitDefinition _before;

        public string Description => $"Rename IC to '{_name}'";

        public RenameSubcircuitEdit(SubcircuitLibrary library, string id, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new CircuitException("Name must not be empty");
            _library = library;
            _id = id;
            _name = name.Trim();
        }

        public void Apply()
        {
            _before = _library.Get(_id);
            _library.Replace(_before.WithName(_name));
        }

        public void Revert() => _library.Replace(_before);
    }

    /// <summary>Points one subcircuit instance at another definition with the same pins.</summary>
    public sealed class SetSubcircuitIdEdit : ICircuitEdit
    {
        private readonly Circuit _circuit;
        private readonly int _componentId;
        private readonly string _after;
        private string _before;

        public string Description => "Change IC";

        public SetSubcircuitIdEdit(Circuit circuit, int componentId, string subcircuitId)
        {
            _circuit = circuit;
            _componentId = componentId;
            _after = subcircuitId;
        }

        public void Apply()
        {
            _before = _circuit.GetComponent(_componentId).SubcircuitId;
            _circuit.SetSubcircuitId(_componentId, _after);
        }

        public void Revert() => _circuit.SetSubcircuitId(_componentId, _before);
    }

    /// <summary>
    /// Replaces a definition with a new version and updates everything that uses it: wires on instances in the
    /// given live circuits and inside other definitions are moved to the matching pin (see <see cref="SubcircuitPinMap"/>)
    /// or removed when the pin no longer exists.
    /// </summary>
    public sealed class UpdateSubcircuitEdit : ICircuitEdit, ILibraryEdit
    {
        private readonly SubcircuitLibrary _library;
        private readonly SubcircuitDefinition _after;
        private readonly List<Circuit> _liveCircuits;
        private Plan _plan;

        private sealed class CircuitPlan
        {
            public Circuit Circuit;
            public readonly List<Wire> Removed = new List<Wire>();
            public readonly List<Wire> Added = new List<Wire>();
        }

        private sealed class Plan
        {
            public SubcircuitDefinition Before;
            public SubcircuitPinMap PinMap;
            public readonly List<(SubcircuitDefinition before, SubcircuitDefinition after)> Containers = new List<(SubcircuitDefinition, SubcircuitDefinition)>();
            public readonly List<CircuitPlan> Circuits = new List<CircuitPlan>();
        }

        public string Description { get; }
        public SubcircuitDefinition Definition => _after;

        /// <summary>Pin mapping from the previous version (available after the first apply).</summary>
        public SubcircuitPinMap PinMap => _plan?.PinMap;

        public UpdateSubcircuitEdit(SubcircuitLibrary library, SubcircuitDefinition after, IEnumerable<Circuit> liveCircuits, string description = null)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _after = after ?? throw new ArgumentNullException(nameof(after));
            _liveCircuits = (liveCircuits ?? Enumerable.Empty<Circuit>()).Where(c => c != null).Distinct().ToList();
            Description = description ?? $"Edit IC '{after.Name}'";
        }

        public void Apply()
        {
            if (_plan == null)
                _plan = BuildPlan();

            foreach (var circuit in _plan.Circuits)
                foreach (var wire in circuit.Removed)
                    circuit.Circuit.RemoveWire(wire.Id);

            try
            {
                using (_library.BatchUpdate())
                {
                    foreach (var (_, after) in _plan.Containers)
                        _library.Replace(after);
                    _library.Replace(_after);
                }
            }
            catch
            {
                using (_library.BatchUpdate())
                {
                    _library.Replace(_plan.Before);
                    foreach (var (before, _) in _plan.Containers)
                        _library.Replace(before);
                }
                RestoreRemovedWires();
                throw;
            }

            foreach (var circuit in _plan.Circuits)
                foreach (var wire in circuit.Added)
                    circuit.Circuit.AddWire(wire);
        }

        public void Revert()
        {
            foreach (var circuit in _plan.Circuits)
                foreach (var wire in circuit.Added)
                    circuit.Circuit.RemoveWire(wire.Id);
            using (_library.BatchUpdate())
            {
                _library.Replace(_plan.Before);
                for (int i = _plan.Containers.Count - 1; i >= 0; i--)
                    _library.Replace(_plan.Containers[i].before);
            }
            RestoreRemovedWires();
        }

        private void RestoreRemovedWires()
        {
            foreach (var circuit in _plan.Circuits)
                foreach (var wire in circuit.Removed.OrderBy(w => w.Id))
                    if (circuit.Circuit.CanConnect(wire.From, wire.To, out _))
                        circuit.Circuit.AddWire(wire);
        }

        private Plan BuildPlan()
        {
            var before = _library.Get(_after.Id);
            var plan = new Plan { Before = before, PinMap = SubcircuitPinMap.Create(before, _after) };
            if (plan.PinMap.IsIdentity)
                return plan;

            foreach (var circuit in _liveCircuits)
            {
                var circuitPlan = new CircuitPlan { Circuit = circuit };
                PlanWires(circuit, _after.Id, plan.PinMap, circuitPlan.Removed, circuitPlan.Added, circuit.ReserveWireId);
                if (circuitPlan.Removed.Count > 0)
                    plan.Circuits.Add(circuitPlan);
            }

            foreach (var container in _library.DirectUsersOf(_after.Id).ToList())
            {
                var removed = new List<Wire>();
                var added = new List<Wire>();
                int nextWireId = container.Circuit.Wires.Select(w => w.Id).DefaultIfEmpty(0).Max() + 1;
                PlanWires(container.Circuit, _after.Id, plan.PinMap, removed, added, () => nextWireId++);
                if (removed.Count == 0)
                    continue;
                var removedIds = new HashSet<int>(removed.Select(w => w.Id));
                var wires = container.Circuit.Wires.Where(w => !removedIds.Contains(w.Id)).Concat(added).ToList();
                // Pins that only exist in the new version are unknown to the library until Apply, so the wires
                // cannot be validated yet.
                var circuit = new Circuit(container.Circuit.Library);
                circuit.ReplaceContentsUnchecked(container.Circuit.Components.Select(c => c.Clone()).ToList(), wires.Select(w => w.Clone()).ToList());
                plan.Containers.Add((container, new SubcircuitDefinition(container.Id, container.Name, circuit)));
            }
            return plan;
        }

        internal static void PlanWires(Circuit circuit, string subcircuitId, SubcircuitPinMap map, List<Wire> removed, List<Wire> added, Func<int> reserveWireId)
        {
            var instances = new HashSet<int>(circuit.InstancesOf(subcircuitId).Select(c => c.Id));
            if (instances.Count == 0)
                return;
            foreach (var wire in circuit.Wires.ToList())
            {
                bool fromInstance = instances.Contains(wire.From.ComponentId);
                bool toInstance = instances.Contains(wire.To.ComponentId);
                if (!fromInstance && !toInstance)
                    continue;
                int fromPin = fromInstance ? map.MapOutput(wire.From.Pin) : wire.From.Pin;
                int toPin = toInstance ? map.MapInput(wire.To.Pin) : wire.To.Pin;
                if (fromPin == wire.From.Pin && toPin == wire.To.Pin)
                    continue;
                removed.Add(wire);
                if (fromPin >= 0 && toPin >= 0)
                    added.Add(new Wire(reserveWireId(), new PinRef(wire.From.ComponentId, fromPin), new PinRef(wire.To.ComponentId, toPin)));
            }
        }
    }

    /// <summary>Restructuring operations on subcircuits. Each returns a single undoable edit.</summary>
    public static class SubcircuitRefactoring
    {
        /// <summary>Components that stay in the parent when a selection is collapsed: they are how the user drives and watches the circuit.</summary>
        public static bool StaysOutsideWhenCollapsing(ComponentKind kind)
        {
            switch (kind)
            {
                case ComponentKind.Switch:
                case ComponentKind.Button:
                case ComponentKind.Bulb:
                case ComponentKind.HexDisplay:
                case ComponentKind.PinIn:
                case ComponentKind.PinOut:
                    return true;
                default:
                    return false;
            }
        }

        public sealed class CollapseResult
        {
            public ICircuitEdit Edit;
            public SubcircuitDefinition Definition;
            public CircuitComponent Instance;

            /// <summary>Selected switches, bulbs and pins that were left in the parent circuit.</summary>
            public IReadOnlyList<int> KeptOutside;
        }

        /// <summary>
        /// Moves the selected components into a new subcircuit and puts an instance in their place. Wires that cross the
        /// selection boundary become input/output pins (one per driving output), so the circuit behaves exactly as before.
        /// </summary>
        public static CollapseResult Collapse(Circuit circuit, IEnumerable<int> componentIds, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new CircuitException("Name must not be empty");
            name = name.Trim();
            var requested = componentIds.Distinct().Where(id => circuit.TryGetComponent(id, out _)).ToList();
            var kept = requested.Where(id => StaysOutsideWhenCollapsing(circuit.GetComponent(id).Kind)).ToList();
            var selected = new HashSet<int>(requested.Except(kept));
            if (selected.Count == 0)
                throw new CircuitException("Select at least one gate, flip-flop or IC to collapse (switches, bulbs and pins stay outside)");

            var members = selected.Select(circuit.GetComponent).OrderBy(c => c.Id).ToList();
            float minX = members.Min(c => c.X), maxX = members.Max(c => c.X);
            float minY = members.Min(c => c.Y), maxY = members.Max(c => c.Y);

            var internalWires = circuit.Wires.Where(w => selected.Contains(w.From.ComponentId) && selected.Contains(w.To.ComponentId)).ToList();
            var inbound = circuit.Wires.Where(w => !selected.Contains(w.From.ComponentId) && selected.Contains(w.To.ComponentId)).ToList();
            var outbound = circuit.Wires.Where(w => selected.Contains(w.From.ComponentId) && !selected.Contains(w.To.ComponentId)).ToList();

            var inputGroups = inbound.GroupBy(w => w.From)
                .OrderByDescending(g => circuit.GetComponent(g.Key.ComponentId).Y)
                .ThenBy(g => circuit.GetComponent(g.Key.ComponentId).X)
                .ThenBy(g => g.Key.ComponentId).ThenBy(g => g.Key.Pin)
                .ToList();
            var outputGroups = outbound.GroupBy(w => w.From)
                .OrderByDescending(g => circuit.GetComponent(g.Key.ComponentId).Y)
                .ThenBy(g => circuit.GetComponent(g.Key.ComponentId).X)
                .ThenBy(g => g.Key.ComponentId).ThenBy(g => g.Key.Pin)
                .ToList();

            // Inner circuit: selected components keep their ids, pins get fresh ones.
            var innerComponents = members.Select(c => c.Clone()).ToList();
            var innerWires = internalWires.Select(w => w.Clone()).ToList();
            int nextId = selected.Max() + 1;
            int nextWire = internalWires.Select(w => w.Id).DefaultIfEmpty(0).Max() + 1;
            float top = (float)Math.Round(maxY);
            var inputPins = new List<int>();
            var outputPins = new List<int>();

            for (int k = 0; k < inputGroups.Count; k++)
            {
                var source = circuit.GetComponent(inputGroups[k].Key.ComponentId);
                var pin = ComponentFactory.Create(ComponentKind.PinIn, (float)Math.Floor(minX) - 3, top - 2 * k,
                    label: InputPinLabel(circuit, source, inputGroups[k].Key.Pin, k));
                ComponentFactory.SetDetachedId(pin, nextId++);
                innerComponents.Add(pin);
                inputPins.Add(pin.Id);
                foreach (var wire in inputGroups[k])
                    innerWires.Add(new Wire(nextWire++, new PinRef(pin.Id, 0), wire.To));
            }

            for (int k = 0; k < outputGroups.Count; k++)
            {
                var source = circuit.GetComponent(outputGroups[k].Key.ComponentId);
                var targets = outputGroups[k].Select(w => circuit.GetComponent(w.To.ComponentId));
                var pin = ComponentFactory.Create(ComponentKind.PinOut, (float)Math.Ceiling(maxX) + 3, top - 2 * k,
                    label: OutputPinLabel(circuit, source, outputGroups[k].Key.Pin, targets, k));
                ComponentFactory.SetDetachedId(pin, nextId++);
                innerComponents.Add(pin);
                outputPins.Add(pin.Id);
                innerWires.Add(new Wire(nextWire++, outputGroups[k].Key, new PinRef(pin.Id, 0)));
            }

            var library = circuit.Library;
            var innerCircuit = new Circuit(library);
            var problems = innerCircuit.ReplaceContents(innerComponents, innerWires);
            if (problems.Count > 0)
                throw new CircuitException("Could not build the subcircuit: " + string.Join("; ", problems));
            var definition = new SubcircuitDefinition(library.CreateUniqueId(name), name, innerCircuit);

            var instance = ComponentFactory.CreateSubcircuit(definition.Id,
                (float)Math.Round((minX + maxX) / 2), (float)Math.Round((minY + maxY) / 2));
            ComponentFactory.ReserveId(circuit, instance);

            var outerWires = new List<Wire>();
            for (int k = 0; k < inputGroups.Count; k++)
            {
                int pinIndex = definition.IndexOfInputComponent(inputPins[k]);
                outerWires.Add(new Wire(circuit.ReserveWireId(), inputGroups[k].Key, new PinRef(instance.Id, pinIndex)));
            }
            for (int k = 0; k < outputGroups.Count; k++)
            {
                int pinIndex = definition.IndexOfOutputComponent(outputPins[k]);
                foreach (var wire in outputGroups[k].OrderBy(w => w.Id))
                    outerWires.Add(new Wire(circuit.ReserveWireId(), new PinRef(instance.Id, pinIndex), wire.To));
            }

            var edit = new CompositeEdit($"Collapse to IC '{name}'", new ICircuitEdit[]
            {
                new AddSubcircuitDefinitionEdit(library, definition),
                new RemoveElementsEdit(circuit, selected),
                new AddElementsEdit(circuit, new[] { instance }, outerWires, $"Add IC '{name}'")
            });
            return new CollapseResult { Edit = edit, Definition = definition, Instance = instance, KeptOutside = kept };
        }

        private static string InputPinLabel(Circuit circuit, CircuitComponent source, int pin, int index)
        {
            bool multi = circuit.GetOutputCount(source) > 1;
            var pinName = circuit.GetOutputName(source, pin);
            if (!string.IsNullOrEmpty(source.Label))
                return multi ? source.Label + "." + pinName : source.Label;
            return multi ? pinName : "in" + index;
        }

        private static string OutputPinLabel(Circuit circuit, CircuitComponent source, int pin, IEnumerable<CircuitComponent> targets, int index)
        {
            bool multi = circuit.GetOutputCount(source) > 1;
            var pinName = circuit.GetOutputName(source, pin);
            var targetList = targets.ToList();
            // A bulb or output pin outside names the signal better than the gate that drives it.
            var shown = targetList.Where(t => t.Spec.IsInterfaceOutput && !string.IsNullOrEmpty(t.Label)).Select(t => t.Label).Distinct().ToList();
            if (shown.Count == 1)
                return shown[0];
            if (!string.IsNullOrEmpty(source.Label))
                return multi ? source.Label + "." + pinName : source.Label;
            var labeled = targetList.Where(t => !string.IsNullOrEmpty(t.Label)).Select(t => t.Label).Distinct().ToList();
            if (labeled.Count == 1)
                return labeled[0];
            return multi ? pinName : "out" + index;
        }

        public sealed class ExplodeResult
        {
            public ICircuitEdit Edit;

            /// <summary>Components created in the parent circuit (detached until the edit is applied).</summary>
            public IReadOnlyList<CircuitComponent> Components;
        }

        /// <summary>Replaces a subcircuit instance with a copy of its contents, wired exactly like the instance was.</summary>
        public static ExplodeResult Explode(Circuit circuit, int instanceId)
        {
            var instance = circuit.GetComponent(instanceId);
            if (instance.Kind != ComponentKind.Subcircuit)
                throw new CircuitException($"{instance} is not a subcircuit");
            var definition = circuit.Library.Get(instance.SubcircuitId);
            var inner = definition.Circuit;

            var innerParts = inner.Components.Where(c => !c.Spec.IsInterfaceInput && !c.Spec.IsInterfaceOutput).ToList();
            float cx = 0, cy = 0;
            if (innerParts.Count > 0)
            {
                cx = (float)Math.Round((innerParts.Min(c => c.X) + innerParts.Max(c => c.X)) / 2);
                cy = (float)Math.Round((innerParts.Min(c => c.Y) + innerParts.Max(c => c.Y)) / 2);
            }

            var idMap = new Dictionary<int, int>();
            var created = new List<CircuitComponent>();
            foreach (var part in innerParts)
            {
                var copy = part.Clone();
                ComponentFactory.SetDetachedId(copy, circuit.ReserveComponentId());
                ComponentFactory.SetDetachedPosition(copy, part.X - cx + instance.X, part.Y - cy + instance.Y);
                idMap.Add(part.Id, copy.Id);
                created.Add(copy);
            }

            var externalDrivers = new PinRef?[definition.InputComponentIds.Count];
            for (int pin = 0; pin < externalDrivers.Length; pin++)
                externalDrivers[pin] = circuit.GetWireInto(instanceId, pin)?.From;

            var visiting = new HashSet<int>();

            PinRef? SourceOfInnerOutput(int outputIndex)
            {
                if (outputIndex < 0 || outputIndex >= definition.OutputComponentIds.Count || !visiting.Add(outputIndex))
                    return null;
                try
                {
                    var wire = inner.GetWireInto(definition.OutputComponentIds[outputIndex], 0);
                    return wire == null ? null : Resolve(wire.From);
                }
                finally
                {
                    visiting.Remove(outputIndex);
                }
            }

            PinRef? Resolve(PinRef innerSource)
            {
                if (idMap.TryGetValue(innerSource.ComponentId, out int mapped))
                    return new PinRef(mapped, innerSource.Pin);
                int inputIndex = definition.IndexOfInputComponent(innerSource.ComponentId);
                if (inputIndex < 0)
                    return null;
                var driver = externalDrivers[inputIndex];
                if (driver == null)
                    return null;
                // The instance feeding itself: follow the loop through the inside.
                return driver.Value.ComponentId == instanceId ? SourceOfInnerOutput(driver.Value.Pin) : driver;
            }

            var wires = new List<Wire>();
            foreach (var wire in inner.Wires)
            {
                if (!idMap.TryGetValue(wire.To.ComponentId, out int target))
                    continue;
                var source = Resolve(wire.From);
                if (source != null)
                    wires.Add(new Wire(circuit.ReserveWireId(), source.Value, new PinRef(target, wire.To.Pin)));
            }
            foreach (var wire in circuit.GetWiresOf(instanceId).Where(w => w.From.ComponentId == instanceId && w.To.ComponentId != instanceId).OrderBy(w => w.Id).ToList())
            {
                var source = SourceOfInnerOutput(wire.From.Pin);
                if (source != null)
                    wires.Add(new Wire(circuit.ReserveWireId(), source.Value, wire.To));
            }

            var edit = new CompositeEdit($"Explode IC '{definition.Name}'", new ICircuitEdit[]
            {
                new RemoveElementsEdit(circuit, new[] { instanceId }),
                new AddElementsEdit(circuit, created, wires, "Explode")
            });
            return new ExplodeResult { Edit = edit, Components = created };
        }

        /// <summary>Gives one instance its own copy of the definition so it can be edited without affecting the others.</summary>
        public static ICircuitEdit MakeUnique(Circuit circuit, int instanceId, string name, out SubcircuitDefinition copy)
        {
            var instance = circuit.GetComponent(instanceId);
            if (instance.Kind != ComponentKind.Subcircuit)
                throw new CircuitException($"{instance} is not a subcircuit");
            var library = circuit.Library;
            var definition = library.Get(instance.SubcircuitId);
            if (string.IsNullOrWhiteSpace(name))
                name = UniqueName(library, definition.Name);
            copy = definition.WithId(library.CreateUniqueId(name), name.Trim());
            return new CompositeEdit($"Make IC '{definition.Name}' unique", new ICircuitEdit[]
            {
                new AddSubcircuitDefinitionEdit(library, copy),
                new SetSubcircuitIdEdit(circuit, instanceId, copy.Id)
            });
        }

        public static string UniqueName(SubcircuitLibrary library, string baseName)
        {
            var names = new HashSet<string>(library.Definitions.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            var trimmed = baseName.TrimEnd();
            int suffix = 2;
            // "Adder 2" -> "Adder 3" rather than "Adder 2 2".
            int space = trimmed.LastIndexOf(' ');
            if (space > 0 && int.TryParse(trimmed.Substring(space + 1), out int existing))
            {
                trimmed = trimmed.Substring(0, space);
                suffix = existing + 1;
            }
            string candidate;
            do
                candidate = trimmed + " " + suffix++;
            while (names.Contains(candidate));
            return candidate;
        }
    }
}
