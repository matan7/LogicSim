using System.Collections;
using System.Linq;
using Core;
using LogicSim.Simulation;
using LogicSim.Views;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LogicSim.Tests.PlayMode
{
    /// <summary>Components with an atlas symbol get a <see cref="SymbolComponentView"/> drawn by the symbol renderer.</summary>
    public class SymbolRenderingTests
    {
        private static Circuit Circuit => SystemCore.CircuitService.Circuit;

        private static ComponentView ViewOf(CircuitComponent component)
        {
            Assert.That(CircuitView.Instance.TryGetComponentView(component.Id, out var view), Is.True, $"view of {component.Kind}");
            return view;
        }

        [UnityTest]
        public IEnumerator GatesAndInputsWithASymbolAreDrawnAsInstances()
        {
            SceneManager.LoadScene("SampleScene");
            for (int i = 0; i < 5; i++)
                yield return null;
            var symbols = CircuitView.Instance.Symbols;
            Assert.That(symbols, Is.Not.Null, "symbol rendering is available");
            Assert.That(symbols.Count, Is.Zero);

            var withSymbols = new[]
            {
                ComponentFactory.Create(ComponentKind.And, 10, 0),
                ComponentFactory.Create(ComponentKind.Not, 20, 0),
                ComponentFactory.Create(ComponentKind.Switch, 0, 0),
                ComponentFactory.Create(ComponentKind.Bulb, 30, 0),
                ComponentFactory.Create(ComponentKind.Clock, 0, -10),
                ComponentFactory.Create(ComponentKind.Constant, 0, -20, value: true),
            };
            var withoutSymbols = new[]
            {
                ComponentFactory.Create(ComponentKind.And, 10, -10, inputs: 3),
                ComponentFactory.Create(ComponentKind.DFlipFlop, 20, -10),
            };
            SystemCore.CircuitService.Editor.Apply(new AddElementsEdit(Circuit, withSymbols.Concat(withoutSymbols)));
            yield return null;

            foreach (var component in withSymbols)
                Assert.That(ViewOf(component), Is.InstanceOf<SymbolComponentView>(), component.Kind.ToString());
            foreach (var component in withoutSymbols)
                Assert.That(ViewOf(component), Is.Not.InstanceOf<SymbolComponentView>(), component.Kind.ToString());
            Assert.That(symbols.Count, Is.EqualTo(withSymbols.Length));

            // A third input has no symbol yet: the AND becomes a generated box.
            var and = withSymbols[0];
            var properties = and.GetProperties();
            properties.InputCount = 3;
            SystemCore.CircuitService.Editor.Apply(new SetPropertiesEdit(Circuit, and.Id, properties));
            Assert.That(ViewOf(and), Is.InstanceOf<GenericComponentView>());
            Assert.That(symbols.Count, Is.EqualTo(withSymbols.Length - 1));

            // Rotating keeps the symbol view; removing a component removes its instance in the same frame.
            var not = withSymbols[1];
            properties = not.GetProperties();
            properties.Rotation = 1;
            SystemCore.CircuitService.Editor.Apply(new SetPropertiesEdit(Circuit, not.Id, properties));
            Assert.That(ViewOf(not), Is.InstanceOf<SymbolComponentView>());
            SystemCore.CircuitService.Editor.Apply(new RemoveElementsEdit(Circuit, new[] { not.Id }, new int[0]));
            Assert.That(symbols.Count, Is.EqualTo(withSymbols.Length - 2));

            // The pins stay where the wires expect them: the bulb's input at (0, -2) from its origin.
            var bulb = ViewOf(withSymbols[3]);
            Assert.That((Vector2)bulb.Inputs[0].transform.position, Is.EqualTo(new Vector2(30, -2)));
            yield return null;
        }
    }
}
