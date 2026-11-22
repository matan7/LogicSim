using Core;
using LogicSim.UI;
using LogicSim.Views;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LogicSim.App
{
    /// <summary>
    /// Adds the runtime parts of the app (views, simulation loop, MCP server, generated UI) after a scene with a
    /// SystemCore loads, so the scene only needs the existing SystemCore, ActionsManager and UI objects.
    /// </summary>
    public static class LogicSimBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            // Scenes loaded later (PlayMode tests load the app scene after an empty one) start the app too.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (SystemCore.Instance == null)
            {
                Debug.LogWarning("[LogicSim] No SystemCore in the scene; the simulator is not started.");
                return;
            }
            StartApp();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (SystemCore.Instance != null)
                StartApp();
        }

        private static void StartApp()
        {
            if (Object.FindAnyObjectByType<CircuitView>() != null)
                return;

            ComputeLutBuilder.Register();

            var root = new GameObject("LogicSim");
            root.AddComponent<SimulationDriver>();
            root.AddComponent<CircuitView>();
            root.AddComponent<McpServerHost>();
            root.AddComponent<AppUi>();
        }
    }
}
