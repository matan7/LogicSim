using UnityEngine;
using UnityEngine.InputSystem;
using Core.Command;
using Core.Settings;
using Core.Input;
using Core.Simulator;
using Core.Selecting;
using Core.Clipboard;

namespace Core
{
    public class SystemCore : MonoBehaviour
    {
        // Singleton
        public static SystemCore Instance { get; private set; }

        // Services
        public static CommandService CommandService { get; private set; }
        public static InputService InputService { get; private set; }
        public static SimulatorService SimulatorService { get; private set; }
        public static SettingsService SettingsService { get; private set; }
        public static SelectingService SelectingService { get; private set; }
        public static ClipboardService ClipboardService { get; private set; }

        [Header("Dependencies")]
        [SerializeField] private InputActionAsset _defaultInputActions;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            else
            {
                Instance = this;
            }

            // Initialization of services
            SettingsService = new SettingsService();
            InputService = new InputService(_defaultInputActions, SettingsService);
            CommandService = new CommandService();
            SimulatorService = new SimulatorService();
            SelectingService = new SelectingService();
            ClipboardService = new ClipboardService();

        }
    }
}

