using System;
using System.Diagnostics;
using System.Threading;
using LogicSim.Simulation;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LogicSim.App
{
    /// <summary>
    /// Builds neural block lookup tables with a compute shader (one GPU thread per input pattern). Used for networks
    /// with 2^10 or more patterns when the platform supports compute shaders; otherwise the CPU builds them.
    /// Results can differ from the CPU only for patterns whose output is within float rounding of the 0.5 boundary.
    /// </summary>
    public sealed class ComputeLutBuilder : INeuralLutBuilder
    {
        private const string ShaderName = "NeuralLut";
        private const int MaxWidth = 64;

        private readonly ComputeShader _shader;
        private readonly int _kernel;
        private readonly int _mainThread;
        private bool _logged;

        private ComputeLutBuilder(ComputeShader shader)
        {
            _shader = shader;
            _kernel = shader.FindKernel("BuildLut");
            _mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>Installs the builder if the GPU and the shader are available. Call from the main thread.</summary>
        public static void Register()
        {
            if (!SystemInfo.supportsComputeShaders)
                return;
            var shader = Resources.Load<ComputeShader>(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning("[LogicSim] Compute shader " + ShaderName + " not found; neural lookup tables are built on the CPU.");
                return;
            }
            NeuralNetwork.LutBuilder = new ComputeLutBuilder(shader);
        }

        public ulong[] Build(NeuralNetwork network)
        {
            // GPU resources may only be used from the main thread; anything else falls back to the CPU.
            if (Thread.CurrentThread.ManagedThreadId != _mainThread || network.MaxWidth > MaxWidth)
                return null;

            var watch = Stopwatch.StartNew();
            int layers = network.LayerSizes.Count;
            int patterns = network.PatternCount;
            int outputs = network.OutputCount;
            int words32 = Math.Max(1, patterns >> 5);

            int weightCount = 0, biasCount = 0;
            for (int l = 0; l < network.ConnectionCount; l++)
            {
                weightCount += network.GetWeights(l).Count;
                biasCount += network.GetBiases(l).Count;
            }
            var weights = new float[weightCount];
            var biases = new float[biasCount];
            int w = 0, b = 0;
            for (int l = 0; l < network.ConnectionCount; l++)
            {
                foreach (var value in network.GetWeights(l))
                    weights[w++] = value;
                foreach (var value in network.GetBiases(l))
                    biases[b++] = value;
            }
            var sizes = new int[layers];
            for (int i = 0; i < layers; i++)
                sizes[i] = network.LayerSizes[i];

            var weightBuffer = new ComputeBuffer(weights.Length, sizeof(float));
            var biasBuffer = new ComputeBuffer(biases.Length, sizeof(float));
            var layerBuffer = new ComputeBuffer(sizes.Length, sizeof(int));
            var resultBuffer = new ComputeBuffer(outputs * words32, sizeof(uint));
            try
            {
                weightBuffer.SetData(weights);
                biasBuffer.SetData(biases);
                layerBuffer.SetData(sizes);
                resultBuffer.SetData(new uint[outputs * words32]);

                _shader.SetBuffer(_kernel, "Weights", weightBuffer);
                _shader.SetBuffer(_kernel, "Biases", biasBuffer);
                _shader.SetBuffer(_kernel, "Layers", layerBuffer);
                _shader.SetBuffer(_kernel, "Result", resultBuffer);
                _shader.SetInt("LayerCount", layers);
                _shader.SetInt("PatternCount", patterns);
                _shader.SetInt("WordsPerOutput", words32);
                _shader.SetInt("Activation", (int)network.Activation);
                _shader.Dispatch(_kernel, (patterns + 63) / 64, 1, 1);

                var result = new uint[outputs * words32];
                resultBuffer.GetData(result);

                int words64 = network.LutWordsPerOutput;
                var lut = new ulong[outputs * words64];
                for (int m = 0; m < outputs; m++)
                    for (int i = 0; i < words32; i++)
                        lut[m * words64 + (i >> 1)] |= (ulong)result[m * words32 + i] << ((i & 1) * 32);

                if (!_logged)
                {
                    _logged = true;
                    Debug.Log($"[LogicSim] Neural lookup table built on the GPU: {patterns} patterns x {outputs} outputs ({network.Describe()}) in {watch.Elapsed.TotalMilliseconds:0.0} ms");
                }
                return lut;
            }
            finally
            {
                weightBuffer.Release();
                biasBuffer.Release();
                layerBuffer.Release();
                resultBuffer.Release();
            }
        }
    }
}
