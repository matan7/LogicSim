using System.Collections.Generic;
using Behaviours;
using LogicSim.Simulation;
using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>
    /// Scene representation of a <see cref="CircuitComponent"/>. Views never change the model;
    /// they are created, moved and destroyed by <see cref="CircuitView"/> in response to model events.
    /// </summary>
    public abstract class ComponentView : MonoBehaviour
    {
        private readonly List<PinView> _inputs = new List<PinView>();
        private readonly List<PinView> _outputs = new List<PinView>();

        public int ComponentId { get; private set; }
        public CircuitComponent Component { get; private set; }
        protected Circuit Circuit { get; private set; }

        public IReadOnlyList<PinView> Inputs => _inputs;
        public IReadOnlyList<PinView> Outputs => _outputs;

        // The body in local coordinates (origin at the component's position, y up), for picking, framing and routing.
        private Rect _localBody = new Rect(-0.5f, -0.5f, 1f, 1f);
        private bool _hasLocalBody;

        /// <summary>Generated views set their body; prefab views take it from their collider or sprite.</summary>
        protected Rect LocalBody
        {
            get => _localBody;
            set
            {
                _localBody = value;
                _hasLocalBody = true;
            }
        }

        /// <summary>Kind shown by this view (prefab views are fixed to one kind).</summary>
        public abstract ComponentKind Kind { get; }

        /// <summary>Number of inputs the view was built for; a different count needs a new view.</summary>
        public int BuiltInputCount { get; private set; }
        public int BuiltOutputCount { get; private set; }

        public void Bind(CircuitComponent component, Circuit circuit)
        {
            Component = component;
            ComponentId = component.Id;
            Circuit = circuit;
            name = string.IsNullOrEmpty(component.Label) ? $"{component.Kind} #{component.Id}" : $"{component.Kind} '{component.Label}' #{component.Id}";

            BuildVisuals(component, circuit);
            _inputs.Clear();
            _outputs.Clear();
            CollectPins(_inputs, _outputs);
            for (int i = 0; i < _inputs.Count; i++)
                _inputs[i].Bind(this, i, ShowPinDots);
            for (int i = 0; i < _outputs.Count; i++)
                _outputs[i].Bind(this, i, ShowPinDots);
            BuiltInputCount = circuit.GetInputCount(component);
            BuiltOutputCount = circuit.GetOutputCount(component);

            EnsureInteraction();
            ApplyTransform();
            OnPropertiesChanged();
        }

        /// <summary>Creates renderers/colliders (generated views) or validates prefab parts.</summary>
        protected virtual void BuildVisuals(CircuitComponent component, Circuit circuit)
        {
        }

        protected abstract void CollectPins(List<PinView> inputs, List<PinView> outputs);

        /// <summary>Prefab pins are drawn into the sprite; generated views show pin dots.</summary>
        protected virtual bool ShowPinDots => false;

        /// <summary>True when the view can display the component without being rebuilt.</summary>
        public virtual bool CanDisplay(CircuitComponent component, Circuit circuit)
        {
            return component.Kind == Kind
                && circuit.GetInputCount(component) == BuiltInputCount
                && circuit.GetOutputCount(component) == BuiltOutputCount;
        }

        public virtual void ApplyTransform()
        {
            transform.position = new Vector3(Component.X, Component.Y, 0);
            transform.rotation = Quaternion.Euler(0, 0, -90 * Component.Rotation);
            RefreshCaption();
        }

        /// <summary>Generated views draw their own caption; prefab views get one above the body.</summary>
        protected virtual bool DrawsOwnCaption => false;

        private TMPro.TextMeshPro _caption;

        // Called from ApplyTransform, which also runs after every property change (label edits).
        private void RefreshCaption()
        {
            if (DrawsOwnCaption || Component == null)
                return;
            if (string.IsNullOrEmpty(Component.Label))
            {
                if (_caption != null)
                    _caption.gameObject.SetActive(false);
                return;
            }
            if (_caption == null)
            {
                var go = new GameObject("Caption");
                go.transform.SetParent(transform, false);
                _caption = go.AddComponent<TMPro.TextMeshPro>();
                _caption.alignment = TMPro.TextAlignmentOptions.Center;
                _caption.enableAutoSizing = true;
                _caption.fontSizeMin = 1f;
                _caption.fontSizeMax = 14f;
                _caption.color = new Color(VisualStyle.LabelColor.r, VisualStyle.LabelColor.g, VisualStyle.LabelColor.b, 0.7f);
                _caption.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                _caption.sortingOrder = VisualStyle.LabelSortingOrder;
                _caption.raycastTarget = false;
            }
            _caption.gameObject.SetActive(true);
            _caption.text = Component.Label;
            // Upright above the body whatever the rotation.
            var body = GetBodyBounds();
            _caption.rectTransform.sizeDelta = new Vector2(body.size.x + 4, 1.4f);
            _caption.transform.SetPositionAndRotation(new Vector3(body.center.x, body.max.y + 0.9f, -0.05f), Quaternion.identity);
        }

        /// <summary>World-space bounds of the drawn body, valid right after a transform change (for the caption).</summary>
        protected virtual Bounds GetBodyBounds()
        {
            var renderer = GetComponent<Renderer>();
            return renderer != null ? renderer.bounds : new Bounds(transform.position, Vector3.one);
        }

        /// <summary>Label, value or other properties changed.</summary>
        public virtual void OnPropertiesChanged()
        {
        }

        /// <summary>Shows simulation values; <paramref name="session"/> is null outside the simulation.</summary>
        public virtual void ShowValues(ISignalReader session)
        {
            for (int i = 0; i < _inputs.Count; i++)
                _inputs[i].SetValue(session == null ? (bool?)null : session.GetInputPinValue(ComponentId, i));
            for (int i = 0; i < _outputs.Count; i++)
                _outputs[i].SetValue(session == null ? (bool?)null : session.GetOutputValue(ComponentId, i));
        }

        /// <summary>World rectangle of the body, valid right after a move. Picking, framing and wire routing use it.</summary>
        public Rect GetFootprint()
        {
            Vector3 a = transform.TransformPoint(_localBody.min), c = transform.TransformPoint(_localBody.max);
            return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
        }

        /// <summary>World-space bounds of the body (for framing).</summary>
        public Bounds GetBounds()
        {
            var r = GetFootprint();
            return new Bounds(new Vector3(r.center.x, r.center.y, 0), new Vector3(r.width, r.height, 0));
        }

        private void EnsureInteraction()
        {
            // Picking uses CircuitView's spatial grid. Prefabs still carry colliders: their box is the body, then they go.
            if (!_hasLocalBody)
            {
                var spriteRenderer = GetComponent<SpriteRenderer>();
                if (GetComponent<Collider2D>() is BoxCollider2D box)
                    LocalBody = new Rect(box.offset - box.size / 2, box.size);
                else if (spriteRenderer != null)
                {
                    var bounds = spriteRenderer.sprite != null ? spriteRenderer.sprite.bounds : spriteRenderer.bounds;
                    LocalBody = new Rect(bounds.min, bounds.size);
                }
            }
            foreach (var collider in GetComponentsInChildren<Collider2D>(true))
            {
                if (Application.isPlaying)
                    Destroy(collider);
                else
                    DestroyImmediate(collider);
            }
            if (GetComponent<Draggable>() == null)
                gameObject.AddComponent<Draggable>();
            if (GetComponent<SelectableSprite>() == null && GetComponent<SpriteRenderer>() != null)
                gameObject.AddComponent<SelectableSprite>();
        }
    }
}
