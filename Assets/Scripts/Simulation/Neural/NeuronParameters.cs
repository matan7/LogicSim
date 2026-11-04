using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicSim.Simulation
{
    /// <summary>
    /// Settings of a <see cref="ComponentKind.Neuron"/> (perceptron) or <see cref="ComponentKind.SpikingNeuron"/>
    /// (leaky integrate-and-fire). Immutable: edits create a new instance, so undo snapshots can share it.
    /// </summary>
    public sealed class NeuronParameters : IEquatable<NeuronParameters>
    {
        public const int MaxInputs = 16;

        private readonly float[] _weights;

        /// <summary>One weight per input. Inputs are 0 or 1, so a weight is what that input adds when it is high.</summary>
        public IReadOnlyList<float> Weights => _weights;

        /// <summary>Perceptron: fires when the weighted sum reaches it. Spiking: membrane potential that triggers a spike.</summary>
        public float Threshold { get; }

        /// <summary>Spiking only: fraction of the potential kept from one tick to the next (0 = no memory, 1 = no leak).</summary>
        public float Leak { get; }

        /// <summary>Spiking only: potential after a spike.</summary>
        public float Reset { get; }

        /// <summary>Spiking only: ticks after a spike during which inputs are ignored.</summary>
        public int Refractory { get; }

        public NeuronParameters(IEnumerable<float> weights, float threshold, float leak = 0.8f, float reset = 0f, int refractory = 2)
        {
            _weights = (weights ?? Enumerable.Empty<float>()).Select(Sanitize).ToArray();
            if (_weights.Length == 0 || _weights.Length > MaxInputs)
                throw new CircuitException($"A neuron needs 1..{MaxInputs} weights");
            Threshold = Sanitize(threshold);
            Leak = Math.Max(0f, Math.Min(1f, Sanitize(leak)));
            Reset = Sanitize(reset);
            Refractory = Math.Max(0, Math.Min(1_000_000, refractory));
        }

        private static float Sanitize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;

        /// <summary>Perceptron default: AND of two inputs (1 + 1 >= 1.5).</summary>
        public static NeuronParameters DefaultPerceptron(int inputs)
        {
            inputs = Math.Max(1, Math.Min(MaxInputs, inputs));
            return new NeuronParameters(Enumerable.Repeat(1f, inputs), inputs - 0.5f);
        }

        /// <summary>Spiking default: one input alone needs two ticks to charge up, both together fire at once.</summary>
        public static NeuronParameters DefaultSpiking(int inputs)
        {
            inputs = Math.Max(1, Math.Min(MaxInputs, inputs));
            return new NeuronParameters(Enumerable.Repeat(0.6f, inputs), 1f, 0.8f, 0f, 2);
        }

        public static NeuronParameters Default(ComponentKind kind, int inputs)
        {
            return kind == ComponentKind.SpikingNeuron ? DefaultSpiking(inputs) : DefaultPerceptron(inputs);
        }

        /// <summary>Same settings for another number of inputs: extra inputs copy the last weight, removed ones are dropped.</summary>
        public NeuronParameters WithInputCount(int inputs)
        {
            inputs = Math.Max(1, Math.Min(MaxInputs, inputs));
            if (inputs == _weights.Length)
                return this;
            var weights = new float[inputs];
            for (int i = 0; i < inputs; i++)
                weights[i] = i < _weights.Length ? _weights[i] : _weights[_weights.Length - 1];
            return new NeuronParameters(weights, Threshold, Leak, Reset, Refractory);
        }

        public NeuronParameters WithWeights(IEnumerable<float> weights) => new NeuronParameters(weights, Threshold, Leak, Reset, Refractory);
        public NeuronParameters WithThreshold(float threshold) => new NeuronParameters(_weights, threshold, Leak, Reset, Refractory);
        public NeuronParameters WithLeak(float leak) => new NeuronParameters(_weights, Threshold, leak, Reset, Refractory);
        public NeuronParameters WithReset(float reset) => new NeuronParameters(_weights, Threshold, Leak, reset, Refractory);
        public NeuronParameters WithRefractory(int ticks) => new NeuronParameters(_weights, Threshold, Leak, Reset, ticks);

        /// <summary>Perceptron output for the given input bits (bit i = input i).</summary>
        public bool Fires(int inputBits)
        {
            float sum = 0;
            for (int i = 0; i < _weights.Length; i++)
                if ((inputBits >> i & 1) != 0)
                    sum += _weights[i];
            return sum >= Threshold;
        }

        public bool Equals(NeuronParameters other)
        {
            if (ReferenceEquals(this, other))
                return true;
            return other != null && Threshold == other.Threshold && Leak == other.Leak && Reset == other.Reset
                && Refractory == other.Refractory && _weights.SequenceEqual(other._weights);
        }

        public override bool Equals(object obj) => obj is NeuronParameters other && Equals(other);

        public override int GetHashCode()
        {
            var hash = HashCode.Combine(Threshold, Leak, Reset, Refractory);
            foreach (var w in _weights)
                hash = HashCode.Combine(hash, w);
            return hash;
        }

        public override string ToString()
        {
            return $"w=[{string.Join(", ", _weights.Select(w => w.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)))}] threshold={Threshold}";
        }
    }
}
