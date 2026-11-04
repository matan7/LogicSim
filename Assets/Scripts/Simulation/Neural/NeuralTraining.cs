using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicSim.Simulation
{
    /// <summary>Learns neuron weights from examples: the perceptron rule for single neurons, backpropagation for networks.</summary>
    public static class NeuralTraining
    {
        public sealed class Sample
        {
            public bool[] Inputs { get; }
            public bool[] Outputs { get; }

            public Sample(bool[] inputs, bool[] outputs)
            {
                Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
                Outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
            }
        }

        public sealed class Result
        {
            /// <summary>Trained network (network training only).</summary>
            public NeuralNetwork Network;

            /// <summary>Trained neuron (perceptron training only).</summary>
            public NeuronParameters Neuron;

            /// <summary>Samples whose outputs are all correct.</summary>
            public int Correct;
            public int Total;
            public int Epochs;
            public float Loss;

            public float Accuracy => Total == 0 ? 1 : (float)Correct / Total;
            public bool Perfect => Correct == Total;
        }

        /// <summary>
        /// Every input combination of <paramref name="circuit"/> with the expected outputs (from a reset state).
        /// Input/output order follows the given components, or the subcircuit pin order when omitted.
        /// </summary>
        public static List<Sample> SamplesFromCircuit(Circuit circuit, IList<CircuitComponent> inputs = null, IList<CircuitComponent> outputs = null)
        {
            if (inputs == null || outputs == null)
            {
                var pins = new SubcircuitDefinition("training", "training", circuit);
                inputs = inputs ?? pins.InputComponentIds.Select(circuit.GetComponent).ToList();
                outputs = outputs ?? pins.OutputComponentIds.Select(circuit.GetComponent).ToList();
            }
            var table = TruthTableGenerator.Generate(circuit, inputs, outputs);
            if (!table.AllStable)
                throw new CircuitException("The circuit does not settle for every input (it oscillates), so it cannot be learned");
            return table.Rows.Select(r => new Sample(r.Inputs.ToArray(), r.Outputs.ToArray())).ToList();
        }

        public sealed class Options
        {
            public int? Epochs;
            public float? LearningRate;
            public int Seed = 1;

            /// <summary>Neural block: hidden layer sizes to start from (null keeps the current shape when the pins match).</summary>
            public IReadOnlyList<int> Hidden;

            public NeuralActivation? Activation;
        }

        /// <summary>
        /// Samples for input patterns 0, 1, 2, ... (bit i of the pattern is input i); <paramref name="targets"/>[p] holds the
        /// expected outputs for pattern p.
        /// </summary>
        public static List<Sample> SamplesFromTargets(IReadOnlyList<bool[]> targets)
        {
            int count = targets?.Count ?? 0;
            int bits = 0;
            while ((1 << bits) < count)
                bits++;
            if (count < 2 || (1 << bits) != count || bits > NeuralNetwork.MaxInputs)
                throw new CircuitException("Give one target per input pattern (2, 4, 8, ... entries)");
            if (targets.Select(t => t.Length).Distinct().Count() != 1 || targets[0].Length == 0)
                throw new CircuitException("Every target needs the same number of outputs");
            return Enumerable.Range(0, count)
                .Select(p => new Sample(Enumerable.Range(0, bits).Select(i => (p >> i & 1) != 0).ToArray(), targets[p]))
                .ToList();
        }

        /// <summary>
        /// Parses targets such as "0110" (one output, one digit per pattern) or "00 01 01 10" (several outputs, one group
        /// per pattern, output 0 first).
        /// </summary>
        public static List<Sample> SamplesFromText(string text)
        {
            var groups = (text ?? string.Empty).Split(new[] { ' ', ',', ';', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<bool[]> targets;
            if (groups.Length == 1)
                targets = groups[0].Select(c => new[] { ParseBit(c) }).ToList();
            else
                targets = groups.Select(g => g.Select(ParseBit).ToArray()).ToList();
            return SamplesFromTargets(targets);
        }

        private static bool ParseBit(char c)
        {
            if (c == '0')
                return false;
            if (c == '1')
                return true;
            throw new CircuitException($"Targets are written with 0 and 1 (found '{c}')");
        }

        /// <summary>
        /// Trains a Neuron (perceptron rule) or NeuralBlock (backpropagation; the block is resized when the samples have
        /// another number of inputs or outputs) and returns the properties to apply with a <see cref="SetPropertiesEdit"/>.
        /// </summary>
        public static Result TrainComponent(CircuitComponent component, IReadOnlyList<Sample> samples, Options options, out ComponentProperties properties)
        {
            options = options ?? new Options();
            if (samples == null || samples.Count == 0)
                throw new CircuitException("No training samples");
            int inputs = samples[0].Inputs.Length;
            int outputs = samples[0].Outputs.Length;
            properties = component.GetProperties();
            Result result;
            switch (component.Kind)
            {
                case ComponentKind.Neuron:
                    if (outputs != 1)
                        throw new CircuitException("A neuron has one output; use a neural block for several");
                    result = TrainPerceptron(component.Neuron.WithInputCount(inputs), samples, options.Epochs ?? 1000, options.LearningRate ?? 0.1f);
                    properties.Neuron = result.Neuron;
                    properties.InputCount = inputs;
                    return result;
                case ComponentKind.NeuralBlock:
                {
                    var current = component.Network;
                    var activation = options.Activation ?? current.Activation;
                    var start = current;
                    if (options.Hidden != null || current.InputCount != inputs || current.OutputCount != outputs || activation != current.Activation)
                    {
                        var hidden = options.Hidden ?? (current.InputCount == inputs
                            ? (IReadOnlyList<int>)current.LayerSizes.Skip(1).Take(current.LayerSizes.Count - 2).ToList()
                            : new[] { Math.Min(NeuralNetwork.MaxLayerSize, Math.Max(4, inputs * 2)) });
                        start = NeuralNetwork.Create(inputs, hidden, outputs, activation, options.Seed);
                    }
                    result = TrainNetwork(start, samples, options.Epochs ?? 3000, options.LearningRate ?? 0.5f, options.Seed);
                    properties.Network = result.Network;
                    return result;
                }
                case ComponentKind.SpikingNeuron:
                    throw new CircuitException("Spiking neurons react over time and are not trained from truth tables; adjust weights, threshold and leak instead");
                default:
                    throw new CircuitException($"{component} cannot be trained");
            }
        }

        private static void Check(IReadOnlyList<Sample> samples, int inputs, int outputs)
        {
            if (samples == null || samples.Count == 0)
                throw new CircuitException("No training samples");
            foreach (var sample in samples)
            {
                if (sample.Inputs.Length != inputs)
                    throw new CircuitException($"Every sample needs {inputs} inputs (got {sample.Inputs.Length})");
                if (sample.Outputs.Length != outputs)
                    throw new CircuitException($"Every sample needs {outputs} outputs (got {sample.Outputs.Length})");
            }
        }

        /// <summary>
        /// Rosenblatt's perceptron rule. Converges when the function is linearly separable (AND, OR, majority, ...);
        /// for others (XOR) the best weights found are returned with an accuracy below 1.
        /// </summary>
        public static Result TrainPerceptron(NeuronParameters start, IReadOnlyList<Sample> samples, int epochs = 1000, float learningRate = 0.1f)
        {
            int n = start.Weights.Count;
            Check(samples, n, 1);
            var w = start.Weights.ToArray();
            float threshold = start.Threshold;
            var best = (w: (float[])w.Clone(), threshold, correct: Count(w, threshold, samples));
            bool checkEveryUpdate = samples.Count <= 256;
            int epoch = 0;
            for (; epoch < epochs && best.correct < samples.Count; epoch++)
            {
                foreach (var sample in samples)
                {
                    float sum = 0;
                    for (int i = 0; i < n; i++)
                        if (sample.Inputs[i])
                            sum += w[i];
                    int error = (sample.Outputs[0] ? 1 : 0) - (sum >= threshold ? 1 : 0);
                    if (error == 0)
                        continue;
                    for (int i = 0; i < n; i++)
                        if (sample.Inputs[i])
                            w[i] += learningRate * error;
                    threshold -= learningRate * error;
                    // Pocket algorithm: keep the best weights seen, which matters when the rule never converges.
                    if (checkEveryUpdate)
                    {
                        int correct = Count(w, threshold, samples);
                        if (correct > best.correct)
                            best = ((float[])w.Clone(), threshold, correct);
                    }
                }
                if (!checkEveryUpdate)
                {
                    int correct = Count(w, threshold, samples);
                    if (correct > best.correct)
                        best = ((float[])w.Clone(), threshold, correct);
                }
            }
            var neuron = new NeuronParameters(best.w.Select(Round), Round(best.threshold), start.Leak, start.Reset, start.Refractory);
            return new Result { Neuron = neuron, Correct = Count(neuron.Weights.ToArray(), neuron.Threshold, samples), Total = samples.Count, Epochs = epoch };
        }

        // Keeps learned values readable (0.30000001 -> 0.3) without changing the decisions in practice.
        private static float Round(float value) => (float)Math.Round(value, 4);

        private static int Count(float[] w, float threshold, IReadOnlyList<Sample> samples)
        {
            int correct = 0;
            foreach (var sample in samples)
            {
                float sum = 0;
                for (int i = 0; i < w.Length; i++)
                    if (sample.Inputs[i])
                        sum += w[i];
                if ((sum >= threshold) == sample.Outputs[0])
                    correct++;
            }
            return correct;
        }

        /// <summary>
        /// Stochastic gradient descent with backpropagation (sigmoid outputs, cross-entropy loss). Stops early once every
        /// sample is classified correctly with a margin. The work is capped so 16-input tables still finish quickly.
        /// </summary>
        public static Result TrainNetwork(NeuralNetwork start, IReadOnlyList<Sample> samples, int epochs = 3000, float learningRate = 0.5f, int seed = 1)
        {
            Check(samples, start.InputCount, start.OutputCount);
            const long MaxSamplePasses = 2_000_000;
            epochs = (int)Math.Max(1, Math.Min(epochs, MaxSamplePasses / samples.Count));

            var layers = start.LayerSizes.ToArray();
            int connections = layers.Length - 1;
            var w = start.CopyWeights();
            var b = start.CopyBiases();
            var a = layers.Select(n => new float[n]).ToArray();
            var delta = layers.Select(n => new float[n]).ToArray();
            var order = Enumerable.Range(0, samples.Count).ToArray();
            var random = new Random(seed);
            var activation = start.Activation;
            float loss = 0;
            int epoch = 0;

            for (; epoch < epochs; epoch++)
            {
                for (int i = order.Length - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }

                loss = 0;
                bool allConfident = true;
                foreach (var index in order)
                {
                    var sample = samples[index];
                    for (int i = 0; i < layers[0]; i++)
                        a[0][i] = sample.Inputs[i] ? 1 : 0;
                    Forward(layers, w, b, a, activation);

                    // Output layer: sigmoid + cross-entropy => gradient is (y - t).
                    var output = a[connections];
                    var outDelta = delta[connections];
                    for (int m = 0; m < output.Length; m++)
                    {
                        float y = output[m];
                        float t = sample.Outputs[m] ? 1 : 0;
                        outDelta[m] = y - t;
                        loss -= t * (float)Math.Log(Math.Max(1e-7, y)) + (1 - t) * (float)Math.Log(Math.Max(1e-7, 1 - y));
                        if (Math.Abs(y - t) > 0.3f)
                            allConfident = false;
                    }

                    // Hidden layers.
                    for (int l = connections - 1; l >= 1; l--)
                    {
                        int size = layers[l], next = layers[l + 1];
                        var weights = w[l];
                        for (int i = 0; i < size; i++)
                        {
                            float sum = 0;
                            for (int j = 0; j < next; j++)
                                sum += weights[j * size + i] * delta[l + 1][j];
                            delta[l][i] = sum * Derivative(activation, a[l][i]);
                        }
                    }

                    // Gradient step.
                    for (int l = 0; l < connections; l++)
                    {
                        int from = layers[l], to = layers[l + 1];
                        var weights = w[l];
                        var biases = b[l];
                        for (int j = 0; j < to; j++)
                        {
                            float g = learningRate * delta[l + 1][j];
                            if (g == 0)
                                continue;
                            int row = j * from;
                            for (int i = 0; i < from; i++)
                                weights[row + i] -= g * a[l][i];
                            biases[j] -= g;
                        }
                    }
                }
                loss /= samples.Count;
                if (allConfident)
                {
                    epoch++;
                    break;
                }
            }

            var network = new NeuralNetwork(layers, activation, w, b);
            int correct = 0;
            foreach (var sample in samples)
            {
                int bits = 0;
                for (int i = 0; i < sample.Inputs.Length; i++)
                    if (sample.Inputs[i])
                        bits |= 1 << i;
                var outputs = network.Evaluate(bits);
                if (outputs.SequenceEqual(sample.Outputs))
                    correct++;
            }
            return new Result { Network = network, Correct = correct, Total = samples.Count, Epochs = epoch, Loss = loss };
        }

        private static void Forward(int[] layers, float[][] w, float[][] b, float[][] a, NeuralActivation activation)
        {
            int connections = layers.Length - 1;
            for (int l = 0; l < connections; l++)
            {
                int from = layers[l], to = layers[l + 1];
                var weights = w[l];
                bool last = l == connections - 1;
                for (int j = 0; j < to; j++)
                {
                    float z = b[l][j];
                    int row = j * from;
                    for (int i = 0; i < from; i++)
                        z += weights[row + i] * a[l][i];
                    a[l + 1][j] = NeuralNetwork.Activate(last ? NeuralActivation.Sigmoid : activation, z);
                }
            }
        }

        private static float Derivative(NeuralActivation activation, float output)
        {
            switch (activation)
            {
                case NeuralActivation.Tanh: return 1 - output * output;
                case NeuralActivation.Relu: return output > 0 ? 1 : 0;
                default: return output * (1 - output);
            }
        }
    }
}
