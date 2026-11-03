using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>A connection point of a component view. Prefab pins only have a collider; the dot is created on demand.</summary>
    public abstract class PinView : MonoBehaviour
    {
        public const float DotSize = 0.55f;

        public ComponentView Owner { get; private set; }
        public int Index { get; private set; }
        public abstract bool IsOutput { get; }

        public Simulation.PinRef PinRef => new Simulation.PinRef(Owner.ComponentId, Index);

        private SpriteRenderer _dot;
        private bool _alwaysShowDot;
        private bool _highlighted;
        private bool? _value;

        public void Bind(ComponentView owner, int index, bool alwaysShowDot)
        {
            Owner = owner;
            Index = index;
            _alwaysShowDot = alwaysShowDot;
            if (GetComponent<Collider2D>() == null)
            {
                var box = gameObject.AddComponent<BoxCollider2D>();
                box.size = new Vector2(0.9f, 0.9f);
                box.isTrigger = true;
            }
            Refresh();
        }

        public void SetHighlighted(bool highlighted)
        {
            if (_highlighted == highlighted)
                return;
            _highlighted = highlighted;
            Refresh();
        }

        /// <summary>Signal value to show, or null outside the simulation.</summary>
        public void SetValue(bool? value)
        {
            if (_value == value)
                return;
            _value = value;
            Refresh();
        }

        private void Refresh()
        {
            bool visible = _alwaysShowDot || _highlighted;
            if (!visible)
            {
                if (_dot != null)
                    _dot.enabled = false;
                return;
            }
            if (_dot == null)
                _dot = CreateDot();
            _dot.enabled = true;
            if (_highlighted)
                _dot.color = VisualStyle.PinHighlight;
            else if (_value.HasValue)
                _dot.color = VisualStyle.SignalColor(_value.Value);
            else
                _dot.color = VisualStyle.PinIdle;
            _dot.transform.localScale = Vector3.one * (_highlighted ? DotSize * 1.6f : DotSize);
        }

        private SpriteRenderer CreateDot()
        {
            var go = new GameObject("Dot");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 0, -0.1f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = VisualStyle.Dot;
            renderer.sortingOrder = VisualStyle.PinSortingOrder;
            return renderer;
        }
    }
}
