using System.Linq;
using Core.Input;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace LogicSim.Tests.PlayMode
{
    /// <summary>Letter shortcuts follow the characters of the keyboard layout and fall back to US keys without Latin letters.</summary>
    public class KeyboardLayoutTests : InputTestFixture
    {
        private static readonly Key[] Letters = Enumerable.Range((int)Key.A, 26).Select(k => (Key)k).ToArray();

        private Keyboard _keyboard;
        private GeneralInputActions _actions;
        private int _selectAll;
        private int _undo;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _actions = new GeneralInputActions();
            _actions.Enable();
            // NUnit runs every test of a fixture on the same instance.
            _selectAll = 0;
            _undo = 0;
            _actions.Commands.SelectAllCommand.performed += _ => _selectAll++;
            _actions.Commands.UndoCommand.performed += _ => _undo++;
        }

        public override void TearDown()
        {
            _actions.Dispose();
            base.TearDown();
        }

        private void Ctrl(KeyControl key)
        {
            Press(_keyboard.leftCtrlKey);
            // Undo and Redo need the modifier pressed first: give the key a later time stamp.
            currentTime += 0.05;
            PressAndRelease(key);
            Release(_keyboard.leftCtrlKey);
        }

        private void UsLayout()
        {
            foreach (var key in Letters)
                SetKeyInfo(key, key.ToString());
        }

        [Test]
        public void OnAzerty_CtrlA_IsWhereTheAIs()
        {
            UsLayout();
            SetKeyInfo(Key.A, "Q");
            SetKeyInfo(Key.Q, "A");
            SetKeyInfo(Key.Z, "W");
            SetKeyInfo(Key.W, "Z");
            Assert.That(KeyboardLayoutFallback.Apply(_actions.asset, _keyboard), Is.Zero, "every letter is on the layout");

            Ctrl(_keyboard.qKey);
            Assert.That(_selectAll, Is.EqualTo(1), "Ctrl + the key that types A");
            Ctrl(_keyboard.aKey);
            Assert.That(_selectAll, Is.EqualTo(1), "Ctrl + the key that types Q does nothing");
            Ctrl(_keyboard.wKey);
            Assert.That(_undo, Is.EqualTo(1), "Ctrl + the key that types Z undoes");
        }

        [Test]
        public void OnACyrillicLayout_ShortcutsUseTheUsKeys_AndBackOnUs()
        {
            const string cyrillic = "фисвуапршолдьтщзйкыегмцчня";
            for (int i = 0; i < Letters.Length; i++)
                SetKeyInfo(Letters[i], cyrillic[i].ToString());
            Assert.That(KeyboardLayoutFallback.Apply(_actions.asset, _keyboard), Is.GreaterThan(0));

            Ctrl(_keyboard.aKey);
            Assert.That(_selectAll, Is.EqualTo(1), "Ctrl + the key in the A position");
            Ctrl(_keyboard.zKey);
            Assert.That(_undo, Is.EqualTo(1));

            UsLayout();
            Assert.That(KeyboardLayoutFallback.Apply(_actions.asset, _keyboard), Is.Zero);
            Assert.That(_actions.asset.SelectMany(a => a.bindings).All(b => string.IsNullOrEmpty(b.overridePath)), Is.True, "the fallbacks are removed");
            Ctrl(_keyboard.aKey);
            Assert.That(_selectAll, Is.EqualTo(2));
        }
    }
}
