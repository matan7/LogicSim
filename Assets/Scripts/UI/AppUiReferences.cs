using UnityEngine;
using UnityEngine.UI;

namespace LogicSim.UI
{
    /// <summary>
    /// Scene objects the runtime UI builds on, assigned in the scene (on the root canvas) instead of being looked up by
    /// name. <see cref="AppUi"/> reads them at start-up.
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
        public RectTransform CustomCircuitIcons;

        [Header("Buttons")]
        [Tooltip("Also the template for the generated toolbar buttons.")]
        public GameObject UndoButton;
        public GameObject RedoButton;
        public Toggle SimulationToggle;
    }
}
