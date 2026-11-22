using System;
using System.Runtime.InteropServices;
using LogicSim.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace LogicSim.Views
{
    /// <summary>
    /// Draws all wires with one procedural instanced draw call: one quad per straight segment. Each wire knows the net
    /// that drives it; the simulation's net values are copied to the GPU once per frame, so coloring the wires costs no
    /// per-wire work on the CPU. Segments change only when wires are added, removed or re-routed.
    /// </summary>
    public sealed class WireRenderer : IDisposable
    {
        public const string ShaderResource = "Shaders/Wires";
        /// <summary>Segments per wire: <see cref="WireView.Route"/> makes at most six points.</summary>
        public const int SegmentsPerWire = 5;
        private const uint Unused = 0xFFFFFFFF;
        private const uint SelectedFlag = 1;

        // Keep in sync with the structs in Wires.shader.
        [StructLayout(LayoutKind.Sequential)]
        private struct SegmentData
        {
            public Vector2 A;
            public Vector2 B;
            public uint Wire;
            public uint Pad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WireData
        {
            public int Net;
            public uint Flags;
        }

        private static readonly int SegmentsId = Shader.PropertyToID("_Segments");
        private static readonly int WiresId = Shader.PropertyToID("_Wires");
        private static readonly int NetsId = Shader.PropertyToID("_Nets");
        private static readonly int ShowValuesId = Shader.PropertyToID("_ShowValues");
        private static readonly int WireColorId = Shader.PropertyToID("_WireColor");
        private static readonly int HighColorId = Shader.PropertyToID("_HighColor");
        private static readonly int LowColorId = Shader.PropertyToID("_LowColor");
        private static readonly int SelectionColorId = Shader.PropertyToID("_SelectionColor");

        private readonly Material _material;
        private GraphicsBuffer _segmentBuffer;
        private GraphicsBuffer _wireBuffer;
        private GraphicsBuffer _netBuffer;

        private SegmentData[] _segments = new SegmentData[256 * SegmentsPerWire];
        private WireData[] _wires = new WireData[256];
        private WireView[] _owners = new WireView[256];
        private int _count;
        private bool _segmentsDirty;
        private bool _wiresDirty;
        private Bounds _bounds;

        private byte[] _netBytes = new byte[0];
        private uint[] _netWords = new uint[1];
        private bool _showValues;

        /// <summary>Number of wires drawn.</summary>
        public int Count => _count;

        /// <summary>Null when the shader is missing or the GPU cannot read structured buffers in vertex shaders.</summary>
        public static WireRenderer TryCreate()
        {
            if (!SystemInfo.supportsComputeShaders || SystemInfo.maxComputeBufferInputsVertex < 3)
                return null;
            var shader = Resources.Load<Shader>(ShaderResource);
            if (shader == null || !shader.isSupported)
                return null;
            return new WireRenderer(shader);
        }

        private WireRenderer(Shader shader)
        {
            _material = new Material(shader) { name = "LogicSim Wires", hideFlags = HideFlags.HideAndDontSave };
            _material.SetFloat("_HalfWidth", WireView.LineWidth / 2);
            _material.SetFloat("_SelectedHalfWidth", WireView.LineWidth * 1.6f / 2);
            _netBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            _material.SetBuffer(NetsId, _netBuffer);
        }

        public void Dispose()
        {
            _segmentBuffer?.Release();
            _wireBuffer?.Release();
            _netBuffer?.Release();
            _segmentBuffer = _wireBuffer = _netBuffer = null;
            if (_material == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_material);
            else
                UnityEngine.Object.DestroyImmediate(_material);
        }

        #region Wires

        internal int Add(WireView owner)
        {
            if (_count == _wires.Length)
            {
                int capacity = _wires.Length * 2;
                Array.Resize(ref _wires, capacity);
                Array.Resize(ref _owners, capacity);
                Array.Resize(ref _segments, capacity * SegmentsPerWire);
            }
            int slot = _count++;
            _wires[slot] = new WireData { Net = -1 };
            _owners[slot] = owner;
            for (int i = 0; i < SegmentsPerWire; i++)
                _segments[slot * SegmentsPerWire + i] = new SegmentData { Wire = Unused };
            _segmentsDirty = _wiresDirty = true;
            return slot;
        }

        /// <summary>Removes a wire; the last one moves into its slot.</summary>
        internal void Remove(int slot)
        {
            if (slot < 0 || slot >= _count)
                return;
            int last = --_count;
            if (slot != last)
            {
                _wires[slot] = _wires[last];
                _owners[slot] = _owners[last];
                _owners[slot].Slot = slot;
                for (int i = 0; i < SegmentsPerWire; i++)
                {
                    var segment = _segments[last * SegmentsPerWire + i];
                    if (segment.Wire != Unused)
                        segment.Wire = (uint)slot;
                    _segments[slot * SegmentsPerWire + i] = segment;
                }
            }
            _owners[last] = null;
            _segmentsDirty = _wiresDirty = true;
        }

        /// <summary>The wire's route: <paramref name="count"/> points, at most <see cref="SegmentsPerWire"/> + 1.</summary>
        internal void SetRoute(int slot, Vector3[] points, int count)
        {
            int segments = Mathf.Min(count - 1, SegmentsPerWire);
            for (int i = 0; i < SegmentsPerWire; i++)
            {
                _segments[slot * SegmentsPerWire + i] = i < segments
                    ? new SegmentData { A = points[i], B = points[i + 1], Wire = (uint)slot }
                    : new SegmentData { Wire = Unused };
            }
            _segmentsDirty = true;
        }

        internal void SetSelected(int slot, bool selected)
        {
            uint flags = selected ? _wires[slot].Flags | SelectedFlag : _wires[slot].Flags & ~SelectedFlag;
            if (_wires[slot].Flags == flags)
                return;
            _wires[slot].Flags = flags;
            _wiresDirty = true;
        }

        internal void SetNet(int slot, int net)
        {
            if (_wires[slot].Net == net)
                return;
            _wires[slot].Net = net;
            _wiresDirty = true;
        }

        /// <summary>Hands every wire view its net in <paramref name="signals"/> (after a recompile or a level change).</summary>
        public void ResolveNets(ISignalReader signals, Circuit circuit)
        {
            for (int slot = 0; slot < _count; slot++)
            {
                int net = -1;
                if (circuit.TryGetWire(_owners[slot].WireId, out var wire))
                    net = signals.GetOutputNet(wire.From.ComponentId, wire.From.Pin);
                SetNet(slot, net);
            }
        }

        #endregion

        #region Values

        /// <summary>Shows the engine's current net values; call once per frame while the simulation shows values.</summary>
        public void ShowValues(ISimulationEngine engine, int netCount)
        {
            if (_netBytes.Length < netCount)
            {
                // Whole words, so that the bytes copy into them in one block.
                _netBytes = new byte[(netCount + 3) & ~3];
                _netWords = new uint[Math.Max(1, _netBytes.Length / 4)];
            }
            engine.CopyNetValues(_netBytes);
            Buffer.BlockCopy(_netBytes, 0, _netWords, 0, _netBytes.Length);
            if (_netBuffer.count < _netWords.Length)
            {
                _netBuffer.Release();
                _netBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _netWords.Length, sizeof(uint));
                _material.SetBuffer(NetsId, _netBuffer);
            }
            _netBuffer.SetData(_netWords, 0, 0, Math.Max(1, (netCount + 3) / 4));
            _showValues = true;
        }

        /// <summary>Outside the simulation (or without values for this level) wires have the plain wire color.</summary>
        public void HideValues() => _showValues = false;

        #endregion

        #region Drawing

        public void Render()
        {
            if (_count == 0)
                return;
            Upload();
            _material.SetFloat(ShowValuesId, _showValues ? 1 : 0);
            _material.SetColor(WireColorId, VisualStyle.WireColor);
            _material.SetColor(HighColorId, VisualStyle.HighColor);
            _material.SetColor(LowColorId, VisualStyle.LowColor);
            _material.SetColor(SelectionColorId, VisualStyle.SelectionColor);
            var parameters = new RenderParams(_material)
            {
                worldBounds = _bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
            Graphics.RenderPrimitives(parameters, MeshTopology.Triangles, 6, _count * SegmentsPerWire);
        }

        private void Upload()
        {
            if (_wireBuffer == null || _wireBuffer.count < _count)
            {
                _segmentBuffer?.Release();
                _wireBuffer?.Release();
                _segmentBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _segments.Length, Marshal.SizeOf<SegmentData>());
                _wireBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _wires.Length, Marshal.SizeOf<WireData>());
                _material.SetBuffer(SegmentsId, _segmentBuffer);
                _material.SetBuffer(WiresId, _wireBuffer);
                _segmentsDirty = _wiresDirty = true;
            }
            if (_segmentsDirty)
            {
                _segmentBuffer.SetData(_segments, 0, 0, _count * SegmentsPerWire);
                _bounds = ComputeBounds();
                _segmentsDirty = false;
            }
            if (_wiresDirty)
            {
                _wireBuffer.SetData(_wires, 0, 0, _count);
                _wiresDirty = false;
            }
        }

        /// <summary>
        /// Bounds of all segments. Their center lies one unit behind the components (z = 1), so that the transparent
        /// queue, sorted by distance for the orthographic camera, draws the wires after the background and before the
        /// component bodies.
        /// </summary>
        private Bounds ComputeBounds()
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            int n = _count * SegmentsPerWire;
            for (int i = 0; i < n; i++)
            {
                if (_segments[i].Wire == Unused)
                    continue;
                min = Vector2.Min(min, Vector2.Min(_segments[i].A, _segments[i].B));
                max = Vector2.Max(max, Vector2.Max(_segments[i].A, _segments[i].B));
            }
            if (min.x > max.x)
                min = max = Vector2.zero;
            min -= Vector2.one;
            max += Vector2.one;
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(min.x, min.y, 0.5f), new Vector3(max.x, max.y, 1.5f));
            return bounds;
        }

        #endregion
    }
}
