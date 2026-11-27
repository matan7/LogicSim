using System.Linq;
using LogicSim.Simulation;
using NUnit.Framework;

namespace LogicSim.Tests
{
    /// <summary>The pixel screen: its pins, its write port in the simulation, and its settings.</summary>
    public class ScreenTests
    {
        private static CircuitComponent AddScreen(TestCircuit t, int width, int height, int pixelSize = 1)
        {
            var screen = t.Add(ComponentKind.Screen);
            SetSize(t.Circuit, screen, width, height, pixelSize);
            return screen;
        }

        private static void SetSize(Circuit circuit, CircuitComponent screen, int width, int height, int pixelSize = 1)
        {
            var p = screen.GetProperties();
            screen.Spec.FindSetting("width").Set(ref p, width);
            screen.Spec.FindSetting("height").Set(ref p, height);
            screen.Spec.FindSetting("pixel_size").Set(ref p, pixelSize);
            circuit.SetProperties(screen.Id, p);
        }

        private static string[] InputNames(Circuit circuit, CircuitComponent component)
            => Enumerable.Range(0, circuit.GetInputCount(component)).Select(i => circuit.GetInputName(component, i)).ToArray();

        /// <summary>A screen with a switch on every input, labelled like the pins.</summary>
        private sealed class Rig
        {
            public readonly TestCircuit T = new TestCircuit();
            public readonly CircuitComponent Screen;
            public readonly CircuitComponent[] Inputs;
            public readonly SimulationSession Session;
            public readonly int Width;
            public readonly int Height;

            public Rig(int width, int height)
            {
                Width = width;
                Height = height;
                Screen = AddScreen(T, width, height);
                var names = InputNames(T.Circuit, Screen);
                Inputs = names.Select(n => T.Add(ComponentKind.Switch, n)).ToArray();
                for (int pin = 0; pin < Inputs.Length; pin++)
                    T.Connect(Inputs[pin], Screen, pin);
                Session = T.CreateSession();
                Session.Settle(100);
            }

            public void Set(string name, bool value)
            {
                T.Circuit.SetInputValue(Inputs.Single(i => i.Label == name).Id, value);
                Session.Settle(100);
            }

            private void SetAddress(int x, int y)
            {
                for (int bit = 0; bit < ScreenLayout.XBits(Screen); bit++)
                    Set("x" + bit, ((x >> bit) & 1) != 0);
                for (int bit = 0; bit < ScreenLayout.YBits(Screen); bit++)
                    Set("y" + bit, ((y >> bit) & 1) != 0);
            }

            public void Write(int x, int y, bool value)
            {
                SetAddress(x, y);
                Set("D", value);
                Set("WR", true);
                Set("WR", false);
            }

            public void Clear()
            {
                Set("CLR", true);
                Set("CLR", false);
            }

            public byte[] Pixels()
            {
                var pixels = new byte[Width * Height];
                Assert.That(Session.TryGetScreen(Screen, pixels), Is.True, "the netlist has the screen");
                return pixels;
            }
        }

        [Test]
        public void Pins_AreTheAddressBitsThenDWrClr()
        {
            var t = new TestCircuit();
            var screen = t.Add(ComponentKind.Screen);
            Assert.That(InputNames(t.Circuit, screen), Is.EqualTo(new[] { "x0", "x1", "x2", "x3", "y0", "y1", "y2", "y3", "D", "WR", "CLR" }),
                "16 x 16 by default");
            Assert.That(t.Circuit.GetOutputCount(screen), Is.Zero);

            SetSize(t.Circuit, screen, 20, 30);
            Assert.That(InputNames(t.Circuit, screen), Is.EqualTo(new[] { "x0", "x1", "x2", "x3", "x4", "y0", "y1", "y2", "y3", "y4", "D", "WR", "CLR" }));

            SetSize(t.Circuit, screen, 1, 2);
            Assert.That(InputNames(t.Circuit, screen), Is.EqualTo(new[] { "y0", "D", "WR", "CLR" }), "one column needs no x address");
        }

        [Test]
        public void ARisingWr_StoresD_AtTheAddressedPixel()
        {
            var rig = new Rig(20, 30);
            Assert.That(rig.Pixels(), Is.All.EqualTo(0), "the screen starts dark");

            rig.Write(3, 7, true);
            rig.Write(19, 29, true);
            rig.Write(0, 0, true);
            var pixels = rig.Pixels();
            Assert.That(pixels[7 * 20 + 3], Is.EqualTo(1));
            Assert.That(pixels[29 * 20 + 19], Is.EqualTo(1), "the last pixel");
            Assert.That(pixels[0], Is.EqualTo(1));
            Assert.That(pixels.Count(p => p != 0), Is.EqualTo(3));

            rig.Write(3, 7, false);
            Assert.That(rig.Pixels()[7 * 20 + 3], Is.EqualTo(0), "D = 0 clears the pixel");
        }

        [Test]
        public void TheAddressAndDAloneWriteNothing_AndAddressesOutsideAreIgnored()
        {
            var rig = new Rig(20, 30);
            rig.Write(25, 2, true); // x has 5 bits: up to 31, but the screen is 20 wide
            rig.Write(2, 31, true);
            Assert.That(rig.Pixels(), Is.All.EqualTo(0));

            // Changing D and the address while WR stays high writes nothing either: only the edge does.
            rig.Write(1, 1, true);
            rig.Set("WR", true);
            rig.Set("x1", true);
            rig.Set("D", true);
            Assert.That(rig.Pixels().Count(p => p != 0), Is.EqualTo(1));
            Assert.That(rig.Pixels()[1 * 20 + 1], Is.EqualTo(1));
        }

        [Test]
        public void ARisingClr_ClearsTheScreen_AndResetDoesToo()
        {
            var rig = new Rig(4, 4);
            for (int i = 0; i < 4; i++)
                rig.Write(i, i, true);
            Assert.That(rig.Pixels().Count(p => p != 0), Is.EqualTo(4));

            rig.Clear();
            Assert.That(rig.Pixels(), Is.All.EqualTo(0));

            rig.Write(2, 1, true);
            rig.Session.Reset();
            Assert.That(rig.Pixels(), Is.All.EqualTo(0), "a reset clears the pixels");
        }

        [Test]
        public void GrowingTheScreen_KeepsEveryWireOnItsPin()
        {
            var rig = new Rig(4, 4);
            var before = rig.T.Circuit.Wires.ToDictionary(w => w.Id, w => rig.T.Circuit.GetInputName(rig.Screen, w.To.Pin));

            SetSize(rig.T.Circuit, rig.Screen, 20, 30);

            Assert.That(rig.T.Circuit.GetInputCount(rig.Screen), Is.EqualTo(13));
            foreach (var wire in rig.T.Circuit.Wires)
                Assert.That(rig.T.Circuit.GetInputName(rig.Screen, wire.To.Pin), Is.EqualTo(before[wire.Id]), $"wire {wire.Id} stays on its pin");
            var session = rig.T.CreateSession();
            session.Settle(100);
            Assert.That(session.Compiled.HasErrors, Is.False);
        }

        [Test]
        public void ShrinkingTheScreen_IsRefusedWhileARemovedPinIsWired_AndAllowedOnceFree()
        {
            var rig = new Rig(8, 8);
            var error = Assert.Throws<CircuitException>(() => SetSize(rig.T.Circuit, rig.Screen, 4, 8));
            Assert.That(error.Message, Does.Contain("x2"));

            var x2 = rig.T.Circuit.Wires.Single(w => rig.T.Circuit.GetInputName(rig.Screen, w.To.Pin) == "x2");
            rig.T.Circuit.RemoveWire(x2.Id);
            SetSize(rig.T.Circuit, rig.Screen, 4, 8);
            Assert.That(InputNames(rig.T.Circuit, rig.Screen), Is.EqualTo(new[] { "x0", "x1", "y0", "y1", "y2", "D", "WR", "CLR" }));
        }

        [Test]
        public void Settings_AreCheckedAndSurviveSaving()
        {
            var t = new TestCircuit();
            var screen = AddScreen(t, 20, 30, pixelSize: 3);
            Assert.Throws<CircuitException>(() => SetSize(t.Circuit, screen, ScreenLayout.MaxSize + 1, 4));
            Assert.Throws<CircuitException>(() => SetSize(t.Circuit, screen, 4, 4, ScreenLayout.MaxPixelSize + 1));

            var json = CircuitSerializer.Serialize(t.Circuit, "screen");
            Assert.That(json, Does.Contain("\"width\"").And.Contain("\"pixelSize\""));
            var document = CircuitSerializer.Deserialize(json, new SubcircuitLibrary());
            Assert.That(document.Warnings, Is.Empty);
            var circuit = new Circuit();
            document.Snapshot.Restore(circuit);
            var loaded = circuit.Components.Single();
            Assert.That(loaded.Kind, Is.EqualTo(ComponentKind.Screen));
            Assert.That(ScreenLayout.Width(loaded), Is.EqualTo(20));
            Assert.That(ScreenLayout.Height(loaded), Is.EqualTo(30));
            Assert.That(ScreenLayout.PixelSize(loaded), Is.EqualTo(3));
        }

        [Test]
        public void AScreenInsideAnIc_IsReadThroughItsInstance()
        {
            var library = new SubcircuitLibrary();
            var inner = new TestCircuit(library);
            var screen = AddScreen(inner, 2, 2);
            var names = InputNames(inner.Circuit, screen);
            for (int pin = 0; pin < names.Length; pin++)
                inner.Connect(inner.Add(ComponentKind.Switch, names[pin]), screen, pin);
            library.Add(new SubcircuitDefinition("display", "Display", inner.Circuit));

            var outer = new TestCircuit(library);
            var instance = outer.AddSubcircuit("display");
            var switches = names.Select(n => outer.Add(ComponentKind.Switch, n)).ToArray();
            for (int pin = 0; pin < switches.Length; pin++)
                outer.Connect(switches[pin], instance, pin);
            var session = outer.CreateSession();
            session.Settle(100);

            void Set(string name, bool value)
            {
                outer.Circuit.SetInputValue(switches[System.Array.IndexOf(names, name)].Id, value);
                session.Settle(100);
            }
            Set("x0", true);
            Set("y0", true);
            Set("D", true);
            Set("WR", true);

            var pixels = new byte[4];
            var reader = session.CreateReader(new[] { instance.Id });
            Assert.That(reader.TryGetScreen(screen, pixels), Is.True);
            Assert.That(pixels, Is.EqualTo(new byte[] { 0, 0, 0, 1 }), "pixel (1, 1)");
            Assert.That(session.TryGetScreen(screen, pixels), Is.False, "the document level has no such screen");
        }
    }
}
