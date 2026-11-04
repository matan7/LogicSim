using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicSim.Simulation
{
    /// <summary>Undo history with nested scopes: every opened IC gets its own scope.</summary>
    public interface IEditHistory
    {
        int UndoCount { get; }
        int RedoCount { get; }
        int ScopeDepth { get; }

        void Execute(ICircuitEdit edit);

        /// <summary>Adds an edit that has already been applied.</summary>
        void Record(ICircuitEdit appliedEdit);

        bool Undo();
        bool Redo();

        void PushScope();

        /// <summary>Removes the innermost scope and returns its applied edits, oldest first. Redo entries are dropped.</summary>
        IReadOnlyList<ICircuitEdit> PopScope();
    }

    /// <summary>One level of the circuit hierarchy: the document itself or an IC opened for editing.</summary>
    public sealed class HierarchyLevel
    {
        /// <summary>Null for the document.</summary>
        public string DefinitionId { get; }

        /// <summary>The document, or a working copy of the definition that is written back on apply.</summary>
        public Circuit Circuit { get; }

        /// <summary>Instance in the parent level the IC was opened through; 0 when opened from the library.</summary>
        public int InstanceId { get; }

        public bool IsRoot => DefinitionId == null;

        // Cache of CircuitHierarchy.IsModified, keyed by the versions it was computed for.
        internal int CheckedVersion = -1;
        internal SubcircuitDefinition CheckedDefinition;
        internal int CheckedDefinitionVersion;
        internal bool Modified;

        internal HierarchyLevel(string definitionId, Circuit circuit, int instanceId)
        {
            DefinitionId = definitionId;
            Circuit = circuit;
            InstanceId = instanceId;
        }
    }

    /// <summary>
    /// Navigation into ICs for in-place editing. Opening an IC edits a working copy in a new undo scope; closing it
    /// writes the copy back to the library (every instance updates, see <see cref="UpdateSubcircuitEdit"/>) as one
    /// undo step of the parent level, or throws the changes away.
    /// </summary>
    public sealed class CircuitHierarchy
    {
        private readonly List<HierarchyLevel> _levels = new List<HierarchyLevel>();
        private readonly IEditHistory _history;

        public Circuit Root { get; }
        public SubcircuitLibrary Library => Root.Library;
        public IReadOnlyList<HierarchyLevel> Levels => _levels;
        public HierarchyLevel Current => _levels[_levels.Count - 1];
        public bool IsAtRoot => _levels.Count == 1;
        public int Depth => _levels.Count - 1;

        /// <summary>Applies edits to the current level through the history.</summary>
        public ICircuitEditor Editor { get; }

        /// <summary>A level was opened or closed.</summary>
        public event Action Changed;

        public CircuitHierarchy(Circuit root, IEditHistory history)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _levels.Add(new HierarchyLevel(null, root, 0));
            Editor = new LevelEditor(this);
        }

        private sealed class LevelEditor : ICircuitEditor
        {
            private readonly CircuitHierarchy _hierarchy;

            public LevelEditor(CircuitHierarchy hierarchy)
            {
                _hierarchy = hierarchy;
            }

            public Circuit Circuit => _hierarchy.Current.Circuit;

            public void Apply(ICircuitEdit edit) => _hierarchy._history.Execute(edit);
        }

        public string GetName(HierarchyLevel level)
        {
            if (level.IsRoot)
                return "Main";
            return Library.TryGet(level.DefinitionId, out var definition) ? definition.Name : level.DefinitionId;
        }

        /// <summary>Definition ids of all open ICs (an instance of these, or of anything that contains them, cannot be placed).</summary>
        public IEnumerable<string> OpenDefinitionIds => _levels.Where(l => !l.IsRoot).Select(l => l.DefinitionId);

        public bool CanPlace(string definitionId) => !Library.WouldCreateCycle(definitionId, OpenDefinitionIds);

        #region Open

        public HierarchyLevel OpenInstance(int instanceId)
        {
            var instance = Current.Circuit.GetComponent(instanceId);
            if (instance.Kind != ComponentKind.Subcircuit)
                throw new CircuitException($"{instance} is not a subcircuit");
            return Open(instance.SubcircuitId, instanceId);
        }

        public HierarchyLevel OpenDefinition(string definitionId) => Open(definitionId, 0);

        private HierarchyLevel Open(string definitionId, int instanceId)
        {
            var definition = Library.Get(definitionId);
            if (OpenDefinitionIds.Contains(definitionId))
                throw new CircuitException($"'{definition.Name}' is already open");
            var level = new HierarchyLevel(definitionId, definition.Circuit.Clone(), instanceId);
            _history.PushScope();
            _levels.Add(level);
            Changed?.Invoke();
            return level;
        }

        #endregion

        #region Apply and close

        /// <summary>
        /// True when the working copy differs from the definition in the library. Cheap to call every frame: the
        /// comparison only runs again after the copy or the definition changed.
        /// </summary>
        public bool IsModified(HierarchyLevel level)
        {
            if (level.IsRoot)
                return false;
            if (!Library.TryGet(level.DefinitionId, out var definition))
                return true;
            int version = level.Circuit.ContentVersion;
            int definitionVersion = definition.Circuit.ContentVersion;
            if (level.CheckedVersion != version || !ReferenceEquals(level.CheckedDefinition, definition)
                || level.CheckedDefinitionVersion != definitionVersion)
            {
                level.Modified = !CircuitComparer.SameContents(level.Circuit, definition.Circuit);
                level.CheckedVersion = version;
                level.CheckedDefinition = definition;
                level.CheckedDefinitionVersion = definitionVersion;
            }
            return level.Modified;
        }

        /// <summary>Writes the working copy of <paramref name="level"/> back to the library, recorded in the current scope.</summary>
        /// <returns>The applied edit, or null when nothing changed.</returns>
        public UpdateSubcircuitEdit Apply(HierarchyLevel level)
        {
            if (level.IsRoot || !IsModified(level))
                return null;
            var definition = Library.Get(level.DefinitionId);
            var updated = new SubcircuitDefinition(definition.Id, definition.Name, level.Circuit.Clone());
            if (updated.DependsOn(updated.Id, Library))
                throw new CircuitException($"'{definition.Name}' cannot contain itself");
            var live = _levels.Where(l => l != level).Select(l => l.Circuit);
            var edit = new UpdateSubcircuitEdit(Library, updated, live, $"Edit IC '{definition.Name}'");
            _history.Execute(edit);
            return edit;
        }

        /// <summary>Applies every open level, innermost first (e.g. before the simulation starts).</summary>
        public int ApplyAll()
        {
            int applied = 0;
            for (int i = _levels.Count - 1; i > 0; i--)
                if (Apply(_levels[i]) != null)
                    applied++;
            return applied;
        }

        /// <summary>
        /// Closes the current level. With <paramref name="apply"/> its changes (including changes to nested ICs)
        /// become one undo step of the parent level; otherwise everything done inside is reverted.
        /// </summary>
        public void Close(bool apply)
        {
            if (IsAtRoot)
                throw new CircuitException("No IC is open");
            var level = Current;
            var name = GetName(level);
            if (apply)
                Apply(level);

            var applied = _history.PopScope();
            _levels.RemoveAt(_levels.Count - 1);

            if (apply)
            {
                // Edits of the working copy are reflected in the update; keeping them in the step (they only touch
                // the discarded copy) keeps the whole sequence replayable in order.
                if (applied.Any(AffectsLibrary))
                    _history.Record(new CompositeEdit($"Edit IC '{name}'", applied));
            }
            else
            {
                for (int i = applied.Count - 1; i >= 0; i--)
                    applied[i].Revert();
            }
            Changed?.Invoke();
        }

        /// <summary>Closes levels until <paramref name="depth"/> levels are left open (0 = back to the document).</summary>
        public void CloseTo(int depth, bool apply)
        {
            depth = Math.Max(0, depth);
            while (Depth > depth)
                Close(apply);
        }

        public static bool AffectsLibrary(ICircuitEdit edit)
        {
            if (edit is ILibraryEdit)
                return true;
            if (edit is CompositeEdit composite)
                return composite.Edits.Any(AffectsLibrary);
            // Restores the library captured before the replacement.
            return edit is ReplaceCircuitEdit;
        }

        #endregion

        #region Probing

        /// <summary>
        /// Instance ids from the document down to <paramref name="level"/>, used to read the values of that particular
        /// instance during the simulation. Null when a level was opened from the library or the path no longer exists.
        /// </summary>
        public IReadOnlyList<int> GetInstancePath(HierarchyLevel level)
        {
            int index = _levels.IndexOf(level);
            if (index < 0)
                return null;
            var path = new List<int>(index);
            for (int i = 1; i <= index; i++)
            {
                var current = _levels[i];
                var parent = _levels[i - 1];
                if (current.InstanceId == 0 || !parent.Circuit.TryGetComponent(current.InstanceId, out var instance)
                    || instance.Kind != ComponentKind.Subcircuit || instance.SubcircuitId != current.DefinitionId)
                    return null;
                path.Add(current.InstanceId);
            }
            return path;
        }

        #endregion
    }

    /// <summary>Structural equality of two circuits (component ids, kinds, properties, positions and connections).</summary>
    public static class CircuitComparer
    {
        public static bool SameContents(Circuit a, Circuit b)
        {
            if (a.ComponentCount != b.ComponentCount || a.WireCount != b.WireCount)
                return false;
            foreach (var x in a.Components)
            {
                if (!b.TryGetComponent(x.Id, out var y))
                    return false;
                if (x.Kind != y.Kind || x.X != y.X || x.Y != y.Y || !x.GetProperties().Equals(y.GetProperties())
                    || x.SubcircuitId != y.SubcircuitId)
                    return false;
            }
            var wires = new HashSet<(PinRef, PinRef)>(a.Wires.Select(w => (w.From, w.To)));
            return b.Wires.All(w => wires.Contains((w.From, w.To)));
        }
    }
}
