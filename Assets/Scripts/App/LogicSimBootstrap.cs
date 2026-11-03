using Core;
using LogicSim.UI;
using LogicSim.Views;
using UnityEngine;

namespace LogicSim.App
{
    /// <summary>
    /// Adds the runtime parts of the app (views, simulation loop, MCP server, generated UI) after the scene loads,
    /// so the scene only needs the existing SystemCore, ActionsManager and UI objects.
    /// </summary>
    public static class LogicSimBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (SystemCore.Instance == null)
            {
                Debug.LogWarning("[LogicSim] No SystemCore in the scene; the simulator is not started.");
                return;
            }
            if (Object.FindFirstObjectByType<CircuitView>() != null)
                return;

            var root = new GameObject("LogicSim");
            root.AddComponent<SimulationDriver>();
            root.AddComponent<CircuitView>();
            root.AddComponent<McpServerHost>();
            root.AddComponent<AppUi>();
        }
    }
}
