using System.Linq;
using LogicSim.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace LogicSim.Tests.App
{
    /// <summary>The bundled Sierpinski screen draws its triangle by itself: pixel (x, y) is lit when x AND y is 0.</summary>
    public class ExampleScreenTests
    {
        private const int Size = 32;

        private static (Circuit circuit, CircuitComponent screen, CircuitComponent clock) Load()
        {
            var asset = Resources.Load<TextAsset>("Examples/sierpinski-screen");
            Assert.That(asset, Is.Not.Null);
            var library = new SubcircuitLibrary();
            var document = CircuitSerializer.Deserialize(asset.text, library);
            Assert.That(document.Warnings, Is.Empty);
            var circuit = new Circuit(library);
            document.Snapshot.Restore(circuit);
            var screen = circuit.Components.Single(c => c.Kind == ComponentKind.Screen);
            Assert.That(ScreenLayout.Width(screen), Is.EqualTo(Size));
            Assert.That(ScreenLayout.Height(screen), Is.EqualTo(Size));
            return (circuit, screen, circuit.Components.Single(c => c.Kind == ComponentKind.Clock));
        }

        private static byte Expected(int pixel) => ((pixel % Size) & (pixel / Size)) == 0 ? (byte)1 : (byte)0;

        [Test]
        public void TheSierpinskiScreen_DrawsTheTriangle()
        {
            var (circuit, screen, clock) = Load();
            int half = clock.ClockHalfPeriod;
            var pixels = new byte[Size * Size];

            using var session = new SimulationSession(circuit, new ManagedSimulationEngine());
            session.Reset();
            // One clock period per pixel, written half a period after the counter moved on.
            session.Run(2L * half * (Size * Size / 2));
            Assert.That(session.TryGetScreen(screen, pixels), Is.True);
            Assert.That(pixels.Skip(Size * Size / 2).All(p => p == 0), Is.True, "halfway, the lower half is still dark");

            session.Run(2L * half * (Size * Size / 2 + 1));
            Assert.That(session.TryGetScreen(screen, pixels), Is.True);
            for (int i = 0; i < pixels.Length; i++)
                Assert.That(pixels[i], Is.EqualTo(Expected(i)), $"pixel ({i % Size}, {i / Size})");
            Assert.That(pixels.Count(p => p != 0), Is.EqualTo(243), "3^5 lit pixels");
        }

        /// <summary>
        /// The counter is synchronous: every bit changes one tick after the clock edge and the carries and D settle two
        /// ticks later, so even a clock half period of 2 writes each pixel at its own address, in order.
        /// </summary>
        [Test]
        public void TheSierpinskiScreen_WritesEveryPixelInOrder_WithTheFastestClock()
        {
            var (circuit, screen, clock) = Load();
            const int half = 2;
            var properties = clock.GetProperties();
            properties.ClockHalfPeriod = half;
            circuit.SetProperties(clock.Id, properties);
            var pixels = new byte[Size * Size];

            using var session = new SimulationSession(circuit, new ManagedSimulationEngine());
            session.Reset();
            for (int k = 0; k < pixels.Length; k++)
            {
                // Pixel k is written at tick 2 * k * half + 2; look just before pixel k + 1.
                session.Run(2L * (k + 1) * half + 1 - session.Tick);
                session.TryGetScreen(screen, pixels);
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte expected = i <= k ? Expected(i) : (byte)0;
                    if (pixels[i] != expected)
                        Assert.Fail($"after pixel {k}: pixel ({i % Size}, {i / Size}) is {pixels[i]}, expected {expected}");
                }
            }
        }
    }
}
