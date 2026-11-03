using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Core.Settings
{
    internal class SettingsIO
    {
        private const string SettingsFileName = "Settings.json";
        private const string InputOverridesFileName = "InputBindingOverrides.json";

        private readonly string _settingsFullPath;
        private readonly string _inputOverridesFullPath;

        // Unity vectors and colors expose computed properties (normalized, linear...) that Json.NET would follow forever.
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Converters = { new Vector2Converter(), new ColorConverter() },
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public SettingsIO()
        {
            _settingsFullPath = Path.Combine(Application.persistentDataPath, SettingsFileName);
            _inputOverridesFullPath = Path.Combine(Application.persistentDataPath, InputOverridesFileName);
        }

        public SettingsData LoadSettingsFile()
        {
            if (File.Exists(_settingsFullPath))
            {
                try
                {
                    var data = JsonConvert.DeserializeObject<SettingsData>(File.ReadAllText(_settingsFullPath), JsonSettings);
                    if (data != null)
                        return data;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"{nameof(SettingsIO)}: {_settingsFullPath} is invalid, using defaults. {exception.Message}");
                }
            }

            var defaults = new SettingsData();
            _ = SaveSettingsFile(defaults);
            return defaults;
        }

        public async Awaitable<bool> SaveSettingsFile(SettingsData data)
        {
            try
            {
                string json = JsonConvert.SerializeObject(data, JsonSettings);
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

        internal string LoadInputOverrides()
        {
            return File.Exists(_inputOverridesFullPath) ? File.ReadAllText(_inputOverridesFullPath) : null;
        }

        internal async Awaitable<bool> SaveInputOverrides(string json)
        {
            try
            {
                await File.WriteAllTextAsync(_inputOverridesFullPath, json ?? string.Empty);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError($"{nameof(SettingsIO)}: Input binding overrides failed to save to : {_inputOverridesFullPath}");
                return false;
            }
        }

        private sealed class Vector2Converter : JsonConverter<Vector2>
        {
            public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
            {
                writer.WriteStartArray();
                writer.WriteValue(value.x);
                writer.WriteValue(value.y);
                writer.WriteEndArray();
            }

            public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                var values = serializer.Deserialize<float[]>(reader);
                return values != null && values.Length >= 2 ? new Vector2(values[0], values[1]) : existingValue;
            }
        }

        private sealed class ColorConverter : JsonConverter<Color>
        {
            public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
            {
                writer.WriteValue("#" + ColorUtility.ToHtmlStringRGBA(value));
            }

            public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                var text = reader.Value as string;
                return text != null && ColorUtility.TryParseHtmlString(text, out var color) ? color : existingValue;
            }
        }
    }
}
