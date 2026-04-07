using UnityEngine.InputSystem;
using System;

namespace Core.Settings
{
    public class SettingsService
    {
        public static SettingsData Settings { get; private set; }

        private SettingsIO _settingsIO;

        public static event Action SettingsChanged;

        public SettingsService() 
        { 
            _settingsIO = new SettingsIO();
            LoadSettings();
        }

        internal void LoadSettings()
        {
            Settings = _settingsIO.LoadSettingsFile();
        }
        internal void SaveSettings()
        {
            _ = _settingsIO.SaveSettingsFile(Settings);
        }

        internal InputActionAsset LoadInputActions()
        {
            return _settingsIO.LoadInputActionsFile();
        }

        public void SaveInputActions(InputActionAsset inputActions)
        {
            _ = _settingsIO.SaveInputActionsFile(inputActions);
        }

    }
}