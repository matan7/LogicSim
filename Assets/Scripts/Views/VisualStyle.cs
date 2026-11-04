using Core.Settings;
using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>Colors, sorting orders and procedurally generated sprites shared by all views.</summary>
    public static class VisualStyle
    {
        public const int WireSortingOrder = -1;
        public const int BodySortingOrder = 0;
        public const int LabelSortingOrder = 1;
        public const int PinSortingOrder = 2;
        public const int PreviewSortingOrder = 5;

        public static readonly Color BodyFill = new Color(0.10f, 0.14f, 0.20f, 1f);
        public static readonly Color Outline = new Color(0.85f, 0.88f, 0.92f, 1f);
        public static readonly Color LabelColor = new Color(0.92f, 0.94f, 0.97f, 1f);
        public static readonly Color PinIdle = new Color(0.85f, 0.88f, 0.92f, 1f);
        public static readonly Color PinHighlight = new Color(1f, 0.82f, 0.25f, 1f);

        public static Color WireColor => Settings?.WireColor ?? Outline;
        public static Color HighColor => Settings?.SignalHighColor ?? new Color(0.35f, 1f, 0.45f, 1f);
        public static Color LowColor => Settings?.SignalLowColor ?? new Color(0.18f, 0.32f, 0.22f, 1f);
        public static Color SelectionColor => Settings?.SelectionColor ?? new Color(0.35f, 0.65f, 1f, 1f);

        private static SettingsData Settings => SettingsService.Settings;

        public static Color SignalColor(bool value) => value ? HighColor : LowColor;

        private static Sprite _box;
        private static Sprite _dot;
        private static Material _lineMaterial;

        /// <summary>9-sliced box with a 1 px outline, pivot top-left, 16 pixels per unit.</summary>
        public static Sprite Box
        {
            get
            {
                if (_box != null)
                    return _box;
                const int size = 16;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[size * size];
                Color32 fill = BodyFill;
                Color32 line = Outline;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool edge = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                    pixels[y * size + x] = edge ? line : fill;
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                _box = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0, 1), 16f, 0, SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
                _box.name = "LogicSim Box";
                return _box;
            }
        }

        /// <summary>Filled circle, 1 unit in diameter, centered pivot.</summary>
        public static Sprite Dot
        {
            get
            {
                if (_dot != null)
                    return _dot;
                const int size = 32;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[size * size];
                float radius = size / 2f - 1;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size / 2f;
                    float dy = y + 0.5f - size / 2f;
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    pixels[y * size + x] = new Color(1, 1, 1, alpha);
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                _dot = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                _dot.name = "LogicSim Dot";
                return _dot;
            }
        }

        /// <summary>Unlit vertex-colored material for line renderers.</summary>
        public static Material LineMaterial
        {
            get
            {
                if (_lineMaterial != null)
                    return _lineMaterial;
                var shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _lineMaterial = new Material(shader) { name = "LogicSim Wire" };
                }
                else
                {
                    // The sprite shader is always in builds through the sprites' default material.
                    var sprite = Object.FindFirstObjectByType<SpriteRenderer>();
                    _lineMaterial = sprite != null ? new Material(sprite.sharedMaterial) : null;
                }
                return _lineMaterial;
            }
        }
    }
}
