using System.Collections.Generic;
using System.Linq;
using LogicSim.Simulation;
using NUnit.Framework;

namespace LogicSim.Tests
{
    public class NeuronTests
    {
        private static TestCircuit Wrap(ComponentKind kind, int inputs, out CircuitComponent component, out CircuitComponent[] switches)
        {
            var t = new TestCircuit();
            switches = Enumerable.Range(0, inputs).Select(i => t.Add(ComponentKind.Switch, "I" + i)).ToArray();
            component = t.Add(kind, inputs: inputs);
            var bulb = t.Add(ComponentKind.Bulb, "Y");
            for (int i = 0; i < inputs; i++)
                t.Connect(switches[i], component, i);
            t.Connect(component, bulb);
            return t;
        }

        /// <summary>Output for every input pattern; bit i of the pattern = input I(i).</summary>
        private static bool[] Outputs(TestCircuit t, CircuitComponent[] switches)
        {
            var table = TruthTableGenerator.Generate(t.Circuit, switches, new[] { t.Circuit.FindByLabel("Y") });
            var result = new bool[1 << switches.Length];
            foreach (var row in table.Rows)
            {
                int bits = 0;
                for (int i = 0; i < row.Inputs.Length; i++)
                    if (row.Inputs[i])
                        bits |= 1 << i;
                result[bits] = row.Outputs[0];
            }
            return result;
        }

        private static List<NeuralTraining.Sample> Samples(int inputs, System.Func<int, bool> function)
        {
            return Enumerable.Range(0, 1 << inputs).Select(p => new NeuralTraining.Sample(
                Enumerable.Range(0, inputs).Select(i => (p >> i & 1) != 0).ToArray(), new[] { function(p) })).ToList();
        }

        [Test]
        public void Perceptron_DefaultsToAnd_AndFollowsItsWeights()
        {
            var t = Wrap(ComponentKind.Neuron, 2, out var neuron, out var switches);
            Assert.That(Outputs(t, switches), Is.EqualTo(new[] { false, false, false, true }));

            var props = neuron.GetProperties();
            props.Neuron = neuron.Neuron.WithWeights(new[] { 1f, -1f }).WithThreshold(0.5f);
            t.Circuit.SetProperties(neuron.Id, props);
            // I0 and not I1.
            Assert.That(Outputs(t, switches), Is.EqualTo(new[] { false, true, false, false }));
        }

        [Test]
        public void Perceptron_InputCountChangeResizesWeights()
        {
            var t = Wrap(ComponentKind.Neuron, 2, out var neuron, out _);
            var props = neuron.GetProperties();
            props.InputCount = 3;
            new DirectCircuitEditor(t.Circuit).Apply(new SetPropertiesEdit(t.Circuit, neuron.Id, props));
            Assert.That(neuron.Neuron.Weights.Count, Is.EqualTo(3));
        }

        [Test]
        public void PerceptronRule_LearnsMajority_ButNotXor()
        {
            var start = NeuronParameters.DefaultPerceptron(3).WithWeights(new[] { 0f, 0f, 0f }).WithThreshold(0);
            var majority = NeuralTraining.TrainPerceptron(start, Samples(3, p => (p & 1) + (p >> 1 & 1) + (p >> 2 & 1) >= 2));
            Assert.That(majority.Perfect, Is.True);
            for (int p = 0; p < 8; p++)
                Assert.That(majority.Neuron.Fires(p), Is.EqualTo((p & 1) + (p >> 1 & 1) + (p >> 2 & 1) >= 2));

            var xor = NeuralTraining.TrainPerceptron(NeuronParameters.DefaultPerceptron(2), Samples(2, p => p == 1 || p == 2));
            Assert.That(xor.Perfect, Is.False);
            Assert.That(xor.Correct, Is.EqualTo(3), "a line separates at most 3 of the 4 XOR rows");
        }

        [Test]
        public void SpikingNeuron_ChargesLeaksAndRests()
        {
            var t = new TestCircuit();
            var sw = t.Add(ComponentKind.Switch);
            var neuron = t.Add(ComponentKind.SpikingNeuron, inputs: 1);
            ComponentFactory.SetNeuron(neuron, new NeuronParameters(new[] { 0.6f }, 1f, leak: 0.8f, reset: 0f, refractory: 2));
            t.Circuit.SetProperties(neuron.Id, neuron.GetProperties());
            t.Connect(sw, neuron);

            using (var session = t.CreateSession())
            {
                session.Reset();
                Assert.That(session.Settle(100), Is.True);
                t.Circuit.SetInputValue(sw.Id, true);
                var spikes = new List<int>();
                float lastPotential = 0;
                for (int tick = 0; tick < 30; tick++)
                {
                    session.Run(1);
                    if (session.GetOutputValue(neuron.Id, 0))
                        spikes.Add(tick);
                    Assert.That(session.TryGetNeuronPotential(neuron, out lastPotential), Is.True);
                }
                // 0.6, then 0.6 * 0.8 + 0.6 = 1.08 >= 1: a spike every 4 ticks (2 to charge, 2 refractory).
                Assert.That(spikes.Count, Is.GreaterThanOrEqualTo(6));
                var gaps = spikes.Zip(spikes.Skip(1), (a, b) => b - a).Distinct().ToList();
                Assert.That(gaps, Is.EqualTo(new[] { 4 }));

                t.Circuit.SetInputValue(sw.Id, false);
                Assert.That(session.Settle(1000), Is.True, "the potential leaks back to rest and the neuron goes idle");
                session.TryGetNeuronPotential(neuron, out lastPotential);
                Assert.That(lastPotential, Is.EqualTo(0f));
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void NeuronCircuits_EventDrivenMatchesFullSweep(int seed)
        {
            var circuit = BenchmarkCircuits.RandomNeural(seed, components: 200, switches: 5);
            var compiled = CircuitCompiler.Compile(circuit);
            var random = new System.Random(seed);
            using (var events = new ManagedSimulationEngine { Mode = SimulationMode.EventDriven })
            using (var sweep = new ManagedSimulationEngine { Mode = SimulationMode.FullSweep })
            {
                events.Load(compiled);
                sweep.Load(compiled);
                for (int step = 0; step < 100; step++)
                {
                    if (random.NextDouble() < 0.3)
                    {
                        int slot = random.Next(compiled.InputSlotCount);
                        bool value = random.Next(2) == 1;
                        events.SetInput(slot, value);
                        sweep.SetInput(slot, value);
                    }
                    long ticks = random.Next(1, 5);
                    events.Run(ticks);
                    sweep.Run(ticks);
                    for (int net = 0; net < compiled.NetCount; net++)
                        Assert.That(sweep.GetNet(net), Is.EqualTo(events.GetNet(net)), $"net {net} at step {step}");
                    for (int n = 0; n < compiled.NeuronCount; n++)
                        Assert.That(sweep.GetNeuronPotential(n), Is.EqualTo(events.GetNeuronPotential(n)), $"neuron {n} at step {step}");
                }
            }
        }

        [Test]
        public void NeuralBlock_DefaultsToXor()
        {
            var t = Wrap(ComponentKind.NeuralBlock, 2, out var block, out var switches);
            Assert.That(t.Circuit.GetInputCount(block), Is.EqualTo(2));
            Assert.That(t.Circuit.GetOutputCount(block), Is.EqualTo(1));
            Assert.That(Outputs(t, switches), Is.EqualTo(new[] { false, true, true, false }));
        }

        [Test]
        public void NeuralBlock_LearnsAFullAdderIc_AndBehavesLikeIt()
        {
            var library = new SubcircuitLibrary();
            var fullAdder = TestLibrary.AddFullAdder(library);
            var samples = NeuralTraining.SamplesFromCircuit(fullAdder.Circuit);
            Assert.That(samples.Count, Is.EqualTo(8));

            var start = NeuralNetwork.Create(3, new[] { 8 }, 2, NeuralActivation.Tanh, seed: 3);
            var result = NeuralTraining.TrainNetwork(start, samples, epochs: 5000, learningRate: 0.3f);
            Assert.That(result.Perfect, Is.True, $"accuracy {result.Accuracy}, loss {result.Loss}");

            // Side by side: the IC and the trained block driven by the same switches.
            var t = new TestCircuit(library);
            var inputs = new[] { t.Add(ComponentKind.Switch, "A"), t.Add(ComponentKind.Switch, "B"), t.Add(ComponentKind.Switch, "Cin") };
            var ic = t.AddSubcircuit("full-adder");
            var block = t.Add(ComponentKind.NeuralBlock);
            ComponentFactory.SetNetwork(block, result.Network);
            t.Circuit.SetProperties(block.Id, block.GetProperties());
            var bulbs = Enumerable.Range(0, 4).Select(i => t.Add(ComponentKind.Bulb, "Y" + i)).ToArray();
            for (int i = 0; i < 3; i++)
            {
                t.Connect(inputs[i], ic, i);
                t.Connect(inputs[i], block, i);
            }
            t.Connect(ic, 0, bulbs[0], 0);
            t.Connect(ic, 1, bulbs[1], 0);
            t.Connect(block, 0, bulbs[2], 0);
            t.Connect(block, 1, bulbs[3], 0);

            var table = TruthTableGenerator.Generate(t.Circuit, inputs, bulbs);
            foreach (var row in table.Rows)
            {
                Assert.That(row.Outputs[2], Is.EqualTo(row.Outputs[0]), "sum");
                Assert.That(row.Outputs[3], Is.EqualTo(row.Outputs[1]), "carry");
            }
        }

        [Test]
        public void LookupTable_MatchesTheNetwork_ForSixteenInputs()
        {
            var network = NeuralNetwork.Create(16, new[] { 12 }, 3, NeuralActivation.Tanh, seed: 5);
            var lut = network.BuildLutOnCpu();
            Assert.That(lut.Length, Is.EqualTo(3 * 1024));
            var random = new System.Random(1);
            for (int k = 0; k < 500; k++)
            {
                int pattern = random.Next(1 << 16);
                var expected = network.Evaluate(pattern);
                for (int m = 0; m < 3; m++)
                    Assert.That((lut[m * 1024 + (pattern >> 6)] >> (pattern & 63) & 1) != 0, Is.EqualTo(expected[m]));
            }
        }

        [Test]
        public void NeuralSettings_RoundTripThroughJson()
        {
            var t = new TestCircuit();
            var neuron = t.Add(ComponentKind.Neuron, inputs: 3);
            var spiking = t.Add(ComponentKind.SpikingNeuron);
            var block = t.Add(ComponentKind.NeuralBlock);
            var p = neuron.GetProperties();
            p.Neuron = new NeuronParameters(new[] { 0.25f, -1f, 2f }, 0.75f);
            t.Circuit.SetProperties(neuron.Id, p);
            p = spiking.GetProperties();
            p.Neuron = new NeuronParameters(new[] { 0.3f, 0.9f }, 1.2f, leak: 0.5f, reset: -0.2f, refractory: 5);
            t.Circuit.SetProperties(spiking.Id, p);
            p = block.GetProperties();
            p.Network = NeuralNetwork.Create(4, new[] { 6, 5 }, 3, NeuralActivation.Relu, seed: 9);
            t.Circuit.SetProperties(block.Id, p);

            var json = CircuitSerializer.Serialize(t.Circuit, "neurons");
            var document = CircuitSerializer.Deserialize(json, new SubcircuitLibrary());
            Assert.That(document.Warnings, Is.Empty);
            var loaded = new Circuit();
            document.Snapshot.Restore(loaded);
            Assert.That(loaded.GetComponent(neuron.Id).Neuron, Is.EqualTo(neuron.Neuron));
            Assert.That(loaded.GetComponent(spiking.Id).Neuron, Is.EqualTo(spiking.Neuron));
            Assert.That(loaded.GetComponent(block.Id).Network, Is.EqualTo(block.Network));
            Assert.That(loaded.GetOutputCount(loaded.GetComponent(block.Id)), Is.EqualTo(3));
        }

        [Test]
        public void ShrinkingANeuralBlock_RemovesWiresOfVanishedPins_UndoRestoresThem()
        {
            var t = new TestCircuit();
            var block = t.Add(ComponentKind.NeuralBlock);
            var wide = block.GetProperties();
            wide.Network = NeuralNetwork.Create(2, new[] { 3 }, 3);
            t.Circuit.SetProperties(block.Id, wide);
            var bulb = t.Add(ComponentKind.Bulb);
            t.Connect(block, 2, bulb, 0);
            var editor = new DirectCircuitEditor(t.Circuit);

            var narrow = block.GetProperties();
            narrow.Network = NeuralNetwork.Create(2, new[] { 3 }, 1);
            editor.Apply(new SetPropertiesEdit(t.Circuit, block.Id, narrow));
            Assert.That(t.Circuit.GetWireInto(bulb.Id, 0), Is.Null);
            editor.Undo();
            Assert.That(t.Circuit.GetWireInto(bulb.Id, 0), Is.Not.Null);
        }

        [Test]
        public void SpikingNeuronInsideAnIc_PotentialIsReadableThroughTheInstance()
        {
            var library = new SubcircuitLibrary();
            var inner = new TestCircuit(library);
            var pin = inner.Add(ComponentKind.PinIn, "in");
            var neuron = inner.Add(ComponentKind.SpikingNeuron, inputs: 1);
            ComponentFactory.SetNeuron(neuron, new NeuronParameters(new[] { 0.4f }, 10f, leak: 1f, reset: 0, refractory: 0));
            inner.Circuit.SetProperties(neuron.Id, neuron.GetProperties());
            var pout = inner.Add(ComponentKind.PinOut, "spike");
            inner.Connect(pin, neuron);
            inner.Connect(neuron, pout);
            library.Add(new SubcircuitDefinition("integrator", "Integrator", inner.Circuit));

            var t = new TestCircuit(library);
            var sw = t.Add(ComponentKind.Switch);
            var ic = t.AddSubcircuit("integrator");
            t.Connect(sw, ic);
            using (var session = t.CreateSession())
            {
                t.Circuit.SetInputValue(sw.Id, true);
                session.Reset();
                session.Run(6);
                var reader = session.CreateReader(new[] { ic.Id });
                Assert.That(reader.TryGetNeuronPotential(library.Get("integrator").Circuit.GetComponent(neuron.Id), out float v), Is.True);
                Assert.That(v, Is.GreaterThan(1.5f).And.LessThan(2.5f));
            }
        }
    }
}
