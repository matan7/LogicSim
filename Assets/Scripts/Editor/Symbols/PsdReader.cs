using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LogicSim.EditorTools.Symbols
{
    /// <summary>A layer or group of a PSD file. Groups have children and no pixels.</summary>
    public sealed class PsdLayer
    {
        public string Name;
        public bool Visible = true;
        public bool IsGroup;

        /// <summary>Pixel bounds in document coordinates (y down): left, top, right (exclusive), bottom (exclusive).</summary>
        public int Left, Top, Right, Bottom;

        /// <summary>Coverage 0..255 per pixel, row-major from the top-left of the bounds. Null for groups and empty layers.</summary>
        public byte[] Alpha;

        /// <summary>Artboard rectangle (document pixels) when this group is an artboard.</summary>
        public bool IsArtboard;
        public double ArtboardLeft, ArtboardTop, ArtboardRight, ArtboardBottom;

        public readonly List<PsdLayer> Children = new List<PsdLayer>();

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public byte AlphaAt(int x, int y)
        {
            if (Alpha == null || x < Left || y < Top || x >= Right || y >= Bottom)
                return 0;
            return Alpha[(y - Top) * Width + (x - Left)];
        }

        public override string ToString() => IsGroup ? $"{Name} (group, {Children.Count})" : $"{Name} [{Left},{Top},{Right},{Bottom}]";
    }

    /// <summary>
    /// Reads the layer tree of an 8-bit PSD: names, visibility, groups, artboard rectangles and the transparency
    /// channel of every layer (shape layers included, as Photoshop and PhotoCraft store them rasterized).
    /// </summary>
    public static class PsdReader
    {
        public sealed class Document
        {
            public int Width, Height;
            /// <summary>Top-level layers, top to bottom as in the Layers panel.</summary>
            public readonly List<PsdLayer> Layers = new List<PsdLayer>();
        }

        public static Document Read(string path) => Read(File.ReadAllBytes(path));

        public static Document Read(byte[] bytes)
        {
            var r = new Reader(bytes);
            if (r.Ascii(4) != "8BPS")
                throw new InvalidDataException("Not a PSD file.");
            int version = r.U16();
            if (version != 1)
                throw new InvalidDataException("PSB files are not supported.");
            r.Skip(6);
            r.U16(); // channels
            var doc = new Document { Height = (int)r.U32(), Width = (int)r.U32() };
            int depth = r.U16();
            if (depth != 8)
                throw new InvalidDataException($"Only 8-bit PSD files are supported (this one is {depth}-bit).");
            r.U16(); // color mode
            r.Skip((int)r.U32()); // color mode data
            r.Skip((int)r.U32()); // image resources

            long layerAndMaskLength = r.U32();
            if (layerAndMaskLength == 0)
                return doc;
            long layerInfoLength = r.U32();
            if (layerInfoLength == 0)
                return doc;
            int count = Math.Abs(r.I16());

            var records = new List<(PsdLayer layer, int section, List<(short id, long length)> channels)>();
            for (int i = 0; i < count; i++)
                records.Add(ReadRecord(r));

            foreach (var (layer, _, channels) in records)
            {
                foreach (var (id, length) in channels)
                {
                    long end = r.Position + length;
                    if (id == -1 && layer.Width > 0 && layer.Height > 0)
                        layer.Alpha = ReadChannel(r, layer.Width, layer.Height, end);
                    r.Position = end;
                }
            }

            // Records go from the bottom of the stack to the top. A group is its section divider (3) below its
            // children and the folder record (1 or 2) above them.
            var stack = new Stack<List<PsdLayer>>();
            var current = new List<PsdLayer>();
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var (layer, section, _) = records[i];
                if (section == 1 || section == 2)
                {
                    layer.IsGroup = true;
                    layer.Alpha = null;
                    current.Add(layer);
                    stack.Push(current);
                    current = layer.Children;
                }
                else if (section == 3)
                {
                    current = stack.Count > 0 ? stack.Pop() : current;
                }
                else
                {
                    current.Add(layer);
                }
            }
            while (stack.Count > 0)
                current = stack.Pop();
            doc.Layers.AddRange(current);
            return doc;
        }

        private static (PsdLayer, int, List<(short, long)>) ReadRecord(Reader r)
        {
            var layer = new PsdLayer();
            layer.Top = r.I32();
            layer.Left = r.I32();
            layer.Bottom = r.I32();
            layer.Right = r.I32();
            int channelCount = r.U16();
            var channels = new List<(short, long)>(channelCount);
            for (int c = 0; c < channelCount; c++)
                channels.Add((r.I16(), r.U32()));
            if (r.Ascii(4) != "8BIM")
                throw new InvalidDataException("Bad blend mode signature.");
            r.Skip(4); // blend mode
            r.Skip(1); // opacity
            r.Skip(1); // clipping
            int flags = r.U8();
            layer.Visible = (flags & 2) == 0;
            r.Skip(1);
            long extraEnd = r.U32() + r.Position;
            r.Skip((int)r.U32()); // mask data
            r.Skip((int)r.U32()); // blending ranges
            int nameLength = r.U8();
            var nameBytes = r.Bytes(nameLength);
            var nameChars = new char[nameLength];
            for (int c = 0; c < nameLength; c++)
                nameChars[c] = (char)nameBytes[c]; // Pascal names are Latin-1; luni below has the Unicode name
            layer.Name = new string(nameChars);
            r.Skip((4 - (1 + nameLength) % 4) % 4);

            int section = 0;
            while (r.Position + 12 <= extraEnd)
            {
                string signature = r.Ascii(4);
                if (signature != "8BIM" && signature != "8B64")
                    break;
                string key = r.Ascii(4);
                long length = r.U32();
                long end = r.Position + length;
                switch (key)
                {
                    case "luni":
                        int chars = (int)r.U32();
                        layer.Name = Encoding.BigEndianUnicode.GetString(r.Bytes(chars * 2));
                        break;
                    case "lsct":
                    case "lsdk":
                        section = (int)r.U32();
                        break;
                    case "artb":
                    case "artd":
                        ReadArtboard(r, end, layer);
                        break;
                }
                r.Position = end + (length % 2);
            }
            r.Position = extraEnd;
            return (layer, section, channels);
        }

        private static byte[] ReadChannel(Reader r, int width, int height, long end)
        {
            int compression = r.U16();
            var pixels = new byte[width * height];
            if (compression == 0)
            {
                Buffer.BlockCopy(r.Data, (int)r.Position, pixels, 0, Math.Min(pixels.Length, (int)(end - r.Position)));
                return pixels;
            }
            if (compression != 1)
                throw new InvalidDataException($"Unsupported channel compression {compression}.");
            var rowLengths = new int[height];
            for (int y = 0; y < height; y++)
                rowLengths[y] = r.U16();
            for (int y = 0; y < height; y++)
            {
                long rowEnd = r.Position + rowLengths[y];
                int x = y * width, rowLimit = x + width;
                while (r.Position < rowEnd && x < rowLimit)
                {
                    int n = r.U8();
                    if (n < 128)
                    {
                        for (int i = 0; i <= n && x < rowLimit; i++)
                            pixels[x++] = (byte)r.U8();
                    }
                    else if (n > 128)
                    {
                        byte value = (byte)r.U8();
                        for (int i = 0; i < 257 - n && x < rowLimit; i++)
                            pixels[x++] = value;
                    }
                }
                r.Position = rowEnd;
            }
            return pixels;
        }

        /// <summary>Reads artboardRect (Top, Left, Btom, Rght) from the artboard descriptor.</summary>
        private static void ReadArtboard(Reader r, long end, PsdLayer layer)
        {
            r.U32(); // descriptor version (16)
            var values = new Dictionary<string, double>();
            ReadDescriptor(r, values, "");
            if (values.TryGetValue("artboardRect.Top ", out var top) && values.TryGetValue("artboardRect.Left", out var left)
                && values.TryGetValue("artboardRect.Btom", out var bottom) && values.TryGetValue("artboardRect.Rght", out var right))
            {
                layer.IsArtboard = true;
                layer.ArtboardTop = top;
                layer.ArtboardLeft = left;
                layer.ArtboardBottom = bottom;
                layer.ArtboardRight = right;
            }
        }

        /// <summary>Action descriptor: collects numeric values by dotted key path; other values are skipped.</summary>
        private static void ReadDescriptor(Reader r, Dictionary<string, double> values, string prefix)
        {
            r.Skip((int)r.U32() * 2); // class name (unicode)
            ReadId(r);                  // class id
            int count = (int)r.U32();
            for (int i = 0; i < count; i++)
            {
                string key = ReadId(r);
                ReadValue(r, r.Ascii(4), values, prefix + key);
            }
        }

        private static void ReadValue(Reader r, string type, Dictionary<string, double> values, string key)
        {
            switch (type)
            {
                case "Objc":
                case "GlbO":
                    ReadDescriptor(r, values, key + ".");
                    break;
                case "VlLs":
                    int n = (int)r.U32();
                    for (int i = 0; i < n; i++)
                        ReadValue(r, r.Ascii(4), values, key + "[" + i + "]");
                    break;
                case "doub":
                    values[key] = r.F64();
                    break;
                case "UntF":
                    r.Skip(4);
                    values[key] = r.F64();
                    break;
                case "long":
                    values[key] = r.I32();
                    break;
                case "comp":
                    r.Skip(8);
                    break;
                case "bool":
                    r.Skip(1);
                    break;
                case "TEXT":
                    r.Skip((int)r.U32() * 2);
                    break;
                case "enum":
                    ReadId(r);
                    ReadId(r);
                    break;
                case "type":
                case "GlbC":
                    r.Skip((int)r.U32() * 2);
                    ReadId(r);
                    break;
                case "tdta":
                case "alis":
                    r.Skip((int)r.U32());
                    break;
                default:
                    throw new InvalidDataException($"Unsupported descriptor value type '{type}'.");
            }
        }

        private static string ReadId(Reader r)
        {
            int length = (int)r.U32();
            return r.Ascii(length == 0 ? 4 : length);
        }

        private sealed class Reader
        {
            public readonly byte[] Data;
            public long Position;

            public Reader(byte[] data) => Data = data;

            public void Skip(int n) => Position += n;
            public int U8() => Data[Position++];

            public int U16()
            {
                int v = (Data[Position] << 8) | Data[Position + 1];
                Position += 2;
                return v;
            }

            public short I16() => (short)U16();

            public uint U32()
            {
                uint v = ((uint)Data[Position] << 24) | ((uint)Data[Position + 1] << 16) | ((uint)Data[Position + 2] << 8) | Data[Position + 3];
                Position += 4;
                return v;
            }

            public int I32() => (int)U32();

            public double F64()
            {
                ulong hi = U32(), lo = U32();
                return BitConverter.Int64BitsToDouble((long)((hi << 32) | lo));
            }

            public byte[] Bytes(int n)
            {
                var b = new byte[n];
                Buffer.BlockCopy(Data, (int)Position, b, 0, n);
                Position += n;
                return b;
            }

            public string Ascii(int n) => Encoding.ASCII.GetString(Bytes(n));
        }
    }
}
