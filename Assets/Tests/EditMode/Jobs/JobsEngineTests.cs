using System.Linq;
using LogicSim.Simulation;
using LogicSim.Simulation.Jobs;
using NUnit.Framework;

namespace LogicSim.Tests.Jobs
{
    /// <summary>
    /// Compares the Jobs backend with the managed backend tick by tick. Unity runs these with the real job system
    /// (and Burst when installed); Headless/LogicSim.JobsTests runs the same file against a stand-in of the job API.
    /// </summary>
    public class JobsEngineTests
    {
        private static void AssertNativeArraysDisposed()
        {
#if !UNITY_5_3_OR_NEWER
            // Only the headless stand-in counts native allocations.
            Assert.That(Unity.Collections.Tracker.Allocated, Is.EqualTo(0), "all native arrays disposed");
#endif
        }

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
            AssertNativeArraysDisposed();
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
                    // Bit-exact: Burst runs with FloatMode.Strict.
                    for (int n = 0; n < compiled.NeuronCount; n++)
                        Assert.That(jobs.GetNeuronPotential(n), Is.EqualTo(reference.GetNeuronPotential(n)), $"neuron {n} at step {step}");
                }
            }
            AssertNativeArraysDisposed();
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

        /// <summary>Two screens written from random switches, plus a clock on one WR: the same pixels in both engines.</summary>
        [TestCase(SimulationMode.EventDriven, 21)]
        [TestCase(SimulationMode.FullSweep, 22)]
        public void JobsEngine_MatchesManagedEngine_OnScreens(SimulationMode mode, int seed)
        {
            var circuit = new Circuit();
            var random = new System.Random(seed);
            var screens = new[] { (width: 5, height: 3), (width: 8, height: 8) }.Select((size, i) =>
            {
                var screen = new CircuitComponent(ComponentKind.Screen, 0, -40 * i);
                circuit.AddComponent(screen);
                var p = screen.GetProperties();
                screen.Spec.FindSetting("width").Set(ref p, size.width);
                screen.Spec.FindSetting("height").Set(ref p, size.height);
                circuit.SetProperties(screen.Id, p);
                return screen;
            }).ToArray();
            foreach (var screen in screens)
            {
                for (int pin = 0; pin < circuit.GetInputCount(screen); pin++)
                {
                    var driver = new CircuitComponent(pin == ScreenLayout.WritePin(screen) && screen == screens[1] ? ComponentKind.Clock : ComponentKind.Switch, -10, -pin);
                    circuit.AddComponent(driver);
                    circuit.Connect(new PinRef(driver.Id, 0), new PinRef(screen.Id, pin));
                }
            }
            var compiled = CircuitCompiler.Compile(circuit);
            Assert.That(compiled.ScreenCount, Is.EqualTo(2));
            Assert.That(compiled.PixelCount, Is.EqualTo(5 * 3 + 8 * 8));

            using (var reference = new ManagedSimulationEngine { Mode = SimulationMode.EventDriven })
            using (var jobs = new JobsSimulationEngine { Mode = mode, ParallelSweepMinGates = 1, ChunkSize = 64 })
            {
                reference.Load(compiled);
                jobs.Load(compiled);
                var expected = new byte[64];
                var actual = new byte[64];
                int written = 0;
                for (int step = 0; step < 300; step++)
                {
                    int slot = random.Next(compiled.InputSlotCount);
                    bool value = random.Next(2) == 1;
                    reference.SetInput(slot, value);
                    jobs.SetInput(slot, value);
                    long ticks = random.Next(1, 40);
                    reference.Run(ticks);
                    jobs.Schedule(ticks);
                    jobs.Complete();
                    for (int screen = 0; screen < 2; screen++)
                    {
                        reference.CopyScreen(screen, expected);
                        jobs.CopyScreen(screen, actual);
                        Assert.That(actual, Is.EqualTo(expected), $"screen {screen} at step {step} ({jobs.ActiveMode})");
                    }
                    written = System.Math.Max(written, expected.Count(p => p != 0));
                }
                Assert.That(written, Is.GreaterThan(0), "the random inputs lit some pixels");
            }
            AssertNativeArraysDisposed();
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
