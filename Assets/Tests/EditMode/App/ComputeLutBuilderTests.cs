using System;
using System.Linq;
using LogicSim.App;
using LogicSim.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace LogicSim.Tests.App
{
    /// <summary>The compute shader must build the same neural block lookup tables as the CPU.</summary>
    public class ComputeLutBuilderTests
    {
        private INeuralLutBuilder _previous;

        [SetUp]
        public void SetUp()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("This GPU has no compute shaders.");
            _previous = NeuralNetwork.LutBuilder;
            NeuralNetwork.LutBuilder = null;
            ComputeLutBuilder.Register();
            Assert.That(NeuralNetwork.LutBuilder, Is.InstanceOf<ComputeLutBuilder>(), "compute shader found");
        }

        [TearDown]
        public void TearDown() => NeuralNetwork.LutBuilder = _previous;

        [Test]
        public void GpuTables_MatchTheCpu_OnRandomNetworks()
        {
            var activations = new[] { NeuralActivation.Sigmoid, NeuralActivation.Tanh, NeuralActivation.Relu };
            int differing = 0, total = 0;
            for (int k = 0; k < 10; k++)
            {
                int inputs = 10 + k % 7;
                var network = NeuralNetwork.Create(inputs, new[] { 16, 8 }, 2, activations[k % 3], seed: 100 + k);
                var gpu = NeuralNetwork.LutBuilder.Build(network);
                Assert.That(gpu, Is.Not.Null, $"network {k} built on the GPU");
                var cpu = network.BuildLutOnCpu();
                Assert.That(gpu.Length, Is.EqualTo(cpu.Length));

                int words = network.LutWordsPerOutput;
                for (int output = 0; output < network.OutputCount; output++)
                    for (int pattern = 0; pattern < network.PatternCount; pattern++)
                    {
                        int word = output * words + (pattern >> 6);
                        bool g = ((gpu[word] >> (pattern & 63)) & 1) != 0;
                        bool c = ((cpu[word] >> (pattern & 63)) & 1) != 0;
                        total++;
                        if (g == c)
                            continue;
                        differing++;
                        // Allowed only right at the decision boundary.
                        var x = Enumerable.Range(0, inputs).Select(i => ((pattern >> i) & 1) != 0 ? 1f : 0f).ToArray();
                        float p = network.Forward(x)[output];
                        Assert.That(Math.Abs(p - 0.5f), Is.LessThan(1e-5f),
                            $"network {k} ({activations[k % 3]}), output {output}, pattern {pattern}: GPU {g}, CPU {c}, p = {p}");
                    }
            }
            TestContext.WriteLine($"{differing} of {total} bits differ");
        }
    }
}
