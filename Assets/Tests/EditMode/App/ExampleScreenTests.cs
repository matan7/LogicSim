using System.Linq;
using LogicSim.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace LogicSim.Tests.App
{
    /// <summary>The bundled Sierpinski screen draws its triangle by itself: pixel (x, y) is lit when x AND y is 0.</summary>
    public class ExampleScreenTests
    {
        [Test]
        public void TheSierpinskiScreen_DrawsTheTriangle()
        {
            var asset = Resources.Load<TextAsset>("Examples/sierpinski-screen");
            Assert.That(asset, Is.Not.Null);
            var library = new SubcircuitLibrary();
            var document = CircuitSerializer.Deserialize(asset.text, library);
            Assert.That(document.Warnings, Is.Empty);
            var circuit = new Circuit(library);
            document.Snapshot.Restore(circuit);

            var screen = circuit.Components.Single(c => c.Kind == ComponentKind.Screen);
            int width = ScreenLayout.Width(screen), height = ScreenLayout.Height(screen);
            int half = circuit.Components.Single(c => c.Kind == ComponentKind.Clock).ClockHalfPeriod;
            var pixels = new byte[width * height];

            using var session = new SimulationSession(circuit, new ManagedSimulationEngine());
            session.Reset();
            // One clock period per pixel, written half a period after the counter moved on.
            session.Run(2L * half * (width * height / 2));
            Assert.That(session.TryGetScreen(screen, pixels), Is.True);
            Assert.That(pixels.Skip(width * height / 2).All(p => p == 0), Is.True, "halfway, the lower half is still dark");

            session.Run(2L * half * (width * height / 2 + 1));
            Assert.That(session.TryGetScreen(screen, pixels), Is.True);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    Assert.That(pixels[y * width + x], Is.EqualTo((x & y) == 0 ? 1 : 0), $"pixel ({x}, {y})");
            Assert.That(pixels.Count(p => p != 0), Is.EqualTo(243), "3^5 lit pixels");
        }
    }
}
