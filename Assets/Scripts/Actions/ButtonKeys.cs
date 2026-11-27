using System;
using System.Collections.Generic;
using Core;
using LogicSim.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

namespace Actions
{
    /// <summary>
    /// Keyboard keys bound to push buttons (their "hotkey" setting): during the simulation a bound key holds its buttons
    /// down while it is pressed, and app shortcuts on that key are left out. Also captures the next key pressed for the
    /// inspector's Key field.
    /// </summary>
    public sealed class ButtonKeys
    {
        public const string Setting = "hotkey";

        public static ButtonKeys Instance { get; private set; }

        private readonly List<(int id, string key)> _bound = new List<(int, string)>();
        private readonly HashSet<int> _held = new HashSet<int>();
        private Circuit _boundFor;
        private int _boundVersion = -1;

        private Action<string> _onCaptured;
        private Action _onCaptureEnded;
        private IDisposable _captureListener;
        private int _captureEndFrame = -1;

        public ButtonKeys(ActionsManager actionsManager)
        {
            Instance = this;
            actionsManager.UpdateEvent += Update;
        }

        #region Capture

        /// <summary>The inspector waits for a key; keyboard shortcuts stay quiet until the frame after it came.</summary>
        public static bool IsCapturing => Instance != null && (Instance._onCaptured != null || Instance._captureEndFrame == Time.frameCount);

        /// <summary>
        /// Calls <paramref name="onCaptured"/> with the name of the next key pressed (<see cref="KeyNames"/>; "None" for
        /// Escape or Delete). A mouse click ends the capture without a call; modifiers and keys without a name are
        /// skipped. Presses are taken from the input events, so a key pressed and released within one frame counts.
        /// </summary>
        public void BeginCapture(Action<string> onCaptured, Action onEnded)
        {
            EndCapture();
            _onCaptured = onCaptured;
            _onCaptureEnded = onEnded;
            _captureListener = InputSystem.onAnyButtonPress.Call(OnAnyButtonPress);
        }

        public void EndCapture()
        {
            _captureListener?.Dispose();
            _captureListener = null;
            if (_onCaptured == null)
                return;
            _onCaptured = null;
            // Shortcuts on the same key run in this frame's input update too: they stay quiet until the next frame.
            _captureEndFrame = Time.frameCount;
            var ended = _onCaptureEnded;
            _onCaptureEnded = null;
            ended?.Invoke();
        }

        private void OnAnyButtonPress(InputControl control)
        {
            if (_onCaptured == null)
                return;
            if (!(control is KeyControl key))
            {
                if (control.device is Mouse)
                    EndCapture();
                return;
            }
            string name = key.keyCode == Key.Escape || key.keyCode == Key.Delete ? KeyNames.None : NameOf(key);
            if (name == null)
                return;
            var captured = _onCaptured;
            EndCapture();
            captured(name);
        }

        #endregion

        #region Key names

        /// <summary>
        /// The <see cref="KeyNames"/> name of a key, or null. Letters are what the keyboard layout prints on the key, so
        /// that a binding to Z means the key labelled Z on any layout.
        /// </summary>
        public static string NameOf(KeyControl key)
        {
            switch (key.keyCode)
            {
                case Key.Space: return "Space";
                case Key.Enter:
                case Key.NumpadEnter: return "Enter";
                case Key.Tab: return "Tab";
                case Key.Backspace: return "Backspace";
                case Key.LeftArrow: return "Left";
                case Key.RightArrow: return "Right";
                case Key.UpArrow: return "Up";
                case Key.DownArrow: return "Down";
                case Key.Digit0: return "0";
            }
            if (key.keyCode >= Key.Digit1 && key.keyCode <= Key.Digit9)
                return ((char)('1' + (key.keyCode - Key.Digit1))).ToString();
            if (key.keyCode < Key.A || key.keyCode > Key.Z)
                return null;
            string shown = key.displayName;
            if (!string.IsNullOrEmpty(shown) && shown.Length == 1)
            {
                char letter = char.ToUpperInvariant(shown[0]);
                if (letter >= 'A' && letter <= 'Z')
                    return letter.ToString();
            }
            return ((char)('A' + (key.keyCode - Key.A))).ToString();
        }

        /// <summary>The key with this <see cref="KeyNames"/> name on the current layout, or null.</summary>
        public static KeyControl Find(Keyboard keyboard, string name)
        {
            if (keyboard == null || string.IsNullOrEmpty(name) || name == KeyNames.None)
                return null;
            switch (name)
            {
                case "Space": return keyboard.spaceKey;
                case "Enter": return keyboard.enterKey;
                case "Tab": return keyboard.tabKey;
                case "Backspace": return keyboard.backspaceKey;
                case "Left": return keyboard.leftArrowKey;
                case "Right": return keyboard.rightArrowKey;
                case "Up": return keyboard.upArrowKey;
                case "Down": return keyboard.downArrowKey;
            }
            if (name.Length != 1)
                return null;
            char c = name[0];
            if (c == '0')
                return keyboard.digit0Key;
            if (c >= '1' && c <= '9')
                return keyboard[Key.Digit1 + (c - '1')];
            if (c < 'A' || c > 'Z')
                return null;
            return keyboard.FindKeyOnCurrentKeyboardLayout(char.ToLowerInvariant(c).ToString()) ?? keyboard[Key.A + (c - 'A')];
        }

        #endregion

        #region Buttons

        /// <summary>True when, during the simulation, <paramref name="key"/> holds a button and its shortcut should not run.</summary>
        public static bool HoldsButton(KeyControl key)
        {
            var instance = Instance;
            if (instance == null || key == null || !SystemCore.AppStateService.IsSimulating)
                return false;
            instance.RefreshBound();
            string name = NameOf(key);
            if (name == null)
                return false;
            foreach (var (_, bound) in instance._bound)
                if (bound == name)
                    return true;
            return false;
        }

        // Buttons with a key in the document: inputs belong to it, also while an IC is open.
        private void RefreshBound()
        {
            var root = SystemCore.CircuitService.Root;
            if (ReferenceEquals(root, _boundFor) && root.StructureVersion == _boundVersion)
                return;
            _boundFor = root;
            _boundVersion = root.StructureVersion;
            _bound.Clear();
            foreach (var component in root.Components)
            {
                if (component.Kind != ComponentKind.Button)
                    continue;
                var setting = component.Spec.FindSetting(Setting);
                string key = setting.ChoiceName(setting.Get(component));
                if (key != KeyNames.None)
                    _bound.Add((component.Id, key));
            }
        }

        private void Update()
        {
            var root = SystemCore.CircuitService.Root;
            var keyboard = Keyboard.current;
            bool active = SystemCore.AppStateService.IsSimulating && keyboard != null && !EditCommands.IsTextInputFocused() && !IsCapturing;
            RefreshBound();
            foreach (var (id, name) in _bound)
            {
                var key = active ? Find(keyboard, name) : null;
                bool down = key != null && key.isPressed;
                bool held = _held.Contains(id);
                if (down == held || !root.TryGetComponent(id, out _))
                    continue;
                // Only changes of the key are applied, so a click with the mouse still works on a bound button.
                if (down)
                    _held.Add(id);
                else
                    _held.Remove(id);
                root.SetInputValue(id, down);
            }
            if (_held.Count > 0)
                _held.RemoveWhere(id => !root.TryGetComponent(id, out _));
        }

        #endregion
    }
}
