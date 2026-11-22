using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace LogicSim.Views
{
    /// <summary>
    /// Draws the bodies of all <see cref="SymbolComponentView"/>s with one procedural instanced draw call: symbols from
    /// the <see cref="SymbolSet"/> atlas, positioned by an instance buffer that changes only when components move, and
    /// lit by a state buffer of one word per instance that is uploaded when the values change. See docs/SYMBOL_FORMAT.md.
    /// </summary>
    public sealed class SymbolRenderer : IDisposable
    {
        public const string ResourceName = "SymbolSet";
        /// <summary>Pin rows a symbol can have (0 to 7 units below its top edge).</summary>
        public const int MaxRows = 8;

        // Instance flags.
        public const uint RotationMask = 3;
        public const uint SelectedFlag = 4;
        public const int InputRowsShift = 8;
        public const int OutputRowsShift = 16;

        // Instance states.
        public const uint LitState = 1;
        public const uint ValuesState = 2;
        public const int InputValuesShift = 8;
        public const int OutputValuesShift = 16;

        /// <summary>Half the width of the outline strokes in the art (16 px at 100 px per unit).</summary>
        private const float StrokeHalfWidth = 0.08f;

        // Keep in sync with the structs in Symbols.shader.
        [StructLayout(LayoutKind.Sequential)]
        private struct InstanceData
        {
            public Vector2 Position;
            public uint Symbol;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SymbolData
        {
            public Vector4 Uv;
            public Vector4 Size;
            public Vector4 Left0, Left1;
            public Vector4 Right0, Right1;
        }

        private static readonly int InstancesId = Shader.PropertyToID("_Instances");
        private static readonly int SymbolsId = Shader.PropertyToID("_Symbols");
        private static readonly int StatesId = Shader.PropertyToID("_States");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int FillOnColorId = Shader.PropertyToID("_FillOnColor");
        private static readonly int FillOffColorId = Shader.PropertyToID("_FillOffColor");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int SelectionColorId = Shader.PropertyToID("_SelectionColor");
        private static readonly int WireColorId = Shader.PropertyToID("_WireColor");
        private static readonly int HighColorId = Shader.PropertyToID("_HighColor");
        private static readonly int LowColorId = Shader.PropertyToID("_LowColor");
        private static readonly int LodOffColorId = Shader.PropertyToID("_LodOffColor");

        private readonly SymbolSet _set;
        private readonly Material _material;
        private readonly GraphicsBuffer _symbolBuffer;
        private GraphicsBuffer _instanceBuffer;
        private GraphicsBuffer _stateBuffer;

        private InstanceData[] _instances = new InstanceData[256];
        private uint[] _states = new uint[256];
        private SymbolComponentView[] _owners = new SymbolComponentView[256];
        private int _count;
        private bool _instancesDirty;
        private bool _statesDirty;
        private Bounds _bounds;

        public SymbolSet Set => _set;

        /// <summary>Number of instances drawn.</summary>
        public int Count => _count;

        /// <summary>Loads the symbol set from Resources; null when it is missing or the GPU cannot run the shader.</summary>
        public static SymbolRenderer TryCreate()
        {
            if (!SystemInfo.supportsComputeShaders || SystemInfo.maxComputeBufferInputsVertex < 2)
                return null;
            var set = Resources.Load<SymbolSet>(ResourceName);
            if (set == null || set.Atlas == null || set.Shader == null || !set.Shader.isSupported || set.Symbols.Count == 0)
                return null;
            return new SymbolRenderer(set);
        }

        private SymbolRenderer(SymbolSet set)
        {
            _set = set;
            _material = new Material(set.Shader) { name = "LogicSim Symbols", hideFlags = HideFlags.HideAndDontSave };
            _material.mainTexture = set.Atlas;
            _material.SetFloat("_Padding", set.Padding);
            _material.SetFloat("_Spread", set.Spread);
            _material.SetFloat("_StrokeHalfWidth", StrokeHalfWidth);
            _material.SetFloat("_StubHalfWidth", WireView.LineWidth / 2);
            _material.SetFloat("_SelectedGrow", 0.06f);
            // About 5 to 3 pixels per unit.
            _material.SetFloat("_LodStart", 0.18f);
            _material.SetFloat("_LodEnd", 0.3f);

            var symbols = new SymbolData[set.Symbols.Count];
            for (int i = 0; i < symbols.Length; i++)
            {
                var s = set.Symbols[i];
                symbols[i] = new SymbolData
                {
                    Uv = new Vector4(s.Uv.x, s.Uv.y, s.Uv.width, s.Uv.height),
                    Size = new Vector4(s.Size.x, s.Size.y, 0, 0),
                    Left0 = Rows(s.Left, 0), Left1 = Rows(s.Left, 4),
                    Right0 = Rows(s.Right, 0), Right1 = Rows(s.Right, 4),
                };
            }
            _symbolBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, symbols.Length, Marshal.SizeOf<SymbolData>());
            _symbolBuffer.SetData(symbols);
            _material.SetBuffer(SymbolsId, _symbolBuffer);
        }

        private static Vector4 Rows(float[] rows, int first)
        {
            float At(int i) => rows != null && first + i < rows.Length ? rows[first + i] : -1f;
            return new Vector4(At(0), At(1), At(2), At(3));
        }

        public void Dispose()
        {
            _symbolBuffer?.Release();
            _instanceBuffer?.Release();
            _stateBuffer?.Release();
            _instanceBuffer = null;
            _stateBuffer = null;
            if (_material == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_material);
            else
                UnityEngine.Object.DestroyImmediate(_material);
        }

        #region Instances

        /// <summary>Index of the symbol with this id, or -1.</summary>
        public int IndexOf(string symbolId) => _set.IndexOf(symbolId);

        /// <summary>Adds an instance and returns its slot. Slots change when other instances are removed.</summary>
        internal int Add(SymbolComponentView owner, int symbol, uint flags)
        {
            if (_count == _instances.Length)
            {
                int capacity = _instances.Length * 2;
                Array.Resize(ref _instances, capacity);
                Array.Resize(ref _states, capacity);
                Array.Resize(ref _owners, capacity);
            }
            int slot = _count++;
            _instances[slot] = new InstanceData { Symbol = (uint)symbol, Flags = flags };
            _states[slot] = 0;
            _owners[slot] = owner;
            _instancesDirty = true;
            _statesDirty = true;
            return slot;
        }

        /// <summary>Removes an instance; the last instance moves into its slot.</summary>
        internal void Remove(int slot)
        {
            if (slot < 0 || slot >= _count)
                return;
            int last = --_count;
            if (slot != last)
            {
                _instances[slot] = _instances[last];
                _states[slot] = _states[last];
                _owners[slot] = _owners[last];
                _owners[slot].Slot = slot;
            }
            _owners[last] = null;
            _instancesDirty = true;
            _statesDirty = true;
        }

        internal void SetTransform(int slot, Vector2 position, int quarterTurns)
        {
            ref var instance = ref _instances[slot];
            uint flags = (instance.Flags & ~RotationMask) | ((uint)quarterTurns & RotationMask);
            if (instance.Position == position && instance.Flags == flags)
                return;
            instance.Position = position;
            instance.Flags = flags;
            _instancesDirty = true;
        }

        internal void SetSelected(int slot, bool selected)
        {
            ref var instance = ref _instances[slot];
            uint flags = selected ? instance.Flags | SelectedFlag : instance.Flags & ~SelectedFlag;
            if (instance.Flags == flags)
                return;
            instance.Flags = flags;
            _instancesDirty = true;
        }

        internal void SetState(int slot, uint state)
        {
            if (_states[slot] == state)
                return;
            _states[slot] = state;
            _statesDirty = true;
        }

        #endregion

        #region Drawing

        /// <summary>Uploads what changed and submits the draw call for this frame's cameras.</summary>
        public void Render()
        {
            if (_count == 0)
                return;
            Upload();
            ApplyColors();
            var parameters = new RenderParams(_material)
            {
                worldBounds = _bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
            Graphics.RenderPrimitives(parameters, MeshTopology.Triangles, 6, _count);
        }

        private void Upload()
        {
            if (_instanceBuffer == null || _instanceBuffer.count < _count)
            {
                _instanceBuffer?.Release();
                _stateBuffer?.Release();
                _instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _instances.Length, Marshal.SizeOf<InstanceData>());
                _stateBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _instances.Length, sizeof(uint));
                _material.SetBuffer(InstancesId, _instanceBuffer);
                _material.SetBuffer(StatesId, _stateBuffer);
                _instancesDirty = true;
                _statesDirty = true;
            }
            if (_instancesDirty)
            {
                _instanceBuffer.SetData(_instances, 0, 0, _count);
                _bounds = ComputeBounds();
                _instancesDirty = false;
            }
            if (_statesDirty)
            {
                _stateBuffer.SetData(_states, 0, 0, _count);
                _statesDirty = false;
            }
        }

        private Bounds ComputeBounds()
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < _count; i++)
            {
                var p = _instances[i].Position;
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            // Symbols extend up to their size from the origin in any rotation.
            const float reach = 8;
            min -= new Vector2(reach, reach);
            max += new Vector2(reach, reach);
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(min.x, min.y, -1), new Vector3(max.x, max.y, 1));
            return bounds;
        }

        private void ApplyColors()
        {
            var high = VisualStyle.HighColor;
            _material.SetColor(OutlineColorId, VisualStyle.Outline);
            _material.SetColor(FillOnColorId, high);
            _material.SetColor(FillOffColorId, VisualStyle.BodyFill);
            _material.SetColor(GlowColorId, new Color(high.r, high.g, high.b, 0.35f));
            _material.SetColor(SelectionColorId, VisualStyle.SelectionColor);
            _material.SetColor(WireColorId, VisualStyle.WireColor);
            _material.SetColor(HighColorId, high);
            _material.SetColor(LowColorId, VisualStyle.LowColor);
            _material.SetColor(LodOffColorId, Color.Lerp(VisualStyle.BodyFill, VisualStyle.Outline, 0.35f));
        }

        #endregion
    }
}
