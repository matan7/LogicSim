using System;
using System.Collections.Generic;
using System.Linq;
using LogicSim.Simulation;
using Newtonsoft.Json.Linq;

namespace LogicSim.Mcp
{
    // Neuron arguments and train_component.
    public sealed partial class LogicSimTools
    {
        /// <summary>Reads neuron / network arguments (weights, threshold, layers, ...) into <paramref name="properties"/>.</summary>
        private static void ApplyNeuralArguments(CircuitComponent component, JObject args, ref ComponentProperties properties)
        {
            bool neuronArgs = args["weights"] != null || args["threshold"] != null || args["leak"] != null || args["reset"] != null || args["refractory"] != null;
            bool networkArgs = args["layers"] != null || args["activation"] != null;
            if (neuronArgs && !component.IsNeuron)
                throw new McpToolException("weights/threshold/leak/reset/refractory only apply to Neuron and SpikingNeuron");
            if (networkArgs && component.Kind != ComponentKind.NeuralBlock)
                throw new McpToolException("layers/activation only apply to NeuralBlock");

            if (component.IsNeuron)
            {
                var neuron = properties.Neuron ?? NeuronParameters.Default(component.Kind, properties.InputCount);
                if (args["inputs"] != null && args["weights"] == null)
                    neuron = neuron.WithInputCount((int)args["inputs"]);
                if (args["weights"] is JArray weights)
                {
                    neuron = neuron.WithWeights(weights.Select(w => (float)w));
                    properties.InputCount = neuron.Weights.Count;
                }
                if (args["threshold"] != null)
                    neuron = neuron.WithThreshold((float)args["threshold"]);
                if (args["leak"] != null)
                    neuron = neuron.WithLeak((float)args["leak"]);
                if (args["reset"] != null)
                    neuron = neuron.WithReset((float)args["reset"]);
                if (args["refractory"] != null)
                    neuron = neuron.WithRefractory((int)args["refractory"]);
                properties.Neuron = neuron;
            }
            else if (component.Kind == ComponentKind.NeuralBlock && networkArgs)
            {
                var current = properties.Network ?? NeuralNetwork.Default;
                var activation = args["activation"] != null ? ParseActivation((string)args["activation"]) : current.Activation;
                if (args["layers"] is JArray layers)
                {
                    var sizes = layers.Select(l => (int)l).ToList();
                    if (sizes.Count < 2)
                        throw new McpToolException("'layers' needs at least an input and an output size, e.g. [2, 4, 1]");
                    int seed = args["seed"] != null ? (int)args["seed"] : 1;
                    properties.Network = NeuralNetwork.Create(sizes[0], sizes.Skip(1).Take(sizes.Count - 2), sizes[sizes.Count - 1], activation, seed);
                }
                else
                {
                    properties.Network = current.WithActivation(activation);
                }
            }
        }

        private static NeuralActivation ParseActivation(string text)
        {
            if (Enum.TryParse(text?.Trim(), true, out NeuralActivation activation))
                return activation;
            throw new McpToolException($"Unknown activation '{text}' (use sigmoid, tanh or relu)");
        }

        private McpToolResult TrainComponent(JObject args)
        {
            var component = ResolveComponent(Required(args, "component"));
            if (component.Kind == ComponentKind.SpikingNeuron)
                throw new McpToolException("Spiking neurons react over time and are not trained from truth tables; adjust weights, threshold and leak instead");
            if (component.Kind != ComponentKind.Neuron && component.Kind != ComponentKind.NeuralBlock)
                throw new McpToolException($"{component} is not a Neuron or NeuralBlock");

            List<NeuralTraining.Sample> samples;
            try
            {
                if (args["subcircuit"] != null)
                {
                    var definition = ResolveDefinition(args["subcircuit"]);
                    samples = NeuralTraining.SamplesFromCircuit(definition.Circuit);
                }
                else if (args["targets"] is JArray targets)
                {
                    samples = SamplesFromTargets(targets);
                }
                else if (args["rows"] is JArray rows)
                {
                    samples = rows.Select(r => new NeuralTraining.Sample(
                        ((JArray)r["inputs"]).Select(ParseBool).ToArray(),
                        ((JArray)r["outputs"]).Select(ParseBool).ToArray())).ToList();
                }
                else
                {
                    throw new McpToolException("Pass 'subcircuit', 'targets' or 'rows'");
                }
            }
            catch (CircuitException exception)
            {
                throw new McpToolException(exception.Message);
            }

            var options = new NeuralTraining.Options
            {
                Epochs = args["epochs"] != null ? (int)args["epochs"] : (int?)null,
                LearningRate = args["learning_rate"] != null ? (float)args["learning_rate"] : (float?)null,
                Seed = args["seed"] != null ? (int)args["seed"] : 1,
                Hidden = args["hidden"] is JArray hidden ? hidden.Select(v => (int)v).ToList() : null,
                Activation = args["activation"] != null ? ParseActivation((string)args["activation"]) : (NeuralActivation?)null
            };
            NeuralTraining.Result result;
            try
            {
                result = NeuralTraining.TrainComponent(component, samples, options, out var properties);
                _workspace.Editor.Apply(new SetPropertiesEdit(Circuit, component.Id, properties));
            }
            catch (CircuitException exception)
            {
                throw new McpToolException(exception.Message);
            }

            var output = new JObject
            {
                ["component"] = component.Id,
                ["accuracy"] = Math.Round(result.Accuracy, 4),
                ["correct_rows"] = result.Correct,
                ["rows"] = result.Total,
                ["epochs"] = result.Epochs
            };
            if (component.Kind == ComponentKind.NeuralBlock)
            {
                output["loss"] = Math.Round(result.Loss, 5);
                output["network"] = component.Network.Describe();
            }
            else
            {
                output["weights"] = new JArray(component.Neuron.Weights);
                output["threshold"] = component.Neuron.Threshold;
            }
            if (!result.Perfect)
                output["note"] = component.Kind == ComponentKind.Neuron
                    ? "A single neuron can only learn linearly separable functions (not XOR); use a NeuralBlock."
                    : "Not every row was learned; try more epochs, a larger hidden layer or another seed.";
            return McpToolResult.Ok(output);
        }

        private static List<NeuralTraining.Sample> SamplesFromTargets(JArray targets)
        {
            var values = new List<bool[]>();
            foreach (var token in targets)
            {
                if (token is JArray array)
                    values.Add(array.Select(ParseBool).ToArray());
                else if (token.Type == JTokenType.String && ((string)token).Length > 1)
                    values.Add(((string)token).Where(c => c == '0' || c == '1').Select(c => c == '1').ToArray());
                else
                    values.Add(new[] { ParseBool(token) });
            }
            return NeuralTraining.SamplesFromTargets(values);
        }
    }
}
