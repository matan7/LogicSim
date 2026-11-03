using System;
using System.Collections.Generic;
using System.Linq;
using LogicSim.Simulation;
using NUnit.Framework;

namespace LogicSim.Tests
{
    public class GateTests
    {
        private static readonly object[] TwoInputCases =
        {
            new object[] { ComponentKind.And, new[] { false, false, false, true } },
            new object[] { ComponentKind.Or, new[] { false, true, true, true } },
            new object[] { ComponentKind.Nand, new[] { true, true, true, false } },
            new object[] { ComponentKind.Nor, new[] { true, false, false, false } },
            new object[] { ComponentKind.Xor, new[] { false, true, true, false } },
            new object[] { ComponentKind.Xnor, new[] { true, false, false, true } },
        };

        [TestCaseSource(nameof(TwoInputCases))]
        public void TwoInputGate_MatchesTruthTable(ComponentKind kind, bool[] expected)
        {
            var t = new TestCircuit();
            var a = t.Add(ComponentKind.Switch, "A");
            var b = t.Add(ComponentKind.Switch, "B");
            var gate = t.Add(kind);
            var q = t.Add(ComponentKind.Bulb, "Q");
            t.Connect(a, gate, 0);
            t.Connect(b, gate, 1);
            t.Connect(gate, q);

            var table = TruthTableGenerator.Generate(t.Circuit);

            Assert.That(table.InputNames, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(table.Rows.Select(r => r.Outputs[0]), Is.EqualTo(expected));
            Assert.That(table.AllStable);
        }

        [TestCase(ComponentKind.Not, true, false)]
        [TestCase(ComponentKind.Buffer, false, true)]
        public void SingleInputGate_MatchesTruthTable(ComponentKind kind, bool whenLow, bool whenHigh)
        {
            var t = new TestCircuit();
            var a = t.Add(ComponentKind.Switch, "A");
            var gate = t.Add(kind);
            var q = t.Add(ComponentKind.Bulb, "Q");
            t.Connect(a, gate);
            t.Connect(gate, q);

            var table = TruthTableGenerator.Generate(t.Circuit);

            Assert.That(table.Rows[0].Outputs[0], Is.EqualTo(whenLow));
            Assert.That(table.Rows[1].Outputs[0], Is.EqualTo(whenHigh));
        }

        [Test]
        public void ThreeInputAnd_IsTrueOnlyWhenAllInputsAreTrue()
        {
            var t = new TestCircuit();
            var inputs = Enumerable.Range(0, 3).Select(i => t.Add(ComponentKind.Switch, "I" + i)).ToList();
            var gate = t.Add(ComponentKind.And, inputs: 3);
            var q = t.Add(ComponentKind.Bulb, "Q");
            for (int i = 0; i < 3; i++)
                t.Connect(inputs[i], gate, i);
            t.Connect(gate, q);

            var table = TruthTableGenerator.Generate(t.Circuit);

            Assert.That(table.Rows.Count, Is.EqualTo(8));
            Assert.That(table.Rows.Count(r => r.Outputs[0]), Is.EqualTo(1));
            Assert.That(table.Rows[7].Outputs[0], Is.True);
        }

        [Test]
        public void UnconnectedInputs_ReadAsLow()
        {
            var t = new TestCircuit();
            var gate = t.Add(ComponentKind.Nor);
            var q = t.Add(ComponentKind.Bulb, "Q");
            t.Connect(gate, q);
            using (var session = t.CreateSession())
            {
                Assert.That(session.Settle(100), Is.True);
                Assert.That(session.GetDisplayValue(q), Is.True);
            }
        }
    }

    public class TimingTests
    {
        [Test]
        public void BufferChain_PropagatesOneTickPerGate()
        {
            const int length = 10;
            var t = new TestCircuit();
            var input = t.Add(ComponentKind.Switch, "In");
            var previous = input;
            for (int i = 0; i < length; i++)
            {
                var buffer = t.Add(ComponentKind.Buffer);
                t.Connect(previous, buffer);
                previous = buffer;
            }
            var bulb = t.Add(ComponentKind.Bulb);
            t.Connect(previous, bulb);

            using (var session = t.CreateSession())
            {
                session.Settle(1000);
                t.Circuit.SetInputValue(input.Id, true);
                long start = session.Tick;

                // The switch gate needs one tick, then each buffer one more.
                session.Run(length);
                Assert.That(session.GetDisplayValue(bulb), Is.False);
                session.Run(1);
                Assert.That(session.GetDisplayValue(bulb), Is.True);
                Assert.That(session.Tick - start, Is.EqualTo(length + 1));
            }
        }

        [Test]
        public void RingOscillator_NeverSettles()
        {
            var t = new TestCircuit();
            var not = t.Add(ComponentKind.Not);
            t.Connect(not, not);
            using (var session = t.CreateSession())
            {
                Assert.That(session.Settle(1000), Is.False);
                bool before = session.GetOutputValue(not.Id, 0);
                session.Run(1);
                Assert.That(session.GetOutputValue(not.Id, 0), Is.Not.EqualTo(before));
            }
        }

        [Test]
        public void NorLatch_HoldsStateAfterSetAndReset()
        {
            var t = new TestCircuit();
            var set = t.Add(ComponentKind.Switch, "S");
            var reset = t.Add(ComponentKind.Switch, "R");
            var top = t.Add(ComponentKind.Nor);
            var bottom = t.Add(ComponentKind.Nor);
            var q = t.Add(ComponentKind.Bulb, "Q");
            t.Connect(reset, top, 0);
            t.Connect(bottom, top, 1);
            t.Connect(set, bottom, 0);
            t.Connect(top, bottom, 1);
            t.Connect(top, q);

            using (var session = t.CreateSession())
            {
                // Pulse R first to leave the symmetric start-up state.
                t.Circuit.SetInputValue(reset.Id, true);
                Assert.That(session.Settle(100), Is.True);
                t.Circuit.SetInputValue(reset.Id, false);
                Assert.That(session.Settle(100), Is.True);
                Assert.That(session.GetDisplayValue(q), Is.False);

                t.Circuit.SetInputValue(set.Id, true);
                session.Settle(100);
                t.Circuit.SetInputValue(set.Id, false);
                Assert.That(session.Settle(100), Is.True);
                Assert.That(session.GetDisplayValue(q), Is.True, "Q stays set");

                t.Circuit.SetInputValue(reset.Id, true);
                session.Settle(100);
                t.Circuit.SetInputValue(reset.Id, false);
                Assert.That(session.Settle(100), Is.True);
                Assert.That(session.GetDisplayValue(q), Is.False, "Q stays reset");
            }
        }

        [Test]
        public void Clock_TogglesEveryHalfPeriod_AndIdleTimeIsSkipped()
        {
            var t = new TestCircuit();
            var clock = t.Add(ComponentKind.Clock);
            t.Circuit.SetProperties(clock.Id, new ComponentProperties { ClockHalfPeriod = 100 });
            var bulb = t.Add(ComponentKind.Bulb);
            t.Connect(clock, bulb);

            using (var session = t.CreateSession())
            {
                var engine = (ManagedSimulationEngine)session.Engine;
                session.Run(50);
                Assert.That(session.GetDisplayValue(bulb), Is.False);
                session.Run(100);
                Assert.That(session.GetDisplayValue(bulb), Is.True);

                long before = engine.TotalEvaluations;
                session.Run(10_000);
                // 100 edges, each evaluating the clock and nothing else: far fewer than 10 000 ticks of work.
                Assert.That(engine.TotalEvaluations - before, Is.LessThanOrEqualTo(101));
                Assert.That(session.Tick, Is.EqualTo(10_150));
            }
        }
    }

    public class SequentialTests
    {
        [Test]
        public void DFlipFlop_CapturesOnRisingEdgeOnly()
        {
            var t = new TestCircuit();
            var d = t.Add(ComponentKind.Switch, "D");
            var clk = t.Add(ComponentKind.Switch, "CLK");
            var ff = t.Add(ComponentKind.DFlipFlop);
            var q = t.Add(ComponentKind.Bulb, "Q");
            var nq = t.Add(ComponentKind.Bulb, "nQ");
            t.Connect(d, ff, 0);
            t.Connect(clk, ff, 1);
            t.Connect(ff, 0, q, 0);
            t.Connect(ff, 1, nq, 0);

            using (var session = t.CreateSession())
            {
                session.Settle(100);
                Assert.That(session.GetDisplayValue(q), Is.False);
                Assert.That(session.GetDisplayValue(nq), Is.True);

                t.Circuit.SetInputValue(d.Id, true);
                session.Settle(100);
                Assert.That(session.GetDisplayValue(q), Is.False, "No edge yet");

                t.Circuit.SetInputValue(clk.Id, true);
                session.Settle(100);
                Assert.That(session.GetDisplayValue(q), Is.True);

                t.Circuit.SetInputValue(d.Id, false);
                session.Settle(100);
                Assert.That(session.GetDisplayValue(q), Is.True, "Clock stays high");

                t.Circuit.SetInputValue(clk.Id, false);
                session.Settle(100);
                Assert.That(session.GetDisplayValue(q), Is.True, "Falling edge ignored");
            }
        }

        [Test]
        public void TFlipFlops_FormRippleCounter()
        {
            var t = new TestCircuit();
            var one = t.Add(ComponentKind.Constant);
            t.Circuit.SetProperties(one.Id, new ComponentProperties { Value = true, ClockHalfPeriod = 1 });
            var clk = t.Add(ComponentKind.Switch, "CLK");
            var stages = new List<CircuitComponent>();
            CircuitComponent previousClock = clk;
            int previousPin = 0;
            for (int i = 0; i < 4; i++)
            {
                var ff = t.Add(ComponentKind.TFlipFlop);
                t.Connect(one, ff, 0);
                t.Connect(previousClock, previousPin, ff, 1);
                stages.Add(ff);
                // Clock the next stage from nQ so the counter counts up.
                previousClock = ff;
                previousPin = 1;
            }

            using (var session = t.CreateSession())
            {
                session.Settle(100);
                for (int pulse = 1; pulse <= 20; pulse++)
                {
                    t.Circuit.SetInputValue(clk.Id, true);
                    session.Settle(100);
                    t.Circuit.SetInputValue(clk.Id, false);
                    session.Settle(100);

                    int value = 0;
                    for (int bit = 0; bit < 4; bit++)
                        if (session.GetOutputValue(stages[bit].Id, 0))
                            value |= 1 << bit;
                    Assert.That(value, Is.EqualTo(pulse % 16), $"after pulse {pulse}");
                }
            }
        }

        [Test]
        public void JKFlipFlop_SetsResetsAndToggles()
        {
            var t = new TestCircuit();
            var j = t.Add(ComponentKind.Switch, "J");
            var k = t.Add(ComponentKind.Switch, "K");
            var clk = t.Add(ComponentKind.Switch, "CLK");
            var ff = t.Add(ComponentKind.JKFlipFlop);
            t.Connect(j, ff, 0);
            t.Connect(k, ff, 1);
            t.Connect(clk, ff, 2);

            using (var session = t.CreateSession())
            {
                void Pulse()
                {
                    t.Circuit.SetInputValue(clk.Id, true);
                    session.Settle(100);
                    t.Circuit.SetInputValue(clk.Id, false);
                    session.Settle(100);
                }

                session.Settle(100);
                t.Circuit.SetInputValue(j.Id, true);
                Pulse();
                Assert.That(session.GetOutputValue(ff.Id, 0), Is.True, "J sets");

                t.Circuit.SetInputValue(j.Id, false);
                t.Circuit.SetInputValue(k.Id, true);
                Pulse();
                Assert.That(session.GetOutputValue(ff.Id, 0), Is.False, "K resets");

                t.Circuit.SetInputValue(j.Id, true);
                Pulse();
                Assert.That(session.GetOutputValue(ff.Id, 0), Is.True, "J and K toggle");
                Pulse();
                Assert.That(session.GetOutputValue(ff.Id, 0), Is.False, "J and K toggle back");
            }
        }
    }

    public class SubcircuitTests
    {
        [Test]
        public void HalfAdder_InterfaceFollowsLayout()
        {
            var library = new SubcircuitLibrary();
            var definition = TestLibrary.AddHalfAdder(library);
            Assert.That(definition.InputNames, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(definition.OutputNames, Is.EqualTo(new[] { "S", "C" }));
        }

        [Test]
        public void FullAdderBuiltFromHalfAdders_AddsCorrectly()
        {
            var library = new SubcircuitLibrary();
            TestLibrary.AddFullAdder(library);
            var t = new TestCircuit(library);
            var a = t.Add(ComponentKind.Switch, "A");
            var b = t.Add(ComponentKind.Switch, "B");
            var cin = t.Add(ComponentKind.Switch, "Cin");
            var adder = t.AddSubcircuit("full-adder");
            var s = t.Add(ComponentKind.Bulb, "S");
            var cout = t.Add(ComponentKind.Bulb, "Cout");
            t.Connect(a, adder, 0);
            t.Connect(b, adder, 1);
            t.Connect(cin, adder, 2);
            t.Connect(adder, 0, s, 0);
            t.Connect(adder, 1, cout, 0);

            var table = TruthTableGenerator.Generate(t.Circuit);
            foreach (var row in table.Rows)
            {
                int sum = row.Inputs.Count(v => v);
                Assert.That(row.Outputs[0], Is.EqualTo(sum % 2 == 1));
                Assert.That(row.Outputs[1], Is.EqualTo(sum >= 2));
            }
        }

        [Test]
        public void FourBitRippleCarryAdder_AddsAllOperands()
        {
            var library = new SubcircuitLibrary();
            TestLibrary.AddFullAdder(library);
            var t = new TestCircuit(library);
            var a = Enumerable.Range(0, 4).Select(i => t.Add(ComponentKind.Switch, "A" + i)).ToList();
            var b = Enumerable.Range(0, 4).Select(i => t.Add(ComponentKind.Switch, "B" + i)).ToList();
            var sums = Enumerable.Range(0, 5).Select(i => t.Add(ComponentKind.Bulb, "S" + i)).ToList();
            CircuitComponent carry = null;
            for (int i = 0; i < 4; i++)
            {
                var fa = t.AddSubcircuit("full-adder");
                t.Connect(a[i], fa, 0);
                t.Connect(b[i], fa, 1);
                if (carry != null)
                    t.Connect(carry, 1, fa, 2);
                t.Connect(fa, 0, sums[i], 0);
                carry = fa;
            }
            t.Connect(carry, 1, sums[4], 0);

            using (var session = t.CreateSession())
            {
                var compiled = CircuitCompiler.Compile(t.Circuit);
                Assert.That(compiled.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
                // 4 adders x (2 half adders x 2 gates + 1 OR) + 8 switches
                Assert.That(compiled.GateCount, Is.EqualTo(4 * 5 + 8));

                for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    for (int bit = 0; bit < 4; bit++)
                    {
                        t.Circuit.SetInputValue(a[bit].Id, ((x >> bit) & 1) != 0);
                        t.Circuit.SetInputValue(b[bit].Id, ((y >> bit) & 1) != 0);
                    }
                    Assert.That(session.Settle(1000), Is.True);
                    int result = 0;
                    for (int bit = 0; bit < 5; bit++)
                        if (session.GetDisplayValue(sums[bit]))
                            result |= 1 << bit;
                    Assert.That(result, Is.EqualTo(x + y), $"{x} + {y}");
                }
            }
        }

        [Test]
        public void SubcircuitBoundaries_AddNoDelay()
        {
            var library = new SubcircuitLibrary();
            var inner = new TestCircuit(library);
            var pinIn = inner.Add(ComponentKind.PinIn, "in");
            var pinOut = inner.Add(ComponentKind.PinOut, "out");
            inner.Connect(pinIn, pinOut);
            library.Add(new SubcircuitDefinition("wire", "Wire", inner.Circuit));

            var t = new TestCircuit(library);
            var sw = t.Add(ComponentKind.Switch);
            var previous = sw;
            for (int i = 0; i < 5; i++)
            {
                var ic = t.AddSubcircuit("wire");
                t.Connect(previous, ic);
                previous = ic;
            }
            var bulb = t.Add(ComponentKind.Bulb);
            t.Connect(previous, bulb);

            var compiled = CircuitCompiler.Compile(t.Circuit);
            Assert.That(compiled.GateCount, Is.EqualTo(1), "Only the switch is a gate");
            Assert.That(compiled.GetInputNet(bulb.Id, 0), Is.EqualTo(compiled.GetOutputNet(sw.Id, 0)));
        }

        [Test]
        public void PinLoopWithoutGates_IsReportedAndTreatedAsLow()
        {
            var library = new SubcircuitLibrary();
            var inner = new TestCircuit(library);
            var pinIn = inner.Add(ComponentKind.PinIn);
            var pinOut = inner.Add(ComponentKind.PinOut);
            inner.Connect(pinIn, pinOut);
            library.Add(new SubcircuitDefinition("wire", "Wire", inner.Circuit));

            var t = new TestCircuit(library);
            var ic = t.AddSubcircuit("wire");
            t.Connect(ic, ic);

            var compiled = CircuitCompiler.Compile(t.Circuit);
            Assert.That(compiled.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning), Is.True);
            Assert.That(compiled.GetOutputNet(ic.Id, 0), Is.EqualTo(CompiledCircuit.LowNet));
        }

        [Test]
        public void MissingDefinition_IsAnError()
        {
            var t = new TestCircuit();
            t.AddSubcircuit("does-not-exist");
            var compiled = CircuitCompiler.Compile(t.Circuit);
            Assert.That(compiled.HasErrors, Is.True);
        }
    }

    public class EngineParityTests
    {
        /// <summary>Random circuits with feedback: event-driven evaluation must equal a full sweep tick by tick.</summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void EventDriven_MatchesFullSweep(int seed)
        {
            var circuit = RandomCircuit(seed, gates: 300, switches: 8);
            var compiled = CircuitCompiler.Compile(circuit);
            var random = new Random(seed * 7919);

            using (var eventEngine = new ManagedSimulationEngine { Mode = SimulationMode.EventDriven })
            using (var sweepEngine = new ManagedSimulationEngine { Mode = SimulationMode.FullSweep })
            {
                eventEngine.Load(compiled);
                sweepEngine.Load(compiled);
                for (int step = 0; step < 200; step++)
                {
                    if (random.NextDouble() < 0.3)
                    {
                        int slot = random.Next(compiled.InputSlotCount);
                        bool value = random.Next(2) == 1;
                        eventEngine.SetInput(slot, value);
                        sweepEngine.SetInput(slot, value);
                    }
                    long ticks = random.Next(1, 5);
                    eventEngine.Run(ticks);
                    sweepEngine.Run(ticks);
                    for (int net = 0; net < compiled.NetCount; net++)
                        Assert.That(eventEngine.GetNet(net), Is.EqualTo(sweepEngine.GetNet(net)), $"net {net} at step {step}");
                }
            }
        }

        [Test]
        public void SwitchingModes_KeepsResultsConsistent()
        {
            var circuit = RandomCircuit(11, gates: 200, switches: 6);
            var compiled = CircuitCompiler.Compile(circuit);
            using (var reference = new ManagedSimulationEngine { Mode = SimulationMode.EventDriven })
            using (var mixed = new ManagedSimulationEngine())
            {
                reference.Load(compiled);
                mixed.Load(compiled);
                for (int step = 0; step < 60; step++)
                {
                    mixed.Mode = step % 3 == 0 ? SimulationMode.FullSweep : SimulationMode.EventDriven;
                    if (step % 5 == 0)
                    {
                        reference.SetInput(step % compiled.InputSlotCount, step % 2 == 0);
                        mixed.SetInput(step % compiled.InputSlotCount, step % 2 == 0);
                    }
                    reference.Run(3);
                    mixed.Run(3);
                    for (int net = 0; net < compiled.NetCount; net++)
                        Assert.That(mixed.GetNet(net), Is.EqualTo(reference.GetNet(net)), $"net {net} at step {step}");
                }
            }
        }

        internal static Circuit RandomCircuit(int seed, int gates, int switches)
        {
            var random = new Random(seed);
            var circuit = new Circuit();
            var outputs = new List<CircuitComponent>();
            for (int i = 0; i < switches; i++)
            {
                var sw = new CircuitComponent(ComponentKind.Switch, 0, -i);
                circuit.AddComponent(sw);
                outputs.Add(sw);
            }
            var kinds = new[]
            {
                ComponentKind.And, ComponentKind.Or, ComponentKind.Nand, ComponentKind.Nor, ComponentKind.Xor,
                ComponentKind.Xnor, ComponentKind.Not, ComponentKind.Buffer, ComponentKind.DFlipFlop, ComponentKind.Clock
            };
            var gateList = new List<CircuitComponent>();
            for (int i = 0; i < gates; i++)
            {
                var kind = kinds[random.Next(kinds.Length)];
                var gate = ComponentFactory.Create(kind, 5, -i, clockHalfPeriod: random.Next(2, 20));
                circuit.AddComponent(gate);
                gateList.Add(gate);
                outputs.Add(gate);
            }
            // Wire every input to a random output anywhere in the circuit, including later gates (feedback).
            foreach (var gate in gateList)
            {
                for (int pin = 0; pin < circuit.GetInputCount(gate); pin++)
                {
                    if (random.NextDouble() < 0.05)
                        continue;
                    var source = outputs[random.Next(outputs.Count)];
                    int outPin = random.Next(circuit.GetOutputCount(source));
                    circuit.Connect(new PinRef(source.Id, outPin), new PinRef(gate.Id, pin));
                }
            }
            return circuit;
        }
    }
}
