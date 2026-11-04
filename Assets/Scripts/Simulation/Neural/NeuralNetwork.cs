using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace LogicSim.Simulation
{
    public enum NeuralActivation
    {
        Sigmoid,
        Tanh,
        Relu
    }

    /// <summary>
    /// Builds the lookup tables of a network on other hardware (the Unity app registers a compute shader).
    /// Returns null to fall back to the CPU.
    /// </summary>
    public interface INeuralLutBuilder
    {
        ulong[] Build(NeuralNetwork network);
    }

    /// <summary>
    /// Small fully connected network (multi-layer perceptron) with binary inputs and outputs, used by
    /// <see cref="ComponentKind.NeuralBlock"/>. Hidden layers use <see cref="Activation"/>, outputs a sigmoid read as
    /// 1 when it is at least 0.5. Immutable; training returns a new network.
    /// <para>
    /// Because the inputs are bits, the network is a pure function of at most 2^16 input patterns: the compiler turns
    /// every output into a lookup table once (on the CPU or the GPU), and the simulation only does table lookups.
    /// </para>
    /// </summary>
    public sealed class NeuralNetwork : IEquatable<NeuralNetwork>
    {
        public const int MaxInputs = 16;
        public const int MaxOutputs = 16;
        public const int MaxHiddenLayers = 4;
        public const int MaxLayerSize = 64;

        private readonly int[] _layers;
        private readonly float[][] _weights;
        private readonly float[][] _biases;
        private readonly object _lutLock = new object();
        private ulong[] _lut;

        /// <summary>Neurons per layer, inputs first and outputs last.</summary>
        public IReadOnlyList<int> LayerSizes => _layers;
        public int InputCount => _layers[0];
        public int OutputCount => _layers[_layers.Length - 1];
        public NeuralActivation Activation { get; }

        /// <summary>Number of weight layers (connections between consecutive layers).</summary>
        public int ConnectionCount => _layers.Length - 1;

        /// <summary>Optional GPU (or other) builder for lookup tables; set by the host application.</summary>
        public static INeuralLutBuilder LutBuilder { get; set; }

        /// <param name="weights">Per connection layer, row-major [to * from].</param>
        public NeuralNetwork(IEnumerable<int> layers, NeuralActivation activation, IEnumerable<IEnumerable<float>> weights, IEnumerable<IEnumerable<float>> biases)
        {
            _layers = (layers ?? throw new ArgumentNullException(nameof(layers))).ToArray();
            Validate(_layers);
            Activation = activation;
            _weights = (weights ?? throw new ArgumentNullException(nameof(weights))).Select(w => w.Select(Sanitize).ToArray()).ToArray();
            _biases = (biases ?? throw new ArgumentNullException(nameof(biases))).Select(b => b.Select(Sanitize).ToArray()).ToArray();
            if (_weights.Length != _layers.Length - 1 || _biases.Length != _layers.Length - 1)
                throw new CircuitException($"A network with {_layers.Length} layers needs {_layers.Length - 1} weight and bias layers");
            for (int l = 0; l < _weights.Length; l++)
            {
                if (_weights[l].Length != _layers[l] * _layers[l + 1])
                    throw new CircuitException($"Weight layer {l} needs {_layers[l] * _layers[l + 1]} values, got {_weights[l].Length}");
                if (_biases[l].Length != _layers[l + 1])
                    throw new CircuitException($"Bias layer {l} needs {_layers[l + 1]} values, got {_biases[l].Length}");
            }
        }

        private static float Sanitize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;

        private static void Validate(int[] layers)
        {
            if (layers.Length < 2)
                throw new CircuitException("A network needs an input and an output layer");
            if (layers.Length - 2 > MaxHiddenLayers)
                throw new CircuitException($"At most {MaxHiddenLayers} hidden layers");
            if (layers[0] < 1 || layers[0] > MaxInputs)
                throw new CircuitException($"A neural block has 1..{MaxInputs} inputs");
            if (layers[layers.Length - 1] < 1 || layers[layers.Length - 1] > MaxOutputs)
                throw new CircuitException($"A neural block has 1..{MaxOutputs} outputs");
            for (int i = 1; i < layers.Length - 1; i++)
                if (layers[i] < 1 || layers[i] > MaxLayerSize)
                    throw new CircuitException($"Hidden layers have 1..{MaxLayerSize} neurons");
        }

        /// <summary>Random (Xavier) initialization, reproducible through <paramref name="seed"/>.</summary>
        public static NeuralNetwork Create(int inputs, IEnumerable<int> hidden, int outputs, NeuralActivation activation = NeuralActivation.Sigmoid, int seed = 1)
        {
            var layers = new List<int> { inputs };
            layers.AddRange(hidden ?? Enumerable.Empty<int>());
            layers.Add(outputs);
            Validate(layers.ToArray());
            var random = new Random(seed);
            var weights = new List<float[]>();
            var biases = new List<float[]>();
            for (int l = 0; l < layers.Count - 1; l++)
            {
                int from = layers[l], to = layers[l + 1];
                float limit = (float)Math.Sqrt(6.0 / (from + to));
                var w = new float[from * to];
                for (int i = 0; i < w.Length; i++)
                    w[i] = (float)(random.NextDouble() * 2 - 1) * limit;
                weights.Add(w);
                biases.Add(new float[to]);
            }
            return new NeuralNetwork(layers, activation, weights, biases);
        }

        private static readonly Lazy<NeuralNetwork> _defaultNetwork = new Lazy<NeuralNetwork>(() =>
        {
            // A fresh block computes XOR, the classic function a single neuron cannot learn.
            var start = Create(2, new[] { 4 }, 1, NeuralActivation.Sigmoid, seed: 7);
            var samples = Enumerable.Range(0, 4).Select(i => new NeuralTraining.Sample(
                new[] { (i & 1) != 0, (i & 2) != 0 }, new[] { ((i & 1) ^ (i >> 1 & 1)) != 0 })).ToList();
            return NeuralTraining.TrainNetwork(start, samples, epochs: 5000, learningRate: 0.8f).Network;
        });

        public static NeuralNetwork Default => _defaultNetwork.Value;

        public IReadOnlyList<float> GetWeights(int connection) => _weights[connection];
        public IReadOnlyList<float> GetBiases(int connection) => _biases[connection];

        internal float[][] CopyWeights() => _weights.Select(w => (float[])w.Clone()).ToArray();
        internal float[][] CopyBiases() => _biases.Select(b => (float[])b.Clone()).ToArray();

        public NeuralNetwork WithActivation(NeuralActivation activation) =>
            activation == Activation ? this : new NeuralNetwork(_layers, activation, _weights, _biases);

        public int MaxWidth => _layers.Max();

        #region Evaluation

        internal static float Activate(NeuralActivation activation, float z)
        {
            switch (activation)
            {
                case NeuralActivation.Tanh: return (float)Math.Tanh(z);
                case NeuralActivation.Relu: return z > 0 ? z : 0;
                default: return 1f / (1f + (float)Math.Exp(-z));
            }
        }

        /// <summary>
        /// Runs the network. <paramref name="activations"/>[l] receives the outputs of layer l (index 0 = inputs);
        /// the last layer holds the output pre-activations (sigmoid not applied).
        /// </summary>
        internal void ForwardRaw(float[][] activations)
        {
            for (int l = 0; l < _weights.Length; l++)
            {
                var input = activations[l];
                var output = activations[l + 1];
                var w = _weights[l];
                var b = _biases[l];
                int from = _layers[l], to = _layers[l + 1];
                bool last = l == _weights.Length - 1;
                for (int j = 0; j < to; j++)
                {
                    float z = b[j];
                    int row = j * from;
                    for (int i = 0; i < from; i++)
                        z += w[row + i] * input[i];
                    output[j] = last ? z : Activate(Activation, z);
                }
            }
        }

        internal float[][] CreateScratch() => _layers.Select(n => new float[n]).ToArray();

        /// <summary>Output probabilities (sigmoid) for real-valued inputs.</summary>
        public float[] Forward(IReadOnlyList<float> inputs)
        {
            var scratch = CreateScratch();
            for (int i = 0; i < InputCount; i++)
                scratch[0][i] = i < inputs.Count ? inputs[i] : 0;
            ForwardRaw(scratch);
            return scratch[scratch.Length - 1].Select(z => Activate(NeuralActivation.Sigmoid, z)).ToArray();
        }

        /// <summary>Binary outputs for input bits (bit i = input i).</summary>
        public bool[] Evaluate(int inputBits)
        {
            var scratch = CreateScratch();
            SetInputBits(scratch[0], inputBits);
            ForwardRaw(scratch);
            return scratch[scratch.Length - 1].Select(z => z >= 0).ToArray();
        }

        private static void SetInputBits(float[] input, int bits)
        {
            for (int i = 0; i < input.Length; i++)
                input[i] = (bits >> i) & 1;
        }

        #endregion

        #region Lookup tables

        public int PatternCount => 1 << InputCount;

        /// <summary>64-bit words per output table.</summary>
        public int LutWordsPerOutput => Math.Max(1, PatternCount >> 6);

        /// <summary>
        /// Lookup tables of all outputs: output m occupies words [m * <see cref="LutWordsPerOutput"/>, ...), bit p of
        /// that range is the output for input pattern p. Built once per network and cached.
        /// </summary>
        public ulong[] GetLut()
        {
            lock (_lutLock)
            {
                if (_lut != null)
                    return _lut;
                ulong[] lut = null;
                var builder = LutBuilder;
                if (builder != null && PatternCount >= 1024)
                {
                    try
                    {
                        lut = builder.Build(this);
                    }
                    catch (Exception)
                    {
                        lut = null;
                    }
                    if (lut != null && lut.Length != LutWordsPerOutput * OutputCount)
                        lut = null;
                }
                _lut = lut ?? BuildLutOnCpu();
                return _lut;
            }
        }

        public ulong[] BuildLutOnCpu()
        {
            int words = LutWordsPerOutput;
            int patterns = PatternCount;
            var lut = new ulong[words * OutputCount];
            // Patterns are split into blocks of 64 so each block fills whole words without sharing them.
            int blocks = Math.Max(1, patterns >> 6);
            void Run(int block)
            {
                var scratch = CreateScratch();
                var outputs = scratch[scratch.Length - 1];
                int start = block << 6;
                int end = Math.Min(patterns, start + 64);
                for (int p = start; p < end; p++)
                {
                    SetInputBits(scratch[0], p);
                    ForwardRaw(scratch);
                    for (int m = 0; m < outputs.Length; m++)
                        if (outputs[m] >= 0)
                            lut[m * words + (p >> 6)] |= 1UL << (p & 63);
                }
            }
            if (blocks >= 64)
                Parallel.For(0, blocks, Run);
            else
                for (int block = 0; block < blocks; block++)
                    Run(block);
            return lut;
        }

        #endregion

        public string Describe()
        {
            return string.Join("-", _layers.Select(n => n.ToString(CultureInfo.InvariantCulture))) + " " + Activation.ToString().ToLowerInvariant();
        }

        public bool Equals(NeuralNetwork other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other == null || Activation != other.Activation || !_layers.SequenceEqual(other._layers))
                return false;
            for (int l = 0; l < _weights.Length; l++)
                if (!_weights[l].SequenceEqual(other._weights[l]) || !_biases[l].SequenceEqual(other._biases[l]))
                    return false;
            return true;
        }

        public override bool Equals(object obj) => obj is NeuralNetwork other && Equals(other);

        public override int GetHashCode()
        {
            var hash = (int)Activation;
            foreach (var n in _layers)
                hash = HashCode.Combine(hash, n);
            foreach (var layer in _weights)
                foreach (var w in layer)
                    hash = HashCode.Combine(hash, w);
            return hash;
        }

        public override string ToString() => Describe();
    }
}
