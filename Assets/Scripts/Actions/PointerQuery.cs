using Core.Selecting;
using LogicSim.Views;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Actions
{
    /// <summary>What lies under the pointer, with pins taking priority over component bodies and wires.</summary>
    public static class PointerQuery
    {
        public static bool IsOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        public static PinView PinAt(Vector2 world)
        {
            PinView best = null;
            float bestDistance = float.MaxValue;
            foreach (var hit in Physics2D.OverlapPointAll(world))
            {
                var pin = hit.GetComponent<PinView>();
                if (pin == null || pin.Owner == null)
                    continue;
                float distance = ((Vector2)pin.transform.position - world).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = pin;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public static ComponentView ComponentAt(Vector2 world)
        {
            foreach (var hit in Physics2D.OverlapPointAll(world))
            {
                var view = hit.GetComponent<ComponentView>();
                if (view != null)
                    return view;
            }
            return null;
        }

        public static WireView WireAt(Vector2 world)
        {
            foreach (var hit in Physics2D.OverlapPointAll(world))
            {
                var wire = hit.GetComponent<WireView>();
                if (wire != null)
                    return wire;
            }
            return null;
        }

        /// <summary>Component (preferred) or wire that can be selected at the point.</summary>
        public static ISelectable SelectableAt(Vector2 world)
        {
            var component = ComponentAt(world);
            if (component != null && component.TryGetComponent<ISelectable>(out var selectable))
                return selectable;
            return WireAt(world);
        }
    }
}
