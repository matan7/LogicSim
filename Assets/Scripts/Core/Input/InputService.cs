using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Core.Settings;

namespace Core.Input
{
    /// <summary>
    /// Owns the input actions. <see cref="Actions"/> is generated from Assets/Data/GeneralInputActions.inputactions, so
    /// actions are typed members instead of names. User rebinds are stored as binding overrides, so actions added in
    /// later versions of the app keep working with old settings files. Letter shortcuts follow the keyboard layout (see
    /// <see cref="KeyboardLayoutFallback"/>).
    /// </summary>
    public class InputService : IDisposable
    {
        public GeneralInputActions Actions { get; }

        /// <summary>The asset behind <see cref="Actions"/> (for binding overrides).</summary>
        public InputActionAsset CurrentInputActions => Actions.asset;

        public InputService(SettingsService settingsService)
        {
            Actions = new GeneralInputActions();

            var overrides = settingsService.LoadInputOverrides();
            if (!string.IsNullOrEmpty(overrides))
            {
                try
                {
                    CurrentInputActions.LoadBindingOverridesFromJson(overrides);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"{nameof(InputService)}: ignoring invalid binding overrides. {exception.Message}");
                }
            }

            KeyboardLayoutFallback.Apply(CurrentInputActions, Keyboard.current);
            InputSystem.onDeviceChange += OnDeviceChange;
            Actions.Enable();
        }

        // A keyboard layout switch is a configuration change of the keyboard.
        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is Keyboard keyboard && (change == InputDeviceChange.ConfigurationChanged || change == InputDeviceChange.Added))
                KeyboardLayoutFallback.Apply(CurrentInputActions, keyboard);
        }

        public void Dispose()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            Actions.Dispose();
        }
    }
}
