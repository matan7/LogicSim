using System;
using System.IO;
using System.Linq;
using LogicSim.EditorTools.Symbols;
using NUnit.Framework;
using UnityEngine;

namespace LogicSim.Tests.App
{
    public class SymbolAtlasTests
    {
        private static readonly string[] ExpectedIds =
        {
            "gate.and", "gate.or", "gate.xor", "gate.not", "gate.buf", "gate.nand", "gate.nor", "gate.xnor",
            "io.switch", "io.bulb", "io.button", "io.clock", "io.constant",
            "icon.neuron", "icon.lif", "icon.network", "mark.bubble", "mark.clock",
        };

        private static PsdReader.Document ReadSource()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, SymbolAtlasBuilder.SourcePath);
            Assert.That(File.Exists(path), Is.True, path);
            return PsdReader.Read(path);
        }

        [Test]
        public void PsdReader_ReadsTheArtboardsAndTheirLayers()
        {
            var document = ReadSource();
            var boards = document.Layers.Where(l => l.IsArtboard).ToList();
            Assert.That(boards.Select(b => b.Name), Is.EquivalentTo(ExpectedIds));

            var and = boards.Single(b => b.Name == "gate.and");
            Assert.That(new[] { and.ArtboardLeft, and.ArtboardTop, and.ArtboardRight, and.ArtboardBottom }, Is.EqualTo(new[] { 100.0, 100, 600, 500 }));
            Assert.That(and.Children.Select(c => c.Name), Is.EquivalentTo(new[] { "outline", "fill" }));
            var fill = and.Children.Single(c => c.Name == "fill");
            Assert.That(fill.Alpha, Is.Not.Null);
            // The body's middle is covered, the artboard's corner is not.
            Assert.That(fill.AlphaAt(350, 300), Is.EqualTo(255));
            Assert.That(fill.AlphaAt(110, 110), Is.EqualTo(0));
        }

        [Test]
        public void DistanceField_OfADisc_IsTheDistanceToItsEdge()
        {
            const int size = 200;
            const float cx = 100, cy = 100, radius = 50;
            var mask = new bool[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    mask[y * size + x] = (x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) <= radius * radius;

            var field = DistanceField.FromMask(mask, size, size);
            foreach (var (x, y) in new[] { (100f, 100f), (130f, 100f), (100f, 160f), (170f, 170f), (5f, 100f), (100f, 140f) })
            {
                float expected = radius - (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                // The disc is rasterized: along the diagonals its edge is up to about a pixel off the true circle.
                Assert.That(field.Sample(x, y), Is.EqualTo(expected).Within(1.5f), $"at ({x}, {y})");
            }
        }

        [Test]
        public void Builder_PacksEverySymbolWithItsFields()
        {
            var result = SymbolAtlasBuilder.Build(ReadSource());
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(result.Cells.Select(c => c.Id), Is.EquivalentTo(ExpectedIds));

            // Cells must not overlap.
            foreach (var a in result.Cells)
                foreach (var b in result.Cells.Where(b => b != a))
                    Assert.That(a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y, Is.True, $"{a.Id} overlaps {b.Id}");

            var and = result.Cells.Single(c => c.Id == "gate.and");
            Assert.That((and.UnitsX, and.UnitsY), Is.EqualTo((5, 4)));
            Color32 At(float unitsX, float unitsYFromTop)
            {
                int tx = SymbolAtlasBuilder.PaddingTexels + (int)(unitsX * SymbolAtlasBuilder.TexelsPerUnit);
                int ty = and.Height - 1 - SymbolAtlasBuilder.PaddingTexels - (int)(unitsYFromTop * SymbolAtlasBuilder.TexelsPerUnit);
                return result.Pixels[(and.Y + ty) * result.Size + and.X + tx];
            }
            // Inside the body: deep in the fill, away from the outline.
            Assert.That(At(2.5f, 2f).g, Is.GreaterThan(200));
            Assert.That(At(2.5f, 2f).r, Is.LessThan(60));
            // The body's flat back is a 0.16-unit stroke from x = 0.99 units inwards.
            Assert.That(At(1.07f, 2f).r, Is.GreaterThan(150));
            // The artboard's top-left corner is empty.
            Assert.That(At(0.1f, 0.1f).g, Is.LessThan(30));
            Assert.That(At(0.1f, 0.1f).r, Is.LessThan(30));

            // Pin stubs run to the outline: the AND's flat back and front, the NAND's bubble.
            Assert.That(and.Left[2], Is.EqualTo(0.99f).Within(0.02f));
            Assert.That(and.Right[2], Is.EqualTo(4.32f).Within(0.02f));
            Assert.That(result.Cells.Single(c => c.Id == "gate.nand").Right[2], Is.EqualTo(4.68f).Within(0.02f));
            // Rows that miss the outline have no edge.
            Assert.That(result.Cells.Single(c => c.Id == "io.clock").Left[0], Is.EqualTo(-1f));
        }

        [Test]
        public void UiIconBuilder_DrawsEveryIcon()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            var icons = UiIconBuilder.Build(PsdReader.Read(Path.Combine(root, UiIconBuilder.IconSourcePath)), ReadSource());
            Assert.That(icons.Keys, Is.SupersetOf(new[] { "ui.new", "ui.undo", "ui.ic", "comp.dff", "comp.pinout" }));
            Assert.That(icons.Keys.Where(k => k.StartsWith(UiIconBuilder.SymbolIconPrefix + "gate.")).Count(), Is.EqualTo(8));
            // Square icons from Icons.psd; the 5 x 4 gates as wide as the icons and as high as their aspect.
            Assert.That((icons["ui.new"].Width, icons["ui.new"].Height), Is.EqualTo((128, 128)));
            Assert.That((icons["comp.gate.and"].Width, icons["comp.gate.and"].Height), Is.EqualTo((128, 104)));
            foreach (var pair in icons)
            {
                var pixels = pair.Value.Pixels;
                Assert.That(pixels.Count(p => p.a > 200), Is.GreaterThan(100), pair.Key + " has a drawing");
                Assert.That(pixels[0].a, Is.Zero, pair.Key + " has a transparent corner");
            }
        }

        [Test]
        public void SymbolSet_InResources_IsComplete()
        {
            // The app loads this asset; it has to be rebuilt and committed after every change to the PSD.
            var set = Resources.Load<LogicSim.Views.SymbolSet>(LogicSim.Views.SymbolRenderer.ResourceName);
            Assert.That(set, Is.Not.Null);
            Assert.That(set.Atlas, Is.Not.Null);
            Assert.That(set.Shader, Is.Not.Null);
            Assert.That(set.Shader.isSupported, Is.True);
            Assert.That(set.Symbols.Select(s => s.Id), Is.EquivalentTo(ExpectedIds));
            foreach (var symbol in set.Symbols)
            {
                Assert.That(symbol.Left, Has.Length.EqualTo((int)symbol.Size.y + 1), symbol.Id);
                Assert.That(symbol.Right, Has.Length.EqualTo((int)symbol.Size.y + 1), symbol.Id);
            }
        }
    }
}
