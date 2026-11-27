using System.Collections.Generic;
using System.Linq;
using Behaviours;
using Core;
using Core.Selecting;
using LogicSim.Simulation;
using LogicSim.Views;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Actions
{
    /// <summary>Editing operations shared by keyboard shortcuts, toolbar buttons and the context of the selection.</summary>
    public static class EditCommands
    {
        private static Circuit Circuit => SystemCore.CircuitService.Circuit;
        private static ICircuitEditor Editor => SystemCore.CircuitService.Editor;

        public static bool CanEdit => SystemCore.AppStateService.IsEditing;

        public static bool IsTextInputFocused()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        public static List<int> SelectedComponentIds()
        {
            return SystemCore.SelectingService.ComponentIds
                .Where(id => Circuit.TryGetComponent(id, out _))
                .OrderBy(id => id)
                .ToList();
        }

        public static List<int> SelectedWireIds()
        {
            return SystemCore.SelectingService.WireIds
                .Where(id => Circuit.TryGetWire(id, out _))
                .OrderBy(id => id)
                .ToList();
        }

        public static bool Copy()
        {
            var ids = SelectedComponentIds();
            if (ids.Count == 0)
                return false;
            SystemCore.ClipboardService.SetFragment(CircuitFragment.Capture(Circuit, ids), Circuit.Library);
            return true;
        }

        public static bool Cut()
        {
            if (!CanEdit || !Copy())
                return false;
            return Delete();
        }

        public static bool Delete()
        {
            if (!CanEdit)
                return false;
            var edit = new RemoveElementsEdit(Circuit, SelectedComponentIds(), SelectedWireIds());
            if (edit.IsEmpty)
                return false;
            SystemCore.SelectingService.DeselectAll();
            Editor.Apply(edit);
            return true;
        }

        /// <summary>Pastes at the pivot point (right click sets it) and selects the pasted components.</summary>
        public static bool Paste()
        {
            if (!CanEdit)
                return false;
            var fragment = SystemCore.ClipboardService.ReadSystemFragment(Circuit.Library);
            if (fragment == null || fragment.IsEmpty)
                fragment = SystemCore.ClipboardService.Fragment;
            if (fragment == null || fragment.IsEmpty)
                return false;

            var (cx, cy) = fragment.Center;
            Vector3 target = PivotPoint.Instance != null ? PivotPoint.Instance.transform.position : Vector3.zero;
            float dx = Mathf.Round(target.x - cx);
            float dy = Mathf.Round(target.y - cy);
            var hierarchy = SystemCore.CircuitService.Hierarchy;
            if (fragment.Components.Any(c => c.Kind == ComponentKind.Subcircuit && !hierarchy.CanPlace(c.SubcircuitId)))
            {
                LogicSim.UI.AppUi.Instance?.ShowToast("The clipboard contains an IC that would contain itself here");
                return false;
            }
            var edit = fragment.CreatePasteEdit(Circuit, dx, dy);
            if (edit.Components.Count == 0)
                return false;
            Editor.Apply(edit);
            Select(edit.Components.Select(c => c.Id));
            return true;
        }

        public static bool Rotate()
        {
            if (!CanEdit)
                return false;
            var ids = SelectedComponentIds();
            if (ids.Count == 0)
                return false;
            var edits = new List<ICircuitEdit>();
            foreach (var id in ids)
            {
                var properties = Circuit.GetComponent(id).GetProperties();
                properties.Rotation = (properties.Rotation + 1) % 4;
                edits.Add(new SetPropertiesEdit(Circuit, id, properties));
            }
            Editor.Apply(edits.Count == 1 ? edits[0] : new CompositeEdit("Rotate", edits));
            return true;
        }

        public static void SelectAll()
        {
            if (!CanEdit || CircuitView.Instance == null)
                return;
            Select(Circuit.Components.Select(c => c.Id));
        }

        public static void Select(IEnumerable<int> componentIds)
        {
            var selecting = SystemCore.SelectingService;
            selecting.DeselectAll();
            selecting.Select(componentIds.Where(id => Circuit.TryGetComponent(id, out _)).Select(SelectionItem.Component).ToList());
        }
    }
}
