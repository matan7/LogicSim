using System.Collections.Generic;
using LogicSim.Simulation;
using TMPro;
using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>
    /// Box view generated at runtime for components without a hand-drawn prefab: clocks, buttons, constants,
    /// displays, flip-flops, subcircuit pins and instances, and gates with more than two inputs.
    /// Pins sit on the grid (2 units apart) so wires stay orthogonal.
    /// </summary>
    public class GenericComponentView : ComponentView
    {
        public const float Width = 6f;
        public const float NarrowWidth = 4f;

        private ComponentKind _kind;
        private SpriteRenderer _body;
        private TextMeshPro _label;
        private TextMeshPro _caption;
        private readonly List<PinView> _inputPins = new List<PinView>();
        private readonly List<PinView> _outputPins = new List<PinView>();
        private bool? _shownValue;

        public override ComponentKind Kind => _kind;

        protected override bool ShowPinDots => true;

        public static float WidthFor(ComponentKind kind)
        {
            switch (kind)
            {
                case ComponentKind.Clock:
                case ComponentKind.Button:
                case ComponentKind.Constant:
                case ComponentKind.PinIn:
                case ComponentKind.PinOut:
                    return NarrowWidth;
                default:
                    return Width;
            }
        }

        protected override void BuildVisuals(CircuitComponent component, Circuit circuit)
        {
            _kind = component.Kind;
            int inputs = circuit.GetInputCount(component);
            int outputs = circuit.GetOutputCount(component);
            int rows = Mathf.Max(1, Mathf.Max(inputs, outputs));
            float width = WidthFor(component.Kind);
            float height = 2 * rows + 2;

            _body = gameObject.AddComponent<SpriteRenderer>();
            _body.sprite = VisualStyle.Box;
            _body.drawMode = SpriteDrawMode.Sliced;
            _body.size = new Vector2(width, height);
            _body.sortingOrder = VisualStyle.BodySortingOrder;

            var collider = gameObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(width, height);
            collider.offset = new Vector2(width / 2, -height / 2);

            // Auto-sizing keeps every text inside its rect (world units), whatever the font scale.
            _label = CreateText("Label", new Vector2(width / 2, -height / 2), new Vector2(width - 1.2f, Mathf.Min(3f, height - 1f)), 30f);
            _caption = CreateText("Caption", new Vector2(width / 2, 0.9f), new Vector2(width + 4, 1.4f), 14f);
            _caption.color = new Color(1, 1, 1, 0.7f);

            CreatePins<InputPin>(_inputPins, inputs, rows, 0, circuit, component, false);
            CreatePins<OutputPin>(_outputPins, outputs, rows, width, circuit, component, true);
        }

        private void CreatePins<T>(List<PinView> list, int count, int rows, float x, Circuit circuit, CircuitComponent component, bool output) where T : PinView
        {
            list.Clear();
            int offset = rows - count;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject(output ? "Out" + i : "In" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(x, -2 - 2 * i - offset, 0);
                list.Add(go.AddComponent<T>());

                var name = output ? circuit.GetOutputName(component, i) : circuit.GetInputName(component, i);
                if (ShowPinName(component, name, count))
                {
                    var text = CreateText("Pin " + name, new Vector2(output ? x - 1.3f : x + 1.3f, go.transform.localPosition.y), new Vector2(2.2f, 1.1f), 10f);
                    text.alignment = output ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
                }
            }
        }

        private static bool ShowPinName(CircuitComponent component, string name, int count)
        {
            // Generic gate inputs (in0, in1...) and single pins are self-explanatory.
            if (count <= 1 && component.Kind != ComponentKind.Subcircuit)
                return false;
            return !(name.StartsWith("in") && component.Spec.Category == ComponentCategory.Gate);
        }

        private TextMeshPro CreateText(string objectName, Vector2 position, Vector2 size, float maxFontSize)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(position.x, position.y, -0.05f);
            var text = go.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = size;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 1f;
            text.fontSizeMax = maxFontSize;
            text.color = VisualStyle.LabelColor;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.sortingOrder = VisualStyle.LabelSortingOrder;
            text.raycastTarget = false;
            return text;
        }

        protected override void CollectPins(List<PinView> inputs, List<PinView> outputs)
        {
            inputs.AddRange(_inputPins);
            outputs.AddRange(_outputPins);
        }

        public override void ApplyTransform()
        {
            base.ApplyTransform();
            // Keep text upright when the component is rotated.
            foreach (var text in GetComponentsInChildren<TextMeshPro>())
                text.transform.rotation = Quaternion.identity;
        }

        public override void OnPropertiesChanged()
        {
            _label.text = BodyText(null);
            _caption.text = Component.Label ?? string.Empty;
            _caption.gameObject.SetActive(!string.IsNullOrEmpty(Component.Label) && Component.Kind != ComponentKind.PinIn && Component.Kind != ComponentKind.PinOut);
        }

        private string BodyText(ISignalReader session)
        {
            var c = Component;
            switch (c.Kind)
            {
                // ASCII only: the default TMP font atlas has no logic symbols.
                case ComponentKind.And: return "AND";
                case ComponentKind.Or: return "OR";
                case ComponentKind.Nand: return "NAND";
                case ComponentKind.Nor: return "NOR";
                case ComponentKind.Xor: return "XOR";
                case ComponentKind.Xnor: return "XNOR";
                case ComponentKind.Not: return "NOT";
                case ComponentKind.Buffer: return "BUF";
                case ComponentKind.Clock: return "CLK";
                case ComponentKind.Button: return "BTN";
                case ComponentKind.Constant: return c.Value ? "1" : "0";
                case ComponentKind.HexDisplay:
                    return session == null ? "-" : session.GetInputBits(c).ToString("X");
                case ComponentKind.DFlipFlop: return "D";
                case ComponentKind.TFlipFlop: return "T";
                case ComponentKind.JKFlipFlop: return "JK";
                case ComponentKind.PinIn:
                case ComponentKind.PinOut:
                    var name = string.IsNullOrEmpty(c.Label) ? (c.Kind == ComponentKind.PinIn ? "IN" : "OUT") : c.Label;
                    if (session != null)
                        name += c.Kind == ComponentKind.PinIn ? (session.GetOutputValue(c.Id, 0) ? " 1" : " 0") : (session.GetInputPinValue(c.Id, 0) ? " 1" : " 0");
                    return name;
                case ComponentKind.Subcircuit:
                    return Circuit.Library.TryGet(c.SubcircuitId, out var definition) ? definition.Name : "? " + c.SubcircuitId;
                default:
                    return c.Spec.DisplayName;
            }
        }

        public override void ShowValues(ISignalReader session)
        {
            base.ShowValues(session);
            var kind = Component.Kind;
            if (kind == ComponentKind.HexDisplay || kind == ComponentKind.PinIn || kind == ComponentKind.PinOut)
                _label.text = BodyText(session);

            bool? value = null;
            if (session != null)
            {
                if (kind == ComponentKind.Clock || kind == ComponentKind.Button || kind == ComponentKind.PinIn)
                    value = session.GetOutputValue(ComponentId, 0);
                else if (kind == ComponentKind.PinOut)
                    value = session.GetInputPinValue(ComponentId, 0);
            }
            if (value == _shownValue)
                return;
            _shownValue = value;
            _label.color = value.HasValue ? VisualStyle.SignalColor(value.Value) : VisualStyle.LabelColor;
        }
    }
}
