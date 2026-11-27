using System.Collections.Generic;
using Core.Selecting;
using LogicSim.Views;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Actions
{
    /// <summary>
    /// What lies under the pointer, with pins taking priority over component bodies and wires. The circuit view answers
    /// from its spatial grids (no physics colliders).
    /// </summary>
    public static class PointerQuery
    {
        public static bool IsOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        public static PinView PinAt(Vector2 world) => CircuitView.Instance != null ? CircuitView.Instance.PinAt(world) : null;

        public static ComponentView ComponentAt(Vector2 world) => CircuitView.Instance != null ? CircuitView.Instance.ComponentAt(world) : null;

        public static WireView WireAt(Vector2 world) => CircuitView.Instance != null ? CircuitView.Instance.WireAt(world) : null;

        /// <summary>True when a pin, a component or a wire lies at the point (not empty canvas).</summary>
        public static bool AnythingAt(Vector2 world) => PinAt(world) != null || ComponentAt(world) != null || WireAt(world) != null;

        /// <summary>Component (preferred) or wire that can be selected at the point.</summary>
        public static bool TrySelectableAt(Vector2 world, out SelectionItem item)
        {
            var component = ComponentAt(world);
            if (component != null)
            {
                item = SelectionItem.Component(component.ComponentId);
                return true;
            }
            var wire = WireAt(world);
            item = wire != null ? SelectionItem.Wire(wire.WireId) : default;
            return wire != null;
        }

        /// <summary>The components and wires that overlap the rectangle between two corners.</summary>
        public static List<SelectionItem> ElementsIn(Vector2 a, Vector2 b)
        {
            var results = new List<SelectionItem>();
            if (CircuitView.Instance != null)
                CircuitView.Instance.ElementsIn(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)), results);
            return results;
        }
    }
}
