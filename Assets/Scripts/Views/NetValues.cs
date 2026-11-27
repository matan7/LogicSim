using System;
using LogicSim.Simulation;
using UnityEngine;

namespace LogicSim.Views
{
    /// <summary>
    /// The simulation's net values on the GPU, one byte per net and four nets per word, copied from the engine once per
    /// frame. The wire and symbol shaders read the values of their nets from it, so showing values costs no work per
    /// wire or component on the CPU.
    /// </summary>
    public sealed class NetValues : IDisposable
    {
        public static readonly int BufferId = Shader.PropertyToID("_Nets");
        public static readonly int ShowId = Shader.PropertyToID("_ShowValues");

        private byte[] _bytes = new byte[0];
        private uint[] _words = new uint[1];
        private GraphicsBuffer _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));

        /// <summary>The simulation shows values (false outside it, or inside an IC opened from the library).</summary>
        public bool Shown { get; private set; }

        /// <summary>Copies the engine's current net values; call once per frame while the simulation shows values.</summary>
        public void Show(ISimulationEngine engine, int netCount)
        {
            if (_bytes.Length < netCount)
            {
                // Whole words, so that the bytes copy into them in one block.
                _bytes = new byte[(netCount + 3) & ~3];
                _words = new uint[Math.Max(1, _bytes.Length / 4)];
            }
            engine.CopyNetValues(_bytes);
            Buffer.BlockCopy(_bytes, 0, _words, 0, _bytes.Length);
            if (_buffer.count < _words.Length)
            {
                _buffer.Release();
                _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _words.Length, sizeof(uint));
            }
            _buffer.SetData(_words, 0, 0, Math.Max(1, (netCount + 3) / 4));
            Shown = true;
        }

        public void Hide() => Shown = false;

        /// <summary>Gives a material the values and whether to show them (before every draw: the buffer may be replaced).</summary>
        public void Bind(Material material)
        {
            material.SetBuffer(BufferId, _buffer);
            material.SetFloat(ShowId, Shown ? 1 : 0);
        }

        public void Dispose()
        {
            _buffer?.Release();
            _buffer = null;
        }
    }
}
