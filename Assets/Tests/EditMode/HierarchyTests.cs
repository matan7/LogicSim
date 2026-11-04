using System.Linq;
using LogicSim.Simulation;
using NUnit.Framework;

namespace LogicSim.Tests
{
    public class HierarchyTests
    {
        /// <summary>Flat full adder: switches A, B, Cin, gates, bulbs S and Cout.</summary>
        private static TestCircuit FlatFullAdder(out CircuitComponent[] gates)
        {
            var t = new TestCircuit();
            var a = t.Add(ComponentKind.Switch, "A");
            var b = t.Add(ComponentKind.Switch, "B");
            var cin = t.Add(ComponentKind.Switch, "Cin");
            var x1 = t.Add(ComponentKind.Xor);
            var x2 = t.Add(ComponentKind.Xor);
            var a1 = t.Add(ComponentKind.And);
            var a2 = t.Add(ComponentKind.And);
            var or = t.Add(ComponentKind.Or);
            var s = t.Add(ComponentKind.Bulb, "S");
            var cout = t.Add(ComponentKind.Bulb, "Cout");
            t.Connect(a, x1, 0);
            t.Connect(b, x1, 1);
            t.Connect(x1, x2, 0);
            t.Connect(cin, x2, 1);
            t.Connect(a, a1, 0);
            t.Connect(b, a1, 1);
            t.Connect(x1, a2, 0);
            t.Connect(cin, a2, 1);
            t.Connect(a1, or, 0);
            t.Connect(a2, or, 1);
            t.Connect(x2, s);
            t.Connect(or, cout);
            gates = new[] { x1, x2, a1, a2, or };
            return t;
        }

        private static TestCircuit FullAdderInstance(SubcircuitLibrary library, out CircuitComponent instance)
        {
            TestLibrary.AddFullAdder(library);
            var t = new TestCircuit(library);
            var a = t.Add(ComponentKind.Switch, "A");
            var b = t.Add(ComponentKind.Switch, "B");
            var cin = t.Add(ComponentKind.Switch, "Cin");
            instance = t.AddSubcircuit("full-adder");
            var s = t.Add(ComponentKind.Bulb, "S");
            var cout = t.Add(ComponentKind.Bulb, "Cout");
            t.Connect(a, instance, 0);
            t.Connect(b, instance, 1);
            t.Connect(cin, instance, 2);
            t.Connect(instance, 0, s, 0);
            t.Connect(instance, 1, cout, 0);
            return t;
        }

        private static string Table(Circuit circuit) => TruthTableGenerator.Generate(circuit).ToText();

        [Test]
        public void Collapse_KeepsBehaviourAndLeavesSwitchesOutside()
        {
            var t = FlatFullAdder(out var gates);
            var before = Table(t.Circuit);
            var editor = new DirectCircuitEditor(t.Circuit);

            // Select everything: switches and bulbs must stay outside.
            var result = SubcircuitRefactoring.Collapse(t.Circuit, t.Circuit.Components.Select(c => c.Id).ToList(), "Adder");
            editor.Apply(result.Edit);

            Assert.That(t.Circuit.Library.Count, Is.EqualTo(1));
            Assert.That(result.KeptOutside.Count, Is.EqualTo(5));
            Assert.That(t.Circuit.Components.Count(c => c.Kind == ComponentKind.Subcircuit), Is.EqualTo(1));
            Assert.That(gates.Any(g => t.Circuit.TryGetComponent(g.Id, out _)), Is.False);
            Assert.That(result.Definition.InputNames, Is.EqualTo(new[] { "A", "B", "Cin" }));
            Assert.That(result.Definition.OutputNames, Is.EqualTo(new[] { "S", "Cout" }));
            Assert.That(Table(t.Circuit), Is.EqualTo(before));

            editor.Undo();
            Assert.That(t.Circuit.Library.Count, Is.EqualTo(0));
            Assert.That(gates.All(g => t.Circuit.TryGetComponent(g.Id, out _)), Is.True);
            Assert.That(Table(t.Circuit), Is.EqualTo(before));
        }

        [Test]
        public void Collapse_OneSourceFeedingSeveralGates_BecomesOnePin()
        {
            var t = FlatFullAdder(out var gates);
            // Only the carry logic: x1 feeds a2 from outside, A and B feed a1.
            var result = SubcircuitRefactoring.Collapse(t.Circuit, new[] { gates[2].Id, gates[3].Id, gates[4].Id }, "Carry");
            Assert.That(result.Definition.InputComponentIds.Count, Is.EqualTo(4)); // A, B, x1, Cin
            Assert.That(result.Definition.OutputComponentIds.Count, Is.EqualTo(1));
            var before = Table(t.Circuit);
            new DirectCircuitEditor(t.Circuit).Apply(result.Edit);
            Assert.That(Table(t.Circuit), Is.EqualTo(before));
        }

        [Test]
        public void Explode_KeepsBehaviour_AndUndoRestoresInstance()
        {
            var library = new SubcircuitLibrary();
            var t = FullAdderInstance(library, out var instance);
            var before = Table(t.Circuit);
            var editor = new DirectCircuitEditor(t.Circuit);

            var result = SubcircuitRefactoring.Explode(t.Circuit, instance.Id);
            editor.Apply(result.Edit);

            Assert.That(t.Circuit.TryGetComponent(instance.Id, out _), Is.False);
            // The full adder contained two half adders and an OR gate; pins disappear.
            Assert.That(t.Circuit.InstancesOf("half-adder").Count(), Is.EqualTo(2));
            Assert.That(t.Circuit.Components.Count(c => c.Kind == ComponentKind.PinIn || c.Kind == ComponentKind.PinOut), Is.EqualTo(0));
            Assert.That(Table(t.Circuit), Is.EqualTo(before));

            editor.Undo();
            Assert.That(t.Circuit.TryGetComponent(instance.Id, out _), Is.True);
            Assert.That(Table(t.Circuit), Is.EqualTo(before));
        }

        [Test]
        public void Explode_PassThroughPin_ConnectsOutsideDirectly()
        {
            var library = new SubcircuitLibrary();
            var inner = new TestCircuit(library);
            var pin = inner.Add(ComponentKind.PinIn, "in");
            var pout = inner.Add(ComponentKind.PinOut, "out");
            inner.Connect(pin, pout);
            library.Add(new SubcircuitDefinition("wire", "Wire", inner.Circuit));

            var t = new TestCircuit(library);
            var sw = t.Add(ComponentKind.Switch);
            var ic = t.AddSubcircuit("wire");
            var bulb = t.Add(ComponentKind.Bulb);
            t.Connect(sw, ic);
            t.Connect(ic, bulb);

            new DirectCircuitEditor(t.Circuit).Apply(SubcircuitRefactoring.Explode(t.Circuit, ic.Id).Edit);
            var wire = t.Circuit.GetWireInto(bulb.Id, 0);
            Assert.That(wire, Is.Not.Null);
            Assert.That(wire.From.ComponentId, Is.EqualTo(sw.Id));
        }

        [Test]
        public void EditingIc_ReorderedAndRenamedPinsKeepTheirWires()
        {
            var library = new SubcircuitLibrary();
            var t = FullAdderInstance(library, out var instance);
            var before = Table(t.Circuit);
            var editor = new DirectCircuitEditor(t.Circuit);
            var hierarchy = new CircuitHierarchy(t.Circuit, editor);

            // Open the full adder, then the first half adder inside it.
            hierarchy.OpenInstance(instance.Id);
            var halfAdderInstance = hierarchy.Current.Circuit.InstancesOf("half-adder").OrderBy(c => c.Id).First();
            hierarchy.OpenInstance(halfAdderInstance.Id);
            var level = hierarchy.Current;
            var a = level.Circuit.FindByLabel("A");
            var b = level.Circuit.FindByLabel("B");
            // Swap A and B vertically (swaps the pin order) and rename A.
            hierarchy.Editor.Apply(MoveComponentsEdit.To(level.Circuit, new[] { a.Id, b.Id }, new[] { (b.X, b.Y), (a.X, a.Y) }));
            var props = a.GetProperties();
            props.Label = "X";
            hierarchy.Editor.Apply(new SetPropertiesEdit(level.Circuit, a.Id, props));
            Assert.That(hierarchy.IsModified(level), Is.True);

            hierarchy.Close(apply: true);
            Assert.That(library.Get("half-adder").InputNames, Is.EqualTo(new[] { "B", "X" }));
            // The working copy of the full adder (and its library version) follow the new pin order.
            var outer = hierarchy.Current.Circuit;
            Assert.That(outer.GetWireInto(halfAdderInstance.Id, 0).From.ComponentId, Is.EqualTo(outer.FindByLabel("B").Id));
            Assert.That(outer.GetWireInto(halfAdderInstance.Id, 1).From.ComponentId, Is.EqualTo(outer.FindByLabel("A").Id));
            var stored = library.Get("full-adder").Circuit;
            Assert.That(stored.GetWireInto(halfAdderInstance.Id, 0).From.ComponentId, Is.EqualTo(stored.FindByLabel("B").Id));
            hierarchy.Close(apply: true);
            Assert.That(hierarchy.IsAtRoot, Is.True);
            Assert.That(Table(t.Circuit), Is.EqualTo(before));

            // One undo step brings back the original half adder.
            Assert.That(editor.UndoCount, Is.EqualTo(1));
            editor.Undo();
            Assert.That(library.Get("half-adder").InputNames, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(Table(t.Circuit), Is.EqualTo(before));
            editor.Redo();
            Assert.That(library.Get("half-adder").InputNames, Is.EqualTo(new[] { "B", "X" }));
            Assert.That(Table(t.Circuit), Is.EqualTo(before));
        }

        [Test]
        public void EditingIc_SwappedPinsOfAsymmetricIc_KeepBehaviour()
        {
            var library = new SubcircuitLibrary();
            var inner = new TestCircuit(library);
            var a = inner.Add(ComponentKind.PinIn, "A");
            var b = inner.Add(ComponentKind.PinIn, "B");
            var not = inner.Add(ComponentKind.Not);
            var and = inner.Add(ComponentKind.And);
            var y = inner.Add(ComponentKind.PinOut, "Y");
            inner.Connect(b, not);
            inner.Connect(a, and, 0);
            inner.Connect(not, and, 1);
            inner.Connect(and, y);
            library.Add(new SubcircuitDefinition("andnot", "A and not B", inner.Circuit));

            var t = new TestCircuit(library);
            var sa = t.Add(ComponentKind.Switch, "A");
            var sb = t.Add(ComponentKind.Switch, "B");
            var ic = t.AddSubcircuit("andnot");
            var bulb = t.Add(ComponentKind.Bulb, "Y");
            t.Connect(sa, ic, 0);
            t.Connect(sb, ic, 1);
            t.Connect(ic, bulb);
            var before = Table(t.Circuit);

            var hierarchy = new CircuitHierarchy(t.Circuit, new DirectCircuitEditor(t.Circuit));
            var level = hierarchy.OpenInstance(ic.Id);
            var pa = level.Circuit.FindByLabel("A");
            var pb = level.Circuit.FindByLabel("B");
            hierarchy.Editor.Apply(MoveComponentsEdit.To(level.Circuit, new[] { pa.Id, pb.Id }, new[] { (pb.X, pb.Y), (pa.X, pa.Y) }));
            hierarchy.Close(apply: true);

            Assert.That(library.Get("andnot").InputNames, Is.EqualTo(new[] { "B", "A" }));
            Assert.That(t.Circuit.GetWireInto(ic.Id, 0).From.ComponentId, Is.EqualTo(sb.Id));
            Assert.That(Table(t.Circuit), Is.EqualTo(before));
        }

        [Test]
        public void EditingIc_RemovedPinDropsItsWires_UndoRestoresThem()
        {
            var library = new SubcircuitLibrary();
            var t = FullAdderInstance(library, out var instance);
            var editor = new DirectCircuitEditor(t.Circuit);
            var hierarchy = new CircuitHierarchy(t.Circuit, editor);

            var level = hierarchy.OpenInstance(instance.Id);
            var cin = level.Circuit.FindByLabel("Cin");
            hierarchy.Editor.Apply(new RemoveElementsEdit(level.Circuit, new[] { cin.Id }));
            hierarchy.Close(apply: true);

            Assert.That(t.Circuit.GetInputCount(instance), Is.EqualTo(2));
            Assert.That(t.Circuit.GetWireInto(instance.Id, 2), Is.Null);
            Assert.That(t.Circuit.GetWiresOf(instance.Id).Count, Is.EqualTo(4));

            editor.Undo();
            Assert.That(t.Circuit.GetInputCount(instance), Is.EqualTo(3));
            Assert.That(t.Circuit.GetWireInto(instance.Id, 2), Is.Not.Null);
        }

        [Test]
        public void ClosingWithoutApply_RevertsEverythingDoneInside()
        {
            var library = new SubcircuitLibrary();
            var t = FullAdderInstance(library, out var instance);
            var editor = new DirectCircuitEditor(t.Circuit);
            var hierarchy = new CircuitHierarchy(t.Circuit, editor);

            var level = hierarchy.OpenInstance(instance.Id);
            var ha = level.Circuit.InstancesOf("half-adder").First();
            hierarchy.Editor.Apply(SubcircuitRefactoring.MakeUnique(level.Circuit, ha.Id, null, out var copy));
            Assert.That(library.Contains(copy.Id), Is.True);
            hierarchy.Close(apply: false);

            Assert.That(library.Contains(copy.Id), Is.False);
            Assert.That(editor.UndoCount, Is.EqualTo(0));
            Assert.That(library.Get("full-adder").Circuit.InstancesOf("half-adder").Count(), Is.EqualTo(2));
        }

        [Test]
        public void OpenIcs_CannotBePlacedInsideThemselves()
        {
            var library = new SubcircuitLibrary();
            var t = FullAdderInstance(library, out var instance);
            var hierarchy = new CircuitHierarchy(t.Circuit, new DirectCircuitEditor(t.Circuit));
            hierarchy.OpenInstance(instance.Id);
            var ha = hierarchy.Current.Circuit.InstancesOf("half-adder").First();
            hierarchy.OpenInstance(ha.Id);

            Assert.That(hierarchy.CanPlace("half-adder"), Is.False);
            Assert.That(hierarchy.CanPlace("full-adder"), Is.False);
            Assert.Throws<CircuitException>(() => hierarchy.OpenDefinition("full-adder"));
        }

        [Test]
        public void InstanceReader_ShowsValuesInsideNestedInstances()
        {
            var library = new SubcircuitLibrary();
            var t = FullAdderInstance(library, out var instance);
            using (var session = t.CreateSession())
            {
                foreach (var label in new[] { "A", "B" })
                    t.Circuit.SetInputValue(t.Circuit.FindByLabel(label).Id, true);
                session.Reset();
                Assert.That(session.Settle(1000), Is.True);

                var fullAdder = library.Get("full-adder").Circuit;
                var inside = session.CreateReader(new[] { instance.Id });
                var or = fullAdder.Components.First(c => c.Kind == ComponentKind.Or);
                Assert.That(inside.GetOutputValue(or.Id, 0), Is.True, "Cout = A AND B");
                Assert.That(inside.GetDisplayValue(fullAdder.FindByLabel("S")), Is.False);
                Assert.That(inside.GetDisplayValue(fullAdder.FindByLabel("A")), Is.True);

                var ha1 = fullAdder.InstancesOf("half-adder").OrderBy(c => c.Id).First();
                var deeper = session.CreateReader(new[] { instance.Id, ha1.Id });
                var halfAdder = library.Get("half-adder").Circuit;
                var and = halfAdder.Components.First(c => c.Kind == ComponentKind.And);
                Assert.That(deeper.GetOutputValue(and.Id, 0), Is.True);
                Assert.That(deeper.GetInputBits(and), Is.EqualTo(3));

                Assert.That(((InstanceSignalReader)session.CreateReader(new[] { 999 })).IsValid, Is.False);
            }
        }

        [Test]
        public void LibraryUpdate_MarksSessionDirty()
        {
            var library = new SubcircuitLibrary();
            var t = FullAdderInstance(library, out _);
            using (var session = t.CreateSession())
            {
                session.EnsureCompiled();
                Assert.That(session.IsDirty, Is.False);
                library.Replace(library.Get("half-adder").WithName("HA"));
                Assert.That(session.IsDirty, Is.True);
            }
        }

        [Test]
        public void MakeUnique_EditingTheCopyLeavesOtherInstancesAlone()
        {
            var library = new SubcircuitLibrary();
            TestLibrary.AddHalfAdder(library);
            var t = new TestCircuit(library);
            var first = t.AddSubcircuit("half-adder");
            var second = t.AddSubcircuit("half-adder");
            var editor = new DirectCircuitEditor(t.Circuit);
            editor.Apply(SubcircuitRefactoring.MakeUnique(t.Circuit, second.Id, null, out var copy));
            Assert.That(copy.Name, Is.EqualTo("Half adder 2"));
            Assert.That(second.SubcircuitId, Is.EqualTo(copy.Id));

            var hierarchy = new CircuitHierarchy(t.Circuit, editor);
            var level = hierarchy.OpenInstance(second.Id);
            var c = level.Circuit.FindByLabel("C");
            hierarchy.Editor.Apply(new RemoveElementsEdit(level.Circuit, new[] { c.Id }));
            hierarchy.Close(apply: true);

            Assert.That(t.Circuit.GetOutputCount(first), Is.EqualTo(2));
            Assert.That(t.Circuit.GetOutputCount(second), Is.EqualTo(1));
        }

        [Test]
        public void ApplyAll_WritesBackEveryOpenLevel()
        {
            var library = new SubcircuitLibrary();
            var t = FullAdderInstance(library, out var instance);
            var editor = new DirectCircuitEditor(t.Circuit);
            var hierarchy = new CircuitHierarchy(t.Circuit, editor);
            var level = hierarchy.OpenInstance(instance.Id);
            var or = level.Circuit.Components.First(c => c.Kind == ComponentKind.Or);
            var props = or.GetProperties();
            props.Label = "carry";
            hierarchy.Editor.Apply(new SetPropertiesEdit(level.Circuit, or.Id, props));

            Assert.That(hierarchy.ApplyAll(), Is.EqualTo(1));
            Assert.That(hierarchy.IsModified(level), Is.False);
            Assert.That(library.Get("full-adder").Circuit.FindByLabel("carry"), Is.Not.Null);
            Assert.That(hierarchy.GetInstancePath(level), Is.EqualTo(new[] { instance.Id }));
        }
    }
}
