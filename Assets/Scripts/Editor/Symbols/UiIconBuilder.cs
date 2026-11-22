using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LogicSim.EditorTools.Symbols
{
    /// <summary>
    /// Builds the UI icon sprites (white on transparent, 128 px): every artboard of Art/UI/Icons.psd, and palette icons
    /// for the components with a symbol, drawn from the outline and fill of Art/Symbols/Symbols.psd so that the palette
    /// matches the canvas (as wide as the icons, as high as the symbol's aspect needs). Edges are antialiased from
    /// distance fields, like the symbol atlas.
    /// </summary>
    public static class UiIconBuilder
    {
        public const string IconSourcePath = "Art/UI/Icons.psd";
        public const string OutputFolder = "Assets/Art/UI/Icons";
        public const int Size = 128;
        /// <summary>Prefix of the palette icons made from symbols: "comp.gate.and" for "gate.and".</summary>
        public const string SymbolIconPrefix = "comp.";

        /// <summary>Stroke width of the icons at <see cref="Size"/> (14 px at 256 in Icons.psd).</summary>
        private const float StrokeWidth = 7f;
        private const float SymbolStrokeWidth = 16f;   // in Symbols.psd
        private const float SymbolMargin = 4f;
        private const float FillAlpha = 0.22f;

        /// <summary>Symbols that get a palette icon.</summary>
        public static readonly string[] SymbolIcons =
        {
            "gate.and", "gate.or", "gate.xor", "gate.nand", "gate.nor", "gate.xnor", "gate.not", "gate.buf",
            "io.switch", "io.bulb", "io.button", "io.clock", "io.constant", "icon.neuron", "icon.lif", "icon.network",
        };

        [MenuItem("Tools/LogicSim/Build UI Icons")]
        public static void BuildMenu()
        {
            var paths = BuildAndSave();
            Debug.Log($"[LogicSim] Built {paths.Count} UI icons into {OutputFolder}.");
        }

        public sealed class Icon
        {
            public int Width, Height;
            public Color32[] Pixels;    // row 0 = bottom
        }

        public static List<string> BuildAndSave()
        {
            string root = Path.GetDirectoryName(Application.dataPath) ?? ".";
            var icons = Build(PsdReader.Read(Path.Combine(root, IconSourcePath)), PsdReader.Read(Path.Combine(root, SymbolAtlasBuilder.SourcePath)));
            Directory.CreateDirectory(OutputFolder);
            var paths = new List<string>();
            foreach (var pair in icons)
            {
                var texture = new Texture2D(pair.Value.Width, pair.Value.Height, TextureFormat.RGBA32, false);
                texture.SetPixels32(pair.Value.Pixels);
                texture.Apply();
                string path = $"{OutputFolder}/{pair.Key}.png";
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                paths.Add(path);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                    continue;
                bool changed = importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled
                    || importer.textureCompression != TextureImporterCompression.Uncompressed || !importer.alphaIsTransparency;
                if (!changed)
                    continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return paths;
        }

        /// <summary>Icon id to icon, Icons.psd first, then the symbol icons.</summary>
        public static Dictionary<string, Icon> Build(PsdReader.Document icons, PsdReader.Document symbols)
        {
            var result = new Dictionary<string, Icon>(StringComparer.Ordinal);
            foreach (var board in icons.Layers.Where(l => l.IsArtboard && l.Visible))
                result[board.Name] = RenderIcon(board);
            foreach (var id in SymbolIcons)
            {
                var board = symbols.Layers.FirstOrDefault(l => l.IsArtboard && l.Name == id);
                if (board != null)
                    result[SymbolIconPrefix + id] = RenderSymbol(board);
            }
            return result;
        }

        /// <summary>Icons.psd: all visible layers of the artboard, scaled to <see cref="Size"/>.</summary>
        private static Icon RenderIcon(PsdLayer board)
        {
            var area = Area.Of(board, 4, 4);
            var field = area.Field(board.Children);
            float scale = (float)Size / (float)(board.ArtboardRight - board.ArtboardLeft);
            var icon = new Icon { Width = Size, Height = Size, Pixels = new Color32[Size * Size] };
            var pixels = icon.Pixels;
            if (field == null)
                return icon;
            for (int ty = 0; ty < Size; ty++)
            {
                for (int tx = 0; tx < Size; tx++)
                {
                    float sx = area.BoardX + (tx + 0.5f) / scale;
                    float sy = area.BoardY + (Size - ty - 0.5f) / scale;
                    pixels[ty * Size + tx] = White(Coverage(field.Sample(sx, sy) * scale));
                }
            }
            return icon;
        }

        /// <summary>A symbol's outline, widened to the icons' stroke, over its fill at a low opacity, centered.</summary>
        private static Icon RenderSymbol(PsdLayer board)
        {
            float width = (float)(board.ArtboardRight - board.ArtboardLeft);
            float height = (float)(board.ArtboardBottom - board.ArtboardTop);
            float scale = (Size - 2 * SymbolMargin) / Mathf.Max(width, height);
            int iconWidth = width >= height ? Size : Mathf.CeilToInt(width * scale + 2 * SymbolMargin);
            int iconHeight = height >= width ? Size : Mathf.CeilToInt(height * scale + 2 * SymbolMargin);
            float grow = Mathf.Max(0, (StrokeWidth / scale - SymbolStrokeWidth) / 2);
            // The area covers the whole icon: art may reach beyond its artboard (the XOR's back curve does), and samples
            // outside a distance field would repeat its border.
            int marginX = (int)Math.Ceiling((iconWidth / scale - width) / 2) + 2;
            int marginY = (int)Math.Ceiling((iconHeight / scale - height) / 2) + 2;
            var area = Area.Of(board, marginX, marginY);
            var outline = area.Field(board.Children.Where(l => Named(l, "outline")));
            var fill = area.Field(board.Children.Where(l => Named(l, "fill")));
            float offsetX = (iconWidth - width * scale) / 2, offsetY = (iconHeight - height * scale) / 2;

            var icon = new Icon { Width = iconWidth, Height = iconHeight, Pixels = new Color32[iconWidth * iconHeight] };
            for (int ty = 0; ty < iconHeight; ty++)
            {
                for (int tx = 0; tx < iconWidth; tx++)
                {
                    float sx = area.BoardX + (tx + 0.5f - offsetX) / scale;
                    float sy = area.BoardY + (iconHeight - ty - 0.5f - offsetY) / scale;
                    float line = outline == null ? 0 : Coverage((outline.Sample(sx, sy) + grow) * scale);
                    float body = fill == null ? 0 : Coverage(fill.Sample(sx, sy) * scale) * FillAlpha;
                    icon.Pixels[ty * iconWidth + tx] = White(line + body * (1 - line));
                }
            }
            return icon;
        }

        private static bool Named(PsdLayer layer, string name) => !layer.IsGroup && layer.Visible && string.Equals(layer.Name.Trim(), name, StringComparison.OrdinalIgnoreCase);

        private static float Coverage(float distancePixels) => Mathf.Clamp01(distancePixels + 0.5f);

        private static Color32 White(float alpha) => new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255));

        /// <summary>The artboard plus a margin, in document pixels, and distance fields of layers over it.</summary>
        private sealed class Area
        {
            public int Left, Top, Width, Height;
            /// <summary>The artboard's top-left corner in area coordinates.</summary>
            public float BoardX, BoardY;

            public static Area Of(PsdLayer board, int marginX, int marginY)
            {
                int left = (int)Math.Floor(board.ArtboardLeft) - marginX;
                int top = (int)Math.Floor(board.ArtboardTop) - marginY;
                return new Area
                {
                    Left = left,
                    Top = top,
                    Width = (int)Math.Ceiling(board.ArtboardRight) + marginX - left,
                    Height = (int)Math.Ceiling(board.ArtboardBottom) + marginY - top,
                    BoardX = (float)board.ArtboardLeft - left,
                    BoardY = (float)board.ArtboardTop - top,
                };
            }

            /// <summary>Distance field of the union of the layers (alpha at least 128), or null when they are empty.</summary>
            public DistanceField Field(IEnumerable<PsdLayer> layers)
            {
                var list = layers.Where(l => !l.IsGroup && l.Visible && l.Alpha != null).ToList();
                var mask = new bool[Width * Height];
                bool any = false;
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        foreach (var layer in list)
                        {
                            if (layer.AlphaAt(Left + x, Top + y) < 128)
                                continue;
                            mask[y * Width + x] = true;
                            any = true;
                            break;
                        }
                    }
                }
                return any ? DistanceField.FromMask(mask, Width, Height) : null;
            }
        }
    }
}
