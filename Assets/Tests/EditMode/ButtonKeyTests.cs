using System.Linq;
using LogicSim.Simulation;
using NUnit.Framework;

namespace LogicSim.Tests
{
    /// <summary>The keyboard key of a push button: a choice setting, written by name in files.</summary>
    public class ButtonKeyTests
    {
        private static ComponentSetting Hotkey => ComponentSpec.Get(ComponentKind.Button).FindSetting("hotkey");

        private static void Bind(Circuit circuit, CircuitComponent button, string key)
        {
            var p = button.GetProperties();
            Assert.That(Hotkey.TryParseChoice(key, out int value), Is.True, key);
            Hotkey.Set(ref p, value);
            circuit.SetProperties(button.Id, p);
        }

        private static Circuit Reload(Circuit circuit, out string json, out CircuitDocument document)
        {
            json = CircuitSerializer.Serialize(circuit, "keys");
            document = CircuitSerializer.Deserialize(json, new SubcircuitLibrary());
            var loaded = new Circuit();
            document.Snapshot.Restore(loaded);
            return loaded;
        }

        [Test]
        public void TheKeyIsAChoiceOfNamedKeys_NoneByDefault()
        {
            var setting = Hotkey;
            Assert.That(setting, Is.Not.Null);
            Assert.That(setting.IsKey && setting.IsChoice, Is.True);
            Assert.That(setting.Label, Is.EqualTo("Key"));
            Assert.That(setting.Choices[setting.Default], Is.EqualTo(KeyNames.None));
            Assert.That(setting.Choices, Does.Contain("A").And.Contain("Z").And.Contain("0").And.Contain("9")
                .And.Contain("Space").And.Contain("Left").And.Contain("Down"));
            Assert.That(setting.TryParseChoice("space", out int space), Is.True, "names in any case");
            Assert.That(setting.ChoiceName(space), Is.EqualTo("Space"));
            Assert.That(setting.TryParseChoice("F13", out _), Is.False);

            var t = new TestCircuit();
            var button = t.Add(ComponentKind.Button);
            Assert.That(setting.ChoiceName(setting.Get(button)), Is.EqualTo(KeyNames.None));
            var p = button.GetProperties();
            var error = Assert.Throws<CircuitException>(() => setting.Set(ref p, setting.Choices.Count));
            Assert.That(error.Message, Does.Contain(KeyNames.Help));
        }

        [Test]
        public void FilesWriteTheKeyByName_AndLeaveItOutWhenThereIsNone()
        {
            var t = new TestCircuit();
            var fire = t.Add(ComponentKind.Button, "Fire");
            var plain = t.Add(ComponentKind.Button, "Plain");
            Bind(t.Circuit, fire, "Space");

            var loaded = Reload(t.Circuit, out string json, out var document);
            Assert.That(document.Warnings, Is.Empty);
            Assert.That(json, Does.Contain("\"hotkey\": \"Space\""));
            Assert.That(json.Split(new[] { "\"hotkey\"" }, System.StringSplitOptions.None).Length - 1, Is.EqualTo(1), "only the bound button has one");
            Assert.That(Hotkey.ChoiceName(Hotkey.Get(loaded.GetComponent(fire.Id))), Is.EqualTo("Space"));
            Assert.That(Hotkey.ChoiceName(Hotkey.Get(loaded.GetComponent(plain.Id))), Is.EqualTo(KeyNames.None));
            Assert.That(loaded.GetComponent(fire.Id).GetProperties(), Is.EqualTo(fire.GetProperties()));
        }

        [Test]
        public void AnUnknownKeyInAFile_IsIgnoredWithAWarning()
        {
            var t = new TestCircuit();
            var button = t.Add(ComponentKind.Button, "B");
            Bind(t.Circuit, button, "Q");
            string json = CircuitSerializer.Serialize(t.Circuit, "keys").Replace("\"hotkey\": \"Q\"", "\"hotkey\": \"Hyper\"");

            var document = CircuitSerializer.Deserialize(json, new SubcircuitLibrary());
            var loaded = new Circuit();
            document.Snapshot.Restore(loaded);
            Assert.That(document.Warnings.Single(), Does.Contain("Hyper"));
            Assert.That(Hotkey.ChoiceName(Hotkey.Get(loaded.Components.Single())), Is.EqualTo(KeyNames.None));
        }

        [Test]
        public void ChangingTheKey_IsAStructuralUndoableEdit()
        {
            var t = new TestCircuit();
            var button = t.Add(ComponentKind.Button, "B");
            int version = t.Circuit.StructureVersion;
            var before = button.GetProperties();
            Bind(t.Circuit, button, "Left");
            Assert.That(t.Circuit.StructureVersion, Is.GreaterThan(version), "the app refreshes its bound keys on structure changes");

            t.Circuit.SetProperties(button.Id, before);
            Assert.That(Hotkey.ChoiceName(Hotkey.Get(button)), Is.EqualTo(KeyNames.None));
        }
    }
}
