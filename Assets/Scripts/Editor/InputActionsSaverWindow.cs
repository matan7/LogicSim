using Core.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.Editor
{
    public class InputActionsSaverWindow : EditorWindow
    {
        private InputActionAsset _inputActions;

        [MenuItem("Tools/Save Input Actions")]
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
            if (GUILayout.Button("Save Input Actions"))
            {
                var settingsService = new Core.Settings.SettingsService();
                settingsService.SaveInputActions(_inputActions);
                Debug.Log($"[InputActionsSaver] Saved '{_inputActions.name}' to persistent data path.");
            }
            GUI.enabled = true;
        }
    }
}
