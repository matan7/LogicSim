using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Core.Settings;

namespace Core.Input
{
    /// <summary>
    /// Owns the input actions. <see cref="Actions"/> is generated from Assets/Data/GeneralInputActions.inputactions, so
    /// actions are typed members instead of names. User rebinds are stored as binding overrides, so actions added in
    /// later versions of the app keep working with old settings files.
    /// </summary>
    public class InputService
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

            Actions.Enable();
        }
    }
}
