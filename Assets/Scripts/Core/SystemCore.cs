using UnityEngine;
using UnityEngine.InputSystem;
using Core.AppState;
using Core.Clipboard;
using Core.Command;
using Core.Documents;
using Core.Input;
using Core.Selecting;
using Core.Settings;
using Core.Simulation;

namespace Core
{
    /// <summary>Creates the application services before anything else runs.</summary>
    [DefaultExecutionOrder(-1000)]
    public class SystemCore : MonoBehaviour
    {
        public static SystemCore Instance { get; private set; }

        public static SettingsService SettingsService { get; private set; }
        public static InputService InputService { get; private set; }
        public static CommandService CommandService { get; private set; }
        public static SelectingService SelectingService { get; private set; }
        public static ClipboardService ClipboardService { get; private set; }
        public static AppStateService AppStateService { get; private set; }
        public static CircuitService CircuitService { get; private set; }
        public static SimulationService SimulationService { get; private set; }

        [Header("Dependencies")]
        [SerializeField] private InputActionAsset _defaultInputActions;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            SettingsService = new SettingsService();
            InputService = new InputService(_defaultInputActions, SettingsService);
            CommandService = new CommandService();
            SelectingService = new SelectingService();
            ClipboardService = new ClipboardService();
            AppStateService = new AppStateService();
            CircuitService = new CircuitService(CommandService, AppStateService);
            SimulationService = new SimulationService(CircuitService.Root, AppStateService, SettingsService.Settings.TicksPerSecond);
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;
            SimulationService?.Dispose();
            Instance = null;
        }
    }
}
