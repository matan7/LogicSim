using System;
using UnityEngine.InputSystem;
using Core.Settings;

namespace Core.Input
{
    public class InputService
    {
        public InputActionAsset CurrentInputActions { get; private set; }

        public event Action<InputActionAsset> InputActionChange;

        private InputActionAsset _defaultInputActions;

        private InputActionAsset _customInputActions;

        private SettingsService _settingsService;   

        public InputService(InputActionAsset defaultInputActions, SettingsService settingsService)
        {
            _defaultInputActions = defaultInputActions;
            _settingsService = settingsService;
#if UNITY_EDITOR
            InputActionAsset inputActions = defaultInputActions;
#else
            InputActionAsset inputActions = _settingsService.LoadInputActions();
#endif
            if (inputActions != null) 
            {                 
                _customInputActions = inputActions;
                CurrentInputActions = _customInputActions;
            }
            else
            {
                _settingsService.SaveInputActions(_defaultInputActions);
                CurrentInputActions = _defaultInputActions;
                _customInputActions = _defaultInputActions;
            }

            CurrentInputActions.Enable();
        }

        public void ChangeInputAction(InputActionAsset inputActions)
        {
            CurrentInputActions = inputActions;
            InputActionChange?.Invoke(inputActions);
        }

        public void SaveInputActions()
        {
            _settingsService.SaveInputActions(CurrentInputActions);
        }
    }
}
