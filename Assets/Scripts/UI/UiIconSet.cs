using UnityEngine;

namespace LogicSim.UI
{
    /// <summary>Icons the runtime UI assigns itself (Resources/UiIcons); the scene's buttons reference theirs directly.</summary>
    [CreateAssetMenu(menuName = "LogicSim/UI Icon Set", fileName = "UiIcons")]
    public class UiIconSet : ScriptableObject
    {
        [Tooltip("Palette entries of the ICs in the library.")]
        public Sprite Subcircuit;

        private static UiIconSet _instance;

        public static UiIconSet Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<UiIconSet>("UiIcons");
                return _instance;
            }
        }
    }
}
