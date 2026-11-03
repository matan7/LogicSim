using System.Diagnostics;
using System.Linq;
using LogicSim.Simulation;
using NUnit.Framework;

namespace LogicSim.Tests
{
    public class EditingTests
    {
        [Test]
        public void ConnectingAlreadyDrivenInput_IsRejected()
        {
            var t = new TestCircuit();
            var a = t.Add(ComponentKind.Switch);
            var b = t.Add(ComponentKind.Switch);
            var not = t.Add(ComponentKind.Not);
            t.Connect(a, not);
            Assert.Throws<CircuitException>(() => t.Connect(b, not));
            Assert.That(t.Circuit.CanConnect(new PinRef(b.Id, 0), new PinRef(not.Id, 1), out var error), Is.False);
            Assert.That(error, Does.Contain("no input pin"));
        }

        [Test]
        public void ConnectEdit_ReplacesExistingDriver_AndUndoRestoresIt()
        {
            var t = new TestCircuit();
            var a = t.Add(ComponentKind.Switch);
            var b = t.Add(ComponentKind.Switch);
            var not = t.Add(ComponentKind.Not);
            var original = t.Connect(a, not);
            var editor = new DirectCircuitEditor(t.Circuit);

            editor.Apply(new ConnectEdit(t.Circuit, new PinRef(b.Id, 0), new PinRef(not.Id, 0)));
            Assert.That(t.Circuit.GetWireInto(not.Id, 0).From.ComponentId, Is.EqualTo(b.Id));

            editor.Undo();
            Assert.That(t.Circuit.GetWireInto(not.Id, 0).Id, Is.EqualTo(original.Id));
        }

        [Test]
        public void RemoveComponent_RemovesWires_AndUndoRestoresEverything()
        {
            var t = new TestCircuit();
            var a = t.Add(ComponentKind.Switch);
            var not = t.Add(ComponentKind.Not);
            var bulb = t.Add(ComponentKind.Bulb);
            t.Connect(a, not);
            t.Connect(not, bulb);
            var editor = new DirectCircuitEditor(t.Circuit);

            editor.Apply(new RemoveElementsEdit(t.Circuit, new[] { not.Id }));
            Assert.That(t.Circuit.ComponentCount, Is.EqualTo(2));
            Assert.That(t.Circuit.WireCount, Is.EqualTo(0));

            editor.Undo();
            Assert.That(t.Circuit.ComponentCount, Is.EqualTo(3));
            Assert.That(t.Circuit.WireCount, Is.EqualTo(2));
            Assert.That(t.Circuit.GetWireInto(bulb.Id, 0).From.ComponentId, Is.EqualTo(not.Id));

            editor.Redo();
            Assert.That(t.Circuit.TryGetComponent(not.Id, out _), Is.False);
        }

        [Test]
        public void AddEdit_RedoKeepsTheSameIds()
        {
            var circuit = new Circuit();
            var editor = new DirectCircuitEditor(circuit);
            var edit = AddElementsEdit.Single(circuit, ComponentFactory.Create(ComponentKind.And, 1, 2));
            editor.Apply(edit);
            int id = edit.Components[0].Id;
            editor.Undo();
            Assert.That(circuit.ComponentCount, Is.EqualTo(0));
            editor.Redo();
            Assert.That(circuit.GetComponent(id).Kind, Is.EqualTo(ComponentKind.And));
        }

        [Test]
        public void ReducingInputCount_DisconnectsRemovedPins_AndUndoReconnects()
        {
            var t = new TestCircuit();
            var gate = t.Add(ComponentKind.Or, inputs: 3);
            var sources = Enumerable.Range(0, 3).Select(i => t.Add(ComponentKind.Switch)).ToList();
            for (int i = 0; i < 3; i++)
                t.Connect(sources[i], gate, i);
            var editor = new DirectCircuitEditor(t.Circuit);

            var properties = gate.GetProperties();
            properties.InputCount = 2;
            editor.Apply(new SetPropertiesEdit(t.Circuit, gate.Id, properties));
            Assert.That(t.Circuit.GetInputCount(gate), Is.EqualTo(2));
            Assert.That(t.Circuit.WireCount, Is.EqualTo(2));

            editor.Undo();
            Assert.That(t.Circuit.GetInputCount(gate), Is.EqualTo(3));
            Assert.That(t.Circuit.GetWireInto(gate.Id, 2), Is.Not.Null);
        }

        [Test]
        public void Fragment_PasteCopiesInternalWiresOnly()
        {
            var t = new TestCircuit();
            var a = t.Add(ComponentKind.Switch);
            var not = t.Add(ComponentKind.Not);
            var bulb = t.Add(ComponentKind.Bulb);
            t.Connect(a, not);
            t.Connect(not, bulb);

            var fragment = CircuitFragment.Capture(t.Circuit, new[] { not.Id, bulb.Id });
            Assert.That(fragment.Wires.Count, Is.EqualTo(1));

            var editor = new DirectCircuitEditor(t.Circuit);
            var paste = fragment.CreatePasteEdit(t.Circuit, 10, 0);
            editor.Apply(paste);

            Assert.That(t.Circuit.ComponentCount, Is.EqualTo(5));
            Assert.That(t.Circuit.WireCount, Is.EqualTo(3));
            Assert.That(paste.Components.Select(c => c.Id), Has.None.EqualTo(not.Id));
            Assert.That(paste.Components[0].X, Is.EqualTo(not.X + 10));
        }

        [Test]
        public void ReplaceCircuitEdit_UndoRestoresPreviousDocument()
        {
            var t = new TestCircuit();
            t.Add(ComponentKind.Switch);
            var editor = new DirectCircuitEditor(t.Circuit);
            editor.Apply(new ReplaceCircuitEdit(t.Circuit, CircuitSnapshot.Empty(), "New"));
            Assert.That(t.Circuit.ComponentCount, Is.EqualTo(0));
            editor.Undo();
            Assert.That(t.Circuit.ComponentCount, Is.EqualTo(1));
        }

        [Test]
        public void Events_AreRaisedForEdits()
        {
            var t = new TestCircuit();
            int added = 0, removed = 0, wires = 0, structure = 0;
            t.Circuit.ComponentAdded += _ => added++;
            t.Circuit.ComponentRemoved += _ => removed++;
            t.Circuit.WireAdded += _ => wires++;
            t.Circuit.StructureChanged += () => structure++;

            var a = t.Add(ComponentKind.Switch);
            var b = t.Add(ComponentKind.Bulb);
            t.Connect(a, b);
            t.Circuit.RemoveComponent(a.Id);

            Assert.That(added, Is.EqualTo(2));
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(wires, Is.EqualTo(1));
            Assert.That(structure, Is.EqualTo(4));
        }
    }

    public class SerializationTests
    {
        [Test]
        public void Document_RoundTripsWithNestedSubcircuits()
        {
            var library = new SubcircuitLibrary();
            TestLibrary.AddFullAdder(library);
            var t = new TestCircuit(library);
            var a = t.Add(ComponentKind.Switch, "A");
            var b = t.Add(ComponentKind.Switch, "B");
            var cin = t.Add(ComponentKind.Switch, "Cin");
            var adder = t.AddSubcircuit("full-adder", "FA");
            var s = t.Add(ComponentKind.Bulb, "S");
            var cout = t.Add(ComponentKind.Bulb, "Cout");
            var clock = t.Add(ComponentKind.Clock);
            t.Circuit.SetProperties(clock.Id, new ComponentProperties { ClockHalfPeriod = 42, Rotation = 1 });
            t.Circuit.SetInputValue(a.Id, true);
            t.Connect(a, adder, 0);
            t.Connect(b, adder, 1);
            t.Connect(cin, adder, 2);
            t.Connect(adder, 0, s, 0);
            t.Connect(adder, 1, cout, 0);

            string json = CircuitSerializer.Serialize(t.Circuit, "Adder");
            var loadedLibrary = new SubcircuitLibrary();
            var document = CircuitSerializer.Deserialize(json, loadedLibrary);
            var loaded = new Circuit(loadedLibrary);
            document.Snapshot.Restore(loaded);

            Assert.That(document.Name, Is.EqualTo("Adder"));
            Assert.That(document.Warnings, Is.Empty);
            Assert.That(loadedLibrary.Count, Is.EqualTo(2));
            Assert.That(loaded.ComponentCount, Is.EqualTo(t.Circuit.ComponentCount));
            Assert.That(loaded.WireCount, Is.EqualTo(t.Circuit.WireCount));
            Assert.That(loaded.GetComponent(clock.Id).ClockHalfPeriod, Is.EqualTo(42));
            Assert.That(loaded.GetComponent(clock.Id).Rotation, Is.EqualTo(1));
            Assert.That(loaded.GetComponent(a.Id).Value, Is.True);

            var expected = TruthTableGenerator.Generate(t.Circuit).ToText();
            var actual = TruthTableGenerator.Generate(loaded).ToText();
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Fragment_RoundTripCarriesSubcircuitDefinitions()
        {
            var library = new SubcircuitLibrary();
            TestLibrary.AddFullAdder(library);
            var t = new TestCircuit(library);
            var adder = t.AddSubcircuit("full-adder");
            var bulb = t.Add(ComponentKind.Bulb);
            t.Connect(adder, bulb);

            var json = CircuitSerializer.SerializeFragment(CircuitFragment.Capture(t.Circuit, new[] { adder.Id, bulb.Id }), library);
            Assert.That(CircuitSerializer.LooksLikeFragment(json), Is.True);

            var otherLibrary = new SubcircuitLibrary();
            var fragment = CircuitSerializer.DeserializeFragment(json, otherLibrary);
            Assert.That(otherLibrary.TryGet("full-adder", out _), Is.True);
            Assert.That(otherLibrary.TryGet("half-adder", out _), Is.True);
            Assert.That(fragment.Components.Count, Is.EqualTo(2));
            Assert.That(fragment.Wires.Count, Is.EqualTo(1));
        }

        [Test]
        public void InvalidJson_ThrowsCircuitException()
        {
            Assert.Throws<CircuitException>(() => CircuitSerializer.Deserialize("{ not json", new SubcircuitLibrary()));
        }

        [TestCase("dff", ComponentKind.DFlipFlop)]
        [TestCase("JK_FlipFlop", ComponentKind.JKFlipFlop)]
        [TestCase("led", ComponentKind.Bulb)]
        [TestCase("XNOR", ComponentKind.Xnor)]
        public void ComponentTypeNames_AreParsedLeniently(string text, ComponentKind expected)
        {
            Assert.That(CircuitSerializer.TryParseKind(text, out var kind), Is.True);
            Assert.That(kind, Is.EqualTo(expected));
        }
    }

    public class PerformanceTests
    {
        [Test]
        public void LargeCircuit_RunsWithinBudget()
        {
            var circuit = BenchmarkCircuits.Random(42, gates: 20_000, switches: 32);
            var stopwatch = Stopwatch.StartNew();
            var compiled = CircuitCompiler.Compile(circuit);
            long compileMs = stopwatch.ElapsedMilliseconds;

            using (var engine = new ManagedSimulationEngine())
            {
                engine.Load(compiled);
                stopwatch.Restart();
                engine.Run(1000);
                long runMs = stopwatch.ElapsedMilliseconds;
                TestContext.WriteLine($"compile {compileMs} ms, 1000 ticks {runMs} ms, {engine.TotalEvaluations / 1_000_000.0:F1} M gate evaluations");
                Assert.That(engine.Tick, Is.EqualTo(1000));
                Assert.That(runMs, Is.LessThan(30_000));
            }
        }
    }
}
