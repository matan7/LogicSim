using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Core.Settings;

namespace Core.Input
{
    /// <summary>
    /// Owns the input action asset. User rebinds are stored as binding overrides, so actions added in
    /// later versions of the app keep working with old settings files.
    /// </summary>
    public class InputService
    {
        public InputActionAsset CurrentInputActions { get; }

        private readonly SettingsService _settingsService;

        public InputService(InputActionAsset defaultInputActions, SettingsService settingsService)
        {
            if (defaultInputActions == null)
                throw new ArgumentNullException(nameof(defaultInputActions));
            _settingsService = settingsService;
            CurrentInputActions = defaultInputActions;

            var overrides = _settingsService.LoadInputOverrides();
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

            CurrentInputActions.Enable();
        }

        /// <summary>Finds an action, logging a warning instead of throwing when it does not exist.</summary>
        public InputAction Find(string nameOrPath)
        {
            var action = CurrentInputActions.FindAction(nameOrPath);
            if (action == null)
                Debug.LogWarning($"{nameof(InputService)}: input action '{nameOrPath}' not found");
            return action;
        }

        public void SaveInputActions()
        {
            _settingsService.SaveInputOverrides(CurrentInputActions.SaveBindingOverridesAsJson());
        }
    }
}
