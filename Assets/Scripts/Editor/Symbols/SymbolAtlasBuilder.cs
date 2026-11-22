using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogicSim.Views;
using UnityEditor;
using UnityEngine;

namespace LogicSim.EditorTools.Symbols
{
    /// <summary>
    /// Builds the symbol atlas from Art/Symbols/Symbols.psd: every top-level artboard is a symbol, its layers named
    /// outline, fill and glow become the R, G and B signed distance fields. See docs/SYMBOL_FORMAT.md.
    /// </summary>
    public static class SymbolAtlasBuilder
    {
        public const string SourcePath = "Art/Symbols/Symbols.psd";
        public const string AtlasPath = "Assets/Art/Symbols/SymbolAtlas.png";
        public const string SetPath = "Assets/Art/Symbols/SymbolSet.asset";

        /// <summary>Pixels per world unit in the PSD.</summary>
        public const int SourcePixelsPerUnit = 100;
        public const int TexelsPerUnit = 32;
        /// <summary>Distance range on each side of an edge, in atlas texels.</summary>
        public const int SpreadTexels = 8;
        /// <summary>Margin around every symbol in its atlas cell (spread plus one texel for filtering).</summary>
        public const int PaddingTexels = SpreadTexels + 1;
        private const int Gap = 2;

        private static readonly string[] Channels = { "outline", "fill", "glow" };

        public sealed class Cell
        {
            public string Id;
            public int UnitsX, UnitsY;
            public int Width, Height;   // texels, padding included
            public int X, Y;            // position in the atlas (bottom-left origin)
            public Color32[] Pixels;    // row 0 = bottom
        }

        public sealed class Result
        {
            public int Size;
            public Color32[] Pixels;
            public readonly List<Cell> Cells = new List<Cell>();
            public readonly List<string> Warnings = new List<string>();
        }

        [MenuItem("Tools/LogicSim/Build Symbol Atlas")]
        public static void BuildMenu()
        {
            try
            {
                var result = BuildAndSave();
                string message = $"Built {result.Cells.Count} symbols into a {result.Size}x{result.Size} atlas.";
                foreach (var warning in result.Warnings)
                    Debug.LogWarning("[LogicSim] Symbol atlas: " + warning);
                Debug.Log("[LogicSim] " + message + " " + AtlasPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("[LogicSim] Building the symbol atlas failed: " + exception.Message);
                throw;
            }
        }

        public static Result BuildAndSave()
        {
            string source = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", SourcePath);
            var result = Build(PsdReader.Read(source));
            Save(result);
            return result;
        }

        /// <summary>Builds the atlas in memory.</summary>
        public static Result Build(PsdReader.Document document)
        {
            var result = new Result();
            foreach (var board in document.Layers)
            {
                if (!board.IsGroup || !board.IsArtboard || !board.Visible)
                    continue;
                var cell = BuildCell(board, result.Warnings);
                if (cell != null)
                    result.Cells.Add(cell);
            }
            if (result.Cells.Count == 0)
                throw new InvalidDataException("The PSD has no artboards.");
            Pack(result);
            return result;
        }

        private static Cell BuildCell(PsdLayer board, List<string> warnings)
        {
            double width = (board.ArtboardRight - board.ArtboardLeft) / SourcePixelsPerUnit;
            double height = (board.ArtboardBottom - board.ArtboardTop) / SourcePixelsPerUnit;
            int unitsX = (int)Math.Round(width), unitsY = (int)Math.Round(height);
            if (unitsX < 1 || unitsY < 1 || Math.Abs(width - unitsX) > 1e-3 || Math.Abs(height - unitsY) > 1e-3)
            {
                warnings.Add($"'{board.Name}' is {width:0.##} x {height:0.##} units; artboards must be whole units ({SourcePixelsPerUnit} px). Skipped.");
                return null;
            }

            var cell = new Cell
            {
                Id = board.Name,
                UnitsX = unitsX,
                UnitsY = unitsY,
                Width = unitsX * TexelsPerUnit + 2 * PaddingTexels,
                Height = unitsY * TexelsPerUnit + 2 * PaddingTexels,
            };
            cell.Pixels = new Color32[cell.Width * cell.Height];
            for (int i = 0; i < cell.Pixels.Length; i++)
                cell.Pixels[i] = new Color32(0, 0, 0, 255);

            // Source region: the artboard plus the padding, plus a margin so that distances near the border are right.
            float sourcePerTexel = (float)SourcePixelsPerUnit / TexelsPerUnit;
            int margin = (int)Math.Ceiling((PaddingTexels + 2) * sourcePerTexel);
            int left = (int)Math.Floor(board.ArtboardLeft) - margin;
            int top = (int)Math.Floor(board.ArtboardTop) - margin;
            int regionWidth = (int)Math.Ceiling(board.ArtboardRight) + margin - left;
            int regionHeight = (int)Math.Ceiling(board.ArtboardBottom) + margin - top;

            bool any = false;
            for (int channel = 0; channel < Channels.Length; channel++)
            {
                var layers = board.Children.Where(l => !l.IsGroup && l.Visible && string.Equals(l.Name.Trim(), Channels[channel], StringComparison.OrdinalIgnoreCase)).ToList();
                if (layers.Count == 0 || layers.All(l => l.Alpha == null))
                    continue;
                var mask = new bool[regionWidth * regionHeight];
                bool covered = false;
                for (int y = 0; y < regionHeight; y++)
                {
                    for (int x = 0; x < regionWidth; x++)
                    {
                        int alpha = 0;
                        foreach (var layer in layers)
                            alpha = Math.Max(alpha, layer.AlphaAt(left + x, top + y));
                        if (alpha >= 128)
                        {
                            mask[y * regionWidth + x] = true;
                            covered = true;
                        }
                    }
                }
                if (!covered)
                    continue;
                any = true;
                var field = DistanceField.FromMask(mask, regionWidth, regionHeight);
                float spreadSource = SpreadTexels * sourcePerTexel;
                for (int ty = 0; ty < cell.Height; ty++)
                {
                    // Texel row 0 is the bottom of the cell; the PSD's y grows downwards.
                    float sy = (float)board.ArtboardTop - top + ((cell.Height - ty - 0.5f) - PaddingTexels) * sourcePerTexel;
                    for (int tx = 0; tx < cell.Width; tx++)
                    {
                        float sx = (float)board.ArtboardLeft - left + (tx + 0.5f - PaddingTexels) * sourcePerTexel;
                        float d = field.Sample(sx, sy);
                        byte value = (byte)Mathf.Clamp(Mathf.RoundToInt((0.5f + d / (2f * spreadSource)) * 255f), 0, 255);
                        var p = cell.Pixels[ty * cell.Width + tx];
                        if (channel == 0) p.r = value;
                        else if (channel == 1) p.g = value;
                        else p.b = value;
                        cell.Pixels[ty * cell.Width + tx] = p;
                    }
                }
            }
            if (!any)
                warnings.Add($"'{board.Name}' has no visible outline, fill or glow layer with pixels.");
            return cell;
        }

        /// <summary>Shelf packing, tallest first, into the smallest power-of-two square that fits.</summary>
        private static void Pack(Result result)
        {
            var order = result.Cells.OrderByDescending(c => c.Height).ThenByDescending(c => c.Width).ToList();
            for (int size = 256; size <= 8192; size *= 2)
            {
                int x = Gap, y = Gap, shelf = 0;
                bool fits = true;
                foreach (var cell in order)
                {
                    if (x + cell.Width + Gap > size)
                    {
                        x = Gap;
                        y += shelf + Gap;
                        shelf = 0;
                    }
                    if (y + cell.Height + Gap > size || cell.Width + 2 * Gap > size)
                    {
                        fits = false;
                        break;
                    }
                    cell.X = x;
                    cell.Y = y;
                    x += cell.Width + Gap;
                    shelf = Math.Max(shelf, cell.Height);
                }
                if (!fits)
                    continue;
                result.Size = size;
                result.Pixels = new Color32[size * size];
                foreach (var cell in result.Cells)
                    for (int row = 0; row < cell.Height; row++)
                        Array.Copy(cell.Pixels, row * cell.Width, result.Pixels, (cell.Y + row) * size + cell.X, cell.Width);
                return;
            }
            throw new InvalidDataException("The symbols do not fit into an 8192 x 8192 atlas.");
        }

        private static void Save(Result result)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AtlasPath) ?? "Assets");
            var texture = new Texture2D(result.Size, result.Size, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(result.Pixels);
            texture.Apply();
            File.WriteAllBytes(AtlasPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            // Refresh rather than ImportAsset: the first build also creates the folder.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(AtlasPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 8192;
                importer.SaveAndReimport();
            }

            var set = AssetDatabase.LoadAssetAtPath<SymbolSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<SymbolSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.Atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            set.TexelsPerUnit = TexelsPerUnit;
            set.Spread = (float)SpreadTexels / TexelsPerUnit;
            set.Padding = (float)PaddingTexels / TexelsPerUnit;
            set.Symbols = result.Cells.OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => new SymbolSet.Symbol
            {
                Id = c.Id,
                Uv = new Rect((float)c.X / result.Size, (float)c.Y / result.Size, (float)c.Width / result.Size, (float)c.Height / result.Size),
                Size = new Vector2(c.UnitsX, c.UnitsY),
            }).ToList();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
        }
    }
}
