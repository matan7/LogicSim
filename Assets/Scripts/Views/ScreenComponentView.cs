using System.Collections.Generic;
using LogicSim.Simulation;
using TMPro;
using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>
    /// View of a <see cref="ComponentKind.Screen"/>: a box with the inputs on the left (x address, y address, D, WR, CLR,
    /// named) and the pixels to their right, each <see cref="ScreenLayout.PixelSize"/> grid units wide. The pixels are a
    /// texture with point filtering, updated from the simulation when they change.
    /// </summary>
    public sealed class ScreenComponentView : ComponentView
    {
        /// <summary>Room for the pin names between the input column and the pixels, in grid units.</summary>
        public const float PinMargin = 4f;

        private int _width;
        private int _height;
        private int _pixelSize;
        private SpriteRenderer _body;
        private SpriteRenderer _screen;
        private Texture2D _texture;
        private Sprite _sprite;
        private Color32[] _colors;
        private byte[] _pixels;
        private byte[] _shown;
        private bool _lit;
        private TextMeshPro _caption;
        private readonly List<PinView> _inputPins = new List<PinView>();

        public override ComponentKind Kind => ComponentKind.Screen;

        protected override bool ShowPinDots => true;

        protected override bool DrawsOwnCaption => true;

        /// <summary>Footprint in grid units: the pins need two units each, the pixels their size, plus a border.</summary>
        public static Vector2Int BodySize(CircuitComponent component, Circuit circuit)
        {
            int size = ScreenLayout.PixelSize(component);
            int width = (int)PinMargin + ScreenLayout.Width(component) * size + 1;
            int height = Mathf.Max(2 * circuit.GetInputCount(component) + 2, ScreenLayout.Height(component) * size + 2);
            return new Vector2Int(width, height);
        }

        protected override void BuildVisuals(CircuitComponent component, Circuit circuit)
        {
            _width = ScreenLayout.Width(component);
            _height = ScreenLayout.Height(component);
            _pixelSize = ScreenLayout.PixelSize(component);
            var size = BodySize(component, circuit);

            _body = gameObject.AddComponent<SpriteRenderer>();
            _body.sprite = VisualStyle.Box;
            _body.drawMode = SpriteDrawMode.Sliced;
            _body.size = size;
            _body.sortingOrder = VisualStyle.BodySortingOrder;
            LocalBody = new Rect(0, -size.y, size.x, size.y);

            _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false)
            {
                name = "Screen pixels",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _colors = new Color32[_width * _height];
            _pixels = new byte[_width * _height];
            _shown = new byte[_width * _height];
            _sprite = Sprite.Create(_texture, new Rect(0, 0, _width, _height), new Vector2(0, 1), 1f / _pixelSize, 0, SpriteMeshType.FullRect);
            var screen = new GameObject("Pixels");
            screen.transform.SetParent(transform, false);
            // One unit below the top edge, in front of the body (same sorting order, nearer to the camera).
            screen.transform.localPosition = new Vector3(PinMargin, -1, -0.02f);
            _screen = screen.AddComponent<SpriteRenderer>();
            _screen.sprite = _sprite;
            _screen.sortingOrder = VisualStyle.BodySortingOrder;
            Paint(lit: false);

            int inputs = circuit.GetInputCount(component);
            _inputPins.Clear();
            for (int i = 0; i < inputs; i++)
            {
                var go = new GameObject("In" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0, -2 - 2 * i, 0);
                _inputPins.Add(go.AddComponent<InputPin>());
                var text = GenericComponentView.CreateText(transform, "Pin " + i, new Vector2(1.6f, -2 - 2 * i), new Vector2(2.6f, 1.1f), 10f);
                text.alignment = TextAlignmentOptions.Left;
                GenericComponentView.SetFittedText(text, circuit.GetInputName(component, i));
            }

            _caption = GenericComponentView.CreateText(transform, "Caption", new Vector2(size.x / 2f, 0.9f), new Vector2(size.x + 4, 1.4f), 14f);
            _caption.color = new Color(1, 1, 1, 0.7f);
        }

        protected override void CollectPins(List<PinView> inputs, List<PinView> outputs)
        {
            inputs.AddRange(_inputPins);
        }

        public override bool CanDisplay(CircuitComponent component, Circuit circuit)
        {
            return base.CanDisplay(component, circuit)
                && ScreenLayout.Width(component) == _width
                && ScreenLayout.Height(component) == _height
                && ScreenLayout.PixelSize(component) == _pixelSize;
        }

        public override void ApplyTransform()
        {
            base.ApplyTransform();
            // Text stays upright when the component is rotated.
            foreach (var text in GetComponentsInChildren<TextMeshPro>())
                text.transform.rotation = Quaternion.identity;
        }

        public override void OnPropertiesChanged()
        {
            _caption.gameObject.SetActive(!string.IsNullOrEmpty(Component.Label));
            GenericComponentView.SetFittedText(_caption, Component.Label ?? string.Empty);
        }

        public override void ShowValues(ISignalReader session)
        {
            base.ShowValues(session);
            // Outside the simulation (or without this screen in the netlist) the screen is dark.
            if (session == null || !session.TryGetScreen(Component, _pixels))
            {
                if (_lit)
                    Paint(lit: false);
                return;
            }
            if (_lit && SamePixels())
                return;
            System.Buffer.BlockCopy(_pixels, 0, _shown, 0, _pixels.Length);
            Paint(lit: true);
        }

        private bool SamePixels()
        {
            for (int i = 0; i < _pixels.Length; i++)
                if (_pixels[i] != _shown[i])
                    return false;
            return true;
        }

        /// <summary>Uploads the shown pixels (row 0 is the top row; the texture's row 0 is the bottom one).</summary>
        private void Paint(bool lit)
        {
            _lit = lit;
            Color32 on = VisualStyle.HighColor;
            Color32 off = Color.Lerp(VisualStyle.BodyFill, VisualStyle.LowColor, 0.35f);
            for (int y = 0; y < _height; y++)
            {
                int row = (_height - 1 - y) * _width;
                for (int x = 0; x < _width; x++)
                    _colors[row + x] = lit && _shown[y * _width + x] != 0 ? on : off;
            }
            _texture.SetPixels32(_colors);
            _texture.Apply(false);
        }

        private void OnDestroy()
        {
            if (_sprite != null)
                Destroy(_sprite);
            if (_texture != null)
                Destroy(_texture);
        }
    }
}
