using System.Collections.Generic;
using Core.Selecting;
using LogicSim.Simulation;
using TMPro;
using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>
    /// View of a component whose body the <see cref="SymbolRenderer"/> draws from the symbol atlas. It has no renderer
    /// of its own: only a collider for picking and dragging, the pins, and text where the symbol needs it.
    /// </summary>
    public sealed class SymbolComponentView : ComponentView, ISelectable
    {
        private sealed class Layout
        {
            public string Symbol;
            /// <summary>Unit rows of the pins below the top edge; inputs sit at x = 0, outputs at the right edge.</summary>
            public int[] InputRows;
            public int[] OutputRows;
        }

        private static readonly int[] None = new int[0];
        private static readonly int[] Middle = { 2 };

        private static readonly Dictionary<ComponentKind, Layout> Layouts = new Dictionary<ComponentKind, Layout>
        {
            { ComponentKind.And, Gate("gate.and") },
            { ComponentKind.Or, Gate("gate.or") },
            { ComponentKind.Xor, Gate("gate.xor") },
            { ComponentKind.Nand, Gate("gate.nand") },
            { ComponentKind.Nor, Gate("gate.nor") },
            { ComponentKind.Xnor, Gate("gate.xnor") },
            { ComponentKind.Not, new Layout { Symbol = "gate.not", InputRows = Middle, OutputRows = Middle } },
            { ComponentKind.Buffer, new Layout { Symbol = "gate.buf", InputRows = Middle, OutputRows = Middle } },
            { ComponentKind.Switch, new Layout { Symbol = "io.switch", InputRows = None, OutputRows = Middle } },
            { ComponentKind.Bulb, new Layout { Symbol = "io.bulb", InputRows = Middle, OutputRows = None } },
            { ComponentKind.Button, new Layout { Symbol = "io.button", InputRows = None, OutputRows = Middle } },
            { ComponentKind.Clock, new Layout { Symbol = "io.clock", InputRows = None, OutputRows = Middle } },
            { ComponentKind.Constant, new Layout { Symbol = "io.constant", InputRows = None, OutputRows = Middle } },
        };

        private static Layout Gate(string symbol) => new Layout { Symbol = symbol, InputRows = new[] { 1, 3 }, OutputRows = Middle };

        private ComponentKind _kind;
        private Layout _layout;
        private int _symbol = -1;
        private Vector2 _size;
        private uint _flags;
        private bool _selected;
        private readonly int[] _nets = new int[SymbolRenderer.NetSlots];
        private TextMeshPro _glyph;
        private readonly List<PinView> _inputPins = new List<PinView>();
        private readonly List<PinView> _outputPins = new List<PinView>();

        /// <summary>Set by <see cref="CircuitView"/> before <see cref="ComponentView.Bind"/>.</summary>
        internal SymbolRenderer Renderer { get; set; }

        /// <summary>Instance slot in the renderer, -1 while not drawn. The renderer updates it when slots move.</summary>
        internal int Slot { get; set; } = -1;

        public override ComponentKind Kind => _kind;

        public Transform Transform => transform;

        /// <summary>True when the renderer has a symbol for the component with its current pin counts.</summary>
        public static bool CanShow(CircuitComponent component, Circuit circuit, SymbolRenderer renderer)
        {
            return renderer != null
                && Layouts.TryGetValue(component.Kind, out var layout)
                && circuit.GetInputCount(component) == layout.InputRows.Length
                && circuit.GetOutputCount(component) == layout.OutputRows.Length
                && renderer.IndexOf(layout.Symbol) >= 0;
        }

        protected override void BuildVisuals(CircuitComponent component, Circuit circuit)
        {
            _kind = component.Kind;
            _layout = Layouts[_kind];
            _symbol = Renderer.IndexOf(_layout.Symbol);
            _size = Renderer.Set.Symbols[_symbol].Size;

            var collider = gameObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = _size;
            collider.offset = new Vector2(_size.x / 2, -_size.y / 2);

            uint inputRows = 0, outputRows = 0;
            CreatePins<InputPin>(_inputPins, _layout.InputRows, 0, ref inputRows);
            CreatePins<OutputPin>(_outputPins, _layout.OutputRows, _size.x, ref outputRows);
            _flags = (inputRows << SymbolRenderer.InputRowsShift) | (outputRows << SymbolRenderer.OutputRowsShift);

            if (_kind == ComponentKind.Constant)
            {
                var go = new GameObject("Value");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(_size.x / 2, -_size.y / 2, -0.05f);
                _glyph = go.AddComponent<TextMeshPro>();
                _glyph.rectTransform.sizeDelta = new Vector2(_size.x / 2, _size.y / 2);
                _glyph.alignment = TextAlignmentOptions.Center;
                _glyph.fontSizeMin = 1f;
                _glyph.fontSizeMax = 30f;
                _glyph.textWrappingMode = TextWrappingModes.NoWrap;
                _glyph.sortingOrder = VisualStyle.LabelSortingOrder;
                _glyph.raycastTarget = false;
            }

            AddInstance();
        }

        private void CreatePins<T>(List<PinView> list, int[] rows, float x, ref uint mask) where T : PinView
        {
            list.Clear();
            for (int i = 0; i < rows.Length; i++)
            {
                var go = new GameObject((typeof(T) == typeof(OutputPin) ? "Out" : "In") + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(x, -rows[i], 0);
                list.Add(go.AddComponent<T>());
                mask |= 1u << rows[i];
            }
        }

        protected override void CollectPins(List<PinView> inputs, List<PinView> outputs)
        {
            inputs.AddRange(_inputPins);
            outputs.AddRange(_outputPins);
        }

        private void AddInstance()
        {
            if (Slot >= 0 || Renderer == null || _symbol < 0)
                return;
            Slot = Renderer.Add(this, _symbol, _flags | (_selected ? SymbolRenderer.SelectedFlag : 0));
            SyncTransform();
            Renderer.SetNets(Slot, _nets);
            UpdateLit();
        }

        private void OnEnable()
        {
            // Views are created enabled; this only matters if one is disabled and enabled again.
            if (Component != null)
                AddInstance();
        }

        // CircuitView disables views before destroying them, so they disappear in the same frame.
        private void OnDisable()
        {
            if (Slot < 0 || Renderer == null)
                return;
            Renderer.Remove(Slot);
            Slot = -1;
        }

        /// <summary>Sends the transform to the renderer: after model moves and while the view is dragged.</summary>
        internal void SyncTransform()
        {
            if (Slot >= 0 && Component != null)
                Renderer.SetTransform(Slot, transform.position, Component.Rotation);
        }

        public override void ApplyTransform()
        {
            base.ApplyTransform();
            SyncTransform();
            if (_glyph != null)
                _glyph.transform.rotation = Quaternion.identity;
        }

        protected override Bounds GetBodyBounds()
        {
            var bounds = new Bounds(transform.TransformPoint(Vector3.zero), Vector3.zero);
            bounds.Encapsulate(transform.TransformPoint(new Vector3(_size.x, -_size.y, 0)));
            return bounds;
        }

        public override void OnPropertiesChanged()
        {
            if (_glyph != null)
            {
                GenericComponentView.SetFittedText(_glyph, Component.Value ? "1" : "0");
                // Dark on the lit fill, light on the dark one.
                _glyph.color = Component.Value ? VisualStyle.BodyFill : VisualStyle.LabelColor;
            }
            UpdateLit();
        }

        /// <summary>Values reach the renderer through the nets (<see cref="ResolveNets"/>), not per frame.</summary>
        public override void ShowValues(ISignalReader session)
        {
        }

        /// <summary>
        /// Looks up the nets of the pins and of the fill in <paramref name="signals"/> (null: none), after a recompile or
        /// a level change. The renderer reads their values on the GPU.
        /// </summary>
        public void ResolveNets(ISignalReader signals)
        {
            for (int i = 0; i < _nets.Length; i++)
                _nets[i] = -1;
            if (signals != null && Component != null)
            {
                for (int i = 0; i < _layout.InputRows.Length; i++)
                    _nets[_layout.InputRows[i]] = signals.GetInputNet(ComponentId, i);
                for (int i = 0; i < _layout.OutputRows.Length; i++)
                    _nets[SymbolRenderer.OutputNetSlot + _layout.OutputRows[i]] = signals.GetOutputNet(ComponentId, i);
                switch (_kind)
                {
                    // Inputs are lit by their own state (UpdateLit), the bulb by its input, the rest by their output.
                    case ComponentKind.Switch:
                    case ComponentKind.Button:
                    case ComponentKind.Constant:
                        break;
                    case ComponentKind.Bulb:
                        _nets[SymbolRenderer.FillNetSlot] = signals.GetInputNet(ComponentId, 0);
                        break;
                    default:
                        _nets[SymbolRenderer.FillNetSlot] = signals.GetOutputNet(ComponentId, 0);
                        break;
                }
            }
            if (Slot >= 0)
                Renderer.SetNets(Slot, _nets);
        }

        // Switches, buttons and constants show the state set in the model, also outside the simulation.
        private void UpdateLit()
        {
            if (Slot < 0 || Component == null)
                return;
            bool own = _kind == ComponentKind.Switch || _kind == ComponentKind.Button || _kind == ComponentKind.Constant;
            Renderer.SetLit(Slot, own && Component.Value);
        }

        public void Select() => SetSelected(true);

        public void Deselect() => SetSelected(false);

        private void SetSelected(bool selected)
        {
            _selected = selected;
            if (Slot >= 0)
                Renderer.SetSelected(Slot, selected);
        }
    }
}
