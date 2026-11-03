using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.Editor
{
    /// <summary>Stores the binding overrides of an input action asset in the player's settings folder.</summary>
    public class InputActionsSaverWindow : EditorWindow
    {
        private InputActionAsset _inputActions;

        [MenuItem("Tools/Save Input Binding Overrides")]
        public static void Open()
        {
            GetWindow<InputActionsSaverWindow>("Input Actions Saver");
        }

        private void OnGUI()
        {
            GUILayout.Label("Input Actions Saver", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _inputActions = (InputActionAsset)EditorGUILayout.ObjectField(
                "Input Action Asset", _inputActions, typeof(InputActionAsset), false);

            EditorGUILayout.Space();

            GUI.enabled = _inputActions != null;
            if (GUILayout.Button("Save Binding Overrides"))
            {
                var settingsService = new Core.Settings.SettingsService();
                settingsService.SaveInputOverrides(_inputActions.SaveBindingOverridesAsJson());
                Debug.Log($"[InputActionsSaver] Saved binding overrides of '{_inputActions.name}' to the persistent data path.");
            }
            GUI.enabled = true;
        }
    }
}
