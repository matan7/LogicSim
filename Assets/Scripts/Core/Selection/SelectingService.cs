using System;
using System.Collections.Generic;

namespace Core.Selecting
{
    /// <summary>A selected element of the circuit shown: a component or a wire, by id.</summary>
    public readonly struct SelectionItem : IEquatable<SelectionItem>
    {
        public readonly bool IsWire;
        public readonly int Id;

        private SelectionItem(bool isWire, int id)
        {
            IsWire = isWire;
            Id = id;
        }

        public static SelectionItem Component(int id) => new SelectionItem(false, id);

        public static SelectionItem Wire(int id) => new SelectionItem(true, id);

        public bool Equals(SelectionItem other) => IsWire == other.IsWire && Id == other.Id;

        public override bool Equals(object obj) => obj is SelectionItem other && Equals(other);

        public override int GetHashCode() => IsWire ? ~Id : Id;

        public override string ToString() => (IsWire ? "wire " : "component ") + Id;
    }

    /// <summary>
    /// The selected components and wires of the circuit shown, by id, so that it does not depend on views: undo, the MCP
    /// tools and recreated views see the same selection. <see cref="ItemChanged"/> fires per element (the circuit view
    /// shows it), <see cref="Changed"/> once after every change.
    /// </summary>
    public class SelectingService
    {
        private readonly HashSet<int> _components = new HashSet<int>();
        private readonly HashSet<int> _wires = new HashSet<int>();

        public IReadOnlyCollection<int> ComponentIds => _components;
        public IReadOnlyCollection<int> WireIds => _wires;
        public int Count => _components.Count + _wires.Count;

        /// <summary>Incremented on every change; lets callers cache values derived from the selection.</summary>
        public int Version { get; private set; }

        public event Action Changed;

        /// <summary>An element was selected (true) or deselected (false); raised before <see cref="Changed"/>.</summary>
        public event Action<SelectionItem, bool> ItemChanged;

        public bool Contains(SelectionItem item) => (item.IsWire ? _wires : _components).Contains(item.Id);

        public void Select(SelectionItem item)
        {
            if (!(item.IsWire ? _wires : _components).Add(item.Id))
                return;
            ItemChanged?.Invoke(item, true);
            NotifyChanged();
        }

        /// <summary>Selects several elements with one <see cref="Changed"/>.</summary>
        public void Select(IEnumerable<SelectionItem> items)
        {
            bool any = false;
            foreach (var item in items)
            {
                if (!(item.IsWire ? _wires : _components).Add(item.Id))
                    continue;
                ItemChanged?.Invoke(item, true);
                any = true;
            }
            if (any)
                NotifyChanged();
        }

        public void Deselect(SelectionItem item)
        {
            if (!(item.IsWire ? _wires : _components).Remove(item.Id))
                return;
            ItemChanged?.Invoke(item, false);
            NotifyChanged();
        }

        public void DeselectAll()
        {
            if (Count == 0)
                return;
            var components = new List<int>(_components);
            var wires = new List<int>(_wires);
            _components.Clear();
            _wires.Clear();
            if (ItemChanged != null)
            {
                foreach (var id in components)
                    ItemChanged(SelectionItem.Component(id), false);
                foreach (var id in wires)
                    ItemChanged(SelectionItem.Wire(id), false);
            }
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            Version++;
            Changed?.Invoke();
        }
    }
}
