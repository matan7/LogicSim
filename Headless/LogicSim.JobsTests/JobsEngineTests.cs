using LogicSim.Simulation;
using LogicSim.Simulation.Jobs;
using NUnit.Framework;

namespace LogicSim.JobsTests
{
    public class JobsEngineTests
    {
        [TestCase(SimulationMode.EventDriven, 1)]
        [TestCase(SimulationMode.FullSweep, 2)]
        [TestCase(SimulationMode.Auto, 3)]
        [TestCase(SimulationMode.Auto, 4)]
        public void JobsEngine_MatchesManagedEngine(SimulationMode mode, int seed)
        {
            var circuit = BenchmarkCircuits.Random(seed, gates: 500, switches: 8);
            var compiled = CircuitCompiler.Compile(circuit);
            var random = new System.Random(seed);

            using (var reference = new ManagedSimulationEngine { Mode = SimulationMode.EventDriven })
            using (var jobs = new JobsSimulationEngine { Mode = mode, ParallelSweepMinGates = 100, ParallelSweepActivity = 0.1f, ChunkSize = 64 })
            {
                reference.Load(compiled);
                jobs.Load(compiled);
                for (int step = 0; step < 150; step++)
                {
                    if (random.NextDouble() < 0.3)
                    {
                        int slot = random.Next(compiled.InputSlotCount);
                        bool value = random.Next(2) == 1;
                        reference.SetInput(slot, value);
                        jobs.SetInput(slot, value);
                    }
                    long ticks = random.Next(1, 6);
                    reference.Run(ticks);
                    jobs.Schedule(ticks);
                    jobs.Complete();
                    Assert.That(jobs.Tick, Is.EqualTo(reference.Tick));
                    for (int net = 0; net < compiled.NetCount; net++)
                        Assert.That(jobs.GetNet(net), Is.EqualTo(reference.GetNet(net)), $"net {net} at step {step} ({jobs.ActiveMode})");
                }
            }
            Assert.That(Unity.Collections.Tracker.Allocated, Is.EqualTo(0), "all native arrays disposed");
        }

        [TestCase(SimulationMode.EventDriven, 11)]
        [TestCase(SimulationMode.FullSweep, 12)]
        [TestCase(SimulationMode.Auto, 13)]
        public void JobsEngine_MatchesManagedEngine_WithNeurons(SimulationMode mode, int seed)
        {
            var circuit = BenchmarkCircuits.RandomNeural(seed, components: 300, switches: 6);
            var compiled = CircuitCompiler.Compile(circuit);
            Assert.That(compiled.NeuronCount, Is.GreaterThan(0));
            var random = new System.Random(seed);

            using (var reference = new ManagedSimulationEngine { Mode = SimulationMode.EventDriven })
            using (var jobs = new JobsSimulationEngine { Mode = mode, ParallelSweepMinGates = 50, ParallelSweepActivity = 0.05f, ChunkSize = 64 })
            {
                reference.Load(compiled);
                jobs.Load(compiled);
                for (int step = 0; step < 120; step++)
                {
                    if (random.NextDouble() < 0.3)
                    {
                        int slot = random.Next(compiled.InputSlotCount);
                        bool value = random.Next(2) == 1;
                        reference.SetInput(slot, value);
                        jobs.SetInput(slot, value);
                    }
                    long ticks = random.Next(1, 6);
                    reference.Run(ticks);
                    jobs.Schedule(ticks);
                    jobs.Complete();
                    for (int net = 0; net < compiled.NetCount; net++)
                        Assert.That(jobs.GetNet(net), Is.EqualTo(reference.GetNet(net)), $"net {net} at step {step} ({jobs.ActiveMode})");
                    for (int n = 0; n < compiled.NeuronCount; n++)
                        Assert.That(jobs.GetNeuronPotential(n), Is.EqualTo(reference.GetNeuronPotential(n)), $"neuron {n} at step {step}");
                }
            }
            Assert.That(Unity.Collections.Tracker.Allocated, Is.EqualTo(0), "all native arrays disposed");
        }

        [Test]
        public void Settle_MatchesManagedEngine_OnAdderBank()
        {
            var circuit = BenchmarkCircuits.AdderBank(adders: 20, bits: 8);
            var compiled = CircuitCompiler.Compile(circuit);
            using (var reference = new ManagedSimulationEngine())
            using (var jobs = new JobsSimulationEngine())
            {
                reference.Load(compiled);
                jobs.Load(compiled);
                Assert.That(jobs.Settle(10_000), Is.EqualTo(reference.Settle(10_000)));
                for (int slot = 0; slot < compiled.InputSlotCount; slot += 3)
                {
                    reference.SetInput(slot, true);
                    jobs.SetInput(slot, true);
                }
                Assert.That(jobs.Settle(10_000), Is.True);
                Assert.That(reference.Settle(10_000), Is.True);
                for (int net = 0; net < compiled.NetCount; net++)
                    Assert.That(jobs.GetNet(net), Is.EqualTo(reference.GetNet(net)));
            }
        }

        [Test]
        public void EmptyCircuit_DoesNotCrash()
        {
            var compiled = CircuitCompiler.Compile(new Circuit());
            using (var jobs = new JobsSimulationEngine { Mode = SimulationMode.FullSweep })
            {
                jobs.Load(compiled);
                jobs.Schedule(10);
                jobs.Complete();
                Assert.That(jobs.Tick, Is.EqualTo(10));
                Assert.That(jobs.Settle(10), Is.True);
            }
        }
    }
}
