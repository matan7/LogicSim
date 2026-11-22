using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogicSim.UI
{
    /// <summary>
    /// Scene objects the runtime UI works with, assigned in the scene (on the root canvas) instead of being looked up by
    /// name. <see cref="AppUi"/> reads them at start-up. The buttons and palette entries themselves live in the scene.
    /// </summary>
    public class AppUiReferences : MonoBehaviour
    {
        [Header("Tool panel")]
        [Tooltip("The panel docked on the left; framing keeps circuits clear of it.")]
        public RectTransform ToolsPanel;
        [Tooltip("Visible area of the tool panel (scrolled with the mouse wheel).")]
        public RectTransform ScrollViewport;
        [Tooltip("Content of the tool panel that scrolls inside the viewport.")]
        public RectTransform ScrollContent;

        [Header("Icon containers of the tool panel sections")]
        public RectTransform MenuIcons;
        public RectTransform EditIcons;
        public RectTransform LogicGatesIcons;
        public RectTransform InputOutputIcons;
        [Tooltip("IC tools and pins; the ICs of the library are added after them at run time.")]
        public RectTransform CustomCircuitIcons;

        [Header("Run-time content")]
        public Toggle SimulationToggle;
        [Tooltip("Status line at the bottom: document, counts, simulation state, MCP server.")]
        public TextMeshProUGUI StatusText;
        [Tooltip("Palette entry prefab for the ICs of the library, created at run time.")]
        public GameObject PaletteEntryPrefab;
    }
}
