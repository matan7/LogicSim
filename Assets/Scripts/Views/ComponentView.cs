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

        /// <summary>World-space bounds of the body (for framing and selection).</summary>
        public Bounds GetBounds()
        {
            var collider = GetComponent<Collider2D>();
            if (collider != null)
                return collider.bounds;
            var renderer = GetComponent<Renderer>();
            return renderer != null ? renderer.bounds : new Bounds(transform.position, Vector3.one);
        }

        private void EnsureInteraction()
        {
            if (GetComponent<Collider2D>() == null)
            {
                var box = gameObject.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                var spriteRenderer = GetComponent<SpriteRenderer>();
                if (spriteRenderer != null)
                {
                    var bounds = spriteRenderer.sprite != null ? spriteRenderer.sprite.bounds : spriteRenderer.bounds;
                    box.size = bounds.size;
                    box.offset = bounds.center;
                }
            }
            if (GetComponent<Draggable>() == null)
                gameObject.AddComponent<Draggable>();
            if (GetComponent<SelectableSprite>() == null && GetComponent<SpriteRenderer>() != null)
                gameObject.AddComponent<SelectableSprite>();
        }
    }
}
