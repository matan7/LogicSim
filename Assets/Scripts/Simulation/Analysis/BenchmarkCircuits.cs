using System;
using System.Collections.Generic;

namespace LogicSim.Simulation
{
    /// <summary>Synthetic circuits for parity tests and benchmarks.</summary>
    public static class BenchmarkCircuits
    {
        private static readonly ComponentKind[] RandomKinds =
        {
            ComponentKind.And, ComponentKind.Or, ComponentKind.Nand, ComponentKind.Nor, ComponentKind.Xor,
            ComponentKind.Xnor, ComponentKind.Not, ComponentKind.Buffer, ComponentKind.DFlipFlop, ComponentKind.Clock
        };

        /// <summary>
        /// Random gates wired to random outputs anywhere in the circuit (lots of feedback, so it stays active).
        /// </summary>
        public static Circuit Random(int seed, int gates, int switches)
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

            var gateList = new List<CircuitComponent>();
            for (int i = 0; i < gates; i++)
            {
                var kind = RandomKinds[random.Next(RandomKinds.Length)];
                var gate = ComponentFactory.Create(kind, 5, -i, clockHalfPeriod: random.Next(2, 20));
                circuit.AddComponent(gate);
                gateList.Add(gate);
                outputs.Add(gate);
            }

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

        /// <summary>
        /// Many independent ripple-carry adders: realistic combinational logic that settles after each input change.
        /// </summary>
        public static Circuit AdderBank(int adders, int bits)
        {
            var circuit = new Circuit();
            for (int n = 0; n < adders; n++)
            {
                float y = -n * 10;
                int carryId = 0;
                for (int bit = 0; bit < bits; bit++)
                {
                    var a = new CircuitComponent(ComponentKind.Switch, 0, y);
                    var b = new CircuitComponent(ComponentKind.Switch, 0, y);
                    var x1 = new CircuitComponent(ComponentKind.Xor, 5, y);
                    var x2 = new CircuitComponent(ComponentKind.Xor, 10, y);
                    var a1 = new CircuitComponent(ComponentKind.And, 5, y);
                    var a2 = new CircuitComponent(ComponentKind.And, 10, y);
                    var or = new CircuitComponent(ComponentKind.Or, 15, y);
                    var s = new CircuitComponent(ComponentKind.Bulb, 20, y);
                    foreach (var c in new[] { a, b, x1, x2, a1, a2, or, s })
                        circuit.AddComponent(c);

                    circuit.Connect(new PinRef(a.Id, 0), new PinRef(x1.Id, 0));
                    circuit.Connect(new PinRef(b.Id, 0), new PinRef(x1.Id, 1));
                    circuit.Connect(new PinRef(a.Id, 0), new PinRef(a1.Id, 0));
                    circuit.Connect(new PinRef(b.Id, 0), new PinRef(a1.Id, 1));
                    circuit.Connect(new PinRef(x1.Id, 0), new PinRef(x2.Id, 0));
                    circuit.Connect(new PinRef(x1.Id, 0), new PinRef(a2.Id, 0));
                    if (carryId != 0)
                    {
                        circuit.Connect(new PinRef(carryId, 0), new PinRef(x2.Id, 1));
                        circuit.Connect(new PinRef(carryId, 0), new PinRef(a2.Id, 1));
                    }
                    circuit.Connect(new PinRef(a1.Id, 0), new PinRef(or.Id, 0));
                    circuit.Connect(new PinRef(a2.Id, 0), new PinRef(or.Id, 1));
                    circuit.Connect(new PinRef(x2.Id, 0), new PinRef(s.Id, 0));
                    carryId = or.Id;
                }
            }
            return circuit;
        }
    }
}
