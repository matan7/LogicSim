using System.Collections;
using System.Linq;
using Behaviours;
using Core;
using LogicSim.Simulation;
using LogicSim.UI;
using LogicSim.Views;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LogicSim.Tests.PlayMode
{
    /// <summary>
    /// The main editing flows driven through the Input System with a virtual mouse and keyboard, on the real scene.
    /// </summary>
    public class AppFlowTests : InputTestFixture
    {
        private Mouse _mouse;
        private Keyboard _keyboard;

        private static Circuit Circuit => SystemCore.CircuitService.Circuit;

        private IEnumerator LoadApp()
        {
            _mouse = InputSystem.AddDevice<Mouse>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            SceneManager.LoadScene("SampleScene");
            // Awake/Start of the scene, then the bootstrap's views and UI.
            for (int i = 0; i < 5; i++)
                yield return null;
            Assert.That(CircuitView.Instance, Is.Not.Null, "the app started");
            Assert.That(AppUi.Instance, Is.Not.Null, "the UI was built");
        }

        [UnityTest]
        public IEnumerator PlaceAGateFromThePalette()
        {
            yield return LoadApp();
            var spawner = Object.FindObjectsByType<ObjectSpawner>(FindObjectsInactive.Exclude)
                .First(s => s.TryGetKind(out var kind) && kind == ComponentKind.And);
            var from = ScrollIntoView((RectTransform)spawner.transform);
            var to = new Vector2(Screen.width * 0.6f, Screen.height * 0.5f);

            yield return Drag(from, to);

            Assert.That(Circuit.ComponentCount, Is.EqualTo(1));
            Assert.That(Circuit.Components.Single().Kind, Is.EqualTo(ComponentKind.And));
        }

        [UnityTest]
        public IEnumerator WireTwoPins()
        {
            yield return LoadApp();
            var (input, output) = AddSwitchAndBulb();
            yield return null;

            yield return Drag(PinOnScreen(input.Id, 0, output: true), PinOnScreen(output.Id, 0, output: false));

            Assert.That(Circuit.WireCount, Is.EqualTo(1));
            var wire = Circuit.Wires.Single();
            Assert.That(wire.From, Is.EqualTo(new PinRef(input.Id, 0)));
            Assert.That(wire.To, Is.EqualTo(new PinRef(output.Id, 0)));
        }

        [UnityTest]
        public IEnumerator WiresFollowADraggedComponent()
        {
            yield return LoadApp();
            var (input, output) = AddSwitchAndBulb();
            SystemCore.CircuitService.Editor.Apply(new ConnectEdit(Circuit, new PinRef(input.Id, 0), new PinRef(output.Id, 0)));
            yield return null;
            var from = BodyOnScreen(output.Id);
            var to = from + new Vector2(0, -Screen.height * 0.2f);

            // Check the wire end while the bulb is still being dragged, then after the drop.
            Move(_mouse.position, from);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            for (int i = 1; i <= 6; i++)
            {
                Move(_mouse.position, Vector2.Lerp(from, to, i / 6f));
                yield return null;
            }
            yield return null;
            AssertWireEndsAtPin(output.Id, "while dragging");
            Release(_mouse.leftButton);
            yield return null;
            yield return null;

            Assert.That(Circuit.GetComponent(output.Id).Y, Is.LessThan(0), "the bulb moved down");
            AssertWireEndsAtPin(output.Id, "after the drop");
        }

        [UnityTest]
        public IEnumerator AnAreaSelectsAndTheSelectionDragsTogether()
        {
            yield return LoadApp();
            var a = ComponentFactory.Create(ComponentKind.And, 10, 0);
            var b = ComponentFactory.Create(ComponentKind.Or, 10, -8);
            var far = ComponentFactory.Create(ComponentKind.Not, 40, 0);
            SystemCore.CircuitService.Editor.Apply(new AddElementsEdit(Circuit, new[] { a, b, far }));
            AppCommands.FrameAll();
            yield return null;

            // From the empty canvas below and right of b, up across both gates; the rectangle only has to overlap them.
            var camera = Camera.main;
            yield return Drag(camera.WorldToScreenPoint(new Vector3(16, -14)), camera.WorldToScreenPoint(new Vector3(12, 3)));
            Assert.That(SystemCore.SelectingService.ComponentIds, Is.EquivalentTo(new[] { a.Id, b.Id }), "the area selects both gates");

            yield return Drag(BodyOnScreen(a.Id), BodyOnScreen(a.Id) + new Vector2(0, -Screen.height * 0.15f));
            float moved = Circuit.GetComponent(a.Id).Y;
            Assert.That(moved, Is.LessThan(0), "the pressed gate moved down");
            Assert.That(Circuit.GetComponent(b.Id).Y, Is.EqualTo(-8 + moved), "the other selected gate moved with it");
            Assert.That(Circuit.GetComponent(far.Id).Y, Is.EqualTo(0), "the unselected gate stayed");
            Assert.That(CircuitView.Instance.ComponentAt(new Vector2(12, moved - 2)), Is.SameAs(ViewOf(a.Id)), "picking follows the move");
        }

        private static void AssertWireEndsAtPin(int componentId, string when)
        {
            var end = Object.FindAnyObjectByType<WireView>().End;
            var pin = ViewOf(componentId).Inputs[0].transform.position;
            Assert.That(Vector2.Distance(end, pin), Is.LessThan(0.01f), $"wire end at the pin {when}");
        }

        [UnityTest]
        public IEnumerator ToggleASwitchInTheSimulation()
        {
            yield return LoadApp();
            var (input, output) = AddSwitchAndBulb();
            SystemCore.CircuitService.Editor.Apply(new ConnectEdit(Circuit, new PinRef(input.Id, 0), new PinRef(output.Id, 0)));
            yield return null;

            PressAndRelease(_keyboard.f5Key);
            yield return null;
            Assert.That(SystemCore.AppStateService.IsSimulating, Is.True, "F5 starts the simulation");

            yield return Click(BodyOnScreen(input.Id));
            for (int i = 0; i < 5; i++)
                yield return null;

            Assert.That(Circuit.GetComponent(input.Id).Value, Is.True, "the switch is on");
            Assert.That(SystemCore.SimulationService.Session.GetInputPinValue(output.Id, 0), Is.True, "the bulb sees it");

            PressAndRelease(_keyboard.f5Key);
            yield return null;
            Assert.That(SystemCore.AppStateService.IsEditing, Is.True, "F5 stops the simulation");
        }

        [UnityTest]
        public IEnumerator DoubleClickIntoAnIcAndBack()
        {
            yield return LoadApp();
            SystemCore.CircuitService.OpenJson(Resources.Load<TextAsset>("Examples/4-bit-adder").text, "4-bit adder");
            AppCommands.FrameAll();
            yield return null;
            var adder = Circuit.Components.First(c => c.Label == "FA1");
            var position = BodyOnScreen(adder.Id);

            yield return Click(position);
            yield return null;
            yield return Click(position);
            yield return null;
            Assert.That(SystemCore.CircuitService.Hierarchy.Depth, Is.EqualTo(1), "double-click opens the IC");
            Assert.That(Circuit.Components.Count(c => c.Kind == ComponentKind.PinIn), Is.EqualTo(3), "inside the full adder");

            PressAndRelease(_keyboard.escapeKey);
            yield return null;
            Assert.That(SystemCore.CircuitService.Hierarchy.Depth, Is.EqualTo(0), "Esc goes back");
        }

        [UnityTest]
        public IEnumerator ClickingTheInspectorKeepsTheSelection()
        {
            yield return LoadApp();
            var block = ComponentFactory.Create(ComponentKind.NeuralBlock, 0, 0);
            SystemCore.CircuitService.Editor.Apply(new AddElementsEdit(Circuit, new[] { block }));
            AppCommands.FrameAll();
            yield return null;
            yield return Click(BodyOnScreen(block.Id));
            yield return null;
            var inspector = Object.FindAnyObjectByType<InspectorPanel>();
            Assert.That(inspector.Panel.gameObject.activeSelf, Is.True, "selecting the block opens the inspector");

            // A click on the panel belongs to the UI, not to the canvas behind it.
            var panel = inspector.Panel;
            Vector2 center = RectTransformUtility.WorldToScreenPoint(null, panel.TransformPoint(panel.rect.center));
            yield return Click(center);
            yield return null;
            Assert.That(SystemCore.SelectingService.Count, Is.EqualTo(1), "the block stays selected");
            Assert.That(inspector.Panel.gameObject.activeSelf, Is.True, "the inspector stays open");

            // A click on the empty canvas still deselects.
            yield return Click(new Vector2(Screen.width * 0.5f, Screen.height * 0.15f));
            yield return null;
            Assert.That(SystemCore.SelectingService.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AnInspectorFieldTakesAClickAndChangesTheComponent()
        {
            yield return LoadApp();
            var block = ComponentFactory.Create(ComponentKind.NeuralBlock, 0, 0);
            SystemCore.CircuitService.Editor.Apply(new AddElementsEdit(Circuit, new[] { block }));
            AppCommands.FrameAll();
            yield return null;
            yield return Click(BodyOnScreen(block.Id));
            yield return null;
            var inspector = Object.FindAnyObjectByType<InspectorPanel>();
            var label = inspector.Panel.GetComponentsInChildren<TMPro.TMP_InputField>()[0];
            var rect = (RectTransform)label.transform;

            yield return Click(RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)));
            yield return null;
            Assert.That(label.isFocused, Is.True, "a click focuses the field");
            Assert.That(label.textViewport.GetComponentInChildren<TMPro.TMP_SelectionCaret>(true), Is.Not.Null, "the field shows a caret");

            // Typed text arrives as IMGUI events, which the virtual keyboard does not produce: enter it directly.
            label.text = "adder";
            label.onEndEdit.Invoke(label.text);
            yield return null;
            Assert.That(Circuit.GetComponent(block.Id).Label, Is.EqualTo("adder"));
        }

        #region Helpers

        private IEnumerator Drag(Vector2 from, Vector2 to, int steps = 6)
        {
            Move(_mouse.position, from);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            for (int i = 1; i <= steps; i++)
            {
                Move(_mouse.position, Vector2.Lerp(from, to, i / (float)steps));
                yield return null;
            }
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        private IEnumerator Click(Vector2 position)
        {
            Move(_mouse.position, position);
            yield return null;
            Press(_mouse.leftButton);
            Release(_mouse.leftButton);
            yield return null;
        }

        private static (CircuitComponent input, CircuitComponent output) AddSwitchAndBulb()
        {
            var input = ComponentFactory.Create(ComponentKind.Switch, 0, 0);
            var output = ComponentFactory.Create(ComponentKind.Bulb, 14, 0);
            SystemCore.CircuitService.Editor.Apply(new AddElementsEdit(Circuit, new[] { input, output }));
            AppCommands.FrameAll();
            return (input, output);
        }

        private static ComponentView ViewOf(int id)
        {
            Assert.That(CircuitView.Instance.TryGetComponentView(id, out var view), Is.True, $"view of component {id}");
            return view;
        }

        private static Vector2 BodyOnScreen(int id)
        {
            return Camera.main.WorldToScreenPoint(ViewOf(id).GetBounds().center);
        }

        private static Vector2 PinOnScreen(int id, int pin, bool output)
        {
            var view = ViewOf(id);
            var pinView = output ? view.Outputs[pin] : view.Inputs[pin];
            return Camera.main.WorldToScreenPoint(pinView.transform.position);
        }

        /// <summary>Scrolls the tool panel so the palette entry is visible and returns its screen position.</summary>
        private static Vector2 ScrollIntoView(RectTransform entry)
        {
            var refs = AppUi.Instance.Canvas.GetComponent<AppUiReferences>();
            var viewport = refs.ScrollViewport;
            var corners = new Vector3[4];
            viewport.GetWorldCorners(corners);
            float centerY = (corners[0].y + corners[1].y) / 2;
            Vector2 position = RectTransformUtility.WorldToScreenPoint(null, entry.position);
            var content = refs.ScrollContent;
            content.anchoredPosition += new Vector2(0, (centerY - position.y) / AppUi.Instance.Canvas.scaleFactor);
            Canvas.ForceUpdateCanvases();
            return RectTransformUtility.WorldToScreenPoint(null, entry.position);
        }

        #endregion
    }
}
