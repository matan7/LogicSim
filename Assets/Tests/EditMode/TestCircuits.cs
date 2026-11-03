using System.Collections.Generic;
using System.Linq;
using LogicSim.Simulation;

namespace LogicSim.Tests
{
    /// <summary>Small fluent helper for building circuits in tests.</summary>
    internal sealed class TestCircuit
    {
        public Circuit Circuit { get; }
        private float _nextY;

        public TestCircuit(SubcircuitLibrary library = null)
        {
            Circuit = new Circuit(library);
        }

        public CircuitComponent Add(ComponentKind kind, string label = null, int inputs = 0)
        {
            var component = new CircuitComponent(kind, 0, _nextY);
            _nextY -= 5;
            Circuit.AddComponent(component);
            if (label != null || inputs > 0)
            {
                var p = component.GetProperties();
                if (label != null)
                    p.Label = label;
                if (inputs > 0)
                    p.InputCount = inputs;
                Circuit.SetProperties(component.Id, p);
            }
            return component;
        }

        public CircuitComponent AddSubcircuit(string definitionId, string label = null)
        {
            var component = ComponentFactory.CreateSubcircuit(definitionId, 0, _nextY, label);
            _nextY -= 5;
            Circuit.AddComponent(component);
            return component;
        }

        public Wire Connect(CircuitComponent from, int fromPin, CircuitComponent to, int toPin)
        {
            return Circuit.Connect(new PinRef(from.Id, fromPin), new PinRef(to.Id, toPin));
        }

        public Wire Connect(CircuitComponent from, CircuitComponent to, int toPin = 0)
        {
            return Connect(from, 0, to, toPin);
        }

        public SimulationSession CreateSession()
        {
            return new SimulationSession(Circuit, new ManagedSimulationEngine());
        }
    }

    internal static class TestLibrary
    {
        /// <summary>Half adder: inputs A, B; outputs S, C.</summary>
        public static SubcircuitDefinition AddHalfAdder(SubcircuitLibrary library)
        {
            var t = new TestCircuit(library);
            var a = t.Add(ComponentKind.Switch, "A");
            var b = t.Add(ComponentKind.Switch, "B");
            var xor = t.Add(ComponentKind.Xor);
            var and = t.Add(ComponentKind.And);
            var s = t.Add(ComponentKind.Bulb, "S");
            var c = t.Add(ComponentKind.Bulb, "C");
            t.Connect(a, xor, 0);
            t.Connect(b, xor, 1);
            t.Connect(a, and, 0);
            t.Connect(b, and, 1);
            t.Connect(xor, s);
            t.Connect(and, c);
            var definition = new SubcircuitDefinition("half-adder", "Half adder", t.Circuit);
            library.Add(definition);
            return definition;
        }

        /// <summary>Full adder made of two half adders: inputs A, B, Cin; outputs S, Cout.</summary>
        public static SubcircuitDefinition AddFullAdder(SubcircuitLibrary library)
        {
            if (!library.TryGet("half-adder", out _))
                AddHalfAdder(library);
            var t = new TestCircuit(library);
            var a = t.Add(ComponentKind.PinIn, "A");
            var b = t.Add(ComponentKind.PinIn, "B");
            var cin = t.Add(ComponentKind.PinIn, "Cin");
            var ha1 = t.AddSubcircuit("half-adder");
            var ha2 = t.AddSubcircuit("half-adder");
            var or = t.Add(ComponentKind.Or);
            var s = t.Add(ComponentKind.PinOut, "S");
            var cout = t.Add(ComponentKind.PinOut, "Cout");
            t.Connect(a, ha1, 0);
            t.Connect(b, ha1, 1);
            t.Connect(ha1, 0, ha2, 0);
            t.Connect(cin, ha2, 1);
            t.Connect(ha2, 0, s, 0);
            t.Connect(ha1, 1, or, 0);
            t.Connect(ha2, 1, or, 1);
            t.Connect(or, cout);
            var definition = new SubcircuitDefinition("full-adder", "Full adder", t.Circuit);
            library.Add(definition);
            return definition;
        }

        public static List<CircuitComponent> Ordered(this IEnumerable<CircuitComponent> components)
        {
            return components.OrderBy(c => c.Id).ToList();
        }
    }
}
