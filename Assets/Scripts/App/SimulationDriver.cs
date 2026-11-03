using Core;
using UnityEngine;

namespace LogicSim.App
{
    /// <summary>
    /// Frame loop of the simulation. The job is scheduled in LateUpdate so it runs on a worker thread while
    /// the frame renders, and is collected at the very start of the next frame. Everything that runs during
    /// Update (input, UI, MCP requests) therefore sees a completed simulation.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class SimulationDriver : MonoBehaviour
    {
        private void Update()
        {
            SystemCore.SimulationService.EndFrame();
        }

        private void LateUpdate()
        {
            SystemCore.SimulationService.BeginFrame(Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            SystemCore.SimulationService?.Session.Complete();
        }
    }
}
