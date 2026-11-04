using System;
using System.Collections.Generic;

namespace Core.Selecting
{
    /// <summary>The selected views. <see cref="Changed"/> fires after every change, so listeners do not need to poll.</summary>
    public class SelectingService
    {
        private readonly List<ISelectable> _selection = new List<ISelectable>();

        public IReadOnlyList<ISelectable> SelectionList => _selection;

        /// <summary>Incremented on every change; lets callers cache values derived from the selection.</summary>
        public int Version { get; private set; }

        public event Action Changed;

        public bool Contains(ISelectable selectable) => _selection.Contains(selectable);

        public void DeselectAll()
        {
            if (_selection.Count == 0)
                return;
            var removed = _selection.ToArray();
            _selection.Clear();
            foreach (var selectable in removed)
                selectable.Deselect();
            NotifyChanged();
        }

        public void Select(ISelectable selectable)
        {
            if (selectable == null || _selection.Contains(selectable))
                return;
            _selection.Add(selectable);
            selectable.Select();
            NotifyChanged();
        }

        public void Deselect(ISelectable selectable)
        {
            if (!_selection.Remove(selectable))
                return;
            selectable.Deselect();
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            Version++;
            Changed?.Invoke();
        }
    }
}
