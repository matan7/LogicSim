using System;
using System.Collections;
using System.Linq;
using Core;
using LogicSim.Simulation;
using LogicSim.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LogicSim.Tests.PlayMode
{
    /// <summary>The tool panel, IC bar and inspector are in the scene; the runtime only binds them.</summary>
    public class UiSceneTests
    {
        [UnityTest]
        public IEnumerator EveryCommandHasAButtonAndUndoWorks()
        {
            SceneManager.LoadScene("SampleScene");
            for (int i = 0; i < 5; i++)
                yield return null;

            var buttons = Object.FindObjectsByType<CommandButton>(FindObjectsInactive.Include);
            var commands = buttons.Select(b => b.Command).ToList();
            foreach (UiCommand command in Enum.GetValues(typeof(UiCommand)))
                if (command != UiCommand.None)
                    Assert.That(commands, Contains.Item(command), "a button for " + command);
            // Tool panel buttons show an icon; the IC bar's Done and Discard are text buttons.
            foreach (var button in buttons.Where(b => b.Command != UiCommand.CloseIcApply && b.Command != UiCommand.CloseIcDiscard))
            {
                var image = button.GetComponentInChildren<Button>(true).targetGraphic as Image;
                Assert.That(image != null && image.sprite != null, Is.True, button.name + " shows an icon");
            }

            // Undo is enabled once there is an edit, and undoes it.
            var undo = buttons.Single(b => b.Command == UiCommand.Undo).GetComponentInChildren<Button>();
            Assert.That(undo.interactable, Is.False);
            SystemCore.CircuitService.Editor.Apply(new AddElementsEdit(SystemCore.CircuitService.Circuit, new[] { ComponentFactory.Create(ComponentKind.And, 0, 0) }));
            yield return null;
            Assert.That(undo.interactable, Is.True);
            undo.onClick.Invoke();
            Assert.That(SystemCore.CircuitService.Circuit.ComponentCount, Is.Zero);
        }
    }
}
