using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.Settings
{
    internal class SettingsIO
    {
        private string _settingsFileName = "Settings.json";
        private string _inputSettingsFileName = "InputSettings.json";
        private string _settingsFullPath = string.Empty;
        private string _inputSettingsFullPath = string.Empty;

        public SettingsIO() 
        {
            _settingsFullPath = Path.Combine(Application.persistentDataPath, _settingsFileName);
            _inputSettingsFullPath = Path.Combine(Application.persistentDataPath, _inputSettingsFileName);
        }

        public SettingsData LoadSettingsFile()
        {            
            if (File.Exists(_settingsFullPath)) 
            { 
                string json = File.ReadAllText(_settingsFullPath);
                SettingsData data = JsonConvert.DeserializeObject<SettingsData>(json);
                return data;
            }
            else
            {
                SettingsData data = new SettingsData();
                _ = SaveSettingsFile(data);
                return data;
            }
        }

        public async Awaitable<bool> SaveSettingsFile(SettingsData data)
        {
            string json = JsonConvert.SerializeObject(data);
            try
            {
                await File.WriteAllTextAsync(_settingsFullPath, json);
                return true;
            }
            catch (Exception exception) 
            { 
                Debug.LogException(exception);
                Debug.LogError($"{nameof(SettingsIO)}: Settings data failed to save to : {_settingsFullPath}");
                return false;
            }
        }

        internal InputActionAsset LoadInputActionsFile()
        {
            if (!File.Exists(_inputSettingsFullPath))
            {
                return null;
            }
            string json = File.ReadAllText(_inputSettingsFullPath);
            InputActionAsset data = InputActionAsset.FromJson(json);
            return data;
        }

        internal async Awaitable<bool> SaveInputActionsFile(InputActionAsset inputActions)
        {
            string json = inputActions.ToJson();
            try
            {
                await File.WriteAllTextAsync(_inputSettingsFullPath, json);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError($"{nameof(SettingsIO)}: Input settings data failed to save to : {_inputSettingsFullPath}");
                return false;
            }
        }
    }
}
