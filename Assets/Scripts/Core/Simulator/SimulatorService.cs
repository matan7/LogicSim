using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.EventSystems;

namespace Core.Simulator
{
    public class SimulatorService
    {
        // false = editor mode (paused), true = simulation mode
        public static bool IsSimMode { get; set; }


        public SimulatorService()
        {
            IsSimMode = false;
        }
    }
}

