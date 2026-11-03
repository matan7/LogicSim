using System;

namespace Core.Settings
{
    public class SettingsService
    {
        public static SettingsData Settings { get; private set; }

        private readonly SettingsIO _settingsIO;

        public static event Action SettingsChanged;

        public SettingsService()
        {
            _settingsIO = new SettingsIO();
            LoadSettings();
        }

        internal void LoadSettings()
        {
            Settings = _settingsIO.LoadSettingsFile();
            SettingsChanged?.Invoke();
        }

        public void SaveSettings()
        {
            _ = _settingsIO.SaveSettingsFile(Settings);
            SettingsChanged?.Invoke();
        }

        /// <summary>JSON produced by InputActionAsset.SaveBindingOverridesAsJson, or null.</summary>
        public string LoadInputOverrides() => _settingsIO.LoadInputOverrides();

        public void SaveInputOverrides(string json)
        {
            _ = _settingsIO.SaveInputOverrides(json);
        }
    }
}
